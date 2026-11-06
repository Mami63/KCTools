namespace KCTools.Models
{
    /// <summary>Algılanan aygıtın servis modu.</summary>
    public enum DeviceKind
    {
        Unknown,
        Serial,
        Android,
        Adb,
        Fastboot,
        QualcommEdl,
        QualcommDiag,
        Qualcomm,
        MtkPreloader,
        MtkBrom,
        Mtk
    }

    /// <summary>Mod etiketleri, kısa gösterimler ve simgeler.</summary>
    public static class DeviceModes
    {
        public static string Label(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.QualcommEdl: return "Qualcomm EDL";
                case DeviceKind.QualcommDiag: return "Qualcomm Diag";
                case DeviceKind.Qualcomm: return "Qualcomm";
                case DeviceKind.MtkPreloader: return "MTK Preloader";
                case DeviceKind.MtkBrom: return "MTK BROM";
                case DeviceKind.Mtk: return "MediaTek";
                case DeviceKind.Adb: return "ADB";
                case DeviceKind.Fastboot: return "Fastboot";
                case DeviceKind.Android: return "Android";
                case DeviceKind.Serial: return "Seri Port";
                default: return "Bilinmeyen";
            }
        }

        public static string Short(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.QualcommEdl: return "EDL";
                case DeviceKind.QualcommDiag: return "DIAG";
                case DeviceKind.Qualcomm: return "QC";
                case DeviceKind.MtkPreloader: return "MTK";
                case DeviceKind.MtkBrom: return "MTK";
                case DeviceKind.Mtk: return "MTK";
                case DeviceKind.Adb: return "ADB";
                case DeviceKind.Fastboot: return "FASTBOOT";
                case DeviceKind.Android: return "ANDROID";
                case DeviceKind.Serial: return "SERİ";
                default: return "—";
            }
        }

        public static string IconKey(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.QualcommEdl:
                case DeviceKind.QualcommDiag:
                case DeviceKind.Qualcomm:
                    return "Icon.Chip";

                case DeviceKind.MtkPreloader:
                case DeviceKind.MtkBrom:
                case DeviceKind.Mtk:
                    return "Icon.Layers";

                case DeviceKind.Fastboot:
                    return "Icon.Bolt";

                case DeviceKind.Adb:
                case DeviceKind.Android:
                    return "Icon.Terminal";

                default:
                    return "Icon.Plug";
            }
        }

        /// <summary>Liste sıralaması: EDL/MTK önce, seri portlar sonda.</summary>
        public static int SortOrder(DeviceKind kind)
        {
            switch (kind)
            {
                case DeviceKind.QualcommEdl: return 0;
                case DeviceKind.QualcommDiag: return 1;
                case DeviceKind.Qualcomm: return 2;
                case DeviceKind.MtkPreloader: return 3;
                case DeviceKind.MtkBrom: return 4;
                case DeviceKind.Mtk: return 5;
                case DeviceKind.Fastboot: return 6;
                case DeviceKind.Adb: return 7;
                case DeviceKind.Android: return 8;
                case DeviceKind.Serial: return 9;
                default: return 10;
            }
        }

        public static bool IsAndroid(DeviceKind kind)
        {
            return kind == DeviceKind.Adb || kind == DeviceKind.Fastboot || kind == DeviceKind.Android;
        }
    }
}
