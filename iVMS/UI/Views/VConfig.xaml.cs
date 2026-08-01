using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using System.ComponentModel;
using V3SClient.libs;
using V3SClient.libs.interfaces;
using V3SClient.ucs.Settings.viewmodels;
using V3SClient.ucs.Settings.views;
using V3SClient.window;

namespace V3SClient.UI.Views
{
    /// <summary>
    /// Interaction logic for VConfig.xaml
    /// </summary>
    public partial class VConfig : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _currentTag = null;
        private bool _isMenuCollapsed = false;

        public bool IsMenuCollapsed
        {
            get => _isMenuCollapsed;
            set
            {
                if (_isMenuCollapsed != value)
                {
                    _isMenuCollapsed = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMenuCollapsed)));
                }
            }
        }

        public VConfig()
        {
            InitializeComponent();
            this.DataContext = this;
            
            // Automatically load Web Config for Choice A
            this.Loaded += (s, e) => LoadInitialWebConfig();
        }

        private void LoadInitialWebConfig()
        {
            if (_webConfigView == null)
            {
                _webConfigView = new VWebConfigView();
            }

            _webConfigView.NavigateTo("/cameras");
            MainContentArea.Content = _webConfigView;
        }
        

        private void ToggleMenu_Click(object sender, RoutedEventArgs e)
        {
            IsMenuCollapsed = !IsMenuCollapsed;

            MenuColumn.Width = IsMenuCollapsed ? new GridLength(60) : new GridLength(250);
            ArrowIcon.Kind = IsMenuCollapsed ? MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronRight : MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronRight;
        }
        private VWebConfigView _webConfigView;

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && !string.IsNullOrEmpty(tag))
            {
                if (tag == _currentTag)
                    return;
                _currentTag = tag;

                if (MainContentArea.Content is IClosableView oldView)
                {
                    oldView.Cleanup();
                }

                foreach (var child in Utils.FindVisualChildren<Button>(this))
                {
                    if (child.Style == FindResource("FlatMenuButton") as Style)
                    {
                        child.ClearValue(Button.BackgroundProperty);
                        child.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#C1C1C1"));
                    }
                }

                // Đổi màu cho button được chọn
                btn.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#332B21"));
                btn.Foreground = Brushes.White;

                // Sử dụng VWebConfigView để nhúng Frontend thay vì các View native cũ
                if (_webConfigView == null)
                {
                    _webConfigView = new VWebConfigView();
                }

                string route = "";
                switch (tag)
                {
                    case "CamInfo":
                        route = "/cameras";
                        break;

                    case "GroupArea":
                        route = "/camera-groups";
                        break;

                    case "RoiConfig":
                        route = "/cameras/rois";
                        break;

                    case "AiConfig":
                        route = "/ai/assignments";
                        break;

                    case "ClientInfo":
                        route = "/settings/client-profiles";
                        break;

                    case "SystemConfig":
                        route = "/configs";
                        break;

                    default:
                        MessageBox.Show($"Không tìm thấy route tương ứng với tag: {tag}");
                        return;
                }

                _webConfigView.NavigateTo(route);
                MainContentArea.Content = _webConfigView;
            }
        }
    }
}
