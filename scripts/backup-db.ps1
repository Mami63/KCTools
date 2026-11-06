[CmdletBinding()]
param(
    [string]$ConnectionString = $env:KCTOOLS_DB_MIGRATION_CONNECTION,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\backups'),
    [int]$RetentionDays = 14,
    [string]$Label = 'manual',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'KCTOOLS_DB_MIGRATION_CONNECTION tanimli degil. Yedek icin migrator baglantisi gerekli.'
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

if (-not $dbHost -or -not $dbName -or -not $dbUser) {
    throw 'Baglanti dizesinde Host, Database ve Username zorunlu.'
}

if ([string]::IsNullOrWhiteSpace($dbPort)) { $dbPort = '5432' }

$pgDump = $env:KCTOOLS_PG_DUMP
if ([string]::IsNullOrWhiteSpace($pgDump)) {
    $pgDump = (Get-Command pg_dump -ErrorAction SilentlyContinue).Source
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$safeLabel = ($Label -replace '[^a-zA-Z0-9_-]', '-')
$fileName = "kctools-$safeLabel-$stamp.dump"
$target = Join-Path $OutputDirectory $fileName

if ($DryRun) {
    Write-Host "DRY-RUN: pg_dump -> $target"
    Write-Host "DRY-RUN: host=$dbHost port=$dbPort db=$dbName user=$dbUser (parola gizli)"
    return
}

if ([string]::IsNullOrWhiteSpace($pgDump) -or -not (Test-Path $pgDump)) {
    throw 'pg_dump bulunamadi. PostgreSQL istemci araclari kurulu degil veya KCTOOLS_PG_DUMP yanlis.'
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$oldPassword = $env:PGPASSWORD

try {
    if (-not [string]::IsNullOrWhiteSpace($dbPassword)) {
        $env:PGPASSWORD = $dbPassword
    }

    & $pgDump -h $dbHost -p $dbPort -d $dbName -U $dbUser -Fc -Z 6 -f $target

    if ($LASTEXITCODE -ne 0) {
        throw "pg_dump basarisiz (exit $LASTEXITCODE)."
    }
}
finally {
    $env:PGPASSWORD = $oldPassword
}

if (-not (Test-Path $target) -or (Get-Item $target).Length -eq 0) {
    throw 'Yedek dosyasi olusmadi veya bos.'
}

$hash = (Get-FileHash -Path $target -Algorithm SHA256).Hash
"$hash  $fileName" | Set-Content -Path "$target.sha256" -Encoding ASCII

$cutoff = (Get-Date).AddDays(-1 * [Math]::Max(1, $RetentionDays))
Get-ChildItem -Path $OutputDirectory -Filter 'kctools-*.dump' |
    Where-Object { $_.LastWriteTime -lt $cutoff } |
    ForEach-Object {
        Remove-Item $_.FullName -Force
        Remove-Item ($_.FullName + '.sha256') -Force -ErrorAction SilentlyContinue
    }

Write-Host "Yedek tamam: $target"
Write-Host "SHA256: $hash"
