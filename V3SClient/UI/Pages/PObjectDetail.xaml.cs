using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using V3SClient.window;

namespace V3SClient.UI.Pages
{
    /// <summary>
    /// Interaction logic for PObjectDetail.xaml
    /// </summary>
    public partial class PObjectDetail : Page
    {
        private ImageViewerWindow _imageViewerWindow;
        public PObjectDetail()
        {
            InitializeComponent();
        }
        public void DisplayImages(Dictionary<string, string> info)
        {
            if (info.TryGetValue("MainImagePath", out string mainPath))
                MainImage.Source = LoadImage(mainPath);

            if (info.TryGetValue("SubImagePath", out string subPath))
                SubImage.Source = LoadImage(subPath);

            if (info.TryGetValue("DetectImagePath", out string detectPath))
                DetectImage.Source = LoadImage(detectPath);
            
        }
        private BitmapImage LoadImage(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
        private void ViewOriginalImage_Click(object sender, RoutedEventArgs e)
        {
            if (_imageViewerWindow != null)
            {
                _imageViewerWindow.Close();
                _imageViewerWindow = null;
            }
            _imageViewerWindow = new ImageViewerWindow();

            _imageViewerWindow.SetImage(DetectImage.Source);

            _imageViewerWindow.Show();
            _imageViewerWindow.Closed += (s, args) => _imageViewerWindow = null;
        }
        private void DetectImage_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var image = sender as Image;
            if (image?.ContextMenu != null)
            {
                image.ContextMenu.PlacementTarget = image;
                image.ContextMenu.IsOpen = true;
            }
        }
       
        public void DisplayFields(Dictionary<string, string> info)
        {
            txt1.Visibility = Visibility.Visible;
            txt2.Visibility = Visibility.Visible;
            InfoStack.Children.Clear(); // Xóa dữ liệu cũ
            foreach (var kvp in info)
            {
                if (kvp.Key.Contains("ImagePath")) continue; // Skip image fields

                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
                panel.Children.Add(new TextBlock
                {
                    Text = $"{kvp.Key}: ",
                    FontWeight = FontWeights.Normal,
                    Width = 250,
                    Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                    Style = (Style)FindResource("General_TextBlock")
                });
                var isUndefined = kvp.Value == "Không xác định"||kvp.Value=="00000000-0000-0000-0000-000000000000";
                panel.Children.Add(new TextBlock
                {
                    Text = kvp.Value,
                    FontWeight = isUndefined ? FontWeights.Normal : FontWeights.Bold,
                    Foreground = isUndefined ? new SolidColorBrush(Colors.Gray) : new SolidColorBrush(Colors.WhiteSmoke),
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)FindResource("General_TextBlock")
                });

                InfoStack.Children.Add(panel);
            }
        }
    }
}
