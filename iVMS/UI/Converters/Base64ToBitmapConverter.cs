using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Diagnostics;


namespace V3SClient.libs
{

    public class Base64ToBitmapConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
               
                // Xá»­ lÃ½ trÆ°á»ng há»£p null an toÃ n
                if (value == null)
                {
                    Debug.WriteLine("Value is null");
                    return null;
                }

                string base64String = value as string;
                if (string.IsNullOrWhiteSpace(base64String))
                {
                    Debug.WriteLine("Base64 string is null or empty");
                    return null;
                }

                byte[] binaryData = System.Convert.FromBase64String(base64String);

                using (var memoryStream = new MemoryStream(binaryData))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.None;
                    bitmap.StreamSource = memoryStream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (ArgumentNullException ex)
            {
                Debug.WriteLine($"ArgumentNullException: {ex.ParamName}");
                return null;
            }
            catch (FormatException)
            {
                Debug.WriteLine("Invalid base64 format");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Image conversion error: {ex.Message}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DateTimeStringFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;

            string dateTimeString = value.ToString();
            if (string.IsNullOrWhiteSpace(dateTimeString)) return null;

            try
            {
                // Parse chuá»—i thành DateTime
                if (DateTime.TryParse(dateTimeString, out DateTime dateTime))
                {
                    // Tráº£ vá» format: ngÃ y giá»:phÃºt:giÃ¢y
                    return dateTime.ToString("dd MMM HH:mm:ss");
                }
                return dateTimeString; // Tráº£ vá» chuá»—i gá»‘c náº¿u không parse được
            }
            catch
            {
                return dateTimeString;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
















