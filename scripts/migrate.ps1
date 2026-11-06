[CmdletBinding()]
param(
    [string]$ConnectionString = $env:KCTOOLS_DB_MIGRATION_CONNECTION,
    [switch]$SkipBackup,
    [switch]$UseBundle,
    [string]$BundlePath,
    [string]$DotnetPath = 'dotnet',
    [string]$EfPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'KCTOOLS_DB_MIGRATION_CONNECTION tanimli degil. Migration migrator roluyle calistirilmali.'
}

$root = Split-Path $PSScriptRoot -Parent

if (-not $SkipBackup) {
    Write-Host 'Migration oncesi yedek aliniyor...'
    & (Join-Path $PSScriptRoot 'backup-db.ps1') `
        -ConnectionString $ConnectionString -Label 'pre-migration'
}

if ($UseBundle) {
    if ([string]::IsNullOrWhiteSpace($BundlePath)) {
        $BundlePath = Join-Path $root 'artifacts\kctools-migrate.exe'
    }

    if (-not (Test-Path $BundlePath)) {
        throw "Migration bundle bulunamadi: $BundlePath. Once migrate-bundle.ps1 calistirin."
    }

    $oldConnection = $env:KCTOOLS_DB_CONNECTION
    try {
        $env:KCTOOLS_DB_CONNECTION = $ConnectionString
        & $BundlePath

        if ($LASTEXITCODE -ne 0) {
            throw "Migration bundle basarisiz (exit $LASTEXITCODE)."
        }
    }
    finally {
        $env:KCTOOLS_DB_CONNECTION = $oldConnection
    }

    Write-Host 'Migration bundle ile tamamlandi.'
    return
}

Push-Location $root

try {
    if ([string]::IsNullOrWhiteSpace($EfPath)) {
        & $DotnetPath tool restore

        if ($LASTEXITCODE -ne 0) {
            throw 'dotnet tool restore basarisiz.'
        }
    }

    $oldConnection = $env:KCTOOLS_DB_CONNECTION
    try {
        $env:KCTOOLS_DB_CONNECTION = $ConnectionString

        if ([string]::IsNullOrWhiteSpace($EfPath)) {
            & $DotnetPath ef database update `
                --project 'server\KCTools.LicenseServer' `
                --startup-project 'server\KCTools.LicenseServer'
        }
        else {
            & $EfPath database update `
                --project 'server\KCTools.LicenseServer' `
                --startup-project 'server\KCTools.LicenseServer'
        }

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet ef database update basarisiz (exit $LASTEXITCODE)."
        }
    }
    finally {
        $env:KCTOOLS_DB_CONNECTION = $oldConnection
    }

    Write-Host 'Migration tamamlandi.'
}
finally {
    Pop-Location
}
