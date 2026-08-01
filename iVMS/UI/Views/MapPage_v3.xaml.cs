using System.Windows;
using System.Windows.Controls;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class MapPage_v3 : UserControl
    {
        public MapPage_v3()
        {
            InitializeComponent();
            DataContext = new MapViewModel_v3();
            Loaded += OnLoaded;
        }
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (MapHost.Content == null) MapHost.Navigate(new VLivePosition(GlobalSystem.Instance.CameraGroups.CamGroupList));
        }
    }
}
