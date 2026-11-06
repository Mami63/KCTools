using System;
using System.Globalization;
using System.Windows.Data;

namespace KCTools.Common
{
    /// <summary>XAML'den ikon anahtarını (string) Geometry'ye çevirir.</summary>
    public sealed class IconKeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return IconLibrary.Get(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
