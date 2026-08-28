using System.Windows;
using System.Windows.Controls;

namespace WpfShop.Controls
{
    /// <summary>自定义控件：验证 DependencyProperty → StyledProperty 转换。</summary>
    public class Spinner : Control
    {
        public static readonly DependencyProperty AngleProperty =
            DependencyProperty.Register("Angle", typeof(double), typeof(Spinner), new PropertyMetadata(0.0));

        public double Angle
        {
            get { return (double)GetValue(AngleProperty); }
            set { SetValue(AngleProperty, value); }
        }
    }
}
