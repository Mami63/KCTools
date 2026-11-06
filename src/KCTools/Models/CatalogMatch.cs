namespace KCTools.Models
{
    /// <summary>Eşleşme güven düzeyi. "Kesin" yalnızca kod/codename doğrulandığında kullanılır.</summary>
    public enum MatchConfidence
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Exact = 4,
        Manual = 5
    }

    /// <summary>Bir cihazın katalog eşleşmesi.</summary>
    public sealed class CatalogMatch
    {
        public CatalogBrand Brand { get; set; }

        public CatalogModel Model { get; set; }

        public MatchConfidence Confidence { get; set; } = MatchConfidence.None;

        /// <summary>ADB, Fastboot, VID/PID, Manuel.</summary>
        public string Source { get; set; } = "—";

        public bool IsKnown => Model != null;

        public string ConfidenceText
        {
            get
            {
                switch (Confidence)
                {
                    case MatchConfidence.Exact: return "KESİN";
                    case MatchConfidence.High: return "YÜKSEK";
                    case MatchConfidence.Medium: return "ORTA";
                    case MatchConfidence.Low: return "TAHMİNÎ";
                    case MatchConfidence.Manual: return "MANUEL";
                    default: return "BİLİNMİYOR";
                }
            }
        }

        public string Display =>
            Model == null
                ? (Brand == null ? "Katalogda yok" : Brand.Name + " · model belirsiz")
                : FullName + " (" + Model.Codename + ")";

        /// <summary>Marka adı modelin başında tekrarlanmadan tam ad.</summary>
        public string FullName
        {
            get
            {
                if (Model == null)
                {
                    return Brand?.Name ?? "—";
                }

                var brand = Brand?.Name ?? string.Empty;

                if (brand.Length > 0 &&
                    Model.Name.StartsWith(brand, System.StringComparison.OrdinalIgnoreCase))
                {
                    return Model.Name;
                }

                return (brand + " " + Model.Name).Trim();
            }
        }
    }
}
