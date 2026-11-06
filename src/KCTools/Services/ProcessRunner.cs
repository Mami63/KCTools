using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KCTools.Services;

namespace KCTools.Services
{
    /// <summary>Bir dış sürecin sonucu.</summary>
    public sealed class ProcessResult
    {
        public string FileName { get; set; }

        public string Arguments { get; set; }

        public int ExitCode { get; set; }

        public string StandardOutput { get; set; } = string.Empty;

        public string StandardError { get; set; } = string.Empty;

        public TimeSpan Duration { get; set; }

        public bool TimedOut { get; set; }

        public bool Canceled { get; set; }

        public bool Success => !TimedOut && !Canceled && ExitCode == 0;

        public string CombinedOutput =>
            ((StandardOutput ?? string.Empty) + "\n" + (StandardError ?? string.Empty)).Trim();

        public override string ToString() =>
            Path.GetFileName(FileName ?? "?") + " " + Arguments + " → çıkış " + ExitCode;
    }

    /// <summary>
    /// adb.exe / fastboot.exe gibi araçları Process ile çalıştırır,
    /// stdout/stderr satırlarını canlı olarak günlük paneline aktarır.
    /// </summary>
    public sealed class ProcessRunner
    {
        /// <summary>Her stdout satırı (UI iş parçacığında değildir).</summary>
        public event EventHandler<string> OutputLine;

        /// <summary>Her stderr satırı.</summary>
        public event EventHandler<string> ErrorLine;

        public async Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null,
            string workingDirectory = null,
            bool echoCommand = true,
            bool logOutput = true,
            string logSource = null,
            Action<string> onLine = null,
            int maxCapturedChars = 400000)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Çalıştırılacak dosya belirtilmedi.", nameof(fileName));
            }

            var argumentList = arguments?.ToList() ?? new List<string>();
            var source = string.IsNullOrWhiteSpace(logSource) ? "SÜREÇ" : logSource;
            var display = Path.GetFileName(fileName);
            if (argumentList.Count > 0)
            {
                display += " " + string.Join(" ", argumentList);
            }

            if (echoCommand)
            {
                LogHub.Instance.Cmd(display, source);
            }

            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.CurrentDirectory
                    : workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var argument in argumentList)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            var startedAt = DateTime.UtcNow;

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }

                    Append(standardOutput, e.Data, maxCapturedChars);
                    OutputLine?.Invoke(this, e.Data);
                    SafeInvoke(onLine, e.Data);

                    if (logOutput)
                    {
                        LogHub.Instance.Output(e.Data, source);
                    }
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }

                    Append(standardError, e.Data, maxCapturedChars);
                    ErrorLine?.Invoke(this, e.Data);
                    SafeInvoke(onLine, e.Data);

                    if (logOutput)
                    {
                        LogHub.Instance.Output(e.Data, source);
                    }
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    LogHub.Instance.Error(
                        Path.GetFileName(fileName) + " başlatılamadı: " + ex.Message, source);

                    return new ProcessResult
                    {
                        FileName = fileName,
                        Arguments = string.Join(" ", argumentList),
                        ExitCode = -1,
                        StandardError = ex.Message,
                        Duration = DateTime.UtcNow - startedAt
                    };
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null)
                using (var linked = timeoutCts == null
                           ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                           : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                using (linked.Token.Register(() =>
                       {
                           try
                           {
                               if (!process.HasExited)
                               {
                                   process.Kill(true);
                               }
                           }
                           catch (Exception)
                           {
                               // süreç zaten kapanmış olabilir
                           }
                       }))
                {
                    try
                    {
                        await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // süreç bizim tarafımızdan sonlandırıldı
                    }

                    // Async okuyucuların kalan satırları işlemesi için tam bekleme.
                    try
                    {
                        process.WaitForExit();
                    }
                    catch (Exception)
                    {
                        // yok sayılır
                    }

                    var timedOut = timeoutCts != null
                                   && timeoutCts.IsCancellationRequested
                                   && !cancellationToken.IsCancellationRequested;

                    var result = new ProcessResult
                    {
                        FileName = fileName,
                        Arguments = string.Join(" ", argumentList),
                        StandardOutput = standardOutput.ToString(),
                        StandardError = standardError.ToString(),
                        Duration = DateTime.UtcNow - startedAt,
                        TimedOut = timedOut,
                        Canceled = cancellationToken.IsCancellationRequested
                    };

                    try
                    {
                        result.ExitCode = process.ExitCode;
                    }
                    catch (Exception)
                    {
                        result.ExitCode = -1;
                    }

                    if (result.TimedOut)
                    {
                        LogHub.Instance.Warn(
                            Path.GetFileName(fileName) + " zaman aşımına uğradı ("
                            + Math.Round(result.Duration.TotalSeconds) + " sn).", source);
                    }
                    else if (!result.Canceled && result.ExitCode != 0)
                    {
                        var tail = LastLines(result.StandardError, 3);
                        LogHub.Instance.Warn(
                            Path.GetFileName(fileName) + " çıkış kodu " + result.ExitCode
                            + (string.IsNullOrWhiteSpace(tail) ? string.Empty : ": " + tail), source);
                    }

                    return result;
                }
            }
        }

        private static void Append(StringBuilder builder, string line, int maxChars)
        {
            lock (builder)
            {
                if (builder.Length >= maxChars)
                {
                    return;
                }

                builder.AppendLine(line);
            }
        }

        internal static string LastLines(string text, int count)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var lines = text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            var take = Math.Max(0, lines.Length - count);
            return string.Join(" | ", lines.Skip(take));
        }

        private static void SafeInvoke(Action<string> callback, string line)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(line);
            }
            catch (Exception ex)
            {
                LogHub.Instance.Error("Çıktı işleyici hatası: " + ex.Message, "SÜREÇ");
            }
        }
    }
}
