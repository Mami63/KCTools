using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace KCTools.Security
{
    /// <summary>HWID bileşenleri (arayüzde maskelenmiş gösterilir).</summary>
    public sealed class HwidDetails
    {
        public string Hwid { get; set; } = "—";

        public string MachineGuid { get; set; } = "—";

        public string BoardSerial { get; set; } = "—";

        public string CpuId { get; set; } = "—";

        public string VolumeSerial { get; set; } = "—";

        public int SourceCount { get; set; }

        public string Summary =>
            Hwid + "  ·  " + SourceCount + "/4 kaynak";
    }

    /// <summary>
    /// Donanım kimliği: MachineGuid + anakart seri no + CPU kimliği + sistem diski
    /// seri numarası SHA-256 ile özetlenir. Eksik/placeholder değerler atlanır,
    /// hiçbiri okunamazsa makine adına düşülür.
    /// </summary>
    public static class HwidProvider
    {
        private static readonly string[] Placeholders =
        {
            "0", "00000000", "N/A", "NONE", "NONE.", "UNKNOWN", "INVALID",
            "DEFAULT STRING", "TO BE FILLED BY O.E.M.", "TO BE FILLED BY OEM",
            "SYSTEM SERIAL NUMBER", "SYSTEM SERIAL NUMBER.", "NOT SPECIFIED",
            "CHASSIS SERIAL NUMBER", "BASE BOARD SERIAL NUMBER"
        };

        public static string GetHwid()
        {
            return GetDetails().Hwid;
        }

        public static HwidDetails GetDetails()
        {
            var machineGuid = ReadMachineGuid();
            var board = Query("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
            var cpu = Query("SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
            var volume = Query(
                "SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID='C:'",
                "VolumeSerialNumber");

            var parts = new List<string>();

            if (IsUsable(machineGuid))
            {
                parts.Add("MG:" + Normalize(machineGuid));
            }

            if (IsUsable(board))
            {
                parts.Add("BB:" + Normalize(board));
            }

            if (IsUsable(cpu))
            {
                parts.Add("CPU:" + Normalize(cpu));
            }

            if (IsUsable(volume))
            {
                parts.Add("VOL:" + Normalize(volume));
            }

            var hwid = parts.Count > 0
                ? Hash(string.Join("|", parts))
                : Hash("FALLBACK|" + Environment.MachineName + "|" + Environment.UserName);

            return new HwidDetails
            {
                Hwid = hwid,
                MachineGuid = Mask(machineGuid),
                BoardSerial = Mask(board),
                CpuId = Mask(cpu),
                VolumeSerial = Mask(volume),
                SourceCount = parts.Count
            };
        }

        private static string ReadMachineGuid()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    return key?.GetValue("MachineGuid") as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Query(string wql, string property)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(wql))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        using (item)
                        {
                            var value = item[property]?.ToString();

                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                return value.Trim();
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // WMI erişilemezse diğer kaynaklarla devam edilir.
            }

            return null;
        }

        private static bool IsUsable(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var compact = Normalize(value);

            if (compact.Length < 3)
            {
                return false;
            }

            // Yalnızca tam eşleşme: "0" gibi kısa dizelerin alt-dize olarak
            // eşleşmesi geçerli seri numaralarını yanlışlıkla eler.
            foreach (var placeholder in Placeholders)
            {
                if (string.Equals(compact, Normalize(placeholder), StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return !compact.StartsWith("TOBEFILLEDBY", StringComparison.Ordinal) &&
                   !compact.StartsWith("DEFAULTSTRING", StringComparison.Ordinal);
        }

        private static string Normalize(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (var c in value.Trim().ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static string Hash(string raw)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes("KCTools-HWID-v1|" + raw));
                var hex = Convert.ToHexString(hash, 0, 8);

                return hex.Substring(0, 4) + "-" + hex.Substring(4, 4) + "-" +
                       hex.Substring(8, 4) + "-" + hex.Substring(12, 4);
            }
        }

        private static string Mask(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "—";
            }

            var trimmed = value.Trim();

            return trimmed.Length <= 6
                ? trimmed
                : trimmed.Substring(0, 3) + "…" + trimmed.Substring(trimmed.Length - 3);
        }
    }
}
