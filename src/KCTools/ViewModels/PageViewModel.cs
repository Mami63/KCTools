using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Models;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>
    /// Tüm modül sayfalarının ortak davranışı: işlem kuyruğu, ilerleme,
    /// durum metni ve günlük entegrasyonu.
    ///
    /// Sayfalar <see cref="ExecuteActionAsync"/> metodunu ezerek gerçek donanım
    /// komutlarını çalıştırabilir; varsayılan uygulama adım simülasyonudur.
    /// </summary>
    public abstract class PageViewModel : ObservableObject
    {
        private bool _isBusy;
        private double _progress;
        private string _statusText = "Hazır — bir işlem seçin.";
        private bool _hasRun;

        protected PageViewModel(string key, string title, string subtitle)
        {
            Key = key;
            Title = title;
            Subtitle = subtitle;
            Tiles = new ObservableCollection<ActionTile>();

            RunCommand = new RelayCommand(
                parameter => { _ = RunAsync(parameter as string); },
                _ => !IsBusy);
        }

        public string Key { get; }

        public string Title { get; }

        public string Subtitle { get; }

        public ObservableCollection<ActionTile> Tiles { get; }

        public ICommand RunCommand { get; }

        /// <summary>Bir cihaz oturumu açıldığında sayfanın alanlarını doldurur.</summary>
        public virtual void OnDeviceAttached(DetectedDevice device)
        {
        }

        /// <summary>Oturum kapandığında / cihaz ayrıldığında alanları temizler.</summary>
        public virtual void OnDeviceDetached()
        {
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!Set(ref _isBusy, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(StateText));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public double Progress
        {
            get => _progress;
            protected set
            {
                if (!Set(ref _progress, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(StateText));
            }
        }

        public string ProgressText => $"{Progress:0}%";

        public string StatusText
        {
            get => _statusText;
            protected set => Set(ref _statusText, value);
        }

        public string StateText
        {
            get
            {
                if (IsBusy)
                {
                    return "ÇALIŞIYOR";
                }

                return _hasRun && Progress >= 100 ? "TAMAM" : "HAZIR";
            }
        }

        /// <summary>İşlemi kuyruğa alır; aynı anda tek işlem çalışır.</summary>
        public async Task RunAsync(string actionKey)
        {
            if (IsBusy || string.IsNullOrWhiteSpace(actionKey))
            {
                return;
            }

            var actionTitle = GetActionTitle(actionKey);

            IsBusy = true;
            Progress = 0;
            StatusText = actionTitle + " hazırlanıyor...";
            _hasRun = false;

            LogHub.Instance.Rule(Title + " · " + actionTitle);

            var success = false;

            EventAggregator.Instance.Publish(new OperationStateChangedEvent
            {
                Module = Title,
                Operation = actionTitle,
                Started = true
            });

            try
            {
                success = await ExecuteActionAsync(actionKey, actionTitle);

                if (success)
                {
                    Progress = 100;

                    if (string.IsNullOrWhiteSpace(StatusText) ||
                        StatusText.EndsWith("hazırlanıyor...", StringComparison.Ordinal))
                    {
                        StatusText = "Tamamlandı — " + actionTitle;
                    }

                    _hasRun = true;
                }
                else if (string.IsNullOrWhiteSpace(StatusText) ||
                         StatusText.EndsWith("hazırlanıyor...", StringComparison.Ordinal))
                {
                    StatusText = actionTitle + " tamamlanamadı.";
                }
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error(actionTitle + " başarısız: " + ex.Message, Title);
                StatusText = "Hata: " + ex.Message;
            }
            finally
            {
                OnPropertyChanged(nameof(StateText));
                IsBusy = false;

                EventAggregator.Instance.Publish(new OperationStateChangedEvent
                {
                    Module = Title,
                    Operation = actionTitle,
                    Started = false,
                    Success = success
                });
            }
        }

        /// <summary>
        /// Varsayılan uygulama: adım listesini gecikmeli olarak canlandırır.
        /// Gerçek komut çalıştıran sayfalar bu metodu ezer.
        /// </summary>
        protected virtual async Task<bool> ExecuteActionAsync(string actionKey, string actionTitle)
        {
            var steps = GetSteps(actionKey);
            if (steps == null || steps.Count == 0)
            {
                LogHub.Instance.Warn("Bu işlem için adım tanımı yok: " + actionKey, Title);
                return false;
            }

            LogHub.Instance.Info(
                "Demo mod: adımlar simüle ediliyor (gerçek donanım katmanı sonraki parçada).", "DEMO");

            for (var i = 0; i < steps.Count; i++)
            {
                await Task.Delay(360 + (i * 40));
                Progress = (i + 1) * 100.0 / steps.Count;
                StatusText = steps[i];
                LogHub.Instance.Step(steps[i], Title);
            }

            await Task.Delay(240);
            LogHub.Instance.Ok(actionTitle + " adımları tamamlandı (demo).", Title);

            OnCompleted(actionKey);
            return true;
        }

        protected virtual void OnCompleted(string actionKey)
        {
        }

        protected abstract string GetActionTitle(string actionKey);

        protected abstract IReadOnlyList<string> GetSteps(string actionKey);

        protected static IReadOnlyList<string> Steps(params string[] steps) => steps;

        /// <summary>Arka plan iş parçacığından gelen güncellemeleri UI'ya taşır.</summary>
        protected static void UpdateOnUi(Action action)
        {
            if (action == null)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(action);
                return;
            }

            action();
        }
    }
}
