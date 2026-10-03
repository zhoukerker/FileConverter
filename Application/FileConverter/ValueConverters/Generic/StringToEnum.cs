// <copyright file="StringToEnum.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter.ValueConverters.Generic
{
    using System;
    using System.Globalization;
    using System.Windows.Data;

    public class StringToEnum : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string stringValue = value as string;
            if (value == null)
            {
                // 待办：确认是否需要枚举默认值。
                return null;
            }

            string typeName = parameter as string;
            if (typeName == null)
            {
                throw new ArgumentNullException(nameof(parameter), "参数必须包含枚举类型名称。");
            }

            Type enumType = Type.GetType(typeName);
            if (enumType == null)
            {
                throw new Exception("枚举类型无效：" + typeName + "。");
            }

            return Enum.Parse(enumType, stringValue);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString();
        }
    }
}
