using System;
using System.Collections.Generic;
using System.IO;
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
using V3SClient.models;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for CameraMarker.xaml
    /// </summary>
    public partial class CameraMarker : UserControl
    {
        public string ImageFilePerson { get; set; } = "c:/Tuan/anh.jpg";
        private ImageSource ImageFileMarkerOffline;
        private ImageSource ImageFileMarkerOnline;
        public ImageSource ImageFileMarker
        {
            get
            {
               return CameraMan.Status=="online"?  this.ImageFileMarkerOnline: this.ImageFileMarkerOffline;
            }
            set
            {

            }
        }

        public models.Camera CameraMan { get; set; }
        
        public CameraMarker(models.Camera cam)
        {
            InitializeComponent();
            this.DataContext = this;
            this.CameraMan = cam;
             ImageFileMarkerOffline= new BitmapImage(new Uri("pack://application:,,,/images/map/marker_off.png"));
            ImageFileMarkerOnline = new BitmapImage(new Uri("pack://application:,,,/images/map/map-marker.png"));
            //ImageFileMarker = new BitmapImage(new Uri("pack://application:,,,/images/map/map-marker.png"));

            if (!File.Exists(ImageFilePerson))
            {
                ImageFilePerson=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","images","person.png");
            }
           
        }

    }
}

















