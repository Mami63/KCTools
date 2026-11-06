using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Models;
using KCTools.Services;

namespace KCTools.ViewModels
{
    /// <summary>
    /// Cihaz kataloğu: marka → model → teknik detay ve bağlı cihazla otomatik eşleştirme.
    /// Yalnızca tanımlama/bilgi amaçlıdır; cihaz üzerinde işlem yapmaz.
    /// </summary>
    public sealed class CatalogViewModel : PageViewModel, IDisposable
    {
        private readonly DeviceCatalogService _catalog = DeviceCatalogService.Instance;
        private readonly IDisposable _subscription;

        private CatalogBrand _selectedBrand;
        private CatalogModel _selectedModel;
        private string _search = string.Empty;
        private string _platform = "Tümü";
        private string _detectedText = "Cihaz bekleniyor — ADB/Fastboot bağlanınca otomatik eşleşir";
        private string _matchText = "—";
        private string _matchConfidence = "—";
        private string _detectedSerial;

        public CatalogViewModel()
            : base("catalog", "Cihaz Kataloğu", "Marka/model veritabanı ve otomatik eşleştirme")
        {
            Brands = new ObservableCollection<CatalogBrand>(_catalog.Brands);
            Models = new ObservableCollection<CatalogModel>();
            Platforms = new ObservableCollection<string> { "Tümü" };

            foreach (var platform in _catalog.Brands
                         .SelectMany(b => b.Models)
                         .Select(m => FirstToken(m.Platform))
                         .Where(p => p.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(p => p))
            {
                Platforms.Add(platform);
            }

            RefreshCommand = new RelayCommand(_ => Reload());
            AssignCommand = new RelayCommand(_ => Assign(),
                _ => SelectedModel != null && !string.IsNullOrWhiteSpace(_detectedSerial));

            _subscription = EventAggregator.Instance.Subscribe<DevicePresenceEvent>(OnPresence);

            SelectedBrand = Brands.FirstOrDefault();
        }

        public ObservableCollection<CatalogBrand> Brands { get; }

        public ObservableCollection<CatalogModel> Models { get; }

        public ObservableCollection<string> Platforms { get; }

        public ICommand RefreshCommand { get; }

        public ICommand AssignCommand { get; }

        public CatalogBrand SelectedBrand
        {
            get => _selectedBrand;
            set
            {
                if (Set(ref _selectedBrand, value))
                {
                    RefreshModels();
                }
            }
        }

        public CatalogModel SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (Set(ref _selectedModel, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(DetailTitle));
                    OnPropertyChanged(nameof(DetailCode));
                    OnPropertyChanged(nameof(DetailCodename));
                    OnPropertyChanged(nameof(DetailPlatform));
                    OnPropertyChanged(nameof(DetailModes));
                    OnPropertyChanged(nameof(DetailNotes));
                    OnPropertyChanged(nameof(DetailTested));
                    OnPropertyChanged(nameof(DetailTestedText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string SearchText
        {
            get => _search;
            set
            {
                if (Set(ref _search, value))
                {
                    RefreshModels();
                }
            }
        }

        public string SelectedPlatform
        {
            get => _platform;
            set
            {
                if (Set(ref _platform, value))
                {
                    RefreshModels();
                }
            }
        }

        public string DetectedText
        {
            get => _detectedText;
            private set => Set(ref _detectedText, value);
        }

        public string MatchText
        {
            get => _matchText;
            private set => Set(ref _matchText, value);
        }

        public string MatchConfidence
        {
            get => _matchConfidence;
            private set => Set(ref _matchConfidence, value);
        }

        public bool HasSelection => _selectedModel != null;

        public string DetailTitle => _selectedModel == null
            ? "Model seçin"
            : _selectedModel.Name.StartsWith(_selectedBrand.Name, StringComparison.OrdinalIgnoreCase)
                ? _selectedModel.Name
                : _selectedBrand.Name + " " + _selectedModel.Name;

        public string DetailCode => _selectedModel == null ? "—" : _selectedModel.Code;

        public string DetailCodename =>
            _selectedModel == null || string.IsNullOrWhiteSpace(_selectedModel.Codename)
                ? "—"
                : _selectedModel.Codename;

        public string DetailPlatform =>
            _selectedModel == null ? "—" : _selectedModel.Platform;

        public string DetailModes => _selectedModel == null ? "—" : _selectedModel.ModesText;

        public string DetailNotes =>
            _selectedModel == null || string.IsNullOrWhiteSpace(_selectedModel.Notes)
                ? "—"
                : _selectedModel.Notes;

        public bool DetailTested => _selectedModel != null && _selectedModel.Tested;

        public string DetailTestedText => _selectedModel == null
            ? "—"
            : _selectedModel.Tested ? "TEST EDİLDİ" : "TEST EDİLMEDİ";

        public void Dispose()
        {
            _subscription?.Dispose();
        }

        protected override string GetActionTitle(string actionKey)
        {
            return "Katalog";
        }

        protected override IReadOnlyList<string> GetSteps(string actionKey)
        {
            return Steps("Katalog okunuyor...");
        }

        private void Reload()
        {
            var currentId = SelectedBrand?.Id;

            Brands.Clear();

            foreach (var brand in _catalog.Brands)
            {
                Brands.Add(brand);
            }

            SelectedBrand = Brands.FirstOrDefault(
                                b => string.Equals(b.Id, currentId, StringComparison.OrdinalIgnoreCase))
                            ?? Brands.FirstOrDefault();

            LogHub.Instance.Info(
                "Katalog yenilendi: " + Brands.Count + " marka", "KATALOG");
        }

        private void RefreshModels()
        {
            Models.Clear();

            if (SelectedBrand == null)
            {
                SelectedModel = null;
                return;
            }

            var search = (_search ?? string.Empty).Trim();

            foreach (var model in SelectedBrand.Models)
            {
                if (!MatchesPlatform(model))
                {
                    continue;
                }

                if (search.Length > 0 &&
                    model.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    model.Code.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    model.Codename.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                Models.Add(model);
            }

            SelectedModel = Models.FirstOrDefault();
        }

        private bool MatchesPlatform(CatalogModel model)
        {
            if (string.IsNullOrWhiteSpace(_platform) || _platform == "Tümü")
            {
                return true;
            }

            return FirstToken(model.Platform).Equals(_platform, StringComparison.OrdinalIgnoreCase);
        }

        private void OnPresence(DevicePresenceEvent e)
        {
            if (e?.Device == null)
            {
                return;
            }

            if (!e.Connected)
            {
                if (string.Equals(e.Device.Serial, _detectedSerial, StringComparison.OrdinalIgnoreCase))
                {
                    _detectedSerial = null;
                    DetectedText = "Cihaz ayrıldı";
                    MatchText = "—";
                    MatchConfidence = "—";
                    CommandManager.InvalidateRequerySuggested();
                }

                return;
            }

            if (e.Device.Mode != "ADB")
            {
                return;
            }

            _detectedSerial = e.Device.Serial;
            DetectedText = e.Device.Display + " · " + e.Device.State;
            CommandManager.InvalidateRequerySuggested();

            var match = _catalog.GetOverride(_detectedSerial)
                        ?? _catalog.Match(
                            e.Device.Model,
                            e.Device.Product,
                            null,
                            null);

            if (match.IsKnown)
            {
                MatchText = match.FullName;
                MatchConfidence = match.ConfidenceText + " · " + match.Source;

                if (match.Confidence >= KCTools.Models.MatchConfidence.High)
                {
                    SelectedBrand = Brands.FirstOrDefault(
                                        b => b.Id == match.Brand.Id)
                                    ?? SelectedBrand;
                    SelectedModel = Models.FirstOrDefault(
                                        m => m.Code == match.Model.Code)
                                    ?? match.Model;
                }
            }
            else
            {
                MatchText = "Katalogda bulunamadı — manuel seçin";
                MatchConfidence = "BİLİNMİYOR";
            }
        }

        private void Assign()
        {
            if (SelectedModel == null || string.IsNullOrWhiteSpace(_detectedSerial))
            {
                LogHub.Instance.Warn("Atama için bağlı cihaz ve seçili model gerekli.", "KATALOG");
                return;
            }

            if (_catalog.SetOverride(_detectedSerial, SelectedBrand.Id, SelectedModel.Code))
            {
                MatchText = SelectedModel.Name.StartsWith(SelectedBrand.Name, StringComparison.OrdinalIgnoreCase)
                    ? SelectedModel.Name
                    : SelectedBrand.Name + " " + SelectedModel.Name;
                MatchConfidence = "MANUEL · bu cihaz için kaydedildi";
                StatusText = "Model eşleşmesi kaydedildi";
            }
        }

        private static string FirstToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            var space = trimmed.IndexOf(' ');

            return space < 0 ? trimmed : trimmed.Substring(0, space);
        }
    }
}
