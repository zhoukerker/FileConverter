// <copyright file="ConversionSettingsToString.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ValueConverters
{
    using System;
    using System.Globalization;
    using System.Windows.Data;

    public class ConversionSettingsRelevant : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }

            if (!(value is IConversionSettings))
            {
                throw new ArgumentException("值必须为转换设置对象。");
            }

            IConversionSettings settings = (IConversionSettings)value;

            string key = parameter as string;
            if (key == null)
            {
                throw new ArgumentException("参数必须为字符串。");
            }

            return settings.ContainsKey(key);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
