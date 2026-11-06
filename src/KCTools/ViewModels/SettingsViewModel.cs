using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>Ayarlar sayfası: günlük, arayüz ve lisans tercihleri.</summary>
    public sealed class SettingsViewModel : ObservableObject
    {
        private string _licenseKeyInput = string.Empty;
        private string _licenseMessage = "Lisans anahtarını girip \"Aktive Et\" düğmesine basın.";
        private bool _isActivating;
        private string _selectedLogLevel = AppSettings.LogLevel;
        private string _selectedLanguage = AppSettings.Language;
        private string _selectedTheme = "Karanlık (Material 3)";

        public SettingsViewModel()
        {
            LogLevels = new ObservableCollection<string>
            {
                AppSettings.LevelSimple,
                AppSettings.LevelNormal,
                AppSettings.LevelVerbose
            };

            Languages = new ObservableCollection<string>
            {
                "Türkçe",
                "English (yakında)"
            };

            Themes = new ObservableCollection<string>
            {
                "Karanlık (Material 3)",
                "Sistem teması (yakında)"
            };

            ActivateCommand = new RelayCommand(
                async _ => await ActivateAsync(), _ => !IsActivating);
            RefreshLicenseCommand = new RelayCommand(
                async _ => await RefreshLicenseAsync(), _ => !IsActivating);
            OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
            ClearLogCommand = LogHub.Instance.ClearCommand;
            RefreshToolsCommand = new RelayCommand(_ => RefreshTools());
            RefreshEnvironmentCommand = new RelayCommand(
                async _ => await EnvironmentChecks.RunAsync(), _ => !EnvironmentChecks.IsRunning);
        }

        /// <summary>adb / fastboot konumları ve sürümleri (paylaşımlı servis).</summary>
        public Toolchain Tools => Toolchain.Instance;

        /// <summary>Açılışta çalışan ortam denetiminin sonuçları.</summary>
        public EnvironmentCheckService EnvironmentChecks => EnvironmentCheckService.Instance;

        public ICommand RefreshEnvironmentCommand { get; }

        public ICommand RefreshToolsCommand { get; }

        public ObservableCollection<string> LogLevels { get; }

        public ObservableCollection<string> Languages { get; }

        public ObservableCollection<string> Themes { get; }

        public ICommand ActivateCommand { get; }

        public ICommand RefreshLicenseCommand { get; }

        public ICommand OpenLogFolderCommand { get; }

        public ICommand ClearLogCommand { get; }

        /// <summary>Lisans durumu ve kredi bilgisi (INPC doğrudan servisten gelir).</summary>
        public LicenseService License => LicenseService.Instance;

        public string ServerUrl
        {
            get => License.ServerUrl;
            set
            {
                if (string.Equals(License.ServerUrl, value, StringComparison.Ordinal))
                {
                    return;
                }

                License.ServerUrl = value;
                OnPropertyChanged();
                LogHub.Instance.Info("Lisans sunucusu: " + value, "LİSANS");
            }
        }

        public bool IsActivating
        {
            get => _isActivating;
            private set
            {
                if (Set(ref _isActivating, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string MachineName => AppSettings.InstanceName;

        public string VersionText => "KC Tools " + AppSettings.Version + " · " + AppSettings.Build;

        public string RuntimeText => ".NET 8 · WPF · Windows 10/11";

        public string ThemeName
        {
            get => _selectedTheme;
            set
            {
                if (Set(ref _selectedTheme, value))
                {
                    LogHub.Instance.Info("Tema tercihi: " + value, "AYARLAR");
                }
            }
        }

        public bool VerboseLogging
        {
            get => AppSettings.VerboseLogging;
            set
            {
                if (AppSettings.VerboseLogging == value)
                {
                    return;
                }

                AppSettings.VerboseLogging = value;
                OnPropertyChanged();
                LogHub.Instance.Info("Ayrıntılı günlük " + (value ? "açıldı" : "kapatıldı") + ".", "AYARLAR");
            }
        }

        public bool ConfirmBeforeFlash
        {
            get => AppSettings.ConfirmFlash;
            set
            {
                if (AppSettings.ConfirmFlash == value)
                {
                    return;
                }

                AppSettings.ConfirmFlash = value;
                OnPropertyChanged();
                LogHub.Instance.Info("Yazma öncesi onay " + (value ? "istenecek" : "istenmeyecek") + ".", "AYARLAR");
            }
        }

        public bool AutoDetectPorts
        {
            get => AppSettings.AutoDetectPorts;
            set
            {
                if (AppSettings.AutoDetectPorts == value)
                {
                    return;
                }

                AppSettings.AutoDetectPorts = value;
                OnPropertyChanged();
                LogHub.Instance.Info("Otomatik port algılama " + (value ? "açıldı" : "kapatıldı") + ".", "AYARLAR");
            }
        }

        public string SelectedLogLevel
        {
            get => _selectedLogLevel;
            set
            {
                if (!Set(ref _selectedLogLevel, value) || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                AppSettings.LogLevel = value;
                LogHub.Instance.Info("Günlük seviyesi: " + value, "AYARLAR");
            }
        }

        public string SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (!Set(ref _selectedLanguage, value) || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                AppSettings.Language = value;
                if (value.StartsWith("English", StringComparison.OrdinalIgnoreCase))
                {
                    LogHub.Instance.Warn("İngilizce dil paketi 2. parçada eklenecek.", "AYARLAR");
                }
                else
                {
                    LogHub.Instance.Info("Dil: " + value, "AYARLAR");
                }
            }
        }

        public string LicenseKeyInput
        {
            get => _licenseKeyInput;
            set => Set(ref _licenseKeyInput, value);
        }

        public string LicenseMessage
        {
            get => _licenseMessage;
            private set => Set(ref _licenseMessage, value);
        }

        private async Task ActivateAsync()
        {
            IsActivating = true;

            try
            {
                var result = await License.ActivateAsync(LicenseKeyInput);
                LicenseMessage = result.Message;

                if (result.Success)
                {
                    LicenseKeyInput = string.Empty;
                }
            }
            catch (Exception ex)
            {
                LicenseMessage = "Aktivasyon hatası: " + ex.Message;
                LogHub.Instance.Error("Aktivasyon hatası: " + ex.Message, "LİSANS");
            }
            finally
            {
                IsActivating = false;
            }
        }

        private async Task RefreshLicenseAsync()
        {
            IsActivating = true;

            try
            {
                var result = await License.ValidateAsync();
                LicenseMessage = result.Message;
            }
            catch (Exception ex)
            {
                LicenseMessage = "Doğrulama hatası: " + ex.Message;
                LogHub.Instance.Error("Lisans doğrulama hatası: " + ex.Message, "LİSANS");
            }
            finally
            {
                IsActivating = false;
            }
        }

        private void RefreshTools()
        {
            Tools.Refresh(true);
            _ = Tools.RefreshVersionsAsync();
        }

        private void OpenLogFolder()
        {
            try
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KCTools",
                    "Logs");

                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + folder + "\"",
                    UseShellExecute = true
                });

                LogHub.Instance.Info("Günlük klasörü açıldı: " + folder, "AYARLAR");
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("Klasör açılamadı: " + ex.Message, "AYARLAR");
            }
        }
    }
}
