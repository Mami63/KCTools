[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$DotnetPath = 'dotnet',
    [string]$EfPath,
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'artifacts\kctools-migrate.exe'
}

New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null

Push-Location $root

try {
    if ([string]::IsNullOrWhiteSpace($EfPath)) {
        & $DotnetPath tool restore

        if ($LASTEXITCODE -ne 0) {
            throw 'dotnet tool restore basarisiz.'
        }
    }

    $bundleArgs = @(
        'migrations', 'bundle',
        '--project', 'server\KCTools.LicenseServer',
        '--startup-project', 'server\KCTools.LicenseServer',
        '--configuration', 'Release',
        '-r', $Runtime,
        '--self-contained',
        '-o', $OutputPath,
        '--force'
    )

    if ([string]::IsNullOrWhiteSpace($EfPath)) {
        & $DotnetPath 'ef' @bundleArgs
    }
    else {
        & $EfPath @bundleArgs
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Migration bundle olusturulamadi (exit $LASTEXITCODE)."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path $OutputPath)) {
    throw 'Bundle dosyasi olusmadi.'
}

$hash = (Get-FileHash -Path $OutputPath -Algorithm SHA256).Hash
Write-Host "Bundle: $OutputPath"
Write-Host "SHA256: $hash"
