using System.Text.Json;
using System.Text.RegularExpressions;

var devicesPath = args[0];
var catalogPath = args[1];

var devices = JsonSerializer.Deserialize<Dictionary<string, DeviceEntry>>(
    File.ReadAllText(devicesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

var catalog = JsonSerializer.Deserialize<CatalogRoot>(File.ReadAllText(catalogPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["samsung"] = "samsung",
    ["xiaomi"] = "xiaomi",
    ["redmi"] = "redmi",
    ["poco"] = "poco",
    ["huawei"] = "huawei",
    ["oppo"] = "oppo",
    ["vivo"] = "vivo",
    ["motorola"] = "motorola",
    ["realme"] = "realme",
    ["oneplus"] = "oneplus",
    ["tecno"] = "tecno",
    ["infinix"] = "infinix"
};

var exclude = new Regex(
    "(TV|BOX|PAD|TAB|WATCH|ROUTER|CAMERA|SPEAKER|PROJECTOR|MONITOR)",
    RegexOptions.IgnoreCase);

const int MaxPerBrand = 80;

var fixedName = false;

foreach (var brand in catalog.Brands)
{
    if (brand.Id == "redmi")
    {
        var device = brand.Models.FirstOrDefault(m => m.Code == "2508CRN2BE");

        if (device != null)
        {
            device.Name = "Redmi 15C 5G";
            device.Notes = "KC Tools ile gerçek cihazda doğrulandı (marketname + codename, 2026-11-04).";
            fixedName = true;
        }
    }
}

var added = 0;

foreach (var pair in devices)
{
    var entry = pair.Value;

    if (entry == null || string.IsNullOrWhiteSpace(entry.Brand) || string.IsNullOrWhiteSpace(entry.Name))
    {
        continue;
    }

    if (!map.TryGetValue(entry.Brand, out var brandId))
    {
        continue;
    }

    if (exclude.IsMatch(entry.Name))
    {
        continue;
    }

    var brand = catalog.Brands.FirstOrDefault(b => b.Id == brandId);

    if (brand == null || brand.Models.Count >= MaxPerBrand)
    {
        continue;
    }

    var code = pair.Key.Trim();

    if (code.Length == 0 || brand.Models.Any(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase)))
    {
        continue;
    }

    brand.Models.Add(new CatalogModel
    {
        Code = code,
        Name = entry.Name.Trim(),
        Codename = string.Empty,
        Platform = "—",
        Modes = new List<string> { "adb", "fastboot", "recovery" },
        Tested = false,
        Notes = "Topluluk model listesi; codename doğrulanmadı."
    });

    added++;
}

File.WriteAllText(
    catalogPath,
    JsonSerializer.Serialize(catalog, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }));

Console.WriteLine("catalog brands  : " + catalog.Brands.Count);
Console.WriteLine("dataset entries : " + devices.Count);
Console.WriteLine("added           : " + added);
Console.WriteLine("name fixed      : " + fixedName);
Console.WriteLine("total models    : " + catalog.Brands.Sum(b => b.Models.Count));

foreach (var brand in catalog.Brands)
{
    Console.WriteLine("  " + brand.Name.PadRight(12) + brand.Models.Count);
}

sealed class DeviceEntry
{
    public string Brand { get; set; }

    public string Name { get; set; }
}

sealed class CatalogRoot
{
    public int SchemaVersion { get; set; } = 1;

    public string Updated { get; set; } = "2026-11-04";

    public List<CatalogBrand> Brands { get; set; } = new();
}

sealed class CatalogBrand
{
    public string Id { get; set; }

    public string Name { get; set; }

    public List<string> VidPids { get; set; } = new();

    public List<CatalogModel> Models { get; set; } = new();
}

sealed class CatalogModel
{
    public string Code { get; set; }

    public string Name { get; set; }

    public string Codename { get; set; }

    public string Platform { get; set; }

    public List<string> Modes { get; set; } = new();

    public bool Tested { get; set; }

    public string Notes { get; set; }
}
