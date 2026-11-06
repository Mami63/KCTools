using System;
using System.Collections.Generic;
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

var vm = new FastbootViewModel();
await vm.RunAsync("info");

Check(!vm.IsBusy, "Fastboot: işlem tamamlandı");
Check(vm.HasDevice, "Fastboot: cihaz algılandı");
Check(vm.Serial != "—" && vm.Serial.Length > 0, "Fastboot: seri numarası okundu");
Check(vm.Product != "—" && vm.Product.Length > 0, "Fastboot: ürün kodu okundu");
Check(vm.BootloaderState.Contains("Kilitli"), "Fastboot: bootloader kilit durumu");
Check(!vm.IsUnlocked, "Fastboot: kilitli cihaz doğrulandı");
Check(vm.ActiveSlot != "—", "Fastboot: aktif slot okundu");

var imeiVm = new ImeiViewModel();
await imeiVm.RunAsync("read");
Check(imeiVm.CurrentImeiStatus.Contains("Fastboot"),
    "IMEI: fastboot modunda okunamadığı kullanıcıya bildirildi");

Console.WriteLine();
Console.WriteLine("Seri     : " + vm.Serial);
Console.WriteLine("Ürün     : " + vm.Product);
Console.WriteLine("Kilit    : " + vm.BootloaderState);
Console.WriteLine("Slot     : " + vm.ActiveSlot);
Console.WriteLine("Secure   : " + vm.SecureBoot);
Console.WriteLine();

Console.WriteLine(failures.Count == 0
    ? "SONUC: TUM TESTLER GECTI"
    : "SONUC: " + failures.Count + " basarisiz -> " + string.Join(", ", failures));

return failures.Count == 0 ? 0 : 1;
