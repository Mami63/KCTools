using System;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Protocol;

namespace KCTools.Services
{
    public sealed class NvReadResult
    {
        public bool Success { get; set; }

        public byte Status { get; set; }

        public ushort Item { get; set; }

        public byte[] Data { get; set; } = Array.Empty<byte>();

        public string Error { get; set; }
    }

    public interface IQcdmFrameTransport : IDisposable
    {
        bool IsOpen { get; }

        Task OpenAsync(CancellationToken cancellationToken);

        Task WriteAsync(byte[] frame, CancellationToken cancellationToken);

        Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken);

        /// <summary>Çerçeveyi gönderir ve tek bir yanıt çerçevesi okur.</summary>
        Task<byte[]> SendAndReceiveAsync(byte[] frame, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Qualcomm DIAG NV istemcisi.
    ///
    /// Okuma: DIAG_NV_READ_F (0x26).
    /// Yazma: DIAG_NV_WRITE_F (0x27) — yalnızca
    /// <see cref="IsAuthorizedServiceModeEnabled"/> true iken çalışır ve
    /// yazmadan önce mevcut NV öğesini okuyup <see cref="LastBackup"/>'a alır.
    ///
    /// UYARI: NV yazımı cihaz kimliğini (IMEI vb.) değiştirebilir. Yalnızca
    /// yetkili servis ortamında, sahibi olunan cihazlarda kullanılmalıdır.
    /// </summary>
    public sealed class QcdmNvClient : IDisposable
    {
        public const byte NvReadCommand = 0x26;
        public const byte NvWriteCommand = 0x27;

        /// <summary>
        /// Yasal uyarı: bu sınıf yalnızca yetkili servis işlemleri içindir.
        /// </summary>
        public const string LegalDisclaimerNotice =
            "Bu yazılım yalnızca yetkili servis işlemleri için kullanılmalıdır. " +
            "FRP/hesap doğrulamasını, Knox korumasını veya Bootloader kilitlerini atlatan " +
            "hiçbir komut, exploit veya illegal yöntem bulunmamaktadır.";

        private readonly IQcdmFrameTransport _transport;

        public QcdmNvClient(IQcdmFrameTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>
        /// NV yazımını etkinleştirir. Varsayılan false; yetkili servis modu
        /// açıkça etkinleştirilmeden yazma reddedilir.
        /// </summary>
        public bool IsAuthorizedServiceModeEnabled { get; set; }

        /// <summary>Son yazma öncesi okunan NV yedeği.</summary>
        public NvReadResult LastBackup { get; private set; }

        public async Task<NvReadResult> ReadNvAsync(
            ushort item, CancellationToken cancellationToken = default)
        {
            await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

            var payload = new[]
            {
                (byte)(item & 0xFF),
                (byte)(item >> 8)
            };

            var request = QcdmFramer.Frame(NvReadCommand, payload);
            await _transport.WriteAsync(request, cancellationToken).ConfigureAwait(false);

            var frame = await _transport.ReadFrameAsync(cancellationToken).ConfigureAwait(false);

            if (frame == null)
            {
                return new NvReadResult { Item = item, Error = "QCDM yanıtı alınamadı." };
            }

            if (!QcdmFramer.TryDecode(frame, out var decoded, out var error))
            {
                return new NvReadResult
                {
                    Item = item,
                    Error = error ?? "QCDM çerçevesi çözülemedi."
                };
            }

            if (decoded.Command != NvReadCommand)
            {
                return new NvReadResult
                {
                    Item = item,
                    Error = "Beklenmeyen QCDM komutu: 0x" + decoded.Command.ToString("X2")
                };
            }

            if (decoded.Payload.Length < 1)
            {
                return new NvReadResult { Item = item, Error = "NV yanıtı boş." };
            }

            var status = decoded.Payload[0];
            var data = new byte[Math.Max(0, decoded.Payload.Length - 1)];
            Buffer.BlockCopy(decoded.Payload, 1, data, 0, data.Length);

            return new NvReadResult
            {
                Success = status == 0,
                Status = status,
                Item = item,
                Data = data,
                Error = status == 0 ? null : "NV okuma durumu: 0x" + status.ToString("X2")
            };
        }

        /// <summary>
        /// DIAG_NV_WRITE_F (0x27) ile NV öğesi yazar.
        /// Paket: 0x27 + ItemIDLow + ItemIDHigh + Data.
        /// Yazmadan hemen önce mevcut öğe okunur; okunamazsa yazma iptal edilir.
        /// </summary>
        public async Task WriteNvAsync(
            ushort item, byte[] data, CancellationToken cancellationToken = default)
        {
            if (!IsAuthorizedServiceModeEnabled)
            {
                throw new InvalidOperationException(
                    "NV yazma reddedildi: yetkili servis modu etkin değil. " +
                    "IsAuthorizedServiceModeEnabled = true yapın. " +
                    LegalDisclaimerNotice);
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length == 0)
            {
                throw new ArgumentException("NV verisi boş olamaz.", nameof(data));
            }

            await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

            // Yazma öncesi mevcut değeri yedekle.
            var backup = await ReadNvAsync(item, cancellationToken).ConfigureAwait(false);

            if (!backup.Success)
            {
                throw new InvalidOperationException(
                    "NV yazma öncesi yedek alınamadı: " + (backup.Error ?? "bilinmeyen hata"));
            }

            LastBackup = backup;

            var payload = new byte[2 + data.Length];
            payload[0] = (byte)(item & 0xFF);
            payload[1] = (byte)(item >> 8);
            Buffer.BlockCopy(data, 0, payload, 2, data.Length);

            var frame = QcdmFramer.Frame(NvWriteCommand, payload);
            var response = await _transport.SendAndReceiveAsync(frame, cancellationToken)
                .ConfigureAwait(false);

            if (response == null)
            {
                throw new InvalidOperationException("NV yazma yanıtı alınamadı.");
            }

            if (!QcdmFramer.TryDecode(response, out var decoded, out var error))
            {
                throw new InvalidOperationException(
                    "NV yazma yanıtı çözülemedi: " + (error ?? "bilinmeyen hata"));
            }

            if (decoded.Command != NvWriteCommand)
            {
                throw new InvalidOperationException(
                    "Beklenmeyen NV yazma yanıtı: 0x" + decoded.Command.ToString("X2"));
            }

            if (decoded.Payload.Length < 1)
            {
                throw new InvalidOperationException("NV yazma yanıtı boş.");
            }

            if (decoded.Payload[0] != 0)
            {
                throw new InvalidOperationException(
                    "NV yazma başarısız, durum: 0x" + decoded.Payload[0].ToString("X2"));
            }
        }

        private async Task EnsureOpenAsync(CancellationToken cancellationToken)
        {
            if (!_transport.IsOpen)
            {
                await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _transport.Dispose();
        }
    }
}
