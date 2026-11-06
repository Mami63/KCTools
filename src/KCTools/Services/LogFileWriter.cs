using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace KCTools.Services
{
    /// <summary>
    /// Günlük satırlarını arka plan iş parçacığında diske yazar.
    /// UI'yı bloklamaz; disk hataları sessizce yutulur (günlük yazımı
    /// uygulamayı asla durdurmamalı).
    /// </summary>
    public sealed class LogFileWriter : IDisposable
    {
        private readonly BlockingCollection<string> _queue =
            new BlockingCollection<string>(new ConcurrentQueue<string>(), 8192);

        private readonly Thread _worker;
        private readonly string _filePath;
        private volatile bool _disposed;

        public LogFileWriter(string folder)
        {
            // Önce istenen klasör; yazılamıyorsa uygulama klasörü, son çare TEMP.
            _filePath = CreatePath(folder)
                        ?? CreatePath(Path.Combine(AppContext.BaseDirectory, "logs"))
                        ?? CreatePath(Path.Combine(Path.GetTempPath(), "KCTools"));

            _worker = new Thread(Work)
            {
                IsBackground = true,
                Name = "KCTools.LogFileWriter"
            };

            _worker.Start();
        }

        public string FilePath => _filePath;

        public bool IsActive => !string.IsNullOrWhiteSpace(_filePath);

        public void Enqueue(string line)
        {
            if (_disposed || string.IsNullOrEmpty(line))
            {
                return;
            }

            try
            {
                _queue.TryAdd(line, 50);
            }
            catch (Exception)
            {
                // kuyruk kapandıysa yok say
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _queue.CompleteAdding();
                _worker.Join(800);
            }
            catch (Exception)
            {
                // yok sayılır
            }

            try
            {
                _queue.Dispose();
            }
            catch (Exception)
            {
                // yok sayılır
            }
        }

        private static string CreatePath(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "kctools-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void Work()
        {
            if (string.IsNullOrWhiteSpace(_filePath))
            {
                return;
            }

            try
            {
                using (var stream = new FileStream(
                           _filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    foreach (var line in _queue.GetConsumingEnumerable())
                    {
                        try
                        {
                            writer.WriteLine(line);
                        }
                        catch (IOException)
                        {
                            // disk dolu / dosya kilitli: satır atlanır
                        }
                    }
                }
            }
            catch (Exception)
            {
                // günlük dosyası açılamadı; uygulama çalışmaya devam eder
            }
        }
    }
}
