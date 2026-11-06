<#
.SYNOPSIS
    KC Tools — Release derleme ve paketleme betiği.

.DESCRIPTION
    Sırasıyla:
      1) dotnet publish (win-x64, framework-dependent) -> dist/app
      2) platform-tools varlık kontrolü
      3) Obfuscar (kuruluysa)
      4) Inno Setup ile kurulum paketi -> dist/KCTools-Setup-1.0.0.exe

.PARAMETER SkipObfuscation
    Obfuscar adımını atlar.

.PARAMETER SkipInstaller
    Inno Setup adımını atlar.

.EXAMPLE
    .\build-release.ps1

.EXAMPLE
    .\build-release.ps1 -SkipObfuscation -SkipInstaller
#>
[CmdletBinding()]
param(
    [switch]$SkipObfuscation,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Yollar
# ---------------------------------------------------------------------------
$root       = $PSScriptRoot
$project    = Join-Path $root 'src\KCTools\KCTools.csproj'
$distRoot   = Join-Path $root 'dist'
$appDir     = Join-Path $distRoot 'app'
$issFile    = Join-Path $root 'installer\kctools.iss'
$obfuscarXml = Join-Path $root 'obfuscar.xml'
$setupName  = 'KCTools-Setup-1.0.0.exe'
$setupFile  = Join-Path $distRoot $setupName

# ---------------------------------------------------------------------------
# Konsol yardımcıları
# ---------------------------------------------------------------------------
function Write-Banner {
    param([string]$Text)

    Write-Host ''
    Write-Host ('=' * 68) -ForegroundColor DarkCyan
    Write-Host ("  $Text") -ForegroundColor Cyan
    Write-Host ('=' * 68) -ForegroundColor DarkCyan
}

function Write-Step {
    param([int]$Number, [string]$Text)

    Write-Host ''
    Write-Host ("[$Number/4] $Text") -ForegroundColor Cyan
    Write-Host ('-' * 68) -ForegroundColor DarkCyan
}

function Write-Ok {
    param([string]$Text)
    Write-Host "  [OK]   $Text" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Text)
    Write-Host "  [UYARI] $Text" -ForegroundColor Yellow
}

function Write-Info {
    param([string]$Text)
    Write-Host "  $Text" -ForegroundColor Gray
}

function Write-Fail {
    param([string]$Text)
    Write-Host "  [HATA] $Text" -ForegroundColor Red
}

# ---------------------------------------------------------------------------
# Araç çözümleme
# ---------------------------------------------------------------------------
function Resolve-Dotnet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue

    if ($command) {
        return $command.Source
    }

    $local = Join-Path $root '.dotnet\dotnet.exe'

    if (Test-Path $local) {
        return $local
    }

    throw '.NET 8 SDK bulunamadı. Kurulum: https://dotnet.microsoft.com/download/dotnet/8.0'
}

function Resolve-Obfuscar {
    $command = Get-Command 'obfuscar.console' -ErrorAction SilentlyContinue

    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:USERPROFILE '.dotnet\tools\obfuscar.console.exe'),
        (Join-Path $root '.tools\obfuscar\obfuscar.console.exe')
    )

    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_CLI_HOME)) {
        $candidates += Join-Path $env:DOTNET_CLI_HOME '.dotnet\tools\obfuscar.console.exe'
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Resolve-Iscc {
    if (-not [string]::IsNullOrWhiteSpace($env:ISCC) -and (Test-Path $env:ISCC)) {
        return $env:ISCC
    }

    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue

    if ($command) {
        return $command.Source
    }

    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $programFiles = [Environment]::GetFolderPath('ProgramFiles')
    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $candidates += Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'
        $candidates += Join-Path $programFilesX86 'Inno Setup 5\ISCC.exe'
    }

    if (-not [string]::IsNullOrWhiteSpace($programFiles)) {
        $candidates += Join-Path $programFiles 'Inno Setup 6\ISCC.exe'
        $candidates += Join-Path $programFiles 'Inno Setup 5\ISCC.exe'
    }

    $localPrograms = [Environment]::GetFolderPath('LocalApplicationData')

    if (-not [string]::IsNullOrWhiteSpace($localPrograms)) {
        $candidates += Join-Path $localPrograms 'Programs\Inno Setup 6\ISCC.exe'
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    return $null
}

# ---------------------------------------------------------------------------
# Ana akış
# ---------------------------------------------------------------------------
$exitCode = 0

try {
    Push-Location $root

    Write-Banner 'KC Tools — Release Build'

    if (-not (Test-Path $project)) {
        throw "Proje dosyası bulunamadı: $project"
    }

    if (-not (Test-Path $issFile)) {
        throw "Inno Setup betiği bulunamadı: $issFile"
    }

    $dotnet = Resolve-Dotnet
    Write-Info "dotnet: $dotnet"

    # Yerel SDK kullanılıyorsa alt araçların (obfuscar, ISCC) dotnet bulabilmesi için PATH'e ekle.
    $dotnetDir = Split-Path $dotnet -Parent

    if ($env:PATH -notlike "*$dotnetDir*") {
        $env:PATH = "$dotnetDir;$env:PATH"
    }

    # Obfuscar gibi apphost araçları .NET kökünü PATH'ten bağımsız arar.
    $env:DOTNET_ROOT = $dotnetDir
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'

    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

    # Kısıtlı/servis hesaplarında kullanıcı profilindeki .dotnet yazılamayabilir;
    # build'e özel, çalışma alanı içinde bir CLI home kullan.
    if ([string]::IsNullOrWhiteSpace($env:DOTNET_CLI_HOME)) {
        $env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
    }

    New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

    # MSBuild/NuGet geçici dosyaları: kısıtlı ortamlarda kullanıcı TEMP'i yazılamaz.
    $buildTemp = Join-Path $root '.tmp'
    New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
    $env:TEMP = $buildTemp
    $env:TMP = $buildTemp

    # Build'e özel profil dizinleri: kullanıcı profilindeki NuGet/CLI yazma
    # kısıtlamalarından ve makineye özel ayarlardan etkilenmemek için.
    $env:APPDATA = Join-Path $root '.appdata'
    $env:LOCALAPPDATA = Join-Path $root '.localappdata'
    New-Item -ItemType Directory -Force -Path $env:APPDATA | Out-Null
    New-Item -ItemType Directory -Force -Path $env:LOCALAPPDATA | Out-Null

    $localPackages = Join-Path $root '.nuget\packages'

    if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES) -and (Test-Path $localPackages)) {
        $env:NUGET_PACKAGES = $localPackages
    }

    # -----------------------------------------------------------------------
    # 1) Publish
    # -----------------------------------------------------------------------
    Write-Step 1 'Publish (Release, win-x64, framework-dependent)'

    if (Test-Path $appDir) {
        $resolvedApp = (Resolve-Path $appDir).Path
        $expectedRoot = $distRoot.TrimEnd('\') + '\'

        if (-not $resolvedApp.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Güvenlik: dist/app beklenen konumda değil: $resolvedApp"
        }

        Remove-Item $appDir -Recurse -Force
        Write-Info 'Eski dist/app temizlendi.'
    }

    New-Item -ItemType Directory -Force -Path $appDir | Out-Null

    $publishArgs = @(
        'publish', $project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'false',
        '-o', $appDir
    )

    $nugetConfig = Join-Path $root 'NuGet.config'

    if (Test-Path $nugetConfig) {
        $publishArgs += "-p:RestoreConfigFile=$nugetConfig"
    }

    Write-Info ("dotnet " + ($publishArgs -join ' '))
    & $dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish başarısız (çıkış kodu: $LASTEXITCODE)."
    }

    Write-Ok "Publish tamamlandı: $appDir"

    # -----------------------------------------------------------------------
    # 2) platform-tools kontrolü
    # -----------------------------------------------------------------------
    Write-Step 2 'platform-tools kontrolü'

    $platformTools = Join-Path $appDir 'platform-tools'
    $required = @('adb.exe', 'fastboot.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll')
    $missing = @()

    foreach ($file in $required) {
        $path = Join-Path $platformTools $file

        if (Test-Path $path) {
            Write-Ok "$file"
        }
        else {
            Write-Warn "$file eksik: $path"
            $missing += $file
        }
    }

    if ($missing.Count -gt 0) {
        Write-Warn ("Eksik dosya sayısı: {0}. tools\platform-tools içeriğini dist\app\platform-tools altına kopyalayın." -f $missing.Count)
    }
    else {
        Write-Ok 'Tüm platform-tools dosyaları hazır.'
    }

    # -----------------------------------------------------------------------
    # 3) Obfuscation
    # -----------------------------------------------------------------------
    Write-Step 3 'Kod karartma (Obfuscar)'

    if ($SkipObfuscation) {
        Write-Info 'Atlandı (-SkipObfuscation).'
    }
    elseif (-not (Test-Path $obfuscarXml)) {
        Write-Warn "obfuscar.xml bulunamadı; karartma atlandı: $obfuscarXml"
    }
    else {
        $obfuscar = Resolve-Obfuscar

        if (-not $obfuscar) {
            Write-Warn 'Obfuscar bulunamadı. Kurulum: dotnet tool install --global Obfuscar.GlobalTool'
        }
        else {
            Write-Info "obfuscar: $obfuscar"

            # WPF framework derlemeleri, SDK'nın shared klasöründen çözülür.
            # Makineye özel yolu obfuscar.xml içine gömmemek için geçici bir
            # kopya üretilir; proje yolu kökte kaldığı için göreli InPath korunur.
            $obfuscarConfig = $obfuscarXml
            $generatedConfig = $null
            $desktopRoot = Join-Path $dotnetDir 'shared\Microsoft.WindowsDesktop.App'
            $coreRoot = Join-Path $dotnetDir 'shared\Microsoft.NETCore.App'

            if ((Test-Path $desktopRoot) -and (Test-Path $coreRoot)) {
                $desktop = Get-ChildItem $desktopRoot -Directory |
                    Sort-Object Name -Descending | Select-Object -First 1
                $core = Get-ChildItem $coreRoot -Directory |
                    Sort-Object Name -Descending | Select-Object -First 1

                if ($desktop -and $core) {
                    $frameworkFolders = "$($desktop.FullName);$($core.FullName)"
                    $content = Get-Content $obfuscarXml -Raw

                    $content = [regex]::Replace(
                        $content,
                        '<Var\s+name="ExtraFrameworkFolders"[^>]*/>',
                        '<Var name="ExtraFrameworkFolders" value="' + $frameworkFolders + '" />')

                    $generatedConfig = Join-Path $root 'obfuscar.generated.xml'
                    Set-Content -Path $generatedConfig -Value $content -Encoding UTF8
                    $obfuscarConfig = $generatedConfig
                    Write-Info "Framework klasörleri: $frameworkFolders"
                }
            }

            try {
                & $obfuscar $obfuscarConfig

                if ($LASTEXITCODE -ne 0) {
                    throw "Obfuscar başarısız (çıkış kodu: $LASTEXITCODE)."
                }
            }
            finally {
                if ($generatedConfig -and (Test-Path $generatedConfig)) {
                    Remove-Item $generatedConfig -Force
                }
            }

            $obfuscatedDll = Join-Path $distRoot 'obfuscated\KCTools.dll'

            if (Test-Path $obfuscatedDll) {
                Copy-Item $obfuscatedDll (Join-Path $appDir 'KCTools.dll') -Force
                Write-Ok 'Karartılmış KCTools.dll dist/app içine kopyalandı.'
            }
            else {
                Write-Warn "Karartılmış çıktı bulunamadı: $obfuscatedDll"
            }
        }
    }

    # -----------------------------------------------------------------------
    # 4) Installer
    # -----------------------------------------------------------------------
    Write-Step 4 'Kurulum paketi (Inno Setup)'

    if ($SkipInstaller) {
        Write-Info 'Atlandı (-SkipInstaller).'
    }
    else {
        $iscc = Resolve-Iscc

        if (-not $iscc) {
            throw ('Inno Setup (ISCC.exe) bulunamadı. Kurulum: https://jrsoftware.org/isdl.php ' +
                   'veya -SkipInstaller kullanın.')
        }

        Write-Info "ISCC: $iscc"

        # Eski paketle karışmaması için beklenen çıktıyı önceden kaldır.
        if (Test-Path $setupFile) {
            Remove-Item $setupFile -Force
        }

        & $iscc $issFile

        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup derlemesi başarısız (çıkış kodu: $LASTEXITCODE)."
        }

        if (Test-Path $setupFile) {
            Write-Ok "Kurulum paketi: $setupFile"
        }
        else {
            $produced = Get-ChildItem $distRoot -Filter 'KCTools-Setup-*.exe' -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1

            if ($produced) {
                Write-Warn "Beklenen ad yerine üretilen dosya: $($produced.FullName)"
            }
            else {
                throw "Kurulum paketi üretilmedi (beklenen: $setupFile)."
            }
        }
    }

    Write-Banner 'Bitti'
    Write-Ok "Çıktı dizini: $distRoot"

    if (Test-Path $setupFile) {
        Write-Ok "Kurulum: $setupFile"
    }
}
catch {
    Write-Fail $_.Exception.Message
    Write-Host ''
    Write-Host 'Build başarısız. Ayrıntı için yukarıdaki mesajı inceleyin.' -ForegroundColor Red
    $exitCode = 1
}
finally {
    Pop-Location
}

exit $exitCode
