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
using System.Windows.Shapes;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for ImagePopup.xaml
    /// </summary>
    public partial class ImagePopup : Window
    {
        public ImagePopup()
        {
            InitializeComponent();

        }
        public ImagePopup(string path)
        {
            InitializeComponent();
            FullImage.Source = new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
        }
    }
}

















