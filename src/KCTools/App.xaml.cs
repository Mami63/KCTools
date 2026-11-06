using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KCTools.Models;
using KCTools.Security;
using KCTools.Services;
using KCTools.Views;

namespace KCTools
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var exception = args.ExceptionObject as Exception;

                if (exception != null)
                {
                    WriteCrashLog("AppDomain", exception);
                }

                LogHub.Instance.Error(
                    "Beklenmeyen hata: " + args.ExceptionObject, "APP");
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                WriteCrashLog("Task", args.Exception);
                LogHub.Instance.Error(
                    "Gözlenmeyen görev hatası: " + args.Exception.GetBaseException().Message,
                    "APP");

                args.SetObserved();
            };

            _ = ShowShellAsync();
        }

        private async Task ShowShellAsync()
        {
            try
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                if (LicenseGate.Enabled)
                {
                    using var gate = new LicenseGate();
                    var result = await gate.RestoreAsync();
                    var interactiveLogin = false;

                    if (result.State == GateState.LoginRequired)
                    {
                        var login = new LoginWindow(gate);

                        if (login.ShowDialog() != true)
                        {
                            Shutdown();
                            return;
                        }

                        interactiveLogin = true;
                    }
                    else if (result.State == GateState.Blocked)
                    {
                        MessageBox.Show(
                            result.Message, "KC Tools · Lisans",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        Shutdown();
                        return;
                    }
                    else if (result.State == GateState.Offline)
                    {
                        MessageBox.Show(
                            "Sunucuya ulaşılamadı. İmzalı offline yetki kullanılıyor.\n" +
                            "Lisans bitişi: " + gate.LicenseEndText,
                            "KC Tools · Çevrimdışı",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    if (interactiveLogin)
                    {
                        MessageBox.Show(
                            "Kullanıcı: " + gate.Email + "\n" +
                            "Lisans bitişi: " + gate.LicenseEndText + "\n" +
                            "Durum: " + gate.StatusText,
                            "KC Tools · Lisans",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    LogHub.Instance.Info(
                        "Lisans kapısı: " + gate.StatusText + " · " + gate.Email, "LİSANS");
                }

                var window = new MainWindow();
                MainWindow = window;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                window.Show();
            }
            catch (Exception ex)
            {
                WriteCrashLog("Startup", ex);
                MessageBox.Show(
                    "Uygulama başlatılamadı: " + ex.Message, "KC Tools",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                LogHub.Instance.Info("Uygulama kapatılıyor.", "SİSTEM");
                LogHub.Instance.Shutdown();
            }
            catch (Exception)
            {
                // kapanışta hata yutulur
            }

            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteCrashLog("UI", e.Exception);
            LogHub.Instance.Write(LogSeverity.Error, "Arayüz hatası: " + e.Exception.Message, "APP");

            // Kablo çekme / sürücü hatası gibi geçici durumlarda uygulama ayakta kalır.
            e.Handled = true;
        }

        /// <summary>
        /// Günlük altyapısı çökmeden önce de yazılabilen dosya tabanlı çökme kaydı.
        /// Uygulama klasörüne, olmazsa %TEMP%'e yazar; asla istisna fırlatmaz.
        /// </summary>
        private static void WriteCrashLog(string context, Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            var text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                       " [" + context + "] " + exception.GetType().Name + ": " + exception.Message +
                       Environment.NewLine + exception + Environment.NewLine +
                       new string('-', 70) + Environment.NewLine;

            foreach (var folder in new[]
                     {
                         Path.Combine(AppContext.BaseDirectory, "logs"),
                         Path.Combine(Path.GetTempPath(), "KCTools")
                     })
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, "crash.log"), text);
                    return;
                }
                catch (Exception)
                {
                    // sıradaki konumu dene
                }
            }
        }
    }
}
