using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace FileConverter.ValueConverters.Generic
{
    /// <summary>
    /// 从 ICO 文件或资源中选择指定尺寸的图像。
    /// 无精确尺寸时优先选择最接近的较小图像，否则选择最小的可用图像。
    /// 未指定尺寸时选择最小图像。
    /// </summary>
    public class IcoFileSizeSelectorConverter : IValueConverter
    {
        public virtual object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var size = string.IsNullOrWhiteSpace(parameter?.ToString()) ? 0 : System.Convert.ToInt32(parameter);

            var uri = value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(uri))
            {
                return null;
            }

            if (!uri.StartsWith("pack:"))
            {
                uri = $"pack://application:,,,{uri}";
            }

            var decoder = BitmapDecoder.Create(new Uri(uri), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);

            var result = decoder.Frames.Where(f => f.Width <= size).OrderByDescending(f => f.Width).FirstOrDefault()
                         ?? decoder.Frames.OrderBy(f => f.Width).FirstOrDefault();

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
