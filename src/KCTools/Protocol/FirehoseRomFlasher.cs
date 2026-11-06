using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Services;

namespace KCTools.Protocol
{
    public sealed class FlashReport
    {
        public bool Success { get; set; }

        public int FilesWritten { get; set; }

        public long BytesWritten { get; set; }

        public List<string> Skipped { get; } = new();

        public string Error { get; set; }
    }

    /// <summary>
    /// rawprogram planını Firehose istemcisine besler: configure → dosyaları
    /// sırayla yazar. Eksik/boş dosyalar atlanır; sparse dosyalar olduğu gibi yazılır.
    /// </summary>
    public sealed class FirehoseRomFlasher
    {
        private readonly FirehoseClient _client;

        public FirehoseRomFlasher(FirehoseClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<FlashReport> FlashAsync(
            string rawProgramXml,
            string romDirectory,
            string storageType = "ufs",
            IProgress<int> progress = null,
            CancellationToken cancellationToken = default)
        {
            var report = new FlashReport();
            var plan = RawProgramParser.BuildPlan(rawProgramXml, romDirectory);

            report.Skipped.AddRange(plan.Skipped);

            if (plan.Files.Count == 0)
            {
                report.Error = "Yazılacak dosya bulunamadı.";
                return report;
            }

            await _client.OpenAsync(cancellationToken).ConfigureAwait(false);

            var configure = await _client.ConfigureAsync(storageType, cancellationToken)
                .ConfigureAwait(false);

            if (!configure.Success)
            {
                report.Error = "Firehose configure başarısız: " + configure.Response?.Value;
                return report;
            }

            var total = 0L;

            foreach (var file in plan.Files)
            {
                total += file.Length;
            }

            var written = 0L;

            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var current = file;
                var result = await _client.WriteAsync(
                    current.FullPath,
                    current.Entry.PhysicalPartition,
                    current.Entry.StartSector,
                    value =>
                    {
                        var aggregate = total == 0
                            ? 100
                            : (int)((written + current.Length * (long)value / 100L) * 100L / total);

                        progress?.Report(Math.Clamp(aggregate, 0, 100));
                    },
                    cancellationToken).ConfigureAwait(false);

                if (!result.Success)
                {
                    report.Error = "Firehose yazma başarısız: " +
                                   current.Entry.Filename + " · " + result.Response?.Value;
                    report.FilesWritten = report.FilesWritten;
                    report.BytesWritten = written;
                    return report;
                }

                written += current.Length;
                report.FilesWritten++;
                report.BytesWritten = written;
                progress?.Report(total == 0 ? 100 : (int)(written * 100L / total));
            }

            report.Success = true;
            progress?.Report(100);
            return report;
        }
    }
}
