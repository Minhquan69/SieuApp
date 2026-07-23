//#define VIEW_FULL_NOT_SETTING
//#define VIEW_ONLY


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Windows.Threading;
using GMap.NET;
using Microsoft.Win32;
using Newtonsoft.Json;
using OpenCvSharp.Aruco;
using StackExchange.Redis;
using V3SClient.libs;
using V3SClient.UI.Pages;
using V3SClient.ucs;
using V3SClient.UI.Views;
using V3SClient.window;
using V3SClient.viewModels;
using V3SClient.ucs.Settings.views;


namespace V3SClient
{
    /// <summary>
    /// Interaction logic for mainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public bool ShowSettingButton {  get; set; }   =true;


        System.Windows.Controls.Button SelectedButton { get; set; } = null;

        UI.Views.EMap _eMapWindow = null;
        DownloadManagerWindow _downloadManagerWindow = null;
        UI.Views.DOC_SCAN _docScanPage = null;


        // private static DeviceSyncWindow deviceSyncWindow;
        object SelectedPage { get; set; } = null;
        string selectButtonName { get; set; } = "";
        viewModels.VMTalkGroups CameraGroups => GlobalSystem.Instance.CameraGroups;
        List<models.Camera> CameraList =>GlobalSystem.Instance.CameraList;
        HashSet<string> Devices =>GlobalSystem.Instance.Devices;
        List<models.Camera> ActiveCameras4EMap { get; set; }
        List<CamInfo> ActiveCamInfo { get; set; }
        Dictionary<string, GMap.NET.PointLatLng> _gpsBuffer { get; set; } = new Dictionary<string, GMap.NET.PointLatLng>();

        event EventHandler<Dictionary<string, GMap.NET.PointLatLng>> SendGPSBuffer;

        System.Windows.Threading.DispatcherTimer _timerSendGPSBuffer;

        private System.Threading.Timer _midnightResetTimer;

        private InternalRuntimeInterop _internalRuntimeInterop;
        public MainWindow()
        {
            LoggerManager.LogInfo("Ứng dụng V3SClient bắt đầu khởi chạy.");
            InitializeComponent();

            DataContext = this;
            GlobalSystem.Instance.EventConfigChange += EventConfigChange;


            UsernameDisplay.Text = GlobalUserInfo.Instance.UserName;

            #region License
            runInspector();
            _internalRuntimeInterop = new InternalRuntimeInterop();
            string lMessage = "";
            Dictionary<string, string> features;
            int code = _internalRuntimeInterop.Verify(ref lMessage, out features);
            if (code != 0)
            {
                LoggerManager.LogWarn($"Xác thực bản quyền thất bại: {lMessage}");
                System.Windows.Forms.MessageBox.Show($"Lỗi xác thực bản quyền: {lMessage}", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                System.Windows.Application.Current.Shutdown();
                return;
            }
            else
            {
                LoggerManager.LogInfo("Xác thực bản quyền thành công.");
            }
            #endregion License

            string configuredGStreamerRoot = System.Configuration.ConfigurationManager.AppSettings["GStreamerRoot_v3"];
            string installedGStreamerRoot = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "gstreamer", "1.0", "msvc_x86_64");
            string gstlibBase = !string.IsNullOrWhiteSpace(configuredGStreamerRoot) &&
                Directory.Exists(configuredGStreamerRoot)
                ? configuredGStreamerRoot
                : installedGStreamerRoot;
            string gstPath = System.IO.Path.Combine(gstlibBase, "bin");
            string gstPluginPath = System.IO.Path.Combine(gstlibBase, "lib", "gstreamer-1.0");

            if (File.Exists(System.IO.Path.Combine(gstPath, "gstreamer-1.0-0.dll")) && Directory.Exists(gstPluginPath))
            {
                Environment.SetEnvironmentVariable("GST_PLUGIN_PATH", gstPluginPath, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("PATH", $@"{gstPath};" + Environment.GetEnvironmentVariable("PATH"), EnvironmentVariableTarget.Process);
                LoggerManager.LogInfo("Using GStreamer runtime: " + gstlibBase);
            }
            else
            {
                LoggerManager.LogError("GStreamer runtime is incomplete: " + gstlibBase, null);
            }

            //Debug mode GST
            
            //Environment.SetEnvironmentVariable(
            //    "GST_DEBUG",
            //    "3,rtspsrc:6,rtspconnection:6,rtp*:5"
            //);
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Gst.Application.Init();
                    LoggerManager.LogDebug("GStreamer initialized successfully.");
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, "Lỗi khi khởi tạo GStreamer");
                }
            });

            LoggerManager.LogDebug("Khởi tạo môi trường hoàn tất.");
          
            RoutedEventArgs args = new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent);

            // start up
            //btnPlayback.RaiseEvent(args);
            btnPlaybackHLS.RaiseEvent(args);
            //  btnLive.RaiseEvent(args);


            // Nạp thông tin camera hoạt động
            ActiveCamInfo = GlobalUserInfo.Instance.GetActiveClient();
            LoggerManager.LogInfo("Đã nạp danh sách camera hoạt động.");
            LoggerManager.LogInfo("Khởi tạo ứng dụng hoàn tất, sẵn sàng vận hành.");
            // Reset meta AI 0h everyday
            ScheduleMidnightReset();
            //StartCameraMonitor
            StartCameraMonitor();
        }
        #region Check link camere IP
        private CancellationTokenSource _cts = new CancellationTokenSource();
        public void StartCameraMonitor()
        {
            Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        // 👉 Lấy danh sách camera ip_camera
                        var cameras = ActiveCamInfo.Select(x=>x).Where(x=>x.CamInfo_Type=="ip_cam").ToList();

                        foreach (var cam in cameras)
                        {
                            string error;
                            bool isOnline =Utils. IsRtspUrlValid(cam.CamInfo_Source_Path, out error);
                            var newStatus = isOnline ? DeviceStatus.Online : DeviceStatus.Offline;
                            UpdateCameraStatusById(cam.CamInfo_CamId, newStatus);
                            Thread.Sleep(1000);
                            // 👉 Cập nhật trạng thái trên UI thread (WPF)
                            //Application.Current.Dispatcher.Invoke(() =>
                            //{
                                
                            //});
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerManager.LogException(ex, "Lỗi khi kiểm tra camera trong StartCameraMonitor");
                    }

                    //  Lặp lại sau 30 phút
                    try { await Task.Delay(TimeSpan.FromMinutes(30), _cts.Token); }
                    catch (TaskCanceledException) { break; }
                }
            }, _cts.Token);
        }
        public void StopCameraMonitor()
        {
            _cts.Cancel();
        }
        #endregion Check link camere IP
        private void EventConfigChange(object sender, EventArgs e)
        {
            SelectdPage(selectButtonName);
        }

        private void runInspector()
        {
            string appPath = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = System.IO.Path.Combine(appPath, "inspector.exe");

            if (File.Exists(exePath))
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };

                    Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, "Lỗi khi khởi động inspector.exe");
                }
            }
        }
        private void ScheduleMidnightReset()
        {
            var now = DateTime.Now;
            var midnight = now.Date.AddDays(1);
            var timeUntilMidnight = midnight - now;

            _midnightResetTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    MetaAIResultStorage.Instance.ResetDataIfNewDay();
                    ScheduleMidnightReset();
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, "Lỗi trong ScheduleMidnightReset");
                }
            }, null, timeUntilMidnight, Timeout.InfiniteTimeSpan);
        }
        void HandleWSMessage(string channel, string message)
        {
            Debug.WriteLine($"Received WebSocket [{channel}]: {message}"); // An toàn trên UI Thread

            try
            {
                string json = message;

                if (channel == "DeviceStatus")
                {
                    var deviceStatus = JsonConvert.DeserializeObject<DeviceStatusEvent>(json);
                    if (deviceStatus != null)
                    {
                        bool exists = Devices.Contains(deviceStatus.DeviceID);
                        if (exists)
                        {
                            // Xử lý trạng thái thiết bị
                            Debug.WriteLine($"[DeviceStatus] DeviceID: {deviceStatus.DeviceID}, Status: {deviceStatus.Status}, IsOnline: {deviceStatus.IsOnline}");
                            
                            if (deviceStatus.IsOnline.HasValue)
                            {
                                UpdateCameraStatusById(deviceStatus.DeviceID, deviceStatus.IsOnline.Value ? DeviceStatus.Online : DeviceStatus.Offline);
                            }
                            else
                            {
                                UpdateCameraStatusById(deviceStatus.DeviceID, deviceStatus.Status);
                            }

                            
                            ToastManager.ShowToast("", $"Thiết bị : {deviceStatus.DeviceID} {deviceStatus.Status}", ToastType.Info);
                        }
                    }
                }
                else if (channel == "Talk")
                {
                    var talkEvent = JsonConvert.DeserializeObject<TalkStatusEvent>(json);
                    if (talkEvent != null)
                    {
                        bool exists = Devices.Contains(talkEvent.DeviceID);
                        if (exists)
                        {
                            // Xử lý sự kiện Push-to-Talk
                            Debug.WriteLine($"[Talk] DeviceID: {talkEvent.DeviceID}, Status: {talkEvent.Status}");
                            // Gọi hàm cập nhật trạng thái PTT (Push to Talk) của camera
                            UpdateCameraPTTStatus(talkEvent.DeviceID, talkStatus: talkEvent.Status);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi giải mã JSON từ WebSocket message (Channel: {channel})");
            }
        }
        public void UpdateCameraStatusById(string camId, DeviceStatus newStatus)
        {
            string statusStr = newStatus.ToString().ToLower();

            // Đảm bảo cập nhật trên UI thread để WPF Binding nhận diện ngay lập tức
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                GlobalUserInfo.Instance.UpdateCameraStatus(camId, statusStr);

                // Cập nhật trong danh sách tổng (đã bao gồm đầy đủ mọi cấp độ nhờ GlobalSystem.Init mới)
                var mainCam = GlobalSystem.Instance.CameraList?.FirstOrDefault(c => string.Equals(c.camID, camId, StringComparison.OrdinalIgnoreCase));
                if (mainCam != null)
                {
                    mainCam.Status = statusStr;
                }

                // Cập nhật trong tất cả các nhóm và nhóm con (đệ quy)
                if (CameraGroups?.CamGroupList != null)
                {
                    UpdateStatusInGroupsRecursive(CameraGroups.CamGroupList, camId, statusStr);
                }
            });
        }

        private void UpdateStatusInGroupsRecursive(IEnumerable<VMTalkGroup> groups, string camId, string statusStr)
        {
            if (groups == null) return;
            foreach (var group in groups)
            {
                // Cập nhật camera trong nhóm hiện tại
                var camera = group.Cameras.FirstOrDefault(c => string.Equals(c.camID, camId, StringComparison.OrdinalIgnoreCase));
                if (camera != null)
                {
                    camera.Status = statusStr;
                }
                
                // Tiếp tục đệ quy xuống các nhóm con
                if (group.SubGroups != null && group.SubGroups.Count > 0)
                {
                    UpdateStatusInGroupsRecursive(group.SubGroups, camId, statusStr);
                }
            }
        }
        void UpdateCameraPTTStatus(string deviceID, TalkStatus talkStatus)
        {
            if (SelectedPage is UI.Views.VLiveStream liveStreamPage)
            {
                int setVal = talkStatus == TalkStatus.On ? 5 : 0;
                liveStreamPage.SetCameraWarningById(deviceID, setVal);
            }
        }
        private void btn_Exit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void btn_MaximizeWindow_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
                this.WindowState = WindowState.Maximized;

        }

        private void btn_MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
                return;
            this.WindowState = WindowState.Minimized;
        }


        private void Selected_Page_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button selectedButton = sender as System.Windows.Controls.Button;

            if (selectedButton == null) return;
            if (SelectedButton == selectedButton) return;

            this.SelectedButton = selectedButton;

            Border cover_border = selectedButton.Parent as Border;

            foreach (Border border in stackPanelPages.Children)
                border.Background = new SolidColorBrush(Colors.Transparent);

            if (cover_border != null)
                cover_border.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#D3550E");
            selectButtonName = selectedButton.Name;
            SelectdPage(selectedButton.Name);
        }
        private void SelectdPage(string name)
        {
            switch (name)
            {
                case "btnLive":
                    SelectedPage = new UI.Views.VLiveStream(CameraGroups.CamGroupList);
                    Task.Run(() =>
                    {
                        ((VLiveStream)SelectedPage).ForwardGPSBuffer += MainWindow_SendGPSBuffer;
                        ActiveCameras4EMap = CameraGroups.CamGroupList.Where(group => group != null && group.Cameras.Count > 0)
                         .SelectMany(group => group.Cameras).OrderBy(cam => cam.name, new NaturalStringComparer()).ToList();
                        if (_eMapWindow != null)
                            _eMapWindow.UpdateActiveCameras(ActiveCameras4EMap);

                    });

                    break;
                case "btnPlayback":
                    SelectedPage = new UI.Views.VPlayback(CameraGroups.CamGroupList);
                    ((VPlayback)SelectedPage).ForwardGPSBuffer += MainWindow_SendGPSBuffer;
                    ((VPlayback)SelectedPage).ActiveCamerasChanged += UpdateActiveCam2EMap;
                    break;
                case "btnPlaybackHLS":
                    SelectedPage = new UI.Views.VPlaybackHLS(CameraGroups.CamGroupList);
                    ((VPlaybackHLS)SelectedPage).ForwardGPSBuffer += MainWindow_SendGPSBuffer;
                    ((VPlaybackHLS)SelectedPage).ActiveCamerasChanged += UpdateActiveCam2EMap;
                    break;
                case "btnRegDK":
                    SelectedPage = new UI.Views.VVehicleRegisterManagement();
                    break;
                case "btnQLBSXDK":
                    SelectedPage = new UI.Views.QL_BSX_DK(CameraGroups.CamGroupList);
                    break;
                case "btnDocScan":
                    if (_docScanPage == null)
                        _docScanPage = new UI.Views.DOC_SCAN();
                    SelectedPage = _docScanPage;
                    break;
                case "btnConfig":
                    SelectedPage = new UI.Views.VConfig();
                    break;
                case "btnAIAnalysisLog":
                    SelectedPage = new UI.Pages.VAISearchPage();
                    break;
            }
            Frm_Content.Dispatcher.Invoke(() =>
            {
                Frm_Content.Content = SelectedPage;
            });
        }



        private void UpdateActiveCam2EMap(object sender, List<models.Camera> activeCams)
        {
            ActiveCameras4EMap = activeCams;
            if (_eMapWindow == null)
                return;
            _eMapWindow.UpdateActiveCameras(activeCams);
        }

        private void MainWindow_SendGPSBuffer(object sender, Dictionary<string, PointLatLng> data)
        {
            _gpsBuffer = data;
        }

        private void btnEMap_Click(object sender, RoutedEventArgs e)
        {
            if (_eMapWindow != null)
                return;
            try
            {
                _eMapWindow = new UI.Views.EMap(CameraGroups.CamGroupList);
                _eMapWindow.Owner = this;
                _eMapWindow.EMapClose += EMapWindow_EMapClose;

                _eMapWindow.Show();
                _eMapWindow.UpdateActiveCameras(ActiveCameras4EMap);
            }
            catch (Exception ex)
            {
                _eMapWindow = null;
                LoggerManager.LogError("Không thể mở cửa sổ E-Map", ex);
                System.Windows.MessageBox.Show("Không thể mở bản đồ. Vui lòng kiểm tra WebView2 Runtime.", "E-Map", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _timerSendGPSBuffer = new System.Windows.Threading.DispatcherTimer();
            _timerSendGPSBuffer.Interval = TimeSpan.FromMilliseconds(1000);
            _timerSendGPSBuffer.Tick += TimerSendGPSBuffer_Tick;
            _timerSendGPSBuffer.Start();
        }
     
        
        private void TimerSendGPSBuffer_Tick(object sender, EventArgs e)
        {
            _eMapWindow.CamerasPositionUpdating(_gpsBuffer);
        }

        private void EMapWindow_EMapClose(object sender, EventArgs e)
        {
            _timerSendGPSBuffer.Stop();
            _eMapWindow = null;

        }

        #region Login User

        private void btn_User_Click(object sender, RoutedEventArgs e)
        {
           
            // Gán dữ liệu khi mở popup
            UsernameDisplayPopUp.Text = GlobalUserInfo.Instance.UserName;
            TimeSpan loginDuration = DateTime.Now - GlobalUserInfo.Instance.StartLoginTime;

            string formattedDuration = $"{(int)loginDuration.TotalMinutes} phút";
            if (loginDuration.TotalMinutes < 1)
                formattedDuration = $"{(int)loginDuration.TotalSeconds} giây";

            LoginTimeDisplay.Text = $"Đã đăng nhập: {formattedDuration}";

            // Load danh sách ClientInfo
            if (GlobalUserInfo.Instance.AuthorizedProfiles != null)
            {
                cboActiveClient.ItemsSource = GlobalUserInfo.Instance.AuthorizedProfiles;
                var activeId = GlobalUserInfo.Instance.ActiveClientId;
                if (activeId != Guid.Empty)
                {
                    cboActiveClient.SelectedValue = activeId;
                }
            }

            // Mở popup
            UserPopup.IsOpen = true;
        }

        //private void ChangeClient_Click(object sender, RoutedEventArgs e)
        //{
        //    var selectedItem = ClientComboBox.SelectedItem;
        //    if (selectedItem != null)
        //    {
        //        // Ép kiểu an toàn
        //        var clientIdProp = selectedItem.GetType().GetProperty("ClientInfo_Id");
        //        var clientNameProp = selectedItem.GetType().GetProperty("ClientInfo_Name");

        //        if (clientIdProp != null && clientNameProp != null)
        //        {
        //            var clientId = (Guid)clientIdProp.GetValue(selectedItem);
        //            var clientName = (string)clientNameProp.GetValue(selectedItem);

        //            if (GlobalUserInfo.Instance.GroupClients.TryGetValue(clientId, out var camList))
        //            {
        //                GlobalUserInfo.Instance.SelectedClientName = clientName;
        //                GlobalUserInfo.Instance.SelectedCamList = camList;

        //                GlobalUserInfo.Instance.ActiveClientId = clientId;

        //                System.Windows.MessageBox.Show($"Đã chuyển sang client '{GlobalUserInfo.Instance.SelectedClientName}' với {GlobalUserInfo.Instance.SelectedCamList.Count} camera.");
        //            }
        //        }
        //    }
        //}

        private async void btnSwitchProfile_Click(object sender, RoutedEventArgs e)
        {
            if (cboActiveClient.SelectedItem is ApiManager.ClientProfile selectedProfile)
            {
                if (selectedProfile.Id == GlobalUserInfo.Instance.ActiveClientId)
                {
                    UserPopup.IsOpen = false;
                    return;
                }

                UserPopup.IsOpen = false;

                try
                {
                    this.Cursor = System.Windows.Input.Cursors.Wait;
                    
                    // Cleanup existing monitors and services
                    StopCameraMonitor();
                   // V3SClient.Services.DocumentProcessingManager.Instance.Stop();

                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                    {
                        var camList = await ApiManager.Instance.GetCamInfoAsync(cts.Token, selectedProfile.Id.ToString());
                        
                        if (camList == null || camList.Count == 0)
                        {
                            System.Windows.MessageBox.Show("Profile này không có thiết bị hoặc có lỗi khi tải dữ liệu.", "Cảnh báo");
                            this.Cursor = System.Windows.Input.Cursors.Arrow;
                            return;
                        }

                        // Release old data and update globals
                        GlobalUserInfo.Instance.GroupClients.Clear();
                        GlobalUserInfo.Instance.ActiveClientId = selectedProfile.Id;
                        GlobalUserInfo.Instance.SelectedClientName = selectedProfile.Name;
                        GlobalUserInfo.Instance.GroupClients[selectedProfile.Id] = camList;

                        var commanderIDs = camList.Where(c => c.Device_Role != null && c.Device_Role != "client_device" && c.CamInfo_Type == "body_cam").ToList();
                        if (commanderIDs.Count > 0)
                        {
                            GlobalUserInfo.Instance.Commanders = new System.Collections.ObjectModel.ObservableCollection<CamInfo>(commanderIDs);
                            GlobalUserInfo.Instance.ActiveCommanderID = commanderIDs.First().CamInfo_CamId;
                        }
                        else
                        {
                            GlobalUserInfo.Instance.Commanders?.Clear();
                            GlobalUserInfo.Instance.ActiveCommanderID = null;
                        }

                        // Rebuild tree and Global System config
                        GlobalUserInfo.Instance.BuildTreeViewWithOrganization();
                        GlobalSystem.Instance.ReloadConfig();
                        
                        // Restart Camera Monitor
                        ActiveCamInfo = GlobalUserInfo.Instance.GetActiveClient();
                        StartCameraMonitor();
                        
                        // Force refresh of the current page
                        SelectdPage(selectButtonName);
                        
                        ToastManager.ShowToast("Thông báo", $"Đã chuyển sang Profile: {selectedProfile.Name}", ToastType.Info);
                    }
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, $"Lỗi khi chuyển sang profile {selectedProfile.Name}");
                    System.Windows.MessageBox.Show("Có lỗi xảy ra khi tải profile. Vui lòng thử lại.", "Lỗi tải dữ liệu", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    this.Cursor = System.Windows.Input.Cursors.Arrow;
                }
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Bạn có chắc muốn đăng xuất không?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                Utils.CloseAndResetApp();
            }
        }
        #endregion

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            MetaAIResultStorage.Instance.SaveData();
            MetaAIResultStorage.Instance.StopAutoSave();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeRealTimeAsync();
            
            // Khởi tạo DocumentProcessingManager
            var docConfig = new viewModels.VMDocumentConfig();
            V3SClient.Services.DocumentProcessingManager.Instance.Initialize(docConfig);
        }

        private async Task InitializeRealTimeAsync()
        {
            try
            {
                var wsManager = WebSocketManager.Instance;
                
                // 1. Đăng ký sự kiện trước khi kết nối (tránh mất message)
                wsManager.Subscribe("Talk", (channel, message) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => HandleWSMessage(channel, message));
                });
                wsManager.Subscribe("DeviceStatus", (channel, message) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => HandleWSMessage(channel, message));
                });

                // 2. Kết nối WebSocket
                if (!string.IsNullOrEmpty(ApiManager.Instance.BackendToken))
                {
                    LoggerManager.LogInfo("Đang khởi tạo kết nối Real-time...");
                    await wsManager.ConnectAsync(ApiManager.Instance.BaseUrl, ApiManager.Instance.BackendToken);
                }

                // 3. Đồng bộ trạng thái ban đầu của các thiết bị
                if (ActiveCamInfo != null && ActiveCamInfo.Count > 0)
                {
                    var deviceIds = ActiveCamInfo.Select(c => c.CamInfo_CamId).ToList();
                    LoggerManager.LogInfo($"Đang đồng bộ trạng thái ban đầu cho {deviceIds.Count} thiết bị...");
                    
                    var statusList = await ApiManager.Instance.GetDeviceStatusBatchAsync(deviceIds);
                    if (statusList != null)
                    {
                        foreach (var statusInfo in statusList)
                        {
                            // Ưu tiên kiểm tra boolean IsOnline từ Backend (mới)
                            if (statusInfo.IsOnline.HasValue)
                            {
                                UpdateCameraStatusById(statusInfo.DeviceId, statusInfo.IsOnline.Value ? DeviceStatus.Online : DeviceStatus.Offline);
                            }
                            else
                            {
                                // Fallback cho format cũ (string/int)
                                string s = statusInfo.Status?.ToLower();
                                if (s == "online" || s == "1")
                                {
                                    UpdateCameraStatusById(statusInfo.DeviceId, DeviceStatus.Online);
                                }
                                else if (s == "offline" || s == "0")
                                {
                                    UpdateCameraStatusById(statusInfo.DeviceId, DeviceStatus.Offline);
                                }
                                else if (Enum.TryParse<DeviceStatus>(statusInfo.Status, true, out var status))
                                {
                                    UpdateCameraStatusById(statusInfo.DeviceId, status);
                                }
                            }
                        }
                        LoggerManager.LogInfo("Đồng bộ trạng thái ban đầu hoàn tất.");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi khởi tạo Real-time logic");
            }
        }

        private void btnNativeConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UserPopup.IsOpen = false; // Đóng popup sau khi click

                var configView = new SystemConfigView();
                
                // Tạo một container để bọc ConfigView và thêm nút đóng
                Grid rootGrid = new Grid();
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) }); // Title bar
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                // Border cho Title Bar
                Border titleBar = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111318")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252830")),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(15, 0, 10, 0)
                };
                Grid.SetRow(titleBar, 0);

                StackPanel titleStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                titleStack.Children.Add(new TextBlock { Text = " ", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A9EFF")), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
                titleBar.Child = titleStack;

                // Nút Close
                System.Windows.Controls.Button closeBtn = new System.Windows.Controls.Button 
                { 
                    Content = "✕", 
                    Width = 30, 
                    Height = 30, 
                    Background = Brushes.Transparent, 
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0AAB8")),
                    BorderThickness = new Thickness(0),
                    FontSize = 18
                };
                closeBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                closeBtn.Cursor = System.Windows.Input.Cursors.Hand;
                Grid.SetRow(closeBtn, 0);
                
                Grid.SetRow(configView, 1);
                
                rootGrid.Children.Add(titleBar);
                rootGrid.Children.Add(closeBtn);
                rootGrid.Children.Add(configView);

                Window configWindow = new Window
                {
                    Title = "Cấu hình hệ thống",
                    Content = rootGrid,
                    Width = 950,
                    Height = 750,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D0F12")),
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = false,
                    Owner = this
                };

                closeBtn.Click += (s, ev) => configWindow.Close();

                // Cho phép kéo dãn cửa sổ bằng Title Bar
                titleBar.MouseLeftButtonDown += (s, ev) => { if (ev.LeftButton == MouseButtonState.Pressed) configWindow.DragMove(); };

                configWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi mở cửa sổ cấu hình hệ thống");
                System.Windows.MessageBox.Show("Không thể mở cửa sổ cấu hình. Vui lòng kiểm tra log.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnDownloads_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadManagerWindow != null)
            {
                _downloadManagerWindow.Activate();
                return;
            }
            _downloadManagerWindow = new DownloadManagerWindow();
            _downloadManagerWindow.Owner = this;
            _downloadManagerWindow.Closed += (s, ev) => { _downloadManagerWindow = null; };
            _downloadManagerWindow.Show();
        }

        private void btnCategoryMenu_Click(object sender, RoutedEventArgs e)
        {
            if (CategoryPopup != null)
            {
                CategoryPopup.IsOpen = true;
            }
        }
    }
}
