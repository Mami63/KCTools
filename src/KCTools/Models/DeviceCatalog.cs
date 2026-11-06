using System;
using System.Collections.Generic;
using System.Linq;

namespace KCTools.Models
{
    /// <summary>Katalog kökü (Assets/DeviceCatalog.json).</summary>
    public sealed class CatalogRoot
    {
        public int SchemaVersion { get; set; } = 1;

        public string Updated { get; set; } = string.Empty;

        public List<CatalogBrand> Brands { get; set; } = new List<CatalogBrand>();
    }

    public sealed class CatalogBrand
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>USB Vendor ID ipuçları (04E8 Samsung gibi).</summary>
        public List<string> VidPids { get; set; } = new List<string>();

        public List<CatalogModel> Models { get; set; } = new List<CatalogModel>();

        public int ModelCount => Models?.Count ?? 0;

        public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();

        public override string ToString() => Name;
    }

    public sealed class CatalogModel
    {
        /// <summary>Pazarlama model kodu (ro.product.model ile eşleşir).</summary>
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>Geliştirici codename'i (ro.product.device / fastboot product).</summary>
        public string Codename { get; set; } = string.Empty;

        public string Platform { get; set; } = string.Empty;

        /// <summary>adb, fastboot, recovery, download, edl ...</summary>
        public List<string> Modes { get; set; } = new List<string>();

        /// <summary>Gerçek cihazda doğrulandı mı?</summary>
        public bool Tested { get; set; }

        public string Notes { get; set; } = string.Empty;

        public string ModesText =>
            Modes == null || Modes.Count == 0 ? "—" : string.Join(" · ", Modes);

        public string TestedText => Tested ? "TEST EDİLDİ" : "TEST EDİLMEDİ";

        public string Title => Name + " · " + Code;

        public override string ToString() => Title;
    }
}
