using System.IO;
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

Console.WriteLine("Magisk resmi sürümü alınıyor...");
var release = await MagiskInstaller.GetLatestAsync();

Check(release != null, "Sürüm: GitHub'dan alındı");

if (release == null)
{
    Console.WriteLine("SONUC: BASARISIZ");
    return 1;
}

Console.WriteLine("sürüm  : " + release.Tag);
Console.WriteLine("dosya  : " + release.ApkName);
Console.WriteLine("boyut  : " + (release.Size / 1024 / 1024) + " MB");

var path = await MagiskInstaller.DownloadAsync(release);

Check(!string.IsNullOrWhiteSpace(path) && File.Exists(path), "İndirme: APK indirildi");

if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
{
    var length = new FileInfo(path).Length;
    Console.WriteLine("indirilen: " + (length / 1024 / 1024) + " MB · " + path);
    Check(length == release.Size, "İndirme: boyut doğru");
    Check(length > 5 * 1024 * 1024, "İndirme: makul boyut");
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI"
    : "SONUC: " + failures.Count + " basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;
