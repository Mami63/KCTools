using System;

namespace KCTools.Models
{
    public enum ActivityStatus
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>Telefona uygulanan işlemlerin geçmiş kaydı.</summary>
    public sealed class DeviceActivity
    {
        public DateTime Time { get; set; } = DateTime.Now;

        public string Title { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        public ActivityStatus Status { get; set; } = ActivityStatus.Info;

        public string TimeText => Time.ToString("HH:mm:ss");

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case ActivityStatus.Success: return "TAMAM";
                    case ActivityStatus.Warning: return "UYARI";
                    case ActivityStatus.Error: return "HATA";
                    default: return "BİLGİ";
                }
            }
        }
    }
}
