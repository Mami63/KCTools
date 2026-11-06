using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace KCTools.Protocol
{
    public interface IMtkBromTransport : IDisposable
    {
        Task OpenAsync(CancellationToken cancellationToken);

        Task WriteAsync(byte[] data, CancellationToken cancellationToken);

        Task<byte[]> ReadAsync(int count, CancellationToken cancellationToken);
    }

    public sealed class MtkDaUploadResult
    {
        public bool Success { get; set; }

        public int Blocks { get; set; }

        public int Bytes { get; set; }

        public string Error { get; set; }
    }

    /// <summary>
    /// MTK BROM DA yükleme iskeleti. El sıkışma 0xA0 → 0x5A ve DA bloklarının
    /// adres+uzunluk+checksum ile gönderimi uygulanır. Gerçek cihaz çerçevelemesi
    /// modele göre değişebilir; donanım doğrulaması yapılmadan üretimde kullanılmamalıdır.
    /// Bootloader kilidi bypassı bu sınıfın kapsamı dışındadır.
    /// </summary>
    public sealed class MtkBromEngine : IDisposable
    {
        public const byte HandshakeRequest = 0xA0;
        public const byte HandshakeResponse = 0x5A;

        private readonly IMtkBromTransport _transport;

        public MtkBromEngine(IMtkBromTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<bool> HandshakeAsync(CancellationToken cancellationToken = default)
        {
            await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
            await _transport.WriteAsync(new[] { HandshakeRequest }, cancellationToken)
                .ConfigureAwait(false);

            var response = await _transport.ReadAsync(1, cancellationToken)
                .ConfigureAwait(false);

            return response != null && response.Length == 1 &&
                   response[0] == HandshakeResponse;
        }

        public async Task<MtkDaUploadResult> UploadDaAsync(
            byte[] da, IProgress<int> progress = null, CancellationToken cancellationToken = default)
        {
            if (da == null || da.Length == 0)
            {
                return new MtkDaUploadResult { Error = "DA verisi boş." };
            }

            if (!await HandshakeAsync(cancellationToken).ConfigureAwait(false))
            {
                return new MtkDaUploadResult
                {
                    Error = "BROM el sıkışması başarısız (0x5A alınamadı)."
                };
            }

            var plan = MtkBromTools.PlanUpload(da);
            var sent = 0;

            foreach (var block in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var header = new byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)block.Address);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)block.Length);

                var chunk = new byte[block.Length];
                Buffer.BlockCopy(da, (int)block.Offset, chunk, 0, block.Length);

                var checksum = new byte[2];
                BinaryPrimitives.WriteUInt16LittleEndian(checksum, block.Checksum);

                await _transport.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await _transport.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                await _transport.WriteAsync(checksum, cancellationToken).ConfigureAwait(false);

                var ack = await _transport.ReadAsync(1, cancellationToken).ConfigureAwait(false);

                if (ack == null || ack.Length != 1 || ack[0] != HandshakeResponse)
                {
                    return new MtkDaUploadResult
                    {
                        Blocks = block.Index,
                        Bytes = sent,
                        Error = "DA bloğu " + block.Index + " onaylanmadı."
                    };
                }

                sent += block.Length;
                progress?.Report((int)(sent * 100L / da.Length));
            }

            return new MtkDaUploadResult
            {
                Success = true,
                Blocks = plan.Count,
                Bytes = sent
            };
        }

        public void Dispose()
        {
            _transport.Dispose();
        }
    }

    /// <summary>Donanımsız test için BROM taşıma simülatörü.</summary>
    public sealed class MtkBromSimulatorTransport : IMtkBromTransport
    {
        private readonly System.Collections.Generic.Queue<byte> _responses =
            new System.Collections.Generic.Queue<byte>();

        private readonly System.Collections.Generic.List<byte> _received =
            new System.Collections.Generic.List<byte>();

        public byte[] Received => _received.ToArray();

        public int Blocks { get; private set; }

        public Task OpenAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task WriteAsync(byte[] data, CancellationToken cancellationToken)
        {
            _received.AddRange(data);

            if (data.Length == 1 && data[0] == MtkBromEngine.HandshakeRequest)
            {
                _responses.Enqueue(MtkBromEngine.HandshakeResponse);
            }
            else if (data.Length == 8 || data.Length == 2)
            {
                // header/checksum parçaları; ack veri yazımı tamamlanınca verilir
            }
            else
            {
                Blocks++;
                _responses.Enqueue(MtkBromEngine.HandshakeResponse);
            }

            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAsync(int count, CancellationToken cancellationToken)
        {
            var result = new byte[Math.Min(count, _responses.Count)];

            for (var i = 0; i < result.Length; i++)
            {
                result[i] = _responses.Dequeue();
            }

            return Task.FromResult(result);
        }

        public void Dispose()
        {
        }
    }
}
