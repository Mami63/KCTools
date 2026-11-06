using System;

namespace KCTools.Services
{
    /// <summary>Kalıcı olmayan (oturumluk) uygulama ayarları.</summary>
    public static class AppSettings
    {
        public const string LevelSimple = "Sade";
        public const string LevelNormal = "Normal";
        public const string LevelVerbose = "Ayrıntılı";

        public static string Version => "1.0.0";

        public static string Build => "UI Preview";

        public static bool VerboseLogging { get; set; } = true;

        public static bool ConfirmFlash { get; set; } = true;

        public static bool AutoDetectPorts { get; set; } = true;

        public static string LogLevel { get; set; } = LevelVerbose;

        public static string Language { get; set; } = "Türkçe";

        public static string LicenseKey { get; set; } = string.Empty;

        public static string InstanceName =>
            Environment.MachineName.Length > 0 ? Environment.MachineName : "WORKSTATION";
    }
}
