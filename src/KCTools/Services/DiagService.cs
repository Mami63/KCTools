using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Common;

namespace KCTools.Services
{
    /// <summary>
    /// Qualcomm Diagnostics (DM) portu için ham taşıma katmanı: port açma, hex gönderme,
    /// gelen baytları hex olarak izleme ve AT komutlarıyla diag moduna geçiş denemesi.
    /// QCDM çerçevelemesi (0x7E / CRC) sonraki parçada eklenecek.
    /// </summary>
    public sealed class DiagService : IDisposable
    {
        private readonly SerialTransport _transport = new SerialTransport();

        public DiagService()
        {
            _transport.BytesReceived += OnBytes;
            _transport.LineReceived += (_, line) => Note?.Invoke(this, line);
            _transport.Error += (_, message) => LogHub.Instance.Warn("Diag port hatası: " + message, "DIAG");
        }

        public event EventHandler<string> HexReceived;

        public event EventHandler<string> Note;

        public bool IsOpen => _transport.IsOpen;

        public string PortName => _transport.PortName;

        public int BaudRate => _transport.BaudRate;

        public long BytesSent => _transport.TotalBytesSent;

        public long BytesReceived => _transport.TotalBytesReceived;

        /// <summary>Gelen baytların hex olarak günlüğe yazılması.</summary>
        public bool MonitorEnabled { get; set; } = true;

        public void Open(string portName, int baudRate = 921600)
        {
            Close();

            _transport.Open(
                portName,
                baudRate,
                dataBits: 8,
                parity: Parity.None,
                stopBits: StopBits.One,
                handshake: Handshake.None,
                dtr: false,
                rts: false);

            LogHub.Instance.Ok("Diag portu açıldı: " + portName + " @ " + baudRate, "DIAG");
        }

        public void Close()
        {
            if (_transport.IsOpen)
            {
                _transport.Close();
                LogHub.Instance.Info("Diag portu kapatıldı.", "DIAG");
            }
        }

        public async Task<int> SendHexAsync(string hexText, CancellationToken cancellationToken = default)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Diag portu açık değil.");
            }

            var bytes = Hex.Parse(hexText);

            if (bytes.Length == 0)
            {
                throw new ArgumentException("Geçersiz hex verisi.");
            }

            LogHub.Instance.Cmd("TX " + Hex.ToHex(bytes), "DIAG");
            await _transport.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            return bytes.Length;
        }

        /// <summary>
        /// AT portu üzerinden diag modunu açmayı dener. Cihaz/üretici desteğine göre
        /// komutlardan biri OK dönebilir; kabul edilen komutu döndürür.
        /// </summary>
        public async Task<string> TryEnterDiagModeAsync(
            AtCommandService at,
            CancellationToken cancellationToken = default)
        {
            if (at == null || !at.IsOpen)
            {
                return null;
            }

            var commands = new[]
            {
                "AT+QCDM", "AT$QCDMG", "AT+QCDM=1", "AT+DIAG=1", "AT#DIAG", "AT+QCDM?"
            };

            foreach (var command in commands)
            {
                var response = await at.SendAsync(command, TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);

                if (response.Success)
                {
                    LogHub.Instance.Ok("Diag modu komutu kabul edildi: " + command, "DIAG");
                    return command;
                }
            }

            LogHub.Instance.Warn(
                "AT ile diag modu açılamadı. Port listesini yenileyip 'Qualcomm Diagnostics' " +
                "portunu ayrı seçin (genellikle 921600 baud).", "DIAG");

            return null;
        }

        public void Dispose()
        {
            _transport.Dispose();
        }

        private void OnBytes(object sender, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            var text = Hex.ToHex(data);
            HexReceived?.Invoke(this, text);

            if (MonitorEnabled)
            {
                LogHub.Instance.Output("RX " + text, "DIAG");
            }
        }
    }
}
