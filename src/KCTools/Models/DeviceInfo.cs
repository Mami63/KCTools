namespace KCTools.Models
{
    /// <summary>Bağlı telefondan ADB ile okunan ayrıntılı cihaz bilgisi.</summary>
    public sealed class DeviceInfo
    {
        public string Model { get; set; } = "—";

        public string Brand { get; set; } = "—";

        public string Android { get; set; } = "—";

        public string Sdk { get; set; } = "—";

        public string Serial { get; set; } = "—";

        /// <summary>Codename (ro.product.device).</summary>
        public string Codename { get; set; } = "—";

        /// <summary>Üreticinin pazarlama adı (ro.product.marketname).</summary>
        public string MarketName { get; set; } = "—";

        public string Build { get; set; } = "—";

        public string Bootloader { get; set; } = "—";

        public string VerifiedBoot { get; set; } = "—";

        public string Frp { get; set; } = "—";

        public string Root { get; set; } = "Bilinmiyor";

        public string Battery { get; set; } = "—";

        public string Storage { get; set; } = "—";

        public string Resolution { get; set; } = "—";

        public string Summary => Model + " · Android " + Android + " · " + Battery;
    }
}
