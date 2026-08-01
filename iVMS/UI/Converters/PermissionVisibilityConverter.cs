using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;
using V3SClient.libs;

namespace V3SClient.UI.Converters
{
    public class PermissionVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null) return Visibility.Visible;
            
            string permissionString = parameter.ToString();
            string[] permissions = permissionString.Split('|');
            
            bool hasPermission = permissions.Any(p => GlobalUserInfo.Instance.HasPermission(p.Trim()));
            
            return hasPermission ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
