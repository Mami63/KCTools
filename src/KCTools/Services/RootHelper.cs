using System;
using System.Collections.Generic;

namespace KCTools.Services
{
    public sealed class RootStatus
    {
        public bool BootloaderUnlocked { get; set; }

        public bool Rooted { get; set; }

        public bool MagiskInstalled { get; set; }

        public string VerifiedBoot { get; set; } = "—";

        public string Summary =>
            (BootloaderUnlocked ? "Kilit açık" : "Kilitli") +
            " · root " + (Rooted ? "var" : "yok") +
            " · Magisk " + (MagiskInstalled ? "kurulu" : "yok") +
            " · " + VerifiedBoot;
    }

    /// <summary>
    /// Root durumunu yalnızca resmi sinyallerden değerlendirir:
    /// ro.boot.flash.locked, `which su`, Magisk paketi. Exploit/root aracı içermez.
    /// </summary>
    public static class RootHelper
    {
        public const string MagiskPackage = "com.topjohnwu.magisk";

        public static RootStatus Evaluate(
            IDictionary<string, string> props,
            string suOutput,
            string packageList)
        {
            var status = new RootStatus();

            if (props != null)
            {
                if (props.TryGetValue("ro.boot.flash.locked", out var locked))
                {
                    status.BootloaderUnlocked = locked == "0";
                }

                if (props.TryGetValue("ro.boot.verifiedbootstate", out var verified))
                {
                    status.VerifiedBoot = verified;
                }
                else if (props.TryGetValue("ro.boot.veritymode", out var verity))
                {
                    status.VerifiedBoot = verity;
                }
            }

            status.Rooted = !string.IsNullOrWhiteSpace(suOutput) &&
                            suOutput.IndexOf("su", StringComparison.OrdinalIgnoreCase) >= 0;

            status.MagiskInstalled = !string.IsNullOrWhiteSpace(packageList) &&
                                     packageList.IndexOf(
                                         MagiskPackage,
                                         StringComparison.OrdinalIgnoreCase) >= 0;

            return status;
        }
    }
}
