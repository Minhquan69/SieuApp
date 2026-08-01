using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace V3SClient.UI.Converters
{
    public class NewFolderBadgeVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3) return Visibility.Collapsed;
            string folderPath = values[0]?.ToString();
            string newestPath = values[1]?.ToString();
            DateTime expiresAt;
            if (string.IsNullOrWhiteSpace(folderPath) ||
                !string.Equals(folderPath, newestPath, StringComparison.OrdinalIgnoreCase) ||
                !DateTime.TryParse(values[2]?.ToString(), out expiresAt) ||
                DateTime.Now >= expiresAt)
                return Visibility.Collapsed;

            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
