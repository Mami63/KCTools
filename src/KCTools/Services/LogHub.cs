using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using KCTools.Common;
using KCTools.Models;
using Microsoft.Win32;

namespace KCTools.Services
{
    /// <summary>
    /// Uygulamanın tek günlük merkezi. Tüm modüller buradan yazar,
    /// alt konsol paneli bu koleksiyonu canlı dinler.
    /// </summary>
    public sealed class LogHub : ObservableObject
    {
        private const int MaxEntries = 2000;
        private readonly object _sync = new object();
        private readonly LogFileWriter _fileWriter;
        private bool _autoScroll = true;

        private LogHub()
        {
            Entries = new ObservableCollection<LogEntry>();
            ClearCommand = new RelayCommand(_ => Clear());
            CopyCommand = new RelayCommand(_ => CopyToClipboard());
            SaveCommand = new RelayCommand(_ => SaveToFile());

            try
            {
                _fileWriter = new LogFileWriter(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KCTools", "Logs"));
            }
            catch (Exception)
            {
                _fileWriter = null;
            }
        }

        public static LogHub Instance { get; } = new LogHub();

        public ObservableCollection<LogEntry> Entries { get; }

        /// <summary>Aktif günlük dosyası (yoksa null).</summary>
        public string LogFilePath => _fileWriter?.FilePath;

        /// <summary>Uygulama kapanırken dosya yazıcısını boşaltır.</summary>
        public void Shutdown()
        {
            _fileWriter?.Dispose();
        }

        public ICommand ClearCommand { get; }

        public ICommand CopyCommand { get; }

        public ICommand SaveCommand { get; }

        public event EventHandler<LogEntry> EntryAdded;

        public event EventHandler Cleared;

        public bool AutoScroll
        {
            get => _autoScroll;
            set => Set(ref _autoScroll, value);
        }

        public void Write(LogSeverity severity, string message, string source = "KC TOOLS")
        {
            if (severity == LogSeverity.Step && AppSettings.LogLevel == AppSettings.LevelSimple)
            {
                return;
            }

            var app = Application.Current;
            if (app != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(() => Write(severity, message, source)));
                return;
            }

            var entry = new LogEntry(severity, message, source);

            lock (_sync)
            {
                Entries.Add(entry);
                while (Entries.Count > MaxEntries)
                {
                    Entries.RemoveAt(0);
                }
            }

            _fileWriter?.Enqueue(entry.PlainText);
            EntryAdded?.Invoke(this, entry);
        }

        public void Info(string message, string source = "KC TOOLS") => Write(LogSeverity.Info, message, source);

        public void Step(string message, string source = "KC TOOLS") => Write(LogSeverity.Step, message, source);

        public void Ok(string message, string source = "KC TOOLS") => Write(LogSeverity.Success, message, source);

        public void Warn(string message, string source = "KC TOOLS") => Write(LogSeverity.Warning, message, source);

        public void Error(string message, string source = "KC TOOLS") => Write(LogSeverity.Error, message, source);

        public void Cmd(string message, string source = "KC TOOLS") => Write(LogSeverity.Command, message, source);

        /// <summary>Dış süreç çıktısı (adb / fastboot stdout-stderr).</summary>
        public void Output(string message, string source = "SÜREÇ")
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Write(LogSeverity.Step, "  " + message.TrimEnd(), source);
        }

        /// <summary>Günlüğe başlık ayırıcısı basar: ── BAŞLIK ─────</summary>
        public void Rule(string title, string source = "KC TOOLS")
        {
            var text = string.IsNullOrWhiteSpace(title) ? "İŞLEM" : title.ToUpperInvariant();
            var pad = Math.Max(4, 54 - text.Length);
            Write(LogSeverity.Info, "── " + text + " " + new string('─', pad), source);
        }

        public void Clear()
        {
            lock (_sync)
            {
                Entries.Clear();
                Entries.Add(new LogEntry(LogSeverity.Info, "Günlük temizlendi.", "PANEL"));
            }

            Cleared?.Invoke(this, EventArgs.Empty);
        }

        private void CopyToClipboard()
        {
            try
            {
                var text = string.Join(Environment.NewLine, Entries.Select(e => e.PlainText));
                if (string.IsNullOrEmpty(text))
                {
                    Warn("Kopyalanacak günlük satırı yok.", "PANEL");
                    return;
                }

                Clipboard.SetText(text);
                Ok($"{Entries.Count} satır panoya kopyalandı.", "PANEL");
            }
            catch (Exception ex)
            {
                Error("Panoya kopyalama başarısız: " + ex.Message, "PANEL");
            }
        }

        private void SaveToFile()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Günlüğü kaydet",
                Filter = "Günlük dosyası (*.log)|*.log|Metin dosyası (*.txt)|*.txt",
                FileName = $"kctools-log-{DateTime.Now:yyyyMMdd-HHmm}.log",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var text = new StringBuilder();
                foreach (var entry in Entries)
                {
                    text.AppendLine(entry.PlainText);
                }

                File.WriteAllText(dialog.FileName, text.ToString(), new UTF8Encoding(true));
                Ok("Günlük kaydedildi: " + dialog.FileName, "PANEL");
            }
            catch (Exception ex)
            {
                Error("Günlük kaydedilemedi: " + ex.Message, "PANEL");
            }
        }
    }
}
