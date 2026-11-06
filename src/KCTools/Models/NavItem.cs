using System.Windows.Media;

namespace KCTools.Models
{
    /// <summary>Sol navigasyon menüsündeki bir modül.</summary>
    public sealed class NavItem
    {
        public NavItem(string key, string title, string caption, string subtitle, string iconKey, object view)
        {
            Key = key;
            Title = title;
            Caption = caption;
            Subtitle = subtitle;
            IconKey = iconKey;
            Icon = Common.IconLibrary.Get(iconKey);
            View = view;
        }

        public string Key { get; }

        public string Title { get; }

        /// <summary>Menüde başlığın altında görünen kısa etiket.</summary>
        public string Caption { get; }

        /// <summary>Sayfa başlığında görünen uzun açıklama.</summary>
        public string Subtitle { get; }

        public string IconKey { get; }

        public Geometry Icon { get; }

        public object View { get; }
    }
}
