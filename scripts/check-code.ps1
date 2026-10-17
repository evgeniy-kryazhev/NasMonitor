param(
    [switch]$Cleanup
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try
{
    dotnet tool restore
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Failed to restore the pinned ReSharper tools.'
    }

    New-Item -ItemType Directory -Path 'artifacts' -Force | Out-Null
    if ($Cleanup)
    {
        dotnet jb cleanupcode NasMonitor.slnx '--profile=Built-in: Full Cleanup' `
            '--include=**/*.cs' '--exclude=**/bin/**;**/obj/**' `
            '--disable-settings-layers=GlobalAll;GlobalPerProduct;SolutionPersonal;ProjectPersonal' --no-updates
        if ($LASTEXITCODE -ne 0)
        {
            throw 'ReSharper cleanup failed.'
        }
    }

    dotnet jb inspectcode NasMonitor.slnx --format=Xml --output=artifacts/resharper-report.xml `
        --jobs=2 '--include=**/*.cs' '--exclude=**/bin/**;**/obj/**' `
        '--disable-settings-layers=GlobalAll;GlobalPerProduct;SolutionPersonal;ProjectPersonal' `
        --properties=Configuration=Release --no-updates
    if ($LASTEXITCODE -ne 0)
    {
        throw 'ReSharper analysis failed.'
    }
}
finally
{
    Pop-Location
}
