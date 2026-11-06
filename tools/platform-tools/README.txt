KC Tools — araç zinciri klasörü
===============================

Bu klasöre Android platform-tools paketini koyun:

    tools/platform-tools/adb.exe
    tools/platform-tools/fastboot.exe
    tools/platform-tools/AdbWinApi.dll
    tools/platform-tools/AdbWinUsbApi.dll

İndirme: https://developer.android.com/tools/releases/platform-tools

Arama sırası (Toolchain.cs):
  1. KCTOOLS_TOOLS ortam değişkeni
  2. <uygulama klasörü>/tools/platform-tools
  3. <uygulama klasörü>/platform-tools
  4. Depo köküne doğru 4 üst klasörde tools/platform-tools
  5. %ANDROID_HOME% / %ANDROID_SDK_ROOT% / %LOCALAPPDATA%\Android\Sdk
  6. C:\platform-tools
  7. PATH

Araçlar bulunamazsa ADB ve Fastboot sayfaları demo (simülasyon) modunda
çalışmaya devam eder; Ayarlar > Araç Zinciri kartından yolu görebilirsiniz.
