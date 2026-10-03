// <copyright file="OutputTypeToVisibility.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ValueConverters
{
    using System;
    using System.Globalization;
    using System.Windows.Data;

    public class OutputTypeToVisibility : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is OutputType))
            {
                throw new ArgumentException("值必须为输出格式枚举。");
            }

            OutputType outputType = (OutputType)value;
            
            string referenceTypeName = parameter as string;
            if (string.IsNullOrEmpty(referenceTypeName))
            {
                return "Hidden";
            }

            if (!Enum.TryParse(referenceTypeName, out OutputType referenceType))
            {
                return "Hidden";
            }

            return outputType == referenceType ? "Visible" : "Hidden";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
