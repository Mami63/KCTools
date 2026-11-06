namespace KCTools.Models
{
    /// <summary>ADB/Fastboot yoklamasından gelen canlı cihaz kaydı.</summary>
    public sealed class MonitoredDevice
    {
        public string Serial { get; set; } = "—";

        /// <summary>ADB veya Fastboot.</summary>
        public string Mode { get; set; } = "ADB";

        public string State { get; set; } = "unknown";

        public string Model { get; set; } = "—";

        /// <summary>Codename (product).</summary>
        public string Product { get; set; } = "—";

        public bool IsOnline => State == "device";

        public string Display =>
            Mode + " · " + Serial +
            (string.IsNullOrWhiteSpace(Model) || Model == "—" ? string.Empty : " · " + Model);
    }
}
