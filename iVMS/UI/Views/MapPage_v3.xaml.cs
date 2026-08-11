using System.Windows;
using System.Windows.Controls;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class MapPage_v3 : UserControl
    {
        private bool _recreateMapOnLoad;

        /// <summary>Opt-in isolated software rendering for compact map hosts.</summary>
        public bool UseSoftwareRendering { get; set; }
        /// <summary>Hides the map's camera-list sidebar in overview hosts.</summary>
        public bool CompactOverviewMode { get; set; }

        public MapPage_v3()
        {
            InitializeComponent();
            DataContext = new MapViewModel_v3();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // VLivePosition disposes its WebView when the dashboard is
            // unloaded. Its old visual must therefore not be reused.
            if (_recreateMapOnLoad || MapHost.Content == null)
            {
                _recreateMapOnLoad = false;
                MapHost.Navigate(new VLivePosition(
                GlobalSystem.Instance.CameraGroups.CamGroupList,
                UseSoftwareRendering,
                CompactOverviewMode));
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _recreateMapOnLoad = true;
        }
    }
}
