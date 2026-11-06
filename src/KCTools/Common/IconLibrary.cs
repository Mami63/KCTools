using System.Windows;
using System.Windows.Media;

namespace KCTools.Common
{
    /// <summary>XAML kaynaklarındaki Geometry ikonlarına anahtar ile erişim.</summary>
    public static class IconLibrary
    {
        public static Geometry Get(string resourceKey)
        {
            if (string.IsNullOrWhiteSpace(resourceKey) || Application.Current == null)
            {
                return Geometry.Empty;
            }

            return Application.Current.TryFindResource(resourceKey) as Geometry ?? Geometry.Empty;
        }
    }
}
