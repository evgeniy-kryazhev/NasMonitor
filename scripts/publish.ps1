param(
    [ValidateSet('win-x64', 'osx-arm64', 'osx-x64', 'all')]
    [string]$Runtime = 'all'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$runtimes = @($Runtime)
if ($Runtime -eq 'all')
{
    $runtimes = @('win-x64', 'osx-arm64', 'osx-x64')
}

foreach ($targetRuntime in $runtimes)
{
    $outputDirectory = Join-Path $projectRoot "artifacts/publish-single-file/$targetRuntime"
    dotnet publish (Join-Path $projectRoot 'src/NasMonitor.App/NasMonitor.App.csproj') `
        -c Release -r $targetRuntime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=embedded -p:DebugSymbols=false -p:PublishTrimmed=false -o $outputDirectory
    if ($LASTEXITCODE -ne 0)
    {
        throw "Publish failed: $targetRuntime"
    }

    if ($targetRuntime.StartsWith('osx-'))
    {
        $bundleDirectory = Join-Path $projectRoot "artifacts/bundles-single-file/$targetRuntime/NAS Monitor.app"
        $contentsDirectory = Join-Path $bundleDirectory 'Contents'
        $executableDirectory = Join-Path $contentsDirectory 'MacOS'
        New-Item -ItemType Directory -Path $executableDirectory -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $outputDirectory 'NasMonitor.App') -Destination $executableDirectory -Force
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Info.plist') -Destination $contentsDirectory -Force
        $archiveSource = $bundleDirectory
    }
    else
    {
        $standalonePath = Join-Path $projectRoot "artifacts/NasMonitor-$targetRuntime.exe"
        Copy-Item -LiteralPath (Join-Path $outputDirectory 'NasMonitor.App.exe') -Destination $standalonePath -Force
        $archiveSource = $standalonePath
    }

    $archivePath = Join-Path $projectRoot "artifacts/NasMonitor-spike-$targetRuntime.zip"
    Compress-Archive -LiteralPath $archiveSource -DestinationPath $archivePath -Force
    Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
}

$checksumLines = foreach ($archive in Get-ChildItem (Join-Path $projectRoot 'artifacts/NasMonitor-spike-*.zip'))
{
    $checksum = Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256
    "$($checksum.Hash.ToLowerInvariant())  $($archive.Name)"
}
Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/SHA256SUMS.txt') -Value $checksumLines -Encoding utf8
