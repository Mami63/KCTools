using System.IO.Compression;
using KCTools.ViewModels;
using System.IO;
using System.Text;
using KCTools.Models;
using KCTools.Services;

var failures = new List<string>();

void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "PASS  " : "FAIL  ") + name);

    if (!condition)
    {
        failures.Add(name);
    }
}

string FindRepoFile(string relative)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);

    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, relative);

        if (File.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return null;
}

var catalogPath = FindRepoFile(Path.Combine("src", "KCTools", "Assets", "DeviceCatalog.json"));

if (catalogPath == null)
{
    Console.WriteLine("FAIL  katalog dosyası bulunamadı");
    return 1;
}

// Instance'ın test çıktısında da kataloğu görmesi için kopyala.
var localAssets = Path.Combine(AppContext.BaseDirectory, "Assets");
Directory.CreateDirectory(localAssets);
File.Copy(catalogPath, Path.Combine(localAssets, "DeviceCatalog.json"), true);

// ---------------------------------------------------------------- yükleme
var warnings = new List<string>();
var root = DeviceCatalogService.LoadFrom(catalogPath, warnings);

Check(root != null, "JSON: katalog yüklendi");
Check(root.Brands.Count == 12, "JSON: 12 marka (" + root.Brands.Count + ")");
Check(root.Brands.Sum(b => b.Models.Count) >= 800, "JSON: 800+ model (" + root.Brands.Sum(b => b.Models.Count) + ")");
Check(warnings.Count == 0, "JSON: doğrulama uyarısı yok (" + warnings.Count + ")");
Check(root.Brands.Any(b => b.Id == "samsung" && b.VidPids.Contains("04E8")), "JSON: Samsung VID 04E8");

var tested = root.Brands.SelectMany(b => b.Models).Count(m => m.Tested);
Check(tested == 1, "JSON: yalnızca doğrulanmış kayıt işaretli (" + tested + ")");

// ---------------------------------------------------------------- eşleştirme
var embedded = DeviceCatalogService.LoadEmbedded(new List<string>());
Check(embedded.Brands.Count == 12, "Gömülü katalog: 12 marka (" + embedded.Brands.Count + ")");

var service = DeviceCatalogService.Instance;

var exact = service.Match("2508CRN2BE", "tornado", "Xiaomi", "2717");
Check(exact.Model != null && exact.Model.Code == "2508CRN2BE", "Eşleşme: model kodu (Kesin)");
Check(exact.Confidence == KCTools.Models.MatchConfidence.Exact, "Eşleşme: güven = Kesin");
Check(exact.Model.Tested, "Eşleşme: gerçek cihaz kaydı TEST EDİLDİ");

var samsung = service.Match("SM-S918B", "dm3q", "samsung", "04E8");
Check(samsung.Model != null && samsung.Brand.Id == "samsung", "Eşleşme: Samsung S23 Ultra");

var codename = service.Match("", "lemonadep", null, null);
Check(codename.Model != null && codename.Confidence == KCTools.Models.MatchConfidence.High,
    "Eşleşme: codename (Yüksek)");

var unknown = service.Match("XYZ-123", "unknowncodename", "Bilinmeyen", "FFFF");
Check(unknown.Confidence == KCTools.Models.MatchConfidence.None, "Eşleşme: bilinmeyen cihaz → None");

var vidOnly = service.Match("XYZ-999", "", "Bilinmeyen", "04E8");
Check(vidOnly.Brand != null && vidOnly.Brand.Id == "samsung" &&
      vidOnly.Confidence == KCTools.Models.MatchConfidence.Low,
    "Eşleşme: VID yalnızca marka (Tahminî)");

// ---------------------------------------------------------------- override
var assigned = service.SetOverride("TEST-SERIAL-1", "samsung", "SM-S918B");
Check(assigned, "Override: atama yazıldı");

var back = service.GetOverride("TEST-SERIAL-1");
Check(back != null && back.Model.Code == "SM-S918B", "Override: geri okundu");
Check(back != null && back.Confidence == KCTools.Models.MatchConfidence.Manual,
    "Override: güven = Manuel");

var badAssign = service.SetOverride("TEST-SERIAL-1", "samsung", "YOK-BOYLE");
Check(!badAssign, "Override: geçersiz model reddedildi");

// ---------------------------------------------------------------- bozuk JSON
var brokenPath = Path.Combine(AppContext.BaseDirectory, "broken.json");
File.WriteAllText(brokenPath, "{ bu gecerli json degil ");
var brokenWarnings = new List<string>();
var broken = DeviceCatalogService.LoadFrom(brokenPath, brokenWarnings);
Check(broken.Brands.Count == 0 && brokenWarnings.Count > 0, "Bozuk JSON: boş katalog + uyarı");

// ---------------------------------------------------------------- doğrulama
var duplicate = new CatalogRoot
{
    Brands = new List<CatalogBrand>
    {
        new CatalogBrand
        {
            Id = "test",
            Name = "Test",
            Models = new List<CatalogModel>
            {
                new CatalogModel { Code = "A1", Name = "Bir" },
                new CatalogModel { Code = "A1", Name = "İki" }
            }
        }
    }
};

var duplicateWarnings = new List<string>();
DeviceCatalogService.Validate(duplicate, duplicateWarnings);
Check(duplicateWarnings.Any(w => w.Contains("tekrar eden")), "Doğrulama: tekrar eden kod uyarısı");


// ---------------------------------------------------------------- sinyal (ADB)
var sample = "mSignalStrength=SignalStrength:{mCdma=invalid,mGsm=invalid,mWcdma=invalid,mTdscdma=invalid,mLte=CellSignalStrengthLte: rssi=-87 rsrp=-100 rsrq=-13 rssnr=7 cqiTableIndex=2147483647 cqi=0 ta=0 level=3 parametersUseForLevel=0,mNr=invalid,primary=CellSignalStrengthLte}" +
             Environment.NewLine +
             "mServiceState={mVoiceRegState=0(IN_SERVICE), mOperatorAlphaLong=Turk Telekom, getRilDataRadioTechnology=14(LTE)}";

var signal = TelephonyReader.Parse(sample);
Check(signal != null, "Sinyal: dumpsys ayrıştırıldı");
Check(signal != null && signal.Level == 3, "Sinyal: seviye 3/4");
Check(signal != null && signal.Rsrp == -100, "Sinyal: RSRP -100");
Check(signal != null && signal.Operator == "Turk Telekom", "Sinyal: operatör");
Check(signal != null && signal.Technology == "LTE", "Sinyal: teknoloji LTE");
Check(TelephonyReader.Parse("mSignalStrength=SignalStrength:{mLte=invalid}") == null,
    "Sinyal: geçersiz veri → null");

// ---------------------------------------------------------------- firmware eşlemesi
var flashPlan = FirmwareFlashPlan.Build(new[]
{
    "boot.img", "recovery.img", "vendor_boot.img", "notes.txt", "weird.img"
});

Check(flashPlan.Count == 3, "Firmware: yalnızca bilinen bölümler (" + flashPlan.Count + ")");
Check(flashPlan.Any(p => p.Partition == "boot"), "Firmware: boot eşleşti");
Check(flashPlan.Any(p => p.Partition == "vendor_boot"), "Firmware: vendor_boot eşleşti");
Check(flashPlan.All(p => p.Partition != "weird"), "Firmware: bilinmeyen dosya atlandı");

// ---------------------------------------------------------------- işlem geçmişi
var deviceVm = new DeviceViewModel();
deviceVm.Record("Test işlemi", "detay", ActivityStatus.Success);
Check(deviceVm.Activities.Count == 1, "Geçmiş: kayıt eklendi");
Check(deviceVm.Activities[0].Title == "Test işlemi", "Geçmiş: başlık doğru");
Check(!string.IsNullOrWhiteSpace(deviceVm.Activities[0].TimeText), "Geçmiş: saat üretildi");

for (var i = 0; i < 120; i++)
{
    deviceVm.Record("Kayıt " + i, "", ActivityStatus.Info);
}

Check(deviceVm.Activities.Count <= 80, "Geçmiş: 80 kayıtla sınırlı (" + deviceVm.Activities.Count + ")");
deviceVm.Dispose();

// ---------------------------------------------------------------- root durumu
var rootProps = new Dictionary<string, string>
{
    ["ro.boot.flash.locked"] = "0",
    ["ro.boot.verifiedbootstate"] = "orange"
};

var rootStatus = RootHelper.Evaluate(
    rootProps, "/system/bin/su\n", "package:com.topjohnwu.magisk");

Check(rootStatus.BootloaderUnlocked, "Root: kilit açık algılandı");
Check(rootStatus.Rooted, "Root: su algılandı");
Check(rootStatus.MagiskInstalled, "Root: Magisk algılandı");
Check(rootStatus.VerifiedBoot == "orange", "Root: verified boot okundu");

var lockedStatus = RootHelper.Evaluate(
    new Dictionary<string, string> { ["ro.boot.flash.locked"] = "1" }, "", "");

Check(!lockedStatus.BootloaderUnlocked, "Root: kilitli algılandı");
Check(!lockedStatus.Rooted && !lockedStatus.MagiskInstalled, "Root: root/Magisk yok");

// ---------------------------------------------------------------- Magisk sürüm ayrıştırma
var magiskJson = @"{
  ""tag_name"": ""v31.1"",
  ""assets"": [
    { ""name"": ""app-debug.apk"", ""size"": 37034525, ""browser_download_url"": ""https://example/app-debug.apk"" },
    { ""name"": ""Magisk-v31.1.apk"", ""size"": 10679283, ""browser_download_url"": ""https://example/Magisk-v31.1.apk"" }
  ]
}";

var release = MagiskInstaller.Parse(magiskJson);
Check(release != null, "Magisk: sürüm ayrıştırıldı");
Check(release != null && release.Tag == "v31.1", "Magisk: tag doğru");
Check(release != null && release.ApkName == "Magisk-v31.1.apk", "Magisk: doğru APK seçildi");
Check(MagiskInstaller.Parse("{}") == null, "Magisk: boş JSON → null");

// ---------------------------------------------------------------- firmware boot çıkarma
var zipPath = Path.Combine(AppContext.BaseDirectory, "test-firmware.zip");

if (File.Exists(zipPath)) File.Delete(zipPath);

using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
{
    var entry = archive.CreateEntry("images/boot.img");
    using (var stream = entry.Open())
    {
        stream.Write(new byte[] { 0x4B, 0x43, 0x54, 0x31 }, 0, 4);
    }

    archive.CreateEntry("images/vendor_boot.img");
}

var extracted = FirmwareBootExtractor.ExtractBootFromZip(
    zipPath, Path.Combine(AppContext.BaseDirectory, "firmware-out"));

Check(extracted != null && File.Exists(extracted), "Firmware: boot.img çıkarıldı");
Check(extracted != null && File.ReadAllBytes(extracted).SequenceEqual(new byte[] { 0x4B, 0x43, 0x54, 0x31 }),
    "Firmware: içerik doğru");
Check(extracted == null || !Path.GetFileName(extracted).Contains("vendor"),
    "Firmware: vendor_boot seçilmedi");
Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI"
    : "SONUC: " + failures.Count + " test basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;
