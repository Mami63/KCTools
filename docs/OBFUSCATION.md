# Kod Karartma (Obfuscation) — KC Tools

.NET derlemeleri kolayca geri çözülebilir. Karartma **güvenlik sağlamaz**, yalnızca
tersine mühendislik maliyetini yükseltir. Bu projede gerçek koruma sunucu tarafındadır:
istemci hiçbir zaman JWT imza anahtarını, lisans veritabanını veya kredi kurallarını
içermez; yalnızca sunucunun verdiği süreli token'ı taşır.

## Neyi koruyoruz / neyi korumak zorundayız

| Korunur | Neden |
|---|---|
| `KCTools.App`, `MainWindow`, `Views.*`, `Controls.*` | XAML/BAML tipleri **ada göre** çözer |
| `ViewModels.*`, `Models.*` | `DataTemplate DataType`, `x:Type`, binding yolları |
| `Services.LogHub`, `LicenseService`, `Toolchain` | WPF binding'i bu tiplerin **özellik adlarını** yansıtır |
| `KeepPublicApi=true` | Genel üyeler binding için sabit kalır |

Geriye kalanlar (`Services/` iç mantığı, `Protocol/`, `Security/` istemci kodu) karartılır;
`HideStrings=true` ile anlamlı sabitler şifrelenir.

## Çalıştırma

```powershell
dotnet tool install --global Obfuscar.GlobalTool
dotnet publish src/KCTools -c Release
obfuscar.console obfuscar.xml
# çıktı: dist\obfuscated\KCTools.dll (+ bağımlılıklar kopyalanmalı)
```

Yapılandırma: [`obfuscar.xml`](../obfuscar.xml)

> Karartma sonrası uygulamayı **mutlaka** açıp tüm sayfaları gezin: WPF, eksik tip/ad
> yüzünden `XamlParseException` fırlatırsa ilgili tipi `SkipType` listesine ekleyin.

## Ek sertleştirme önerileri

1. **Sunucu otoritesi** — kredi düşme, HWID kilidi ve plan kontrolü yalnızca API'de.
   İstemci yalnızca görüntüler; token'ı değiştirmek imza nedeniyle işe yaramaz.
2. **HTTPS zorunlu** — istemci sertifika doğrulamasını kapatmaz; üretimde geçerli
   sertifika kullanın, gerekirse sertifika pinleme ekleyin.
3. **DPAPI** — oturum dosyası `ProtectedData` (CurrentUser) ile şifrelenir; başka
   kullanıcı/makinede açılamaz.
4. **İmzalama** — yayınlanan EXE'yi kod imzasıyla imzalayın (SmartScreen + kurcalama
   tespiti için).
5. **String gizleme** — API adresleri/anahtar kalıpları düz metin yerine yapılandırmadan
   veya karartılmış sabitlerden gelsin.
6. **Güncelleme kanalı** — eski sürümleri sunucuda reddedin (`appVersion` alanı zaten
   gönderiliyor).
7. **Hız sınırı / tekrar deneme** — sunucuda IP + HWID bazlı rate limit uygulayın.

## Sınırlar

- Karartma, kararlı bir saldırganı durdurmaz; yalnızca zaman maliyeti ekler.
- İstemci tarafı lisans kontrolü **her zaman** atlatılabilir; bu yüzden değerli iş
  (kredi düşme, plan yetkisi, cihaz başına limit) sunucuda tutulur.
- `PublishSingleFile` + trimming WPF ile sınırlıdır; bu projede kullanılmadı.
