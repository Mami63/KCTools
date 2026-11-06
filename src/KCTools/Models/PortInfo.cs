using System.Windows.Media;
using KCTools.Common;

namespace KCTools.Models
{
    /// <summary>Seri port listesindeki bir giriş.</summary>
    public sealed class PortInfo
    {
        public PortInfo(string name, string device, string friendlyName)
        {
            Name = name;
            Device = device;
            FriendlyName = friendlyName;
        }

        public string Name { get; }

        public string Device { get; }

        public string FriendlyName { get; }

        public string Display => $"{Name}  ·  {FriendlyName}";
    }

    /// <summary>Modül sayfalarındaki işlem kutucuğu.</summary>
    public sealed class ActionTile
    {
        public ActionTile(string key, string title, string description, string iconKey, string category = null)
        {
            Key = key;
            Title = title;
            Description = description;
            Icon = IconLibrary.Get(iconKey);
            Category = category;
        }

        public string Key { get; }

        public string Title { get; }

        public string Description { get; }

        /// <summary>Kutucuk grubu ("Sistem", "Format", ...).</summary>
        public string Category { get; }

        public Geometry Icon { get; }
    }

    /// <summary>Cihaz bilgisi kartındaki etiket / değer satırı.</summary>
    public sealed class DeviceField : ObservableObject
    {
        private string _value;

        public DeviceField(string label, string value = "—")
        {
            Label = label;
            _value = value;
        }

        public string Label { get; }

        public string Value
        {
            get => _value;
            set => Set(ref _value, value);
        }
    }

    /// <summary>ADB cihaz listesi satırı.</summary>
    public sealed class AdbDevice
    {
        public AdbDevice(string serial, string model, string state)
        {
            Serial = serial;
            Model = string.IsNullOrWhiteSpace(model) ? "—" : model;
            State = string.IsNullOrWhiteSpace(state) ? "unknown" : state;
        }

        public string Serial { get; }

        public string Model { get; set; }

        public string State { get; }

        public string Product { get; set; }

        public string DeviceName { get; set; }

        public string TransportId { get; set; }

        public bool IsOnline =>
            string.Equals(State, "device", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Arayüzde gösterilen Türkçe durum etiketi.</summary>
        public string StateLabel
        {
            get
            {
                switch (State.ToLowerInvariant())
                {
                    case "device": return "çevrimiçi";
                    case "unauthorized": return "yetkisiz";
                    case "offline": return "çevrimdışı";
                    case "recovery": return "recovery";
                    case "sideload": return "sideload";
                    case "bootloader": return "fastboot";
                    case "no permissions": return "izin yok";
                    default: return State;
                }
            }
        }
    }
}
