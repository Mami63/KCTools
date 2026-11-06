using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Common;
using KCTools.Security;

namespace KCTools.Services
{
    public enum LicenseState
    {
        Unlicensed,
        Trial,
        Active,
        Offline,
        Expired
    }

    public sealed class LicenseActionResult
    {
        public bool Success { get; set; }

        public bool Offline { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Lisans yaşam döngüsü: HWID üretimi, sunucudan JWT alma, oturum doğrulama,
    /// kredi düşme ve çevrimdışı tolerans. JWT DPAPI ile şifrelenerek saklanır.
    /// </summary>
    public sealed class LicenseService : ObservableObject
    {
        private static readonly Regex KeyPattern = new Regex(
            @"^KC-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$", RegexOptions.Compiled);

        private readonly object _sync = new object();
        private readonly DateTime _trialEndsAt = DateTime.Today.AddDays(30);

        private LicenseApiClient _api;
        private StoredSession _session;

        private string _hwid = "—";
        private string _serverUrl = "https://localhost:5443";
        private LicenseState _state = LicenseState.Trial;
        private string _plan = "TRIAL";
        private int _credits;
        private DateTimeOffset? _expiresAt;
        private string _key = "—";
        private bool _isBusy;
        private bool _initialized;

        private LicenseService()
        {
        }

        public static LicenseService Instance { get; } = new LicenseService();

        /// <summary>Bu makinenin donanım kimliği (XXXX-XXXX-XXXX-XXXX).</summary>
        public string Hwid
        {
            get => _hwid;
            private set
            {
                if (Set(ref _hwid, value))
                {
                    NotifySummary();
                }
            }
        }

        /// <summary>Lisans API kök adresi (HTTPS).</summary>
        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                if (!Set(ref _serverUrl, value))
                {
                    return;
                }

                lock (_sync)
                {
                    _api?.Dispose();
                    _api = null;
                }
            }
        }

        public LicenseState State
        {
            get => _state;
            private set
            {
                if (Set(ref _state, value))
                {
                    NotifySummary();
                }
            }
        }

        public string Plan
        {
            get => _plan;
            private set
            {
                if (Set(ref _plan, value))
                {
                    NotifySummary();
                }
            }
        }

        public int Credits
        {
            get => _credits;
            private set
            {
                if (Set(ref _credits, value))
                {
                    NotifySummary();
                }
            }
        }

        public DateTimeOffset? ExpiresAt
        {
            get => _expiresAt;
            private set
            {
                if (Set(ref _expiresAt, value))
                {
                    NotifySummary();
                }
            }
        }

        public string Key
        {
            get => _key;
            private set
            {
                if (Set(ref _key, value))
                {
                    NotifySummary();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => Set(ref _isBusy, value);
        }

        public bool IsLicensed => State == LicenseState.Active || State == LicenseState.Offline;

        public int DaysLeft
        {
            get
            {
                if (IsLicensed && ExpiresAt.HasValue)
                {
                    return Math.Max(0, (ExpiresAt.Value - DateTimeOffset.UtcNow).Days);
                }

                return Math.Max(0, (_trialEndsAt - DateTime.Today).Days);
            }
        }

        /// <summary>Durum çubuğu ve ayarlar sayfası için kısa özet.</summary>
        public string Summary
        {
            get
            {
                switch (State)
                {
                    case LicenseState.Active:
                        return "PRO · " + Credits + " kredi";

                    case LicenseState.Offline:
                        return "Çevrimdışı · " + Credits + " kredi";

                    case LicenseState.Expired:
                        return "Süre doldu";

                    case LicenseState.Unlicensed:
                        return "Lisans yok";

                    default:
                        return "Deneme · " + DaysLeft + " gün";
                }
            }
        }

        public string Detail
        {
            get
            {
                if (IsLicensed)
                {
                    return Key + " · " +
                           (ExpiresAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—") +
                           " · plan " + Plan;
                }

                return "HWID " + Hwid;
            }
        }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case LicenseState.Active: return "AKTİF";
                    case LicenseState.Offline: return "ÇEVRİMDIŞI";
                    case LicenseState.Expired: return "SÜRE DOLDU";
                    case LicenseState.Unlicensed: return "LİSANS YOK";
                    default: return "DENEME";
                }
            }
        }

        /// <summary>HWID'yi hesaplar, kayıtlı oturumu yükler ve doğrular.</summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            await EnsureHwidAsync(cancellationToken).ConfigureAwait(true);

            lock (_sync)
            {
                _session = TokenStore.Load();
            }

            if (_session != null &&
                !string.Equals(_session.Hwid, Hwid, StringComparison.OrdinalIgnoreCase))
            {
                LogHub.Instance.Warn("Kayıtlı oturum bu makineye ait değil; temizlendi.", "LİSANS");
                TokenStore.Clear();
                _session = null;
            }

            if (_session != null)
            {
                Plan = string.IsNullOrWhiteSpace(_session.Plan) ? "PRO" : _session.Plan;
                Credits = _session.Credits;
                ExpiresAt = _session.ExpiresAt;
                Key = string.IsNullOrWhiteSpace(_session.Key) ? "—" : _session.Key;
                State = LicenseState.Offline;
            }

            LogHub.Instance.Info("HWID: " + Hwid + " · lisans durumu: " + StateText, "LİSANS");

            var result = await ValidateAsync(cancellationToken).ConfigureAwait(true);

            if (!result.Success && _session == null)
            {
                State = LicenseState.Trial;
            }
        }

        public async Task EnsureHwidAsync(CancellationToken cancellationToken = default)
        {
            if (Hwid != "—")
            {
                return;
            }

            try
            {
                Hwid = await Task.Run(() => HwidProvider.GetHwid(), cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("HWID üretilemedi: " + ex.Message, "LİSANS");
                Hwid = "UNKNOWN-HWID";
            }
        }

        /// <summary>Anahtarı sunucuda etkinleştirir, dönen JWT'yi saklar.</summary>
        public async Task<LicenseActionResult> ActivateAsync(
            string licenseKey,
            CancellationToken cancellationToken = default)
        {
            var normalized = (licenseKey ?? string.Empty).Trim().ToUpperInvariant();

            if (!KeyPattern.IsMatch(normalized))
            {
                LogHub.Instance.Warn("Lisans anahtarı biçimi geçersiz: " + normalized, "LİSANS");
                return new LicenseActionResult { Message = "Geçersiz biçim. Örnek: KC-AB12-CD34-EF56" };
            }

            await EnsureHwidAsync(cancellationToken).ConfigureAwait(true);

            IsBusy = true;

            try
            {
                var request = new ActivationRequest
                {
                    Hwid = Hwid,
                    LicenseKey = normalized,
                    AppVersion = AppSettings.Version,
                    MachineName = Environment.MachineName
                };

                var result = await EnsureApi().ActivateAsync(request, cancellationToken)
                    .ConfigureAwait(true);

                if (!result.Success || result.Data == null)
                {
                    var message = result.Offline
                        ? "Sunucuya ulaşılamadı: " + result.Error
                        : result.Error ?? "Aktivasyon başarısız.";

                    LogHub.Instance.Error("Aktivasyon başarısız: " + message, "LİSANS");
                    return new LicenseActionResult { Offline = result.Offline, Message = message };
                }

                var claims = JwtInspector.Read(result.Data.Token);

                _session = new StoredSession
                {
                    Token = result.Data.Token,
                    Hwid = Hwid,
                    Key = normalized,
                    Plan = result.Data.Plan ?? claims?.Plan ?? "PRO",
                    Credits = result.Data.Credits,
                    ExpiresAt = result.Data.ExpiresAt ?? claims?.ExpiresAt
                };

                TokenStore.Save(_session);

                Key = normalized;
                Plan = _session.Plan;
                Credits = _session.Credits;
                ExpiresAt = _session.ExpiresAt;
                State = LicenseState.Active;

                LogHub.Instance.Ok(
                    "Lisans etkinleştirildi: " + normalized + " · " + Plan + " · " +
                    Credits + " kredi", "LİSANS");

                return new LicenseActionResult
                {
                    Success = true,
                    Message = result.Data.Message ?? "Lisans etkinleştirildi."
                };
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Sunucudan oturum durumunu doğrular; ağ yoksa çevrimdışı moda düşer.</summary>
        public async Task<LicenseActionResult> ValidateAsync(
            CancellationToken cancellationToken = default)
        {
            StoredSession session;

            lock (_sync)
            {
                session = _session;
            }

            if (session == null)
            {
                State = LicenseState.Trial;
                return new LicenseActionResult { Message = "Kayıtlı lisans yok." };
            }

            IsBusy = true;

            try
            {
                var result = await EnsureApi().GetStatusAsync(session.Token, cancellationToken)
                    .ConfigureAwait(true);

                if (result.Success && result.Data != null)
                {
                    var claims = JwtInspector.Read(session.Token);

                    if (!string.IsNullOrWhiteSpace(result.Data.Hwid) &&
                        !string.Equals(result.Data.Hwid, Hwid, StringComparison.OrdinalIgnoreCase))
                    {
                        State = LicenseState.Expired;
                        LogHub.Instance.Error("Sunucu HWID uyuşmazlığı bildirdi.", "LİSANS");
                        return new LicenseActionResult { Message = "Lisans bu makineye ait değil." };
                    }

                    Plan = result.Data.Plan ?? claims?.Plan ?? Plan;
                    Credits = result.Data.Credits;
                    ExpiresAt = result.Data.ExpiresAt ?? claims?.ExpiresAt;
                    Key = session.Key ?? Key;
                    State = LicenseState.Active;

                    session.Credits = Credits;
                    session.Plan = Plan;
                    session.ExpiresAt = ExpiresAt;
                    TokenStore.Save(session);

                    LogHub.Instance.Ok(
                        "Lisans doğrulandı · " + Plan + " · " + Credits + " kredi", "LİSANS");

                    return new LicenseActionResult { Success = true, Message = "Lisans doğrulandı." };
                }

                if (result.StatusCode == 401 || result.StatusCode == 403 || result.StatusCode == 404)
                {
                    State = LicenseState.Expired;
                    LogHub.Instance.Warn("Oturum geçersiz: " + result.Error, "LİSANS");
                    return new LicenseActionResult { Message = result.Error ?? "Oturum geçersiz." };
                }

                // Sunucuya ulaşılamadı: süresi dolmamış oturumla çevrimdışı devam.
                if (!session.ExpiresAt.HasValue || session.ExpiresAt.Value > DateTimeOffset.UtcNow)
                {
                    State = LicenseState.Offline;
                    Plan = string.IsNullOrWhiteSpace(session.Plan) ? Plan : session.Plan;
                    Credits = session.Credits;
                    ExpiresAt = session.ExpiresAt;

                    LogHub.Instance.Warn(
                        "Sunucuya ulaşılamadı — çevrimdışı mod (" + (result.Error ?? "ağ hatası") + ")",
                        "LİSANS");

                    return new LicenseActionResult
                    {
                        Success = true,
                        Offline = true,
                        Message = "Çevrimdışı mod"
                    };
                }

                State = LicenseState.Expired;
                LogHub.Instance.Warn("Oturum süresi doldu.", "LİSANS");
                return new LicenseActionResult { Message = "Oturum süresi doldu." };
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Sunucuda kredi düşer (işlem başına). Başarısızsa false döner.</summary>
        public async Task<bool> ConsumeAsync(
            string operation,
            int amount = 1,
            CancellationToken cancellationToken = default)
        {
            StoredSession session;

            lock (_sync)
            {
                session = _session;
            }

            if (session == null)
            {
                LogHub.Instance.Warn("Kredi düşülemedi: etkin lisans yok.", "LİSANS");
                return false;
            }

            var result = await EnsureApi().ConsumeAsync(
                session.Token,
                new ConsumeRequest { Amount = amount, Operation = operation },
                cancellationToken).ConfigureAwait(true);

            if (!result.Success || result.Data == null)
            {
                LogHub.Instance.Warn(
                    "Kredi düşülemedi (" + operation + "): " + (result.Error ?? "bilinmeyen hata"),
                    "LİSANS");
                return false;
            }

            Credits = result.Data.Credits;
            session.Credits = Credits;
            TokenStore.Save(session);

            LogHub.Instance.Info(
                "Kredi -" + amount + " (" + operation + ") → " + Credits + " kaldı", "LİSANS");

            return true;
        }

        private LicenseApiClient EnsureApi()
        {
            lock (_sync)
            {
                return _api ?? (_api = new LicenseApiClient(ServerUrl));
            }
        }

        private void NotifySummary()
        {
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsLicensed));
            OnPropertyChanged(nameof(DaysLeft));
        }
    }
}
