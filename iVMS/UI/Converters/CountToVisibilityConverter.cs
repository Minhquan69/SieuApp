using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace V3SClient.UI.Converters
{
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = (int)value;
            bool reverse = parameter?.ToString().ToLower() == "inverse";
            
            if (reverse)
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
















