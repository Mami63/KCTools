namespace KCTools.Models
{
    /// <summary>AT komutlarından derlenen modem bilgisi.</summary>
    public sealed class ModemInfo
    {
        public string Imei { get; set; } = "—";

        public bool ImeiValid { get; set; }

        public string Model { get; set; } = "—";

        public string Firmware { get; set; } = "—";

        public string Imsi { get; set; } = "—";

        public string SimState { get; set; } = "—";

        public string Operator { get; set; } = "—";

        public string Registration { get; set; } = "—";

        public string Battery { get; set; } = "—";

        /// <summary>AT+CSQ rssi (0-31, 99 bilinmiyor).</summary>
        public int SignalRaw { get; set; } = -1;

        public int SignalDbm { get; set; }

        public string SignalText =>
            SignalRaw < 0 || SignalRaw == 99
                ? "—"
                : SignalRaw + " / 31 · " + SignalDbm + " dBm";
    }
}
