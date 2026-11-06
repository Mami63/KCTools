namespace KCTools.Models
{
    /// <summary>ADB üzerinden okunan telefon kimliği.</summary>
    public sealed class PhoneProfile
    {
        public string Serial { get; set; } = "—";

        public string Manufacturer { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string Device { get; set; } = string.Empty;

        public string AndroidVersion { get; set; } = "—";

        public string Sdk { get; set; } = "—";

        /// <summary>Pazarlama adı (ro.product.marketname).</summary>
        public string MarketName { get; set; } = "—";

        /// <summary>ADB dışı modlar için etiket ("Fastboot" gibi).</summary>
        public string Mode { get; set; }

        public string Bootloader { get; set; } = "—";

        public string Display
        {
            get
            {
                var market = (MarketName ?? string.Empty).Trim();

                if (market.Length > 0 && market != "—")
                {
                    return market;
                }

                var model = (Model ?? string.Empty).Trim();
                var manufacturer = (Manufacturer ?? string.Empty).Trim();

                if (model.Length == 0)
                {
                    return deviceOrFallback();
                }

                if (manufacturer.Length > 0 &&
                    model.IndexOf(manufacturer, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return manufacturer + " " + model;
                }

                return model;
            }
        }

        public string Detail =>
            Display + " · " +
            (string.IsNullOrWhiteSpace(Mode)
                ? "Android " + AndroidVersion +
                  (string.IsNullOrWhiteSpace(Sdk) || Sdk == "—" ? string.Empty : " (SDK " + Sdk + ")")
                : Mode) +
            " · " + Serial;

        /// <summary>Başlık rozeti için kısa metin.</summary>
        public string ShortText =>
            Display + " · " +
            (string.IsNullOrWhiteSpace(Mode) ? "Android " + AndroidVersion : Mode);

        private string deviceOrFallback()
        {
            return string.IsNullOrWhiteSpace(Device) ? "Bilinmeyen model" : Device;
        }
    }
}
