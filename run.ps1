# KC Tools - derleme ve çalıştırma yardımcısı
# Kullanım: .\run.ps1            (dotnet run)
#           .\run.ps1 -Build     (yalnızca derle)

param(
    [switch]$Build,
    [switch]$Release
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\KCTools\KCTools.csproj'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'HATA: .NET SDK bulunamadi.' -ForegroundColor Red
    Write-Host 'Kurulum:  winget install Microsoft.DotNet.SDK.8' -ForegroundColor Yellow
    exit 1
}

$config = if ($Release) { 'Release' } else { 'Debug' }

if ($Build) {
    dotnet build $project -c $config
}
else {
    dotnet run --project $project -c $config
}

exit $LASTEXITCODE
