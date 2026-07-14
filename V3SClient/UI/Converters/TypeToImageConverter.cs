using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace V3SClient.UI.Converters
{
    public class TypeToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string type = value as string;
            string imagePath = string.Empty;

            switch (type?.ToLower())
            {
                case "face":
                    imagePath = "pack://application:,,,/images/face.png";
                    break;
                case "plate":
                    imagePath = "pack://application:,,,/images/licence_plate.png";
                    break;
                default:
                    imagePath = "pack://application:,,,/images/unknow.png";
                    break;
            }

            return new BitmapImage(new Uri(imagePath, UriKind.Absolute));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
















