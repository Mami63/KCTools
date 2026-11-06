using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using KCTools.ViewModels;

var failures = new List<string>();

void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "PASS  " : "FAIL  ") + name);

    if (!condition)
    {
        failures.Add(name);
    }
}

static void SetField(object target, string name, object value)
{
    var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
    field?.SetValue(target, value);
}

static async Task<bool> WaitAsync(Func<bool> condition, int timeoutMs = 15000)
{
    var started = Environment.TickCount64;

    while (Environment.TickCount64 - started < timeoutMs)
    {
        if (condition())
        {
            return true;
        }

        await Task.Delay(100);
    }

    return condition();
}

var root = Path.Combine(Path.GetTempPath(), "kctools-ui-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    // ------------------------------------------------------------ Qualcomm ROM flash
    File.WriteAllBytes(Path.Combine(root, "boot.img"), new byte[1024]);
    File.WriteAllBytes(Path.Combine(root, "system.img"), new byte[2048]);

    const string rawprogram = """
        <data>
          <program label="boot" filename="boot.img" start_sector="2048"
                   num_partition_sectors="2048" physical_partition_number="0" />
          <program label="system" filename="system.img" start_sector="4096"
                   num_partition_sectors="4096" physical_partition_number="0" />
        </data>
        """;

    var rawprogramPath = Path.Combine(root, "rawprogram0.xml");
    File.WriteAllText(rawprogramPath, rawprogram);

    var qualcomm = new QualcommViewModel();
    SetField(qualcomm, "_rawProgramPath", rawprogramPath);
    SetField(qualcomm, "_romDirectory", root);

    qualcomm.FlashRomCommand.Execute(null);

    var flashed = await WaitAsync(() => !qualcomm.IsBusy && qualcomm.Progress >= 100);
    Check(flashed, "UI Qualcomm: komut tamamlandı");
    Check(qualcomm.FlashSummary.Contains("2 dosya"), "UI Qualcomm: 2 dosya yazıldı");
    Check(qualcomm.StatusText.Contains("tamamlandı"), "UI Qualcomm: durum metni");
    Check(qualcomm.Progress >= 100, "UI Qualcomm: ilerleme %100");

    // Cihaz yokken WinUSB komutu kilitlenmemeli.
    qualcomm.FlashRomUsbCommand.Execute(null);

    var usbFinished = await WaitAsync(
        () => !qualcomm.IsBusy &&
              (qualcomm.StatusText.Contains("hata", StringComparison.OrdinalIgnoreCase) ||
               qualcomm.StatusText.Contains("WinUSB", StringComparison.OrdinalIgnoreCase)),
        20000);

    Check(usbFinished, "UI Qualcomm: WinUSB cihaz yoksa tamamlanıyor");
    Check(!qualcomm.IsBusy, "UI Qualcomm: WinUSB hatası sonrası serbest");

    // ------------------------------------------------------------ MTK DA upload
    var da = new byte[5000];
    da[0] = (byte)'M';
    da[1] = (byte)'M';
    da[2] = (byte)'M';
    new Random(11).NextBytes(da.AsSpan(4));

    var daPath = Path.Combine(root, "MT6789_DA.bin");
    File.WriteAllBytes(daPath, da);

    var mtk = new MtkViewModel();
    SetField(mtk, "_daPath", daPath);

    mtk.UploadDaCommand.Execute(null);

    var uploaded = await WaitAsync(() => !mtk.IsBusy && mtk.Progress >= 100);
    Check(uploaded, "UI MTK: DA komutu tamamlandı");
    Check(mtk.DaUploadSummary.Contains("blok"), "UI MTK: blok özeti");
    Check(mtk.Progress >= 100, "UI MTK: ilerleme %100");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI"
    : "SONUC: " + failures.Count + " basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;
