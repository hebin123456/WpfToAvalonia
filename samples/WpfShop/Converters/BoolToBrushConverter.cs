using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WpfShop.Converters
{
    /// <summary>验证 IValueConverter 迁移（签名兼容，仅命名空间变化）。</summary>
    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? Brushes.Green : Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
