using System.Text;
using System.Text.RegularExpressions;

var exe = args[0];
var outPath = args[1];

var bytes = File.ReadAllBytes(exe);

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var enc = Encoding.GetEncoding(1254);

var latin = enc.GetString(bytes);
var utf16 = Encoding.Unicode.GetString(bytes);
var ascii = Encoding.ASCII.GetString(bytes);

var rx = new Regex(
    @"[\u0020-\u007E\u00A0-\u00FF\u011E\u011F\u0130\u0131\u015E\u015F\u00C7\u00E7\u2013\u2014\u2022\u20BA]{4,120}",
    RegexOptions.Compiled);

var keywords = new[]
{
    "format", "recovery", "yedek", "flash", "firmware", "frp", "imei", "unlock", "kilit",
    "bootloader", "edl", "qfil", "diag", "repair", "onar", "samsung", "xiaomi", "huawei",
    "oppo", "vivo", "realme", "tecno", "infinix", "nokia", "mtk", "qualcomm", "spreadtrum",
    "root", "magisk", "wipe", "backup", "restore", "partition", "qcn", "efs", "persist",
    "modem", "baseband", "cihaz", "telefon", "baglan", "bağlan", "kapat", "başlat", "baslat",
    "yazılım", "yazilim", "sıfırla", "sifirla", "hesap", "account", "bypass", "kaldır",
    "kaldir", "kilidi", "desen", "şifre", "sifre", "puk", "network", "operator", "operatör",
    "frp", "mdm", "kg", "knox", "eft", "pro", "tool", "server"
};

var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

void Add(string value)
{
    var s = value.Trim();
    if (s.Length >= 4 && s.Length <= 120)
    {
        set.Add(s);
    }
}

foreach (Match m in rx.Matches(latin)) Add(m.Value);
foreach (Match m in rx.Matches(utf16)) Add(m.Value);
foreach (Match m in rx.Matches(ascii)) Add(m.Value);

var hits = new List<string>();

foreach (var s in set)
{
    var lower = s.ToLowerInvariant();

    foreach (var k in keywords)
    {
        if (lower.Contains(k))
        {
            hits.Add(s);
            break;
        }
    }
}

var ordered = hits.Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
File.WriteAllLines(outPath, ordered);

Console.WriteLine("unique strings: " + set.Count);
Console.WriteLine("keyword hits  : " + ordered.Count);
