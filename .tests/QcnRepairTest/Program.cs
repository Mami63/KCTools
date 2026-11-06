using System.IO;
using System.Text;
using KCTools.Common;
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

// ---------------------------------------------------------------- Luhn
const string Prefix = "49015420323751";
var KnownImei = Prefix + ModemService.ComputeLuhnCheckDigit(Prefix);
var WrongCheck = Prefix + ((ModemService.ComputeLuhnCheckDigit(Prefix) + 1) % 10);

Check(ModemService.IsValidImei(KnownImei), "Luhn: bilinen IMEI geçerli");
Check(!ModemService.IsValidImei(WrongCheck), "Luhn: hatalı son hane reddedildi");

var normalized = ModemService.TryNormalizeImei(
    WrongCheck, out var fixedImei, out var error, out var suggestion);

Check(!normalized, "Luhn: hatalı IMEI normalize edilmedi");
Check(suggestion == KnownImei, "Luhn: doğru öneri üretildi (" + suggestion + ")");
Check(error != null && error.Contains("Luhn"), "Luhn: hata mesajı açıklayıcı");

// ---------------------------------------------------------------- Sentetik QCN
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var samplePath = Path.Combine(root, "sample.qcn");

if (File.Exists(samplePath))
{
    File.Delete(samplePath);
}

var data = new byte[8192];
for (var i = 0; i < data.Length; i++)
{
    data[i] = 0x00;
}

var asciiImei = Encoding.ASCII.GetBytes(KnownImei);
Array.Copy(asciiImei, 0, data, 256, asciiImei.Length);

var bcdImei = Hex.ImeiToBcd(KnownImei);
Array.Copy(bcdImei, 0, data, 1024, bcdImei.Length);

File.WriteAllBytes(samplePath, data);
Console.WriteLine("sentetik QCN: " + samplePath + " (" + data.Length + " bayt)");

// ---------------------------------------------------------------- Ara
var doc = QcnDocument.Load(samplePath);
var matches = doc.FindImei(KnownImei);

Console.WriteLine("eşleşme: " + matches.Count);
foreach (var match in matches)
{
    Console.WriteLine("  " + match.Kind + " @ " + match.OffsetText + " len=" + match.Length);
}

Check(matches.Count == 2, "QCN: ASCII + BCD eşleşmesi bulundu");
Check(matches.Any(m => m.Kind.Contains("ASCII")), "QCN: ASCII eşleşmesi var");
Check(matches.Any(m => m.Kind.Contains("BCD")), "QCN: BCD eşleşmesi var");

// ---------------------------------------------------------------- Değiştir
var prefix = "35693803564380";
var newImei = prefix + ModemService.ComputeLuhnCheckDigit(prefix);

Check(ModemService.IsValidImei(newImei), "Yeni IMEI Luhn uyumlu (" + newImei + ")");

var replaced = 0;

foreach (var match in matches)
{
    if (doc.ReplaceImei(match, newImei, out var replaceError))
    {
        replaced++;
    }
    else
    {
        Console.WriteLine("  değiştirilemedi: " + replaceError);
    }
}

Check(replaced == matches.Count, "QCN: tüm eşleşmeler değiştirildi (" + replaced + ")" );

if (doc.IsDirty == false)
{
    failures.Add("QCN: IsDirty true olmalıydı");
}

var backupPath = doc.Save();

Check(!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath), "QCN: yedek dosyası oluştu");

// ---------------------------------------------------------------- Diskte doğrula
var after = File.ReadAllBytes(samplePath);
var newAscii = Encoding.ASCII.GetBytes(newImei);
var newBcd = Hex.ImeiToBcd(newImei);

var asciiOk = after.Skip(256).Take(newAscii.Length).SequenceEqual(newAscii);
var bcdOk = after.Skip(1024).Take(newBcd.Length).SequenceEqual(newBcd);
var oldGone = !after.Skip(256).Take(KnownImei.Length).SequenceEqual(asciiImei);

Check(asciiOk, "Disk: ASCII IMEI güncellendi");
Check(bcdOk, "Disk: BCD IMEI güncellendi");
Check(oldGone, "Disk: eski IMEI kalmadı");

var backupBytes = File.ReadAllBytes(backupPath);
var backupHasOld = backupBytes.Skip(256).Take(asciiImei.Length).SequenceEqual(asciiImei);
Check(backupHasOld, "Yedek: eski IMEI korunuyor");

// ---------------------------------------------------------------- Yeniden yükle
var reloaded = QcnDocument.Load(samplePath);
var reloadedMatches = reloaded.FindImei(newImei);
Check(reloadedMatches.Count == 2, "Yeniden yükleme: yeni IMEI bulundu (ASCII + BCD)");
Check(reloaded.FindImei(KnownImei).Count == 0, "Yeniden yükleme: eski IMEI yok");

// ---------------------------------------------------------------- Rapor

// ---------------------------------------------------------------- yedekten geri yükleme
var restoredSafety = QcnStore.Restore(backupPath, samplePath);
var restoredBytes = File.ReadAllBytes(samplePath);
var firstDiff = -1;
for (var i = 0; i < Math.Min(backupBytes.Length, restoredBytes.Length); i++) { if (backupBytes[i] != restoredBytes[i]) { firstDiff = i; break; } }
Check(restoredBytes.SequenceEqual(backupBytes), "Geri yükleme: içerik yedeğe eşit");
Check(File.Exists(restoredSafety), "Geri yükleme: güvenlik kopyası alındı");

var afterRestore = QcnDocument.Load(samplePath);
Check(afterRestore.FindImei(KnownImei).Count == 2, "Geri yükleme: eski IMEI geri geldi");
Check(afterRestore.FindImei(newImei).Count == 0, "Geri yükleme: yeni IMEI kalmadı");
Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI (" + (backupPath != null ? "yedek: " + Path.GetFileName(backupPath) : "yedek yok") + ")"
    : "SONUC: " + failures.Count + " test basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;
