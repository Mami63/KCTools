using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KCTools.Services
{
    /// <summary>AT komut yanıtı.</summary>
    public sealed class AtResponse
    {
        public string Command { get; set; } = string.Empty;

        public bool Success { get; set; }

        public bool TimedOut { get; set; }

        public string Final { get; set; } = string.Empty;

        public List<string> Lines { get; set; } = new List<string>();

        public TimeSpan Duration { get; set; }

        public string Text => string.Join(" | ", Lines);

        public string FirstLine(string prefix)
        {
            return Lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>"+CSQ: 24,99" → "24,99"</summary>
        public string ValueAfterColon(string prefix)
        {
            var line = FirstLine(prefix);
            if (line == null)
            {
                return null;
            }

            var index = line.IndexOf(':');
            return index >= 0 ? line.Substring(index + 1).Trim() : line.Substring(prefix.Length).Trim();
        }
    }

    /// <summary>
    /// SerialPort üzerinden AT komutu gönderip yanıt toplayan async servis.
    /// Aynı anda tek komut çalışır; yanıt OK / ERROR / +CME ERROR / zaman aşımı ile kapanır.
    /// </summary>
    public sealed class AtCommandService : IDisposable
    {
        private readonly SerialTransport _transport = new SerialTransport();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _sync = new object();

        private TaskCompletionSource<AtResponse> _pending;
        private List<string> _lines;
        private string _command = string.Empty;

        public AtCommandService()
        {
            _transport.LineReceived += OnLine;
            _transport.Error += (_, message) => TransportError?.Invoke(this, message);
        }

        /// <summary>Her gelen satır (günlük için).</summary>
        public event EventHandler<string> Trace;

        /// <summary>Komut beklemiyorken gelen satır (URC / RING).</summary>
        public event EventHandler<string> Urc;

        public event EventHandler<string> TransportError;

        public bool IsOpen => _transport.IsOpen;

        public string PortName => _transport.PortName;

        public int BaudRate => _transport.BaudRate;

        public long BytesSent => _transport.TotalBytesSent;

        public long BytesReceived => _transport.TotalBytesReceived;

        /// <summary>Portu açar, "AT" ile el sıkışır ve yankıyı kapatır.</summary>
        public async Task<bool> OpenAsync(string portName, int baudRate, CancellationToken cancellationToken = default)
        {
            Close();

            try
            {
                _transport.Open(portName, baudRate);
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("Port açılamadı (" + portName + "): " + ex.Message, "MODEM");
                return false;
            }

            var probe = await SendAsync("AT", TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);

            if (!probe.Success)
            {
                LogHub.Instance.Error(
                    "AT el sıkışması başarısız (" + (probe.TimedOut ? "zaman aşımı" : probe.Final) + "). " +
                    "Yanlış port veya baud hızı olabilir.", "MODEM");

                Close();
                return false;
            }

            await SendAsync("ATE0", TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            await SendAsync("AT+CMEE=1", TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

            LogHub.Instance.Ok("AT portu hazır: " + portName + " @ " + baudRate, "MODEM");
            return true;
        }

        public void Close()
        {
            lock (_sync)
            {
                _pending = null;
                _lines = null;
            }

            _transport.Close();
        }

        public void Dispose()
        {
            Close();
            _gate.Dispose();
            _transport.Dispose();
        }

        /// <summary>Komut gönderir, yanıtı (OK/ERROR/zaman aşımı) döndürür.</summary>
        public async Task<AtResponse> SendAsync(
            string command,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var trimmed = (command ?? string.Empty).Trim();

            if (trimmed.Length == 0)
            {
                return new AtResponse { Final = "Boş komut" };
            }

            if (!IsOpen)
            {
                return new AtResponse { Command = trimmed, Final = "Port açık değil" };
            }

            if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return new AtResponse { Command = trimmed, Final = "Başka bir AT komutu sürüyor" };
            }

            try
            {
                var tcs = new TaskCompletionSource<AtResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                var lines = new List<string>();
                var startedAt = DateTime.UtcNow;

                lock (_sync)
                {
                    _pending = tcs;
                    _lines = lines;
                    _command = trimmed;
                }

                LogHub.Instance.Cmd(trimmed, "MODEM");

                try
                {
                    await _transport.WriteAsync(trimmed + "\r", cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return new AtResponse
                    {
                        Command = trimmed,
                        Final = "yazma hatası: " + ex.Message
                    };
                }

                var limit = timeout ?? TimeSpan.FromSeconds(5);
                AtResponse response;

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(limit, cancellationToken))
                    .ConfigureAwait(false);

                if (completed == tcs.Task)
                {
                    response = await tcs.Task.ConfigureAwait(false);
                }
                else
                {
                    lock (_sync)
                    {
                        _pending = null;
                    }

                    response = new AtResponse
                    {
                        Command = trimmed,
                        TimedOut = !cancellationToken.IsCancellationRequested,
                        Final = cancellationToken.IsCancellationRequested ? "iptal" : "zaman aşımı"
                    };
                }

                response.Command = trimmed;
                response.Duration = DateTime.UtcNow - startedAt;

                lock (_sync)
                {
                    response.Lines = new List<string>(lines);
                }

                return response;
            }
            finally
            {
                lock (_sync)
                {
                    _pending = null;
                    _lines = null;
                    _command = string.Empty;
                }

                _gate.Release();
            }
        }

        /// <summary>Yanıt beklemeden satır gönderir (diag modu komutları, A/ vb.).</summary>
        public async Task SendRawAsync(string text, int waitMilliseconds = 700, CancellationToken cancellationToken = default)
        {
            if (!IsOpen)
            {
                return;
            }

            LogHub.Instance.Cmd(text, "MODEM");

            try
            {
                await _transport.WriteAsync(text + "\r", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("AT yazma hatası: " + ex.Message, "MODEM");
                return;
            }

            if (waitMilliseconds > 0)
            {
                await Task.Delay(waitMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        private void OnLine(object sender, string line)
        {
            var text = line.Trim();

            if (text.Length == 0)
            {
                return;
            }

            Trace?.Invoke(this, text);
            LogHub.Instance.Output(text, "MODEM");

            TaskCompletionSource<AtResponse> pending;
            List<string> lines;
            string command;

            lock (_sync)
            {
                pending = _pending;
                lines = _lines;
                command = _command;
            }

            if (pending == null)
            {
                Urc?.Invoke(this, text);
                return;
            }

            if (!string.IsNullOrEmpty(command) &&
                text.Equals(command, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsFinalResponse(text, out var success))
            {
                var response = new AtResponse
                {
                    Command = command,
                    Success = success,
                    Final = text
                };

                lock (_sync)
                {
                    _pending = null;
                    response.Lines = lines != null ? new List<string>(lines) : new List<string>();
                }

                pending.TrySetResult(response);
                return;
            }

            lock (_sync)
            {
                lines?.Add(text);
            }
        }

        private static bool IsFinalResponse(string text, out bool success)
        {
            success = false;

            if (text.Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                success = true;
                return true;
            }

            if (text.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("+CME ERROR", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("+CMS ERROR", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("NO CARRIER", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("BUSY", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("NO ANSWER", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
