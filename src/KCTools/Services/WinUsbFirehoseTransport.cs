using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Protocol;

namespace KCTools.Services
{
    /// <summary>WinUSB Bulk üzerinden Firehose XML/hex akışı.</summary>
    public sealed class WinUsbFirehoseTransport : IFirehoseTransport
    {
        private readonly WinUsbSession _session;

        public WinUsbFirehoseTransport(WinUsbSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public string Name => "WinUSB (VID_05C6/PID_9008)";

        public bool IsOpen => true;

        public Task OpenAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task WriteAsync(byte[] data, CancellationToken cancellationToken)
        {
            return Task.Run(() => _session.Write(data, 0, data.Length), cancellationToken);
        }

        public Task<string> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var buffer = new byte[4096];
                var read = _session.Read(buffer, 0, buffer.Length);

                if (read <= 0)
                {
                    return (string)null;
                }

                return Encoding.UTF8.GetString(buffer, 0, read);
            }, cancellationToken);
        }

        public void Dispose()
        {
            // Oturum WinUsbEdlDevice tarafından yönetilir.
        }
    }

    /// <summary>Sahara aşaması için WinUSB bulk taşıması.</summary>
    public sealed class WinUsbSaharaTransport : ISaharaTransport
    {
        private readonly WinUsbSession _session;

        public WinUsbSaharaTransport(WinUsbSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.Run(() => _session.Read(buffer, offset, count), cancellationToken);
        }

        public Task WriteAsync(
            byte[] data, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.Run(() => _session.Write(data, offset, count), cancellationToken);
        }

        public void Dispose()
        {
            // Oturum sahibi tarafından kapatılır.
        }
    }

    /// <summary>Aynı WinUSB oturumunu Sahara ve Firehose aşamaları için paylaştırır.</summary>
    public sealed class WinUsbEdlDevice : IDisposable
    {
        private WinUsbEdlDevice(
            WinUsbSession session, WinUsbSaharaTransport sahara,
            WinUsbFirehoseTransport firehose)
        {
            Session = session;
            Sahara = sahara;
            Firehose = firehose;
        }

        public WinUsbSession Session { get; }

        public WinUsbSaharaTransport Sahara { get; }

        public WinUsbFirehoseTransport Firehose { get; }

        public static WinUsbEdlDevice Open(
            ushort vendorId = 0x05C6, ushort productId = 0x9008)
        {
            var session = WinUsbSession.Open(vendorId, productId);

            return new WinUsbEdlDevice(
                session,
                new WinUsbSaharaTransport(session),
                new WinUsbFirehoseTransport(session));
        }

        public void Dispose()
        {
            Session.Dispose();
        }
    }
}
