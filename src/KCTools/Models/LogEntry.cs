using System;
using KCTools.Services;

namespace KCTools.Models
{
    public enum LogSeverity
    {
        Info,
        Step,
        Success,
        Warning,
        Error,
        Command
    }

    /// <summary>Konsol paneline düşen tek bir günlük satırı.</summary>
    public sealed class LogEntry
    {
        public LogEntry(LogSeverity severity, string message, string source)
        {
            Timestamp = DateTime.Now;
            Severity = severity;
            Message = message ?? string.Empty;
            Source = string.IsNullOrWhiteSpace(source) ? "KC TOOLS" : source;
            TimeText = Timestamp.ToString("HH:mm:ss");
        }

        public DateTime Timestamp { get; }

        public LogSeverity Severity { get; }

        public string Message { get; }

        public string Source { get; }

        /// <summary>Panelde gösterilen saat damgası (ayarlardan kapatılabilir).</summary>
        public string TimeText { get; private set; }

        public string Tag
        {
            get
            {
                switch (Severity)
                {
                    case LogSeverity.Step: return "ADIM";
                    case LogSeverity.Success: return "TAMAM";
                    case LogSeverity.Warning: return "UYARI";
                    case LogSeverity.Error: return "HATA";
                    case LogSeverity.Command: return "KOMUT";
                    default: return "BİLGİ";
                }
            }
        }

        public string PlainText => $"[{TimeText}] [{Tag}] {Message}";
    }
}
