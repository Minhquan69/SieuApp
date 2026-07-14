using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using V3SClient.libs;
using static V3SClient.libs.ApiManager;

namespace V3SClient.window
{
    public partial class AIConfigWindow : Window
    {
        private string _camId;
        private List<AIServiceInfo> _allServices;
        private CameraDetailInfo _camera;

        public static readonly DependencyProperty IsBodyCamProperty =
            DependencyProperty.Register("IsBodyCam", typeof(bool), typeof(AIConfigWindow), new PropertyMetadata(false));

        public bool IsBodyCam
        {
            get { return (bool)GetValue(IsBodyCamProperty); }
            set { SetValue(IsBodyCamProperty, value); }
        }

        public AIConfigWindow(string camId)
        {
            InitializeComponent();
            _camId = camId;
            this.DataContext = this;
            Loaded += AIConfigWindow_Loaded;
        }

        private async void AIConfigWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCameraInfo();
            await LoadServices();
            await LoadData();
        }

        private async Task LoadCameraInfo()
        {
            try
            {
                _camera = await ApiManager.Instance.GetCameraDetailAsync(_camId, CancellationToken.None);
                if (_camera != null)
                {
                    IsBodyCam = _camera.CameraType == "body_cam";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi khi tải thông tin camera: " + ex.Message);
            }
        }

        private async Task LoadServices()
        {
            try
            {
                _allServices = await ApiManager.Instance.GetAIServicesAsync(CancellationToken.None);
                UpdateAvailableServices();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi tải danh sách AI Services: " + ex.Message);
            }
        }

        private void UpdateAvailableServices()
        {
            if (_allServices == null) return;
            
            // Filter out services that are already assigned
            var currentAssignments = icAIAssignments.ItemsSource as List<AIAssignmentViewItem>;
            var assignedServiceIds = currentAssignments?.Select(a => a.ServiceId).ToList() ?? new List<Guid>();
            
            var available = _allServices.Where(s => !assignedServiceIds.Contains(s.Id)).ToList();
            cboAvailableServices.ItemsSource = available;
            if (available.Count > 0) cboAvailableServices.SelectedIndex = 0;
        }

        private async Task LoadData()
        {
            try
            {
                icAIAssignments.ItemsSource = null;
                var assignments = await ApiManager.Instance.GetCameraAIConfigsAsync(_camId, CancellationToken.None);
                
                var viewData = new List<AIAssignmentViewItem>();
                if (assignments != null)
                {
                    foreach (var a in assignments)
                    {
                        var service = _allServices?.FirstOrDefault(s => s.Id == a.ServiceId);
                        viewData.Add(new AIAssignmentViewItem(a)
                        {
                            ServiceName = service?.Name ?? "Unknown Node",
                            NodeName = service?.NodeId ?? "N/A"
                        });
                    }
                }
                
                icAIAssignments.ItemsSource = viewData;
                UpdateAvailableServices();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi tải danh sách gán AI: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAssign_Click(object sender, RoutedEventArgs e)
        {
            if (cboAvailableServices.SelectedValue is Guid serviceId)
            {
                try
                {
                    // Default config for body cams
                    BodyCamConfig bodyCamCfg = null;
                    if (IsBodyCam)
                    {
                        bodyCamCfg = new BodyCamConfig
                        {
                            Role = "client_device",
                            FeedbackMode = "none",
                            Streams = new BodyCamStreams { Media = true, Talk = false, Gps = true }
                        };
                    }

                    bool success = await ApiManager.Instance.AssignCameraToAIAsync(_camId, serviceId, new { }, bodyCamCfg, new { }, CancellationToken.None);
                    if (success)
                    {
                        await LoadData();
                    }
                    else
                    {
                        MessageBox.Show("Gán AI thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi: " + ex.Message);
                }
            }
        }

        private async void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.DataContext is AIAssignmentViewItem item)
            {
                if (MessageBox.Show($"Bạn có chắc chắn muốn gỡ bỏ camera khỏi node '{item.ServiceName}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    bool removed = await ApiManager.Instance.RemoveAIAssignmentAsync(item.AssignmentId, CancellationToken.None);
                    if (removed)
                    {
                        await LoadData();
                    }
                    else
                    {
                        MessageBox.Show("Gỡ bỏ thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            // Debounce or immediate update? For now immediate
            FrameworkElement element = sender as FrameworkElement;
            if (element?.DataContext is AIAssignmentViewItem item)
            {
                // Update on server
                var updateData = new
                {
                    is_enabled = item.IsEnabled,
                    bodycam_config = item.BodyCam,
                    ai_params = item.AiParams
                };
                
                await ApiManager.Instance.UpdateCameraAIConfigAsync(item.AssignmentId, updateData, CancellationToken.None);
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadServices();
            await LoadData();
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public class AIAssignmentViewItem : System.ComponentModel.INotifyPropertyChanged
        {
            public Guid AssignmentId { get; set; }
            public Guid ServiceId { get; set; }
            public string ServiceName { get; set; }
            public string NodeName { get; set; }
            
            private bool _isEnabled;
            public bool IsEnabled
            {
                get => _isEnabled;
                set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
            }

            public BodyCamConfig BodyCam { get; set; }
            public AiParamsConfig AiParams { get; set; }

            public AIAssignmentViewItem(CameraAIAssignmentInfo info)
            {
                AssignmentId = info.Id;
                ServiceId = info.ServiceId;
                IsEnabled = info.IsEnabled;
                BodyCam = info.BodyCam ?? new BodyCamConfig();
                AiParams = info.AiParams ?? new AiParamsConfig();
            }

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

















