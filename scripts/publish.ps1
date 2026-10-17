param(
    [ValidateSet('win-x64', 'osx-arm64', 'osx-x64', 'all')]
    [string]$Runtime = 'all',
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$runtimes = @($Runtime)
if ($Runtime -eq 'all')
{
    $runtimes = @('win-x64', 'osx-arm64', 'osx-x64')
}

$releaseFiles = [System.Collections.Generic.List[string]]::new()

foreach ($targetRuntime in $runtimes)
{
    $outputDirectory = Join-Path $projectRoot "artifacts/publish-single-file/$targetRuntime"
    dotnet publish (Join-Path $projectRoot 'src/NasMonitor.App/NasMonitor.App.csproj') `
        -c Release -r $targetRuntime --self-contained true `
        -p:Version=$Version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
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
        $plistPath = Join-Path $contentsDirectory 'Info.plist'
        [xml]$plist = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Info.plist') -Raw
        foreach ($key in $plist.plist.dict.key)
        {
            if ($key.InnerText -in @('CFBundleVersion', 'CFBundleShortVersionString'))
            {
                $key.NextSibling.InnerText = $Version
            }
        }
        $plist.Save($plistPath)
        $standalonePath = Join-Path $projectRoot "artifacts/NasMonitor-$targetRuntime"
        Copy-Item -LiteralPath (Join-Path $outputDirectory 'NasMonitor.App') -Destination $standalonePath -Force
        $archiveSource = $bundleDirectory
    }
    else
    {
        $standalonePath = Join-Path $projectRoot "artifacts/NasMonitor-$targetRuntime.exe"
        Copy-Item -LiteralPath (Join-Path $outputDirectory 'NasMonitor.App.exe') -Destination $standalonePath -Force
        $archiveSource = $standalonePath
    }

    $archivePath = Join-Path $projectRoot "artifacts/NasMonitor-$targetRuntime.zip"
    if ($targetRuntime.StartsWith('osx-'))
    {
        # ZipArchive Unix attributes preserve the executable bit even when cross-publishing on Windows.
        $archiveStream = [System.IO.File]::Open($archivePath, [System.IO.FileMode]::Create)
        $archive = [System.IO.Compression.ZipArchive]::new($archiveStream, [System.IO.Compression.ZipArchiveMode]::Create)
        try
        {
            $bundleParent = Split-Path $bundleDirectory -Parent
            foreach ($file in Get-ChildItem -LiteralPath $bundleDirectory -File -Recurse)
            {
                $entryName = [System.IO.Path]::GetRelativePath($bundleParent, $file.FullName).Replace('\', '/')
                $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName)
                $mode = if ($file.Name -eq 'NasMonitor.App') { 33261 } else { 33188 }
                $entry.ExternalAttributes = $mode -shl 16
            }
        }
        finally
        {
            $archive.Dispose()
            $archiveStream.Dispose()
        }
        # ZipArchive uses the publishing host's creator OS. Mark central-directory entries
        # as Unix so macOS extraction actually applies the executable mode above.
        $zipStream = [System.IO.File]::Open($archivePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite)
        try
        {
            $zipStream.Seek(-22, [System.IO.SeekOrigin]::End) | Out-Null
            $endRecord = [byte[]]::new(22)
            $zipStream.ReadExactly($endRecord)
            if ([BitConverter]::ToUInt32($endRecord, 0) -ne 0x06054b50)
            {
                throw 'Unexpected ZIP end record.'
            }
            $entryCount = [BitConverter]::ToUInt16($endRecord, 10)
            $centralOffset = [long][BitConverter]::ToUInt32($endRecord, 16)
            for ($entryIndex = 0; $entryIndex -lt $entryCount; $entryIndex++)
            {
                $zipStream.Position = $centralOffset
                $header = [byte[]]::new(46)
                $zipStream.ReadExactly($header)
                if ([BitConverter]::ToUInt32($header, 0) -ne 0x02014b50)
                {
                    throw 'Unexpected ZIP central directory.'
                }
                $zipStream.Position = $centralOffset + 5
                $zipStream.WriteByte(3)
                $centralOffset += 46 + [BitConverter]::ToUInt16($header, 28) +
                    [BitConverter]::ToUInt16($header, 30) + [BitConverter]::ToUInt16($header, 32)
            }
        }
        finally
        {
            $zipStream.Dispose()
        }
    }
    else
    {
        Compress-Archive -LiteralPath $archiveSource -DestinationPath $archivePath -Force
    }
    $releaseFiles.Add($standalonePath)
    $releaseFiles.Add($archivePath)
    Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
}

$checksumLines = foreach ($releaseFile in $releaseFiles)
{
    $checksum = Get-FileHash -LiteralPath $releaseFile -Algorithm SHA256
    "$($checksum.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($releaseFile))"
}
Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts/SHA256SUMS.txt') -Value $checksumLines -Encoding utf8
