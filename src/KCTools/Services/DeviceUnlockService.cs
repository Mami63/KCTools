using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KCTools.Services
{
    public sealed class UnlockResult
    {
        public bool Success { get; set; }

        /// <summary>ROM, ADB giriş enjeksiyonunu engelliyor (INJECT_EVENTS).</summary>
        public bool InjectionBlocked { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Kilitli telefonu, kullanıcının girdiği PIN/şifre ile ADB üzerinden açar.
    /// PIN yalnızca kullanıcı isterse DPAPI ile şifrelenerek saklanır.
    /// </summary>
    public static class DeviceUnlockService
    {
        private static readonly Regex SizePattern =
            new Regex(@"(?:Physical|Override) size:\s*(\d+)x(\d+)", RegexOptions.Compiled);

        public static async Task<bool?> IsLockedAsync(
            string serial,
            CancellationToken cancellationToken = default)
        {
            var adb = new AdbClient { Serial = serial };
            var result = await adb.ShellAsync("dumpsys window", cancellationToken)
                .ConfigureAwait(false);

            var text = result?.CombinedOutput ?? string.Empty;

            if (text.IndexOf("mDreamingLockscreen=true", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (text.IndexOf("mDreamingLockscreen=false", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("mShowingLockscreen=false", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return null;
        }

        public static async Task<UnlockResult> UnlockAsync(
            string serial,
            string pin,
            CancellationToken cancellationToken = default)
        {
            var digits = OnlyDigits(pin);

            if (string.IsNullOrWhiteSpace(serial) || digits.Length == 0)
            {
                return new UnlockResult { Message = "Seri numarası veya PIN boş." };
            }

            try
            {
                var adb = new AdbClient { Serial = serial };

                var wake = await adb.ShellAsync("input keyevent 224", cancellationToken)
                    .ConfigureAwait(false);

                if (IsInjectionBlocked(wake))
                {
                    return Blocked();
                }

                await Task.Delay(600, cancellationToken);

                // Kilit ekranındaki "kaydır" adımı (bazı ROM'larda gerekli).
                await adb.ShellAsync("wm dismiss-keyguard", cancellationToken).ConfigureAwait(false);
                await Task.Delay(400, cancellationToken);

                var width = 1080;
                var height = 1920;
                var size = await adb.ShellAsync("wm size", cancellationToken).ConfigureAwait(false);
                var match = SizePattern.Match(size?.CombinedOutput ?? string.Empty);

                if (match.Success &&
                    int.TryParse(match.Groups[1].Value, out var w) &&
                    int.TryParse(match.Groups[2].Value, out var h))
                {
                    width = w;
                    height = h;
                }

                var swipe = await adb.ShellAsync(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "input swipe {0} {1} {0} {2} 300",
                        width / 2,
                        (int)(height * 0.85),
                        (int)(height * 0.35)),
                    cancellationToken).ConfigureAwait(false);

                if (IsInjectionBlocked(swipe))
                {
                    return Blocked();
                }

                await Task.Delay(700, cancellationToken);

                var text = await adb.ShellAsync("input text " + digits, cancellationToken)
                    .ConfigureAwait(false);

                if (IsInjectionBlocked(text))
                {
                    return Blocked();
                }

                await Task.Delay(250, cancellationToken);

                var enter = await adb.ShellAsync("input keyevent 66", cancellationToken)
                    .ConfigureAwait(false);

                if (IsInjectionBlocked(enter))
                {
                    return Blocked();
                }

                await Task.Delay(1600, cancellationToken);

                var locked = await IsLockedAsync(serial, cancellationToken).ConfigureAwait(false);

                if (locked == true)
                {
                    return new UnlockResult
                    {
                        Message = "Kilit açılamadı — PIN hatalı ya da ekran başka bir kilit istiyor olabilir."
                    };
                }

                return new UnlockResult { Success = true, Message = "Telefon kilidi açıldı." };
            }
            catch (OperationCanceledException)
            {
                return new UnlockResult { Message = "İşlem iptal edildi." };
            }
            catch (Exception ex)
            {
                return new UnlockResult { Message = "Kilit açma hatası: " + ex.Message };
            }
        }

        private static UnlockResult Blocked()
        {
            return new UnlockResult
            {
                InjectionBlocked = true,
                Message =
                    "Bu ROM ADB ile tuş/giriş enjeksiyonunu engelliyor (INJECT_EVENTS). " +
                    "MIUI: Geliştirici seçenekleri → 'USB hata ayıklama (Güvenlik ayarları)' açın. " +
                    "Diğer ROM'larda: USB hata ayıklama + güvenlik ayarı gerekir."
            };
        }

        private static bool IsInjectionBlocked(ProcessResult result)
        {
            var text = result?.CombinedOutput ?? string.Empty;

            return text.IndexOf("INJECT_EVENTS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("SecurityException", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string OnlyDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(value.Length);

            foreach (var c in value)
            {
                if (char.IsDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
