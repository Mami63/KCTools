using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Services;

namespace KCTools.Services
{
    /// <summary>Qualcomm Diagnostics seri portu üzerinden QCDM çerçevesi taşıma.</summary>
    public sealed class SerialQcdmTransport : IQcdmFrameTransport
    {
        private readonly SerialPort _port;
        private readonly int _readTimeoutMs;

        public SerialQcdmTransport(string portName, int baudRate = 921600, int readTimeoutMs = 3000)
        {
            _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = readTimeoutMs,
                WriteTimeout = readTimeoutMs,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };

            _readTimeoutMs = readTimeoutMs;
        }

        public bool IsOpen => _port.IsOpen;

        public Task OpenAsync(CancellationToken cancellationToken)
        {
            if (!_port.IsOpen)
            {
                _port.Open();
            }

            return Task.CompletedTask;
        }

        public Task WriteAsync(byte[] frame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _port.Write(frame, 0, frame.Length);
            return Task.CompletedTask;
        }

        public async Task<byte[]> SendAndReceiveAsync(byte[] frame, CancellationToken cancellationToken)
        {
            await WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            return await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(_readTimeoutMs);
            var started = false;
            var buffer = new System.Collections.Generic.List<byte>(512);

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int value;

                try
                {
                    value = _port.ReadByte();
                }
                catch (TimeoutException)
                {
                    break;
                }

                if (value < 0)
                {
                    break;
                }

                var b = (byte)value;

                if (b == 0x7E)
                {
                    if (!started)
                    {
                        started = true;
                        buffer.Add(b);
                        continue;
                    }

                    buffer.Add(b);
                    return Task.FromResult(buffer.ToArray());
                }

                if (started)
                {
                    buffer.Add(b);
                }
            }

            return Task.FromResult(buffer.Count > 0 && started ? buffer.ToArray() : null);
        }

        public void Dispose()
        {
            if (_port.IsOpen)
            {
                _port.Close();
            }

            _port.Dispose();
        }
    }
}
