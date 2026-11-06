namespace KCTools.Models
{
    /// <summary>fastboot getvar all çıktısından derlenen cihaz bilgisi.</summary>
    public sealed class FastbootInfo
    {
        public string Serial { get; set; } = "—";

        public string Product { get; set; } = "—";

        public string BootloaderVersion { get; set; } = "—";

        public string ActiveSlot { get; set; } = "—";

        public string SlotCount { get; set; } = "—";

        public string Unlocked { get; set; } = "Bilinmiyor";

        public string SecureBoot { get; set; } = "—";

        public string Battery { get; set; } = "—";

        public string Baseband { get; set; } = "—";

        public string UnlockAbility { get; set; } = "—";

        public int VariableCount { get; set; }

        public bool IsUnlocked { get; set; }

        public static FastbootInfo Empty { get; } = new FastbootInfo();
    }
}
