// <copyright file="StringToValueType.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ValueConverters.Generic
{
    using System;
    using System.Globalization;
    using System.Windows.Data;

    public class StringToValueType : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }

            string typeName = parameter as string;
            if (typeName == null)
            {
                throw new ArgumentNullException(nameof(parameter), "参数必须包含可转换的类型名称。");
            }

            Type type = Type.GetType(typeName);
            if (type == null)
            {
                throw new Exception("转换类型无效：" + typeName + "。");
            }

            return System.Convert.ChangeType(value, type, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }
            
            return System.Convert.ChangeType(value, typeof(string), culture);
        }
    }
}
