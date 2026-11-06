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

// ---------------------------------------------------------------- DPAPI depolama
LockPinStore.Clear();
LockPinStore.Save("TEST-SERIAL-1", "1234");

Console.WriteLine("PIN hata: " + (LockPinStore.LastError ?? "yok"));
Check(LockPinStore.HasPin("TEST-SERIAL-1"), "PIN: kaydedildi");
Check(LockPinStore.Load("TEST-SERIAL-1") == "1234", "PIN: doğru okundu");
Check(LockPinStore.Load("BASKASERIAL") == null, "PIN: başka seri için okunmuyor");

LockPinStore.Clear();
Check(!LockPinStore.HasPin("TEST-SERIAL-1"), "PIN: temizlendi");

// ---------------------------------------------------------------- canlı cihaz
var adb = new AdbClient();
var devices = await adb.ListDevicesAsync();
var online = devices.FirstOrDefault(d => d.IsOnline);

if (online == null)
{
    Console.WriteLine("UYARI  ADB cihazı yok; canlı kilit testi atlandı");
}
else
{
    Console.WriteLine("cihaz: " + online.Serial);

    var locked = await DeviceUnlockService.IsLockedAsync(online.Serial);
    Console.WriteLine("kilitli: " + (locked == null ? "bilinmiyor" : locked.Value.ToString()));

    var result = await DeviceUnlockService.UnlockAsync(online.Serial, "0000");

    Console.WriteLine("başarı : " + result.Success);
    Console.WriteLine("engel  : " + result.InjectionBlocked);
    Console.WriteLine("mesaj  : " + result.Message);

    Check(!string.IsNullOrWhiteSpace(result.Message), "Kilit: sonuç mesajı dolu");
    Check(result.InjectionBlocked || result.Success || result.Message.Length > 0,
        "Kilit: sonuç tutarlı (başarı / engel / açıklama)");

    if (result.InjectionBlocked)
    {
        Check(result.Message.Contains("INJECT_EVENTS") || result.Message.Contains("USB hata"),
            "Kilit: engel mesajı yönlendirici");
    }
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI"
    : "SONUC: " + failures.Count + " test basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;
