using System.IO;
using KCTools.Models;
using KCTools.Services;

var adb = new AdbClient();
var devices = await adb.ListDevicesAsync();
var online = devices.FirstOrDefault(d => d.IsOnline);

Console.WriteLine("ADB cihaz: " + (online == null ? "yok" : online.Serial + " · " + online.Model));

if (online == null)
{
    return 2;
}

adb.Serial = online.Serial;

var props = await adb.GetPropsAsync();

string P(params string[] keys)
{
    foreach (var key in keys)
    {
        if (props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return "—";
}

var model = P("ro.product.model");
var codename = P("ro.product.device");
var market = P("ro.product.marketname", "ro.product.odm.marketname", "ro.config.marketing_name");
var brand = P("ro.product.manufacturer");

Console.WriteLine("model    : " + model);
Console.WriteLine("codename : " + codename);
Console.WriteLine("market   : " + market);
Console.WriteLine("brand    : " + brand);

var match = DeviceCatalogService.Instance.Match(model, codename, brand, null);
Console.WriteLine("katalog  : " + match.Display + " · " + match.ConfidenceText + " · " + match.Source);

var snapshot = await TelephonyReader.ReadAsync(adb);
Console.WriteLine("sinyal   : " + (snapshot == null ? "okunamadı" : snapshot.Text));

var ok = online != null
         && match.Model != null
         && match.Model.Code == model
         && (string.IsNullOrWhiteSpace(market) || market == "—" || match.Model.Name == "Redmi 15C 5G")
         && snapshot != null
         && snapshot.Level >= 0;

Console.WriteLine();
Console.WriteLine(ok ? "SONUC: CANLI TEST GECTI" : "SONUC: CANLI TEST BASARISIZ");

return ok ? 0 : 1;
