$ErrorActionPreference = 'Stop'
if (-not $IsWindows)
{
    throw 'This local helper integration test runs on Windows.'
}

$projectRoot = Split-Path $PSScriptRoot -Parent
$published = Join-Path $projectRoot 'artifacts/NasMonitor-win-x64.exe'
if (-not (Test-Path -LiteralPath $published))
{
    throw 'Publish the Windows single-file application first.'
}

# Only dedicated fixture copies are replaced. The published executable and user settings are untouched.
$fixtureRoot = Join-Path $projectRoot "artifacts/update install smoke/$([Guid]::NewGuid().ToString('N'))"
$probeDirectory = Join-Path $fixtureRoot 'probe'
$targetDirectory = Join-Path $fixtureRoot 'target'
$runDirectory = Join-Path $fixtureRoot 'run'
$stageDirectory = Join-Path $fixtureRoot "staging/$([Guid]::NewGuid().ToString('N'))"
foreach ($directory in @($probeDirectory, $targetDirectory, $runDirectory, $stageDirectory))
{
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>WinExe</OutputType>
    <Version>0.2.0</Version>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $probeDirectory 'Probe.csproj') -Encoding utf8
@'
System.IO.File.WriteAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "updated.marker"), "updated");
'@ | Set-Content -LiteralPath (Join-Path $probeDirectory 'Program.cs') -Encoding utf8
dotnet publish (Join-Path $probeDirectory 'Probe.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=embedded -o (Join-Path $fixtureRoot 'probe-publish')
if ($LASTEXITCODE -ne 0)
{
    throw 'Failed to build the harmless replacement probe.'
}

$target = Join-Path $targetDirectory 'NAS Monitor fixture.exe'
$payload = Join-Path $stageDirectory 'payload.bin'
$helper = Join-Path $stageDirectory 'updater.exe'
Copy-Item -LiteralPath $published -Destination $target
Copy-Item -LiteralPath $published -Destination $helper
Copy-Item -LiteralPath (Join-Path $fixtureRoot 'probe-publish/Probe.exe') -Destination $payload
$originalHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
$parent = $null
$updater = $null
try
{
    $parent = Start-Process -FilePath $target -ArgumentList '--smoke-test' -WorkingDirectory $runDirectory `
        -WindowStyle Hidden -PassThru
    $job = @{
        ParentId = $parent.Id
        ParentStartTicks = $parent.StartTime.ToUniversalTime().Ticks
        TargetPath = $target
        OriginalSha256 = $originalHash
        PayloadSha256 = $payloadHash
        Version = '0.2.0'
    }
    $jobPath = Join-Path $stageDirectory 'install.json'
    $job | ConvertTo-Json | Set-Content -LiteralPath $jobPath -Encoding utf8
    $start = [System.Diagnostics.ProcessStartInfo]::new($helper)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $start.WorkingDirectory = $runDirectory
    $start.ArgumentList.Add('--apply-update')
    $start.ArgumentList.Add($jobPath)
    $updater = [System.Diagnostics.Process]::Start($start)
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path -LiteralPath (Join-Path $stageDirectory 'ready')))
    {
        if ($updater.HasExited -or [DateTime]::UtcNow -gt $deadline)
        {
            throw 'The helper failed to signal readiness.'
        }
        Start-Sleep -Milliseconds 100
    }
    if ($parent.HasExited -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $originalHash)
    {
        throw 'The executable changed before its parent exited.'
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while (-not $updater.HasExited)
    {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'The helper timed out.' }
        Start-Sleep -Milliseconds 100
    }
    if ($updater.ExitCode -ne 0) { throw "Helper failed with exit code $($updater.ExitCode)." }
    $marker = Join-Path $targetDirectory 'updated.marker'
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not (Test-Path -LiteralPath $marker))
    {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'The updated executable did not launch.' }
        Start-Sleep -Milliseconds 100
    }

    $backup = @(Get-ChildItem -LiteralPath $targetDirectory -Filter '*.backup-*' -File)
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $payloadHash -or $backup.Count -ne 1 -or
        (Get-FileHash -LiteralPath $backup[0].FullName -Algorithm SHA256).Hash -ne $originalHash)
    {
        throw 'Replacement or backup verification failed.'
    }
    $report = @{ HelperReady = $true; WaitedForParent = $true; ReplacedExecutable = $true; BackupVerified = $true; RelaunchedProbe = $true }
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/update-install-smoke.json') -Encoding utf8
    $report
}
finally
{
    foreach ($process in @($parent, $updater))
    {
        if ($null -ne $process)
        {
            if (-not $process.HasExited) { $process.Kill() }
            $process.Dispose()
        }
    }
}
