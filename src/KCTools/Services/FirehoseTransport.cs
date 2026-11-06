using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using KCTools.Protocol;

namespace KCTools.Services
{
    /// <summary>
    /// Firehose taşıma katmanı. Gerçek cihazda USB bulk (WinUSB) veya seri port
    /// kullanılır; bu sürümde loopback simülatörü ile protokol uçtan uca gösterilir.
    /// </summary>
    public interface IFirehoseTransport : IDisposable
    {
        string Name { get; }

        bool IsOpen { get; }

        Task OpenAsync(CancellationToken cancellationToken);

        Task WriteAsync(byte[] data, CancellationToken cancellationToken);

        /// <summary>Bir sonraki metin parçasını döndürür; null ise akış kapalıdır.</summary>
        Task<string> ReadAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Cihazsız test için Firehose davranışını taklit eden taşıma: gelen XML komutunu
    /// ayrıştırır, &lt;read&gt; için desenli veri üretir, &lt;log&gt;/&lt;response&gt; döndürür.
    /// </summary>
    public sealed class LoopbackFirehoseTransport : IFirehoseTransport
    {
        private readonly Queue<string> _outbox = new Queue<string>();
        private readonly StringBuilder _inbox = new StringBuilder();
        private bool _open;
        private string _lastWriteFile = "—";

        public string Name => "Loopback (simülatör)";

        public bool IsOpen => _open;

        /// <summary>Simülasyonda tek okumada döndürülecek en fazla sektör.</summary>
        public int MaxSimulatedSectors { get; set; } = 8;

        public string LastWriteFile => _lastWriteFile;

        public Task OpenAsync(CancellationToken cancellationToken)
        {
            _open = true;

            LogHub.Instance.Info(
                "Loopback Firehose taşıması açıldı (cihaz yok, protokol simülasyonu).", "FIREHOSE");

            return Task.CompletedTask;
        }

        public Task WriteAsync(byte[] data, CancellationToken cancellationToken)
        {
            if (!_open)
            {
                throw new InvalidOperationException("Firehose taşıması açık değil.");
            }

            _inbox.Append(Encoding.UTF8.GetString(data));
            ProcessInbox();

            return Task.CompletedTask;
        }

        public Task<string> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_outbox.Count > 0 ? _outbox.Dequeue() : null);
        }

        public void Dispose()
        {
            _open = false;
            _outbox.Clear();
            _inbox.Clear();
        }

        private void ProcessInbox()
        {
            while (true)
            {
                var text = _inbox.ToString();
                var start = text.IndexOf('<');

                if (start < 0)
                {
                    _inbox.Clear();
                    return;
                }

                if (start > 0)
                {
                    _inbox.Remove(0, start);
                    text = _inbox.ToString();
                }

                if (text.StartsWith("<data>", StringComparison.OrdinalIgnoreCase))
                {
                    var end = text.IndexOf("</data>", StringComparison.OrdinalIgnoreCase);

                    if (end < 0)
                    {
                        return;
                    }

                    _inbox.Remove(0, end + 7);
                    _outbox.Enqueue(FirehoseXml.Response("ACK"));
                    continue;
                }

                var close = text.IndexOf("/>", StringComparison.Ordinal);

                if (close < 0)
                {
                    return;
                }

                var tag = text.Substring(0, close + 2);
                _inbox.Remove(0, close + 2);
                ProcessCommand(tag);
            }
        }

        private void ProcessCommand(string tag)
        {
            XElement root;

            try
            {
                root = XDocument.Parse(tag).Root;
            }
            catch (Exception)
            {
                _outbox.Enqueue(FirehoseXml.Response("NAK", false, "xml parse"));
                return;
            }

            if (root == null)
            {
                _outbox.Enqueue(FirehoseXml.Response("NAK", false, "empty"));
                return;
            }

            var name = root.Name.LocalName.ToLowerInvariant();

            switch (name)
            {
                case "nop":
                case "configure":
                case "power":
                case "patch":
                case "erase":
                    _outbox.Enqueue(FirehoseXml.Response("ACK"));
                    break;

                case "write":
                    _lastWriteFile = Attribute(root, "filename") ?? "—";
                    _outbox.Enqueue(FirehoseXml.Response("ACK"));
                    break;

                case "read":
                    EmitReadData(root);
                    break;

                default:
                    _outbox.Enqueue("<log value=\"Loopback: bilinmeyen komut " + name + "\" />");
                    _outbox.Enqueue(FirehoseXml.Response("NAK", false, "unknown command"));
                    break;
            }
        }

        private void EmitReadData(XElement root)
        {
            var startSector = ToLong(Attribute(root, "start_sector"), 0);
            var sectors = (int)Math.Clamp(ToLong(Attribute(root, "num_partitions"), 1), 1, MaxSimulatedSectors);
            var sectorSize = (int)ToLong(Attribute(root, "SECTOR_SIZE_IN_BYTES"), 512);
            var data = new byte[sectors * sectorSize];

            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)((startSector + i) & 0xFF);
            }

            _outbox.Enqueue(FirehoseXml.DataPacket(data));
            _outbox.Enqueue(FirehoseXml.Response("ACK"));
        }

        private static string Attribute(XElement element, string name)
        {
            foreach (var attribute in element.Attributes())
            {
                if (string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return attribute.Value;
                }
            }

            return null;
        }

        private static long ToLong(string value, long fallback)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }
    }
}
