using System;

namespace KCTools.Models
{
    /// <summary>
    /// Cihaz servisinin ürettiği değişmez algılama kaydı.
    /// "COM4 Connected (Qualcomm EDL)" biçimindeki metni <see cref="Display"/> üretir.
    /// </summary>
    public sealed class DetectedDevice
    {
        public string Key { get; set; }

        /// <summary>Windows PnP örnek kimliği (USB\VID_05C6&amp;PID_9008\...).</summary>
        public string InstanceId { get; set; }

        /// <summary>Windows aygıt adı ("Qualcomm HS-USB QDLoader 9008 (COM4)").</summary>
        public string Name { get; set; }

        public DeviceKind Kind { get; set; }

        /// <summary>Varsa COM portu ("COM4"), yoksa null.</summary>
        public string Port { get; set; }

        public string Serial { get; set; }

        public string Vid { get; set; }

        public string Pid { get; set; }

        /// <summary>PnP sürücü servisi ("qcusbser", "usbser" ...).</summary>
        public string Service { get; set; }

        public string Manufacturer { get; set; }

        public string Status { get; set; }

        public bool IsConnected { get; set; } = true;

        /// <summary>Kaynak: wmi | registry (seri port yedeği).</summary>
        public string Source { get; set; } = "wmi";

        public string PortLabel => string.IsNullOrWhiteSpace(Port) ? "USB" : Port;

        public string ModeLabel => DeviceModes.Label(Kind);

        public string ModeShort => DeviceModes.Short(Kind);

        public string IconKey => DeviceModes.IconKey(Kind);

        /// <summary>Liste/konsol kısa gösterimi: "COM4 · Qualcomm EDL".</summary>
        public string ShortDisplay => $"{PortLabel} · {ModeLabel}";

        /// <summary>İstenen biçim: "COM4 Connected (Qualcomm EDL)".</summary>
        public string Display =>
            $"{PortLabel} {(IsConnected ? "Connected" : "Disconnected")} ({ModeLabel})";

        public string Details =>
            $"{Name}{Environment.NewLine}{InstanceId}{Environment.NewLine}" +
            $"VID:{Vid ?? "—"}  PID:{Pid ?? "—"}  Servis:{Service ?? "—"}";

        public int SortOrder => DeviceModes.SortOrder(Kind);

        /// <summary>Kaldırma olayında durum metnini "Disconnected" yapmak için kopya.</summary>
        public DetectedDevice AsDisconnected()
        {
            return new DetectedDevice
            {
                Key = Key,
                InstanceId = InstanceId,
                Name = Name,
                Kind = Kind,
                Port = Port,
                Serial = Serial,
                Vid = Vid,
                Pid = Pid,
                Service = Service,
                Manufacturer = Manufacturer,
                Status = Status,
                Source = Source,
                IsConnected = false
            };
        }
    }
}
