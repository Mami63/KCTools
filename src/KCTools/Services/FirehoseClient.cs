using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Common;
using KCTools.Protocol;

namespace KCTools.Services
{
    /// <summary>Firehose komut sonucu (yanıt + varsa veri).</summary>
    public sealed class FirehoseResult
    {
        public FirehoseResponse Response { get; set; } = new FirehoseResponse();

        public byte[] Data { get; set; } = Array.Empty<byte>();

        public bool Success => Response != null && Response.IsAck;
    }

    /// <summary>
    /// Firehose istemcisi: XML komutlarını taşımaya yazar, yanıtı (&lt;response&gt;)
    /// ve veri paketlerini (&lt;data&gt;) ayrıştırır; yazma akışını sektör hizalı
    /// parçalara böler ve ilerleme bildirir.
    /// </summary>
    public sealed class FirehoseClient : IDisposable
    {
        private readonly IFirehoseTransport _transport;
        private readonly FirehosePacketParser _parser = new FirehosePacketParser();

        public FirehoseClient(IFirehoseTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public event EventHandler<FirehoseResponse> ResponseReceived;

        public event EventHandler<string> LogReceived;

        public string TransportName => _transport.Name;

        public int SectorSize { get; set; } = 512;

        public int MaxPayloadBytes { get; set; } = 128 * 1024;

        public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(20);

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            return _transport.OpenAsync(cancellationToken);
        }

        public Task<FirehoseResult> NopAsync(CancellationToken cancellationToken = default)
        {
            return SendAsync(FirehoseXml.Nop(), cancellationToken);
        }

        public Task<FirehoseResult> ConfigureAsync(
            string storageType = "ufs",
            CancellationToken cancellationToken = default)
        {
            return SendAsync(
                FirehoseXml.Configure(storageType, MaxPayloadBytes), cancellationToken);
        }

        public Task<FirehoseResult> ReadAsync(
            string fileName,
            int physicalPartition,
            long startSector,
            int sectorCount,
            CancellationToken cancellationToken = default)
        {
            return SendAsync(
                FirehoseXml.Read(fileName, physicalPartition, startSector, SectorSize, sectorCount),
                cancellationToken);
        }

        public Task<FirehoseResult> EraseAsync(
            int physicalPartition,
            long startSector,
            int sectorCount,
            CancellationToken cancellationToken = default)
        {
            return SendAsync(
                FirehoseXml.Erase(physicalPartition, startSector, SectorSize, sectorCount),
                cancellationToken);
        }

        /// <summary>Yalnızca &lt;write&gt; komutunu gönderir (veri akışı ayrıca yapılır).</summary>
        public Task<FirehoseResult> WriteHeaderAsync(
            string fileName,
            int physicalPartition,
            long startSector,
            CancellationToken cancellationToken = default)
        {
            return SendAsync(
                FirehoseXml.Write(fileName, physicalPartition, startSector, SectorSize),
                cancellationToken);
        }

        /// <summary>Dosyayı sektör hizalı hex parçalar halinde yazar ve ilerleme bildirir.</summary>
        public async Task<FirehoseResult> WriteAsync(
            string filePath,
            int physicalPartition,
            long startSector,
            Action<int> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("Yazılacak dosya bulunamadı.", filePath);
            }

            var data = File.ReadAllBytes(filePath);
            var fileName = Path.GetFileName(filePath);

            var header = await WriteHeaderAsync(
                fileName, physicalPartition, startSector, cancellationToken).ConfigureAwait(false);

            if (!header.Success)
            {
                return header;
            }

            var chunks = FirehoseSectors.Chunk(data, SectorSize, MaxPayloadBytes, startSector);
            var sent = 0;

            LogHub.Instance.Info(
                data.Length.ToString("N0") + " bayt · " + chunks.Count + " sektör hizalı parça", "FIREHOSE");

            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var packet = FirehoseXml.DataPacket(chunk.Data);
                var result = await SendAsync(packet, cancellationToken).ConfigureAwait(false);

                if (!result.Success)
                {
                    return result;
                }

                sent += chunk.OriginalLength;
                progress?.Invoke(data.Length == 0 ? 100 : (int)(sent * 100.0 / data.Length));
            }

            return new FirehoseResult
            {
                Response = new FirehoseResponse { Value = "ACK" }
            };
        }

        public void Dispose()
        {
            _transport.Dispose();
        }

        private async Task<FirehoseResult> SendAsync(string xml, CancellationToken cancellationToken)
        {
            if (xml.StartsWith("<data>", StringComparison.OrdinalIgnoreCase))
            {
                LogHub.Instance.Cmd("<data> " + (xml.Length - 13) + " hex karakter", "FIREHOSE");
            }
            else
            {
                LogHub.Instance.Cmd(xml, "FIREHOSE");
            }

            await _transport.WriteAsync(Encoding.UTF8.GetBytes(xml), cancellationToken)
                .ConfigureAwait(false);

            return await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<FirehoseResult> ReadResponseAsync(CancellationToken cancellationToken)
        {
            var data = new List<byte>();

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ResponseTimeout);

                try
                {
                    while (true)
                    {
                        var chunk = await _transport.ReadAsync(timeout.Token).ConfigureAwait(false);

                        if (chunk == null)
                        {
                            break;
                        }

                        foreach (var firehoseEvent in _parser.Feed(chunk))
                        {
                            if (firehoseEvent is FirehoseLogEvent logEvent)
                            {
                                LogReceived?.Invoke(this, logEvent.Value);
                                LogHub.Instance.Output("log: " + logEvent.Value, "FIREHOSE");
                            }
                            else if (firehoseEvent is FirehoseDataEvent dataEvent)
                            {
                                data.AddRange(dataEvent.Data);
                            }
                            else if (firehoseEvent is FirehoseResponseEvent responseEvent)
                            {
                                ResponseReceived?.Invoke(this, responseEvent.Response);

                                return new FirehoseResult
                                {
                                    Response = responseEvent.Response,
                                    Data = data.ToArray()
                                };
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        "Firehose yanıtı zaman aşımına uğradı (" +
                        ResponseTimeout.TotalSeconds.ToString("0") + " sn).");
                }
            }

            throw new TimeoutException("Firehose taşıması kapandı; yanıt alınamadı.");
        }
    }
}
