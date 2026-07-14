using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

namespace V3SClient.UI.Views
{
    /// <summary>
    /// Interaction logic for EMap.xaml
    /// </summary>
    public partial class EMap : Window
    {
        public EventHandler EMapClose;
        private UI.Views.VLivePosition _eMap;
        public EMap(ObservableCollection<viewModels.VMTalkGroup> cam_group_list)
        {
            InitializeComponent();
            _eMap = new VLivePosition(cam_group_list);
            this.MainFrame.Content = _eMap;          
            Closed += EMap_Closed;
            this.StateChanged += MainWindow_StateChanged;
            

           MinHeight = 100;
           MinWidth = 100;

        }

        public void UpdateActiveCameras(List<models.Camera> activeCameras)
        {
            if(activeCameras!=null)
                _eMap.UpdateActiveCameras(activeCameras);
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
                this.WindowState = WindowState.Normal;
        }

        private void EMap_Closed(object sender, EventArgs e)
        {
            EMapClose?.Invoke(this, null);
        }

        // Gọi hàm này để update tọa độ, string là id của camera, PointLatLng là tọa độ
        public void CamerasPositionUpdating(Dictionary<string, GMap.NET.PointLatLng> camera_Position)
        {
            _eMap.CamerasPositionUpdating(camera_Position);
        }
    }
}
