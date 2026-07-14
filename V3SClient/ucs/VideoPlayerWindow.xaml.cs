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
using V3SClient.models;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for VideoPlayerWindow.xaml
    /// </summary>
    public partial class VideoPlayerWindow : Window
    {
        public VideoPlayerWindow(string videoFile)
        {
            InitializeComponent();
            // Create a dummy camera
            var camera = new Camera
            {
                camID = "CAM001",
                name = ">>"
            };

            // Video file list
            var videoFiles = new List<string>
            {
                videoFile  
            };

            // Create ViewCamera control and add to UI
            var viewCamera = new ViewCamera(camera, PlayerType.FilesPlay, videoFiles);
            viewCamera.SetTextCenterButton("Play");
            playerContainer.Content = viewCamera;
        }
    }
}

















