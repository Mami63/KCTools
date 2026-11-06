using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KCTools.Protocol
{
    public enum SaharaCommand : uint
    {
        Hello = 0x01,
        ReadData = 0x02,
        EndTransfer = 0x03,
        Done = 0x04,
        DoneResponse = 0x05,
        Reset = 0x06,
        Error = 0x07
    }

    public sealed class SaharaPacket
    {
        public uint Command { get; set; }

        public byte[] Payload { get; set; } = Array.Empty<byte>();

        public byte[] Raw { get; set; } = Array.Empty<byte>();

        public SaharaCommand CommandType => (SaharaCommand)Command;

        public static bool TryParse(
            byte[] buffer, int count, out SaharaPacket packet, out int consumed, out string error)
        {
            packet = null;
            consumed = 0;
            error = null;

            if (buffer == null || count < 8)
            {
                error = "Sahara paketi en az 8 bayt olmalı.";
                return false;
            }

            var command = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4));

            if (length < 8 || length > int.MaxValue)
            {
                error = "Geçersiz Sahara uzunluğu.";
                return false;
            }

            if (count < length)
            {
                error = "Sahara paketi henüz tamamlanmadı.";
                return false;
            }

            var payload = new byte[length - 8];
            Buffer.BlockCopy(buffer, 8, payload, 0, payload.Length);

            var raw = new byte[length];
            Buffer.BlockCopy(buffer, 0, raw, 0, raw.Length);

            packet = new SaharaPacket { Command = command, Payload = payload, Raw = raw };
            consumed = (int)length;
            return true;
        }
    }

    public sealed class SaharaHello
    {
        public uint Version { get; set; } = 2;

        public uint MinVersion { get; set; } = 1;

        public uint MaxCommandLength { get; set; } = 4096;

        public uint Mode { get; set; }

        public static SaharaHello FromPacket(SaharaPacket packet)
        {
            if (packet == null || packet.CommandType != SaharaCommand.Hello ||
                packet.Payload.Length < 16)
            {
                throw new InvalidOperationException("Sahara HELLO paketi geçersiz.");
            }

            return new SaharaHello
            {
                Version = BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload),
                MinVersion = BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(4)),
                MaxCommandLength = BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(8)),
                Mode = BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(12))
            };
        }

        public byte[] ToPacket()
        {
            var payload = new byte[40];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, Version);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), MinVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), MaxCommandLength);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), Mode);

            var packet = new byte[48];
            BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)SaharaCommand.Hello);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 48);
            Buffer.BlockCopy(payload, 0, packet, 8, payload.Length);

            return packet;
        }
    }

    public interface ISaharaTransport
    {
        Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken);

        Task WriteAsync(byte[] data, int offset, int count, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Sahara el sıkışması ve imaj transferi. USB/WinUSB taşıması
    /// <see cref="ISaharaTransport"/> ile dışarıdan verilir; protokol katmanı
    /// donanımdan bağımsız test edilebilir.
    /// </summary>
    public sealed class SaharaProtocol
    {
        private readonly ISaharaTransport _transport;

        public SaharaProtocol(ISaharaTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<SaharaHello> HandshakeAsync(
            uint hostVersion = 2,
            uint maxCommandLength = 4096,
            CancellationToken cancellationToken = default)
        {
            var helloPacket = await ReadPacketAsync(cancellationToken).ConfigureAwait(false);

            if (helloPacket.CommandType != SaharaCommand.Hello)
            {
                throw new InvalidOperationException(
                    "Beklenen Sahara HELLO değil: 0x" + helloPacket.Command.ToString("X2"));
            }

            var device = SaharaHello.FromPacket(helloPacket);

            var host = new SaharaHello
            {
                Version = Math.Min(device.Version, hostVersion),
                MinVersion = Math.Max(device.MinVersion, 1),
                MaxCommandLength = Math.Min(device.MaxCommandLength, maxCommandLength),
                Mode = 0
            };

            var response = host.ToPacket();
            await _transport.WriteAsync(response, 0, response.Length, cancellationToken)
                .ConfigureAwait(false);

            return device;
        }

        /// <summary>
        /// Cihazın READ_DATA isteklerine imaj dilimleriyle yanıt verir,
        /// END_TRANSFER sonrası DONE gönderir.
        /// </summary>
        public async Task UploadImageAsync(
            byte[] image,
            uint imageId,
            IProgress<int> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            long totalSent = 0;

            while (true)
            {
                var packet = await ReadPacketAsync(cancellationToken).ConfigureAwait(false);

                switch (packet.CommandType)
                {
                    case SaharaCommand.ReadData:
                        if (packet.Payload.Length < 12)
                        {
                            throw new InvalidOperationException("READ_DATA yükü kısa.");
                        }

                        var requestedId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload);
                        var offset = BinaryPrimitives.ReadUInt32LittleEndian(
                            packet.Payload.AsSpan(4));
                        var length = BinaryPrimitives.ReadUInt32LittleEndian(
                            packet.Payload.AsSpan(8));

                        if (requestedId != imageId)
                        {
                            throw new InvalidOperationException(
                                "Beklenmeyen image id: " + requestedId);
                        }

                        if (offset > image.Length)
                        {
                            throw new InvalidOperationException("READ_DATA offset imaj dışında.");
                        }

                        var count = (int)Math.Min(length, (uint)(image.Length - (int)offset));
                        var chunk = new byte[count];
                        Buffer.BlockCopy(image, (int)offset, chunk, 0, count);

                        await _transport.WriteAsync(chunk, 0, chunk.Length, cancellationToken)
                            .ConfigureAwait(false);

                        totalSent += count;
                        progress?.Report(
                            image.Length == 0 ? 100 : (int)(totalSent * 100 / image.Length));
                        break;

                    case SaharaCommand.EndTransfer:
                        var status = packet.Payload.Length >= 4
                            ? BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload)
                            : 0;

                        if (status != 0)
                        {
                            throw new InvalidOperationException(
                                "Sahara END_TRANSFER başarısız: 0x" + status.ToString("X8"));
                        }

                        var done = new byte[8];
                        BinaryPrimitives.WriteUInt32LittleEndian(done, (uint)SaharaCommand.Done);
                        BinaryPrimitives.WriteUInt32LittleEndian(done.AsSpan(4), 8);

                        await _transport.WriteAsync(done, 0, done.Length, cancellationToken)
                            .ConfigureAwait(false);

                        progress?.Report(100);
                        return;

                    case SaharaCommand.Error:
                        throw new InvalidOperationException("Cihaz Sahara ERROR döndürdü.");

                    default:
                        throw new InvalidOperationException(
                            "Beklenmeyen Sahara komutu: 0x" + packet.Command.ToString("X2"));
                }
            }
        }

        private async Task<SaharaPacket> ReadPacketAsync(CancellationToken cancellationToken)
        {
            var header = await ReadExactAsync(8, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));

            if (length < 8 || length > 16 * 1024 * 1024)
            {
                throw new InvalidOperationException("Sahara paket uzunluğu geçersiz.");
            }

            var buffer = new byte[length];
            Buffer.BlockCopy(header, 0, buffer, 0, 8);

            var remaining = (int)length - 8;

            if (remaining > 0)
            {
                var body = await ReadExactAsync(remaining, cancellationToken).ConfigureAwait(false);
                Buffer.BlockCopy(body, 0, buffer, 8, remaining);
            }

            if (!SaharaPacket.TryParse(buffer, buffer.Length, out var packet, out _, out var error))
            {
                throw new InvalidOperationException("Sahara paketi okunamadı: " + error);
            }

            return packet;
        }

        private async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken)
        {
            var buffer = new byte[count];
            var offset = 0;

            while (offset < count)
            {
                var read = await _transport.ReadAsync(
                        buffer, offset, count - offset, cancellationToken)
                    .ConfigureAwait(false);

                if (read <= 0)
                {
                    throw new InvalidOperationException("Sahara akışı kapandı.");
                }

                offset += read;
            }

            return buffer;
        }
    }

    /// <summary>Donanımsız protokol testi için Sahara cihaz simülatörü.</summary>
    public sealed class SaharaSimulatorTransport : ISaharaTransport
    {
        private readonly Queue<byte> _toHost = new Queue<byte>();
        private readonly List<byte> _received = new List<byte>();
        private readonly byte[] _image;
        private readonly int _chunkSize;
        private int _sent;
        private bool _helloWritten;

        public SaharaSimulatorTransport(byte[] image, int chunkSize = 1024)
        {
            _image = image ?? Array.Empty<byte>();
            _chunkSize = Math.Max(1, chunkSize);

            var hello = new SaharaHello
            {
                Version = 2,
                MinVersion = 2,
                MaxCommandLength = 4096,
                Mode = 0
            }.ToPacket();

            Enqueue(hello);
        }

        public byte[] ReceivedData => _received.ToArray();

        public int ReadRequests { get; private set; }

        public Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var copied = 0;

            while (copied < count && _toHost.Count > 0)
            {
                buffer[offset + copied] = _toHost.Dequeue();
                copied++;
            }

            return Task.FromResult(copied);
        }

        public Task WriteAsync(
            byte[] data, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_helloWritten)
            {
                _helloWritten = true;
                EnqueueReadRequests();
                return Task.CompletedTask;
            }

            // Host'un DONE paketi veri değildir; imaja eklenmez.
            if (count >= 8 &&
                BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) ==
                (uint)SaharaCommand.Done)
            {
                return Task.CompletedTask;
            }

            for (var i = 0; i < count; i++)
            {
                _received.Add(data[offset + i]);
            }

            _sent += count;

            if (_sent >= _image.Length && !HasPendingEnd())
            {
                Enqueue(EndTransfer(0));
            }

            return Task.CompletedTask;
        }

        private bool HasPendingEnd()
        {
            return _toHost.Count > 0;
        }

        private void EnqueueReadRequests()
        {
            var offset = 0;

            while (offset < _image.Length)
            {
                var length = Math.Min(_chunkSize, _image.Length - offset);
                Enqueue(ReadData((uint)offset, (uint)length));
                ReadRequests++;
                offset += length;
            }

            if (_image.Length == 0)
            {
                Enqueue(EndTransfer(0));
            }
        }

        private static byte[] ReadData(uint offset, uint length)
        {
            var packet = new byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)SaharaCommand.ReadData);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), offset);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), length);
            return packet;
        }

        private static byte[] EndTransfer(uint status)
        {
            var packet = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)SaharaCommand.EndTransfer);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 12);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), status);
            return packet;
        }

        private void Enqueue(byte[] data)
        {
            foreach (var value in data)
            {
                _toHost.Enqueue(value);
            }
        }
    }
}
