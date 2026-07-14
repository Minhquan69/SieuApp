using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace V3SClient.UI.Converters
{
    public class ReflectionBindingConverter : IMultiValueConverter
    {
        private object _editItem;
        public ReflectionBindingConverter(object editItem)
        {
            _editItem = editItem;
        }
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
           
            if (values.Length < 2 || values[0] == null || values[1] == null)
                return null;

            var item = values[0];
            var propName = values[1].ToString();
            var prop = item.GetType().GetProperty(propName);
            return prop?.GetValue(item);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            if (value == null)
                return new object[] { Binding.DoNothing, Binding.DoNothing };

            if (parameter is string propName && _editItem != null)
            {
                var prop = _editItem.GetType().GetProperty(propName);
                if (prop != null && prop.CanWrite)
                {
                    try
                    {
                        object convertedValue = ConvertValue(value, prop.PropertyType, culture);
                        prop.SetValue(_editItem, convertedValue);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ConvertBack Error: {ex.Message}");
                    }
                }
            }

            return new object[] { Binding.DoNothing, Binding.DoNothing };
        }

        private object ConvertValue(object value, Type targetType, CultureInfo culture)
        {
            var type = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return type.IsValueType ? Activator.CreateInstance(type) : null;

            try
            {
                if (type == typeof(Guid))
                    return Guid.TryParse(value.ToString(), out var g) ? g : Guid.Empty;
                if (type.IsEnum)
                    return Enum.Parse(type, value.ToString(), true);
                return System.Convert.ChangeType(value, type, culture);
            }
            catch
            {
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }
        }
    }


}
















