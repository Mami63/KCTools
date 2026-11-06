[CmdletBinding()]
param(
    [string]$AdminConnectionString = $env:KCTOOLS_DB_ADMIN_CONNECTION,
    [string]$Database = $env:KCTOOLS_DB_NAME,
    [string]$AppPassword = $env:KCTOOLS_DB_APP_PASSWORD,
    [string]$MigratorPassword = $env:KCTOOLS_DB_MIGRATOR_PASSWORD,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($AdminConnectionString)) {
    throw 'KCTOOLS_DB_ADMIN_CONNECTION tanimli degil (superuser baglantisi).'
}

if ([string]::IsNullOrWhiteSpace($Database)) {
    throw 'KCTOOLS_DB_NAME tanimli degil.'
}

if ([string]::IsNullOrWhiteSpace($AppPassword) -or
    [string]::IsNullOrWhiteSpace($MigratorPassword)) {
    throw 'KCTOOLS_DB_APP_PASSWORD ve KCTOOLS_DB_MIGRATOR_PASSWORD zorunlu.'
}

if ($Database -notmatch '^[a-zA-Z0-9_]+$') {
    throw 'Veritabani adi yalnizca harf, rakam ve alt cizgi icermeli.'
}

$template = Join-Path $PSScriptRoot 'sql\roles.sql'

if (-not (Test-Path $template)) {
    throw "Rol sablonu bulunamadi: $template"
}

$psql = $env:KCTOOLS_PSQL
if ([string]::IsNullOrWhiteSpace($psql)) {
    $psql = (Get-Command psql -ErrorAction SilentlyContinue).Source
}

if ($DryRun) {
    Write-Host "DRY-RUN: kctools_app ve kctools_migrator rolleri '$Database' icin olusturulacak."
    Write-Host 'DRY-RUN: parolalar yalnizca ortam degiskenlerinden okunur.'
    return
}

if ([string]::IsNullOrWhiteSpace($psql) -or -not (Test-Path $psql)) {
    throw 'psql bulunamadi.'
}

function ConvertTo-SqlLiteral([string]$value) {
    return $value.Replace("'", "''")
}

$sql = Get-Content $template -Raw
$sql = $sql.Replace('__APP_PASSWORD__', (ConvertTo-SqlLiteral $AppPassword))
$sql = $sql.Replace('__MIGRATOR_PASSWORD__', (ConvertTo-SqlLiteral $MigratorPassword))
$sql = $sql.Replace('__DATABASE__', $Database)

$tempFile = Join-Path $env:TEMP ('kctools-roles-' + [guid]::NewGuid().ToString('N') + '.sql')

try {
    Set-Content -Path $tempFile -Value $sql -Encoding UTF8

    # Yalnizca mevcut kullaniciya okuma/yazma izni
    & icacls $tempFile /inheritance:r /grant:r "$($env:USERNAME):(R,W)" | Out-Null

    & $psql $AdminConnectionString -v ON_ERROR_STOP=1 -f $tempFile

    if ($LASTEXITCODE -ne 0) {
        throw "Rol kurulumu basarisiz (exit $LASTEXITCODE)."
    }

    Write-Host 'Roller hazir: kctools_migrator (DDL), kctools_app (DML).'
}
finally {
    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
}
