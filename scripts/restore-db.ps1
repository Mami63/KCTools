[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupFile,
    [string]$ConnectionString = $env:KCTOOLS_DB_MIGRATION_CONNECTION,
    [string]$TargetDatabase,
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $BackupFile)) {
    throw "Yedek dosyasi bulunamadi: $BackupFile"
}

$checksumFile = "$BackupFile.sha256"

if (Test-Path $checksumFile) {
    $expected = (Get-Content $checksumFile -Raw).Split(' ')[0].Trim()
    $actual = (Get-FileHash -Path $BackupFile -Algorithm SHA256).Hash

    if ($expected -ne $actual) {
        throw 'Yedek dosyasi SHA256 dogrulamasi basarisiz; geri yukleme durduruldu.'
    }

    Write-Host 'SHA256 dogrulandi.'
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'KCTOOLS_DB_MIGRATION_CONNECTION tanimli degil.'
}

function Get-ConnectionValue([string]$name) {
    $match = [regex]::Match($ConnectionString, "(?i)\b$name\s*=\s*([^;]+)")
    if (-not $match.Success) { return $null }
    return $match.Groups[1].Value.Trim()
}

$dbHost = Get-ConnectionValue 'Host'
$dbPort = Get-ConnectionValue 'Port'
$dbName = Get-ConnectionValue 'Database'
$dbUser = Get-ConnectionValue 'Username'
$dbPassword = Get-ConnectionValue 'Password'

if (-not $dbHost -or -not $dbUser) {
    throw 'Baglanti dizesinde Host ve Username zorunlu.'
}

if ([string]::IsNullOrWhiteSpace($dbPort)) { $dbPort = '5432' }

if ([string]::IsNullOrWhiteSpace($TargetDatabase)) {
    $TargetDatabase = 'kctools_restore_' + (Get-Date -Format 'yyyyMMdd_HHmmss')
}

if ($TargetDatabase -notmatch '^[a-zA-Z0-9_]+$') {
    throw 'Hedef veritabani adi yalnizca harf, rakam ve alt cizgi icermeli.'
}

$pgRestore = $env:KCTOOLS_PG_RESTORE
if ([string]::IsNullOrWhiteSpace($pgRestore)) {
    $pgRestore = (Get-Command pg_restore -ErrorAction SilentlyContinue).Source
}

$psql = $env:KCTOOLS_PSQL
if ([string]::IsNullOrWhiteSpace($psql)) {
    $psql = (Get-Command psql -ErrorAction SilentlyContinue).Source
}

if ($DryRun) {
    Write-Host "DRY-RUN: $BackupFile -> yeni veritabani '$TargetDatabase'"
    Write-Host "DRY-RUN: host=$dbHost port=$dbPort user=$dbUser (parola gizli)"
    return
}

if ([string]::IsNullOrWhiteSpace($pgRestore) -or -not (Test-Path $pgRestore)) {
    throw 'pg_restore bulunamadi.'
}

if ([string]::IsNullOrWhiteSpace($psql) -or -not (Test-Path $psql)) {
    throw 'psql bulunamadi.'
}

$oldPassword = $env:PGPASSWORD

try {
    if (-not [string]::IsNullOrWhiteSpace($dbPassword)) {
        $env:PGPASSWORD = $dbPassword
    }

    $maintenanceDb = 'postgres'
    $exists = & $psql -h $dbHost -p $dbPort -U $dbUser -d $maintenanceDb -tAc `
        "SELECT 1 FROM pg_database WHERE datname = '$TargetDatabase'"

    if ($LASTEXITCODE -ne 0) {
        throw 'Hedef veritabani kontrolu basarisiz.'
    }

    if ($exists -and -not $Force) {
        throw "Hedef veritabani zaten var: $TargetDatabase. Uzerine yazmak icin -Force kullanin."
    }

    if (-not $exists) {
        & $psql -h $dbHost -p $dbPort -U $dbUser -d $maintenanceDb -v ON_ERROR_STOP=1 `
            -c "CREATE DATABASE \"$TargetDatabase\""

        if ($LASTEXITCODE -ne 0) {
            throw 'Hedef veritabani olusturulamadi.'
        }
    }

    & $pgRestore -h $dbHost -p $dbPort -U $dbUser -d $TargetDatabase `
        --no-owner --clean --if-exists "$BackupFile"

    if ($LASTEXITCODE -ne 0) {
        throw "pg_restore basarisiz (exit $LASTEXITCODE)."
    }
}
finally {
    $env:PGPASSWORD = $oldPassword
}

Write-Host "Geri yukleme tamam: $TargetDatabase"
Write-Host 'Guvenlik: mevcut veritabani uzerine yazilmadi; yeni veritabani olusturuldu.'
