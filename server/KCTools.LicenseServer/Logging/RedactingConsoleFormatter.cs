using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace KCTools.LicenseServer.Logging
{
    /// <summary>
    /// Production için konsol formatter'ı: tüm mesajlar ve istisnalar
    /// SensitiveDataRedactor'dan geçirilerek yazılır.
    /// </summary>
    public sealed class RedactingConsoleFormatter : ConsoleFormatter
    {
        public const string FormatterName = "redacted";

        public RedactingConsoleFormatter(IOptions<ConsoleFormatterOptions> options)
            : base(FormatterName)
        {
        }

        public override void Write<TState>(
            in LogEntry<TState> logEntry,
            IExternalScopeProvider? scopeProvider,
            TextWriter textWriter)
        {
            var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception)
                          ?? string.Empty;

            var line = DateTimeOffset.Now.ToString("HH:mm:ss") +
                       " [" + logEntry.LogLevel.ToString().ToUpperInvariant() + "] " +
                       logEntry.Category + ": " +
                       SensitiveDataRedactor.Redact(message);

            textWriter.WriteLine(line);

            if (logEntry.Exception != null)
            {
                textWriter.WriteLine(SensitiveDataRedactor.Redact(logEntry.Exception.ToString()));
            }
        }
    }
}
