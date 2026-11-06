using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KCTools.Models;

namespace KCTools.Services
{
    /// <summary>
    /// Marka/model kataloğunu JSON'dan yükler ve cihaz bilgileriyle eşleştirir.
    /// Veri dosyası: Assets/DeviceCatalog.json (çıktı klasörüne kopyalanır).
    /// </summary>
    public sealed class DeviceCatalogService
    {
        private static readonly Lazy<DeviceCatalogService> Lazy =
            new Lazy<DeviceCatalogService>(() => new DeviceCatalogService());

        private readonly List<string> _warnings = new List<string>();
        private Dictionary<string, (string BrandId, string Code)> _overrides;

        private DeviceCatalogService()
        {
            var path = DefaultPath();

            Catalog = File.Exists(path)
                ? LoadFrom(path, _warnings)
                : LoadEmbedded(_warnings);

            Catalog ??= new CatalogRoot();

            foreach (var warning in _warnings)
            {
                LogHub.Instance.Warn("Katalog: " + warning, "KATALOG");
            }

            LogHub.Instance.Info(
                "Katalog yüklendi: " + Catalog.Brands.Count + " marka · " +
                Catalog.Brands.Sum(b => b.Models.Count) + " model", "KATALOG");

            _overrides = LoadOverrides();
        }

        public static DeviceCatalogService Instance => Lazy.Value;

        public CatalogRoot Catalog { get; }

        public IReadOnlyList<CatalogBrand> Brands => Catalog.Brands;

        public IReadOnlyList<string> Warnings => _warnings;

        public static string DefaultPath()
        {
            var local = Path.Combine(AppContext.BaseDirectory, "Assets", "DeviceCatalog.json");

            return File.Exists(local)
                ? local
                : Path.Combine(AppContext.BaseDirectory, "DeviceCatalog.json");
        }

        /// <summary>Test/dışa açık yükleme: JSON'u okur, doğrular, hatalı kayıtları atlar.</summary>
        public static CatalogRoot LoadFrom(string path, List<string> warnings = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    warnings?.Add("Katalog dosyası bulunamadı: " + path);
                    return new CatalogRoot();
                }

                var json = File.ReadAllText(path);
                var root = JsonSerializer.Deserialize<CatalogRoot>(
                    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (root == null)
                {
                    warnings?.Add("Katalog boş veya geçersiz JSON.");
                    return new CatalogRoot();
                }

                Validate(root, warnings);
                return root;
            }
            catch (Exception ex)
            {
                warnings?.Add("Katalog okunamadı: " + ex.Message);
                return new CatalogRoot();
            }
        }

        /// <summary>Tek dosya dağıtımda gömülü kaynaktan yükler.</summary>
        public static CatalogRoot LoadEmbedded(List<string> warnings = null)
        {
            try
            {
                using (var stream = typeof(DeviceCatalogService).Assembly
                           .GetManifestResourceStream("KCTools.DeviceCatalog.json"))
                {
                    if (stream == null)
                    {
                        warnings?.Add("Gömülü katalog kaynağı bulunamadı.");
                        return new CatalogRoot();
                    }

                    using (var reader = new StreamReader(stream))
                    {
                        var root = JsonSerializer.Deserialize<CatalogRoot>(
                            reader.ReadToEnd(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (root == null)
                        {
                            warnings?.Add("Gömülü katalog boş.");
                            return new CatalogRoot();
                        }

                        Validate(root, warnings);
                        return root;
                    }
                }
            }
            catch (Exception ex)
            {
                warnings?.Add("Gömülü katalog okunamadı: " + ex.Message);
                return new CatalogRoot();
            }
        }

        public static void Validate(CatalogRoot root, List<string> warnings = null)
        {
            root.Brands ??= new List<CatalogBrand>();

            if (root.SchemaVersion != 1)
            {
                warnings?.Add("Beklenmeyen şema sürümü: " + root.SchemaVersion);
            }

            var brandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var b = root.Brands.Count - 1; b >= 0; b--)
            {
                var brand = root.Brands[b];

                if (brand == null || string.IsNullOrWhiteSpace(brand.Id) ||
                    string.IsNullOrWhiteSpace(brand.Name))
                {
                    warnings?.Add("Kimliksiz marka atlandı.");
                    root.Brands.RemoveAt(b);
                    continue;
                }

                if (!brandIds.Add(brand.Id))
                {
                    warnings?.Add("Tekrar eden marka: " + brand.Id);
                }

                brand.Models ??= new List<CatalogModel>();

                var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (var m = brand.Models.Count - 1; m >= 0; m--)
                {
                    var model = brand.Models[m];

                    if (model == null || string.IsNullOrWhiteSpace(model.Code))
                    {
                        warnings?.Add(brand.Name + ": model kodu boş kayıt atlandı.");
                        brand.Models.RemoveAt(m);
                        continue;
                    }

                    model.Code = model.Code.Trim();
                    model.Name = (model.Name ?? string.Empty).Trim();
                    model.Codename = (model.Codename ?? string.Empty).Trim();
                    model.Platform = (model.Platform ?? string.Empty).Trim();
                    model.Notes = model.Notes ?? string.Empty;
                    model.Modes ??= new List<string>();

                    if (!codes.Add(model.Code))
                    {
                        warnings?.Add(brand.Name + ": tekrar eden model kodu " + model.Code);
                    }
                }
            }
        }

        public CatalogBrand FindBrand(string brandId)
        {
            return Brands.FirstOrDefault(
                b => string.Equals(b.Id, brandId, StringComparison.OrdinalIgnoreCase));
        }

        public CatalogModel FindModel(string brandId, string code)
        {
            return FindBrand(brandId)?.Models.FirstOrDefault(
                m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Sıra: model kodu (Kesin) → codename (Yüksek) → model adı (Orta) →
        /// VID/PID yalnızca marka (Tahminî).
        /// </summary>
        public CatalogMatch Match(string model, string codename, string manufacturer, string vid)
        {
            var code = Normalize(model);
            var product = Normalize(codename);

            if (code.Length > 0)
            {
                foreach (var brand in Brands)
                {
                    foreach (var item in brand.Models)
                    {
                        if (string.Equals(Normalize(item.Code), code, StringComparison.Ordinal))
                        {
                            return new CatalogMatch
                            {
                                Brand = brand,
                                Model = item,
                                Confidence = MatchConfidence.Exact,
                                Source = "ADB/Fastboot"
                            };
                        }
                    }
                }
            }

            if (product.Length > 0)
            {
                foreach (var brand in Brands)
                {
                    foreach (var item in brand.Models)
                    {
                        if (item.Codename.Length > 0 &&
                            string.Equals(Normalize(item.Codename), product, StringComparison.Ordinal))
                        {
                            return new CatalogMatch
                            {
                                Brand = brand,
                                Model = item,
                                Confidence = MatchConfidence.High,
                                Source = "Codename"
                            };
                        }
                    }
                }
            }

            var maker = Normalize(manufacturer);
            var modelText = Normalize(model);

            if (modelText.Length > 2)
            {
                foreach (var brand in Brands)
                {
                    foreach (var item in brand.Models)
                    {
                        var name = Normalize(item.Name);

                        if (name.Length > 2 &&
                            (name.Contains(modelText) || modelText.Contains(name)))
                        {
                            return new CatalogMatch
                            {
                                Brand = brand,
                                Model = item,
                                Confidence = MatchConfidence.Medium,
                                Source = "Model adı"
                            };
                        }
                    }
                }
            }

            if (vid != null)
            {
                var brand = Brands.FirstOrDefault(
                    b => b.VidPids.Any(v => string.Equals(v, vid, StringComparison.OrdinalIgnoreCase)));

                if (brand != null)
                {
                    return new CatalogMatch
                    {
                        Brand = brand,
                        Model = null,
                        Confidence = MatchConfidence.Low,
                        Source = "VID/PID"
                    };
                }
            }

            if (maker.Length > 0)
            {
                var brand = Brands.FirstOrDefault(b => Normalize(b.Name).Contains(maker) ||
                                                       maker.Contains(Normalize(b.Name)));

                if (brand != null)
                {
                    return new CatalogMatch
                    {
                        Brand = brand,
                        Model = null,
                        Confidence = MatchConfidence.Low,
                        Source = "Üretici"
                    };
                }
            }

            return new CatalogMatch { Confidence = MatchConfidence.None, Source = "—" };
        }

        // ------------------------------------------------------------ override

        public CatalogMatch GetOverride(string serial)
        {
            if (string.IsNullOrWhiteSpace(serial) || !_overrides.TryGetValue(serial, out var entry))
            {
                return null;
            }

            var model = FindModel(entry.BrandId, entry.Code);

            if (model == null)
            {
                return null;
            }

            return new CatalogMatch
            {
                Brand = FindBrand(entry.BrandId),
                Model = model,
                Confidence = MatchConfidence.Manual,
                Source = "Manuel"
            };
        }

        public bool SetOverride(string serial, string brandId, string code)
        {
            if (string.IsNullOrWhiteSpace(serial) ||
                FindModel(brandId, code) == null)
            {
                return false;
            }

            _overrides[serial] = (brandId, code);
            SaveOverrides();
            LogHub.Instance.Ok(
                "Model eşleşmesi kaydedildi: " + serial + " → " + brandId + "/" + code, "KATALOG");
            return true;
        }

        public string OverridePath()
        {
            var primary = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KCTools", "device-overrides.json");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(primary));
                return primary;
            }
            catch (Exception)
            {
                return Path.Combine(AppContext.BaseDirectory, "device-overrides.json");
            }
        }

        private Dictionary<string, (string BrandId, string Code)> LoadOverrides()
        {
            var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var path = OverridePath();

                if (!File.Exists(path))
                {
                    return result;
                }

                var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                    File.ReadAllText(path));

                if (data == null)
                {
                    return result;
                }

                foreach (var pair in data)
                {
                    if (pair.Value != null &&
                        pair.Value.TryGetValue("brandId", out var brandId) &&
                        pair.Value.TryGetValue("code", out var code))
                    {
                        result[pair.Key] = (brandId, code);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("Override dosyası okunamadı: " + ex.Message, "KATALOG");
            }

            return result;
        }

        private void SaveOverrides()
        {
            try
            {
                var path = OverridePath();
                var data = _overrides.ToDictionary(
                    p => p.Key,
                    p => new Dictionary<string, string>
                    {
                        ["brandId"] = p.Value.BrandId,
                        ["code"] = p.Value.Code
                    });

                File.WriteAllText(
                    path,
                    JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                LogHub.Instance.Warn("Override dosyası yazılamadı: " + ex.Message, "KATALOG");
            }
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(value.Length);

            foreach (var c in value.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
