using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KCTools.Services
{
    /// <summary>
    /// SerialPort üzerinde ince bir taşıma katmanı: açma/kapama, satır ve ham bayt
    /// olayları, iş parçacığı güvenli yazma. Okuma arka plan görevinde yapılır.
    /// </summary>
    public sealed class SerialTransport : IDisposable
    {
        private readonly object _writeLock = new object();
        private readonly StringBuilder _lineBuffer = new StringBuilder();

        private SerialPort _port;
        private CancellationTokenSource _readCts;
        private Task _readTask;
        private long _bytesSent;
        private long _bytesReceived;

        public event EventHandler<string> LineReceived;

        public event EventHandler<byte[]> BytesReceived;

        public event EventHandler<string> Error;

        public bool IsOpen => _port != null && _port.IsOpen;

        public string PortName { get; private set; }

        public int BaudRate { get; private set; }

        public long TotalBytesSent => Interlocked.Read(ref _bytesSent);

        public long TotalBytesReceived => Interlocked.Read(ref _bytesReceived);

        public void Open(
            string portName,
            int baudRate,
            int dataBits = 8,
            Parity parity = Parity.None,
            StopBits stopBits = StopBits.One,
            Handshake handshake = Handshake.None,
            bool dtr = true,
            bool rts = true)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new ArgumentException("Port adı boş.", nameof(portName));
            }

            Close();

            var port = new SerialPort(portName.Trim(), baudRate, parity, dataBits, stopBits)
            {
                Handshake = handshake,
                ReadTimeout = 150,
                WriteTimeout = 3000,
                DtrEnable = dtr,
                RtsEnable = rts,
                NewLine = "\r",
                Encoding = Encoding.ASCII
            };

            try
            {
                port.Open();
            }
            catch (Exception)
            {
                try
                {
                    port.Dispose();
                }
                catch (Exception)
                {
                    // yok sayılır
                }

                throw;
            }

            _port = port;
            PortName = portName.Trim();
            BaudRate = baudRate;
            _lineBuffer.Clear();

            _readCts = new CancellationTokenSource();
            var readToken = _readCts.Token;
            _readTask = Task.Run(() => ReadLoop(readToken));
        }

        public void Close()
        {
            try
            {
                _readCts?.Cancel();
            }
            catch (Exception)
            {
                // yok sayılır
            }

            try
            {
                _port?.Close();
            }
            catch (Exception)
            {
                // yok sayılır
            }

            try
            {
                _readTask?.Wait(400);
            }
            catch (Exception)
            {
                // yok sayılır
            }

            _readTask = null;

            try
            {
                _port?.Dispose();
            }
            catch (Exception)
            {
                // yok sayılır
            }

            _port = null;

            try
            {
                _readCts?.Dispose();
            }
            catch (Exception)
            {
                // yok sayılır
            }

            _readCts = null;
            _lineBuffer.Clear();
        }

        public void Write(string text)
        {
            if (text == null)
            {
                return;
            }

            Write(Encoding.ASCII.GetBytes(text));
        }

        public void Write(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            var port = _port;

            if (port == null || !port.IsOpen)
            {
                throw new InvalidOperationException("Port açık değil.");
            }

            lock (_writeLock)
            {
                port.BaseStream.Write(data, 0, data.Length);
                port.BaseStream.Flush();
            }

            Interlocked.Add(ref _bytesSent, data.Length);
        }

        public Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Write(data), cancellationToken);
        }

        public Task WriteAsync(string text, CancellationToken cancellationToken = default)
        {
            return WriteAsync(Encoding.ASCII.GetBytes(text ?? string.Empty), cancellationToken);
        }

        public void Dispose()
        {
            Close();
        }

        private void ReadLoop(CancellationToken token)
        {
            var buffer = new byte[512];
            var port = _port;

            while (!token.IsCancellationRequested && port != null)
            {
                try
                {
                    var read = port.BaseStream.Read(buffer, 0, buffer.Length);

                    if (read <= 0)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    Interlocked.Add(ref _bytesReceived, read);

                    var chunk = new byte[read];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, read);

                    BytesReceived?.Invoke(this, chunk);
                    AppendLines(chunk);
                }
                catch (TimeoutException)
                {
                    // ReadTimeout doldu; döngü devam eder.
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Error?.Invoke(this, ex.Message);
                    }

                    break;
                }
            }
        }

        private void AppendLines(byte[] chunk)
        {
            var completed = new System.Collections.Generic.List<string>();

            lock (_lineBuffer)
            {
                foreach (var b in chunk)
                {
                    if (b == (byte)'\r' || b == (byte)'\n')
                    {
                        if (_lineBuffer.Length > 0)
                        {
                            completed.Add(_lineBuffer.ToString());
                            _lineBuffer.Clear();
                        }

                        continue;
                    }

                    _lineBuffer.Append(b < 32 ? ' ' : (char)b);

                    if (_lineBuffer.Length > 8192)
                    {
                        _lineBuffer.Clear();
                    }
                }
            }

            foreach (var line in completed)
            {
                var trimmed = line.Trim();

                if (trimmed.Length > 0)
                {
                    LineReceived?.Invoke(this, trimmed);
                }
            }
        }
    }
}
