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
using System.Windows.Navigation;
using System.Windows.Shapes;
using V3SClient.models;
using V3SClient.ucs;
using V3SClient.UI.Views;

namespace V3SClient.UI.Pages
{
    /// <summary>
    /// Interaction logic for miniSystemAndMap.xaml
    /// </summary>
    public partial class SystemAndMap : Page
    {
        private Frame contentFrame;
        private  UI.Views.VLivePosition _eMap { get; set; }
        private SystemMonitor _systemMonitor { get; set; }
        private Button _activeButton { get; set; }
        public VMetaAIResult MetaAIResult { get; set; }

        public SystemAndMap(ObservableCollection<viewModels.VMTalkGroup> cam_group_list)
        {
            InitializeComponent();
            _eMap = new VLivePosition(cam_group_list);
            _systemMonitor = new SystemMonitor();
            MetaAIResult=new VMetaAIResult();
            StackPanel stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
            contentFrame = new Frame();
            contentFrame.NavigationUIVisibility = NavigationUIVisibility.Hidden;
            Button btnPage0 = new Button { Content = "AI logs",Tag="ai_logs", Margin = new Thickness(1) };
            Button btnPage1 = new Button { Content = "System",Tag= "system", Margin = new Thickness(1) };
            Button btnPage2 = new Button { Content = "E-Map",Tag= "e_map", Margin = new Thickness(1) };
            try
            {
                ResourceDictionary resourceDict = new ResourceDictionary();
                resourceDict.Source = new Uri("styles/customize_transparent_gray_button.xaml", UriKind.Relative);
                btnPage0.Style = (Style)resourceDict["TransparentGrayButton"];
                btnPage1.Style = (Style)resourceDict["TransparentGrayButton"];
                btnPage2.Style = (Style)resourceDict["TransparentGrayButton"];
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error load Style: " + ex.Message);
                return;
            }
            btnPage0.Click += ActivePage;
            btnPage1.Click += ActivePage;
            btnPage2.Click += ActivePage;


            stackPanel.Children.Add(btnPage0);
            stackPanel.Children.Add(btnPage1);
            stackPanel.Children.Add(btnPage2);

            DockPanel dockPanel = new DockPanel();
            DockPanel.SetDock(stackPanel, Dock.Top);
            dockPanel.Children.Add(stackPanel);
            dockPanel.Children.Add(contentFrame);           
            container.Content= dockPanel;
            btnPage0.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private void ActivePage(object sender, RoutedEventArgs e)
        {
            foreach (Button btn in ((StackPanel)((Button)sender).Parent).Children)
            {
                btn.Foreground = Brushes.WhiteSmoke;
            }
            Button selectedButton = (Button)sender;
            selectedButton.Foreground = Brushes.Yellow;
            if (_activeButton != null && _activeButton == selectedButton)
                return;

            _activeButton = selectedButton;

            if (_activeButton.Tag.ToString() == "system")
                contentFrame.Navigate(_systemMonitor);
            else if (_activeButton.Tag.ToString() == "ai_logs")
                contentFrame.Navigate(MetaAIResult);
            else
                contentFrame.Navigate(_eMap);
        }

        public void UpdateGPS(Dictionary<string, GMap.NET.PointLatLng> gpsCameras)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_activeButton != null && _activeButton.Content.ToString() == "E-Map")
                    _eMap.CamerasPositionUpdating(gpsCameras);
            });


        }

    }
}

















