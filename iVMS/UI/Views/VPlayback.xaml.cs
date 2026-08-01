using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using GLib;
using Gst;
using V3SClient.ucs;
using V3SClient.viewModels;
using static System.Net.Mime.MediaTypeNames;
using EventArgs = System.EventArgs;

using V3SClient.models;
using V3SClient.UI.Pages;
using GMap.NET;
using V3SClient.libs;

namespace V3SClient.UI.Views
{
    /// <summary>
    /// Interaction logic for Playback_page.xaml
    /// </summary>
    public partial class VPlayback : Page, INotifyPropertyChanged
    {
        System.Timers.Timer _timerGPS;

        public event EventHandler<List<models.Camera>> ActiveCamerasChanged;
        public event EventHandler<Dictionary<string, GMap.NET.PointLatLng>> ForwardGPSBuffer;
        Dictionary<string, GMap.NET.PointLatLng> _gpsBuffer { get; set; } = new Dictionary<string, GMap.NET.PointLatLng>();

        public event PropertyChangedEventHandler PropertyChanged;
       

        private const int InactivityThreshold = 2000;

        private DispatcherTimer clockDeactive;
        public bool IsPlaying { get; set; } = true;

        private const float stepRate = 0.2f;

        private float _currentRate;
        private Dictionary<string, string> _camWithRtspUrls = new Dictionary<string, string>();
        private Dictionary<string, string> _deviceSessionIds = new Dictionary<string, string>();
        private System.DateTime? _searchStartTime;
        private DispatcherTimer _playheadTimer;
        private System.DateTime _currentPlaybackTime;
        private ApiManager.PlaybackSearchResult _lastSearchResult;
        private bool _isSearching = false;
        private System.DateTime _lastSeekInteractionTime = System.DateTime.MinValue;

        private float CurrentRate
        {

            get { return _currentRate; }
            set
            {
                if (_currentRate != value)
                {
                    _currentRate = value;
                    OnPropertyChanged("CurrentRate");
                    txtCurrentRate.Text = string.Format("Play rate {0:f1} x", _currentRate);
                }
            }
        }

        // Dùng để xác định số lượng SelectedCamera đã Thay đổi
        private Visibility _IsObsolete;
        public Visibility IsObsolete
        {
            get { return _IsObsolete; }
            set
            {
                if (_IsObsolete != value)
                {
                    _IsObsolete = value;
                    OnPropertyChanged("IsObsolete");

                }
            }
        }
        public void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        private ObservableCollection<viewModels.VMTalkGroup> CamGroupList { get; set; }
        public bool IsSelectedCameraListEmpty { get; set; } = true;

        public LeftMenu leftMenu { get; set; }
        public UI.Pages.RightPlayback RightMenu { get; set; }
        private UI.Pages.ViewSearch _viewSearch = new UI.Pages.ViewSearch();

        private RightPlayback logPage { get; set; }



        public ObservableCollection<models.Camera> SelecedCameraList { get; set; }
            = new ObservableCollection<models.Camera>();
       
        // Constructor
        public VPlayback(ObservableCollection<VMTalkGroup> cam_group_list)
        {
            InitializeComponent();
            DataContext = this;
           // Gst.Application.Init();

            Unloaded += Playback_page_Unloaded;
            CurrentRate = 1.0f;
            IsObsolete = Visibility.Hidden;
            CamGroupList = cam_group_list;
            AllowSelectingCamera();

            leftMenu = new LeftMenu(CamGroupList, heightZoneCameraList: 350);
            RightMenu = new UI.Pages.RightPlayback();

            // Nhận thông tin về việc check/ uncheck camera từ user control
            leftMenu.Event_Camera_Selected_Changed += Add_Remove_SelectedCameraList;
            leftMenu.Event_Nodes_Camera_Selected_Changed += LeftMenu_Event_Nodes_Camera_Selected_Changed;
            SelecedCameraList.CollectionChanged += Camera_Selected_Changed;

            leftMenu.frameBottom.Navigate(_viewSearch);
            _viewSearch.EventSeachClick += btnSearch_Click;
            frmLeftMenu.Navigate(leftMenu);
            logPage = new UI.Pages.RightPlayback();

            frmRightSide.Content = logPage;

            frmRightSide.Content = logPage;

            // Initialize Playhead Timer
            _playheadTimer = new DispatcherTimer();
            _playheadTimer.Interval = TimeSpan.FromSeconds(1);
            _playheadTimer.Tick += PlayheadTimer_Tick;

            Loaded += Page_Loaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Subscribe to data changes to handle late-loading scenarios
            if (CamGroupList != null)
            {
                CamGroupList.CollectionChanged -= CamGroupList_CollectionChanged;
                CamGroupList.CollectionChanged += CamGroupList_CollectionChanged;
            }

            if (GlobalUserInfo.Instance.AreaTree != null)
            {
                GlobalUserInfo.Instance.AreaTree.CollectionChanged -= AreaTree_CollectionChanged;
                GlobalUserInfo.Instance.AreaTree.CollectionChanged += AreaTree_CollectionChanged;
            }

            // Use Dispatcher to ensure properties are set after UI is fully ready and data is bound
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AllowSelectingCamera();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void CamGroupList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            AllowSelectingCamera();
        }

        private void AreaTree_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            AllowSelectingCamera();
        }

        private void PlayheadTimer_Tick(object sender, EventArgs e)
        {
            if (IsPlaying && _searchStartTime.HasValue)
            {
                _currentPlaybackTime = _currentPlaybackTime.AddSeconds(1 * CurrentRate);
                
                // Update playheads in all camera views
                foreach (var child in gridCameraList.Children)
                {
                    if (child is ViewCamera cam)
                    {
                        // In per-camera timeline mode, the player position handles the visual sync
                        // but we need to ensure the local camera logic is aware of the current global time if needed.
                        // For simplicity, we rely on the Player.VideoPosition which triggers UpdateMiniPlayhead()
                    }
                }
            }
        }

        private void Test_Show_AIResult()
        {
            //File
            //string videoFile = @"C:\tmp\videos\2026\01\19\8E0D73C9-4D\8E0D73C9-4D_2026_01_19_15_09_34.mp4";
            //models.Camera cam = new models.Camera
            //{
            //    camID = "cam1",
            //    name = "Camera 1",
            //    type = "test"
            //};
            //ViewCamera viewCam = new ViewCamera(cam, PlayerType.RtspPlay, new List<string> { videoFile });

            models.Camera cam = new models.Camera
            {
                camID = "cam1",
                name = "Camera 1",
                type = "test",rtps="link rtsp"
            };
            ViewCamera viewCam = new ViewCamera(cam);

            gridCameraList.Children.Add(viewCam);

            viewCam.SendMetaAIResult += ShowAIResult;


        }

        private async void btnSearch_Click(object sender, List<System.DateTime?> e)
        {
            if (_isSearching) return;
            _isSearching = true;

            try
            {
                System.DateTime fromdate = (System.DateTime)e[0];
                System.DateTime todate = (System.DateTime)e[1];

                LoggerManager.LogDebug($"Bắt đầu tìm kiếm video từ {fromdate} đến {todate}");

                TimeSpan duration = todate - fromdate;
                if (duration.TotalHours > 6)
                {
                    LoggerManager.LogWarn("Khoảng thời gian tìm kiếm quá lớn (> 6 giờ)");
                    MessageBox.Show("Khoảng thời gian tìm kiếm vượt quá 6 giờ !", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                List<string> cameraIds = SelecedCameraList?.Select(cam => cam.camID).ToList() ?? new List<string>();
                if (cameraIds.Count == 0)
                {
                    LoggerManager.LogWarn("Người dùng chưa chọn thiết bị để tìm kiếm.");
                    MessageBox.Show("Bạn chưa chọn thiết bị", "Tìm kiếm video", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Call Playback Server API
                _viewSearch.txtResultSearch.Inlines.Clear();
                _viewSearch.txtResultSearch.Inlines.Add(new Run("Đang tìm kiếm video \nvà kích hoạt session...") { Foreground = new SolidColorBrush(Colors.LightBlue) });

                _camWithRtspUrls.Clear();
                _deviceSessionIds.Clear();
                btnDownload.Visibility = Visibility.Collapsed;
                _searchStartTime = fromdate;
                _currentPlaybackTime = fromdate;

                // Batch Search
                LoggerManager.LogDebug($"Đang gọi SearchPlaybackAsync cho {cameraIds.Count} camera.");
                var result = await ApiManager.Instance.SearchPlaybackAsync(cameraIds, fromdate, todate, CancellationToken.None);
                _lastSearchResult = result;

                if (result != null && result.Sessions != null && result.Sessions.Count > 0)
                {
                    LoggerManager.LogInfo($"Tìm thấy {result.Sessions.Count} sessions video.");
                    foreach (var sess in result.Sessions)
                    {
                        // Track session ID per device for later renewal
                        _deviceSessionIds[sess.DeviceId] = sess.SessionId;

                        // 1. Activate session for RTSP URL
                        string rtspUrl = await ApiManager.Instance.GetPlaybackPlayInfoAsync(sess.SessionId, CancellationToken.None);
                        if (!string.IsNullOrEmpty(rtspUrl))
                        {
                            _camWithRtspUrls[sess.DeviceId] = rtspUrl;
                            LoggerManager.LogDebug($"Kích hoạt thành công RTSP cho {sess.DeviceId}: {rtspUrl}");
                        }
                    }
                }

                // Update search result log
                _viewSearch.txtResultSearch.Inlines.Clear();
                foreach (var cam in SelecedCameraList)
                {
                    bool found = _camWithRtspUrls.ContainsKey(cam.camID);
                    var run = new Run(found
                        ? $"{cam.name} --> Thành công\n"
                        : $"{cam.name} --> Không có dữ liệu \n");
                    run.Foreground = new SolidColorBrush(found ? Colors.WhiteSmoke : Colors.OrangeRed);
                    _viewSearch.txtResultSearch.Inlines.Add(run);
                }

                if (_camWithRtspUrls.Count > 0)
                {
                    LoggerManager.LogInfo($"Bắt đầu phát lại cho {_camWithRtspUrls.Count} camera.");
                    Playback();
                    _playheadTimer.Start();
                }
                else
                {
                    LoggerManager.LogInfo("Không tìm thấy dữ liệu video nào phù hợp.");
                    _playheadTimer.Stop();
                    MessageBox.Show("Không tìm thấy video trong khoảng thời gian này.", "Tìm kiếm video", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi tìm kiếm hoặc kích hoạt session xem lại");
                MessageBox.Show("Có lỗi xảy ra trong quá trình tìm kiếm dữ liệu từ máy chủ. Vui lòng thử lại sau.", "Lỗi hệ thống", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isSearching = false;
            }
        }

        // Redundant - relocated to individual camera windows
        private void TimelineControl_SelectionChanged(object sender, (System.DateTime start, System.DateTime end) e)
        {
        }

        private void btnDownload_Click(object sender, EventArgs e)
        {
        }

        // Redundant - relocated to MainWindow
        private void btnOpenDashboard_Click(object sender, RoutedEventArgs e)
        {
        }

        private void SyncSeek(System.DateTime time)
        {
            if (!_searchStartTime.HasValue) return;

            // Throttling: Only allow seeking every 150ms during active interaction
            if ((System.DateTime.Now - _lastSeekInteractionTime).TotalMilliseconds < 150)
                return;

            _lastSeekInteractionTime = System.DateTime.Now;
            _currentPlaybackTime = time;

            // Capture cameras list on UI thread
            var cameras = gridCameraList.Children.OfType<ViewCamera>().ToList();

            // Perform seeking on background task to avoid UI lag
            System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var vCam in cameras)
                {
                    if (vCam.Player != null)
                    {
                        // Use camera-specific visual offset to handle concatenated streams with gaps
                        long offsetGst = vCam.GetVisualOffsetNs(time);
                        vCam.Player.SeekTo(offsetGst);
                    }
                }
            });
        }

        private void Playback_page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (CamGroupList != null)
                CamGroupList.CollectionChanged -= CamGroupList_CollectionChanged;

            if (GlobalUserInfo.Instance.AreaTree != null)
                GlobalUserInfo.Instance.AreaTree.CollectionChanged -= AreaTree_CollectionChanged;

            if (_timerGPS != null)
            {
                _timerGPS.Stop();
                _timerGPS.Dispose();
                _timerGPS = null;
            }

            if (_playheadTimer != null)
            {
                _playheadTimer.Stop();
                _playheadTimer = null;
            }

            DestroyViewCameras();
        }

        private void ConfigGrid(int rows, int columns)
        {
            // IMPORTANT: Destroy existing cameras before clearing the children collection to prevent memory leaks
            DestroyViewCameras();

            // Reset grid
            gridCameraList.Children.Clear();
            gridCameraList.ColumnDefinitions.Clear();
            gridCameraList.RowDefinitions.Clear();


            for (int i = 0; i < rows; i++)
            {
                RowDefinition row = new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star)
                };
                gridCameraList.RowDefinitions.Add(row);
            }

            for (int j = 0; j < columns; j++)
            {
                ColumnDefinition col = new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                };
                gridCameraList.ColumnDefinitions.Add(col);
            }


        }

        private void ShowCamera(int rows, int columns)
        {
            var camListPlayed = SelecedCameraList.Where(x=> _camWithRtspUrls.ContainsKey(x.camID)).ToList(); 
            int camidx = 0;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < columns; j++)
                {
                    var camModel = camListPlayed[camidx];

                    ViewCamera cam = new ViewCamera(camModel, PlayerType.RtspPlay);
                    cam.PlaybackRtspUrl = _camWithRtspUrls[camModel.camID];
                    cam.IsPlaybackMode = true;
                    
                    if (_lastSearchResult != null && _searchStartTime.HasValue && !(_viewSearch.datetimeTo.Value is null))
                    {
                        var deviceVideos = _lastSearchResult.Videos.ContainsKey(camModel.camID) 
                            ? _lastSearchResult.Videos[camModel.camID] 
                            : new List<ApiManager.PlaybackVideoInfo>();
                        cam.SetSegments(deviceVideos, _searchStartTime.Value, _viewSearch.datetimeTo.Value.Value);
                    }

                    cam.SetTextCenterButton("Play");
                    Grid.SetRow(cam, i); Grid.SetColumn(cam, j);
                    gridCameraList.Children.Add(cam);
                    camidx++;
                    cam.SendGPS += GpsReceiver;
                    cam.SendMetaAIResult += ShowAIResult;
                }

            if (_timerGPS != null)
                _timerGPS.Stop();

            _timerGPS = new System.Timers.Timer(1000);
            _timerGPS.Elapsed += (s, smg) =>
            {
                ForwardGPSBuffer?.Invoke("Playback", _gpsBuffer);                
            };
            _timerGPS.Start();
        }

        private void ShowAIResult(object sender, List<MetaAIResult> aiResult)
        {
            this.logPage.vAIResultLog.ShowAIResult(aiResult);

            // Also add to timeline if within search range
            if (aiResult != null && _searchStartTime.HasValue)
            {
                foreach (var res in aiResult)
                {
                    if (System.DateTime.TryParse(res.TimeStamp, out System.DateTime eventTime))
                    {
                        // Markers are now added to the individual camera's mini-timeline via ViewCamera.ForwardMetaAI
                    }
                }
            }
        }

        private void GpsReceiver(object sender, PointLatLng gps)
        {
            string camid = sender as string;
            _gpsBuffer[camid] = gps;
        }

        public void Playback()
        {
            this.CurrentRate = 1.0f;
            int rows, columns;

            int n = _camWithRtspUrls.Count;
            LoggerManager.LogDebug($"Khởi tạo lưới phát lại cho {n} camera.");
            DestroyViewCameras();

            // Calculate grid size based on exact camera count
            rows = (int)Math.Ceiling(Math.Sqrt(n));
            columns = (int)Math.Ceiling((double)n / rows);

            ConfigGrid(rows, columns);
            ShowCamera(rows, columns);
            txtTotalCam.Text = string.Format("Cam: {0} / {1}", _camWithRtspUrls.Count, SelecedCameraList.Count);
            System.Threading.Tasks.Task.Run(() => {
                List<models.Camera> camList = SelecedCameraList.Where(cam => _camWithRtspUrls.ContainsKey(cam.camID)).ToList();               
                ActiveCamerasChanged?.Invoke(this, camList);
            });
        }


        private void Camera_Selected_Changed(object sender, NotifyCollectionChangedEventArgs e)
        {
            IsObsolete = Visibility;
            startPlayback.Visibility = SelecedCameraList.Count == 0 ? Visibility.Visible : Visibility.Hidden;
        }


        private void Add_Remove_SelectedCameraList(object sender, models.Camera cam)
        {
            if (SelecedCameraList.Contains(cam))
                SelecedCameraList.Remove(cam);
            else
                SelecedCameraList.Add(cam);
            txtTotalCam.Text = string.Format("Cam: {0} / {1}", _camWithRtspUrls.Count, SelecedCameraList.Count);
        }

        private void LeftMenu_Event_Nodes_Camera_Selected_Changed(object sender, List<models.Camera> cameras)
        {
            // Clear and update the entire selection list to keep it in sync with the tree view checkboxes
            SelecedCameraList.Clear();
            if (cameras != null)
            {
                foreach (var cam in cameras)
                {
                    SelecedCameraList.Add(cam);
                }
            }
            txtTotalCam.Text = string.Format("Cam: {0} / {1}", _camWithRtspUrls.Count, SelecedCameraList.Count);
        }

        private void AllowSelectingCamera()
        {
            if (CamGroupList != null)
            {
                foreach (VMTalkGroup group in CamGroupList)
                    EnableSelectionRecursive(group);
            }

            if (GlobalUserInfo.Instance.AreaTree != null)
            {
                foreach (var area in GlobalUserInfo.Instance.AreaTree)
                    EnableSelectionRecursive(area);
            }
        }

        private void EnableSelectionRecursive(VMTalkGroup group)
        {
            if (group.Cameras != null)
            {
                foreach (var c in group.Cameras)
                {
                    c.AllowSelecting = Visibility.Visible;
                    c.IsChecked = false; // Reset for new session
                }
            }
            if (group.SubGroups != null)
            {
                foreach (var s in group.SubGroups) EnableSelectionRecursive(s);
            }
        }

        private void EnableSelectionRecursive(AreaNode area)
        {
            if (area.Units != null)
            {
                foreach (var u in area.Units) EnableSelectionRecursive(u);
            }
        }

        private void EnableSelectionRecursive(UnitNode unit)
        {
            if (unit.Cams != null)
            {
                foreach (var c in unit.Cams)
                {
                    c.AllowSelecting = Visibility.Visible;
                    c.IsChecked = false; // Reset for new session
                }
            }
            if (unit.SubUnits != null)
            {
                foreach (var s in unit.SubUnits) EnableSelectionRecursive(s);
            }
        }

        public void PlaybackControl(object sender, RoutedEventArgs e)
        {
            //  if (gridCameraList.Children.Count<=0) return;

            Button selectedButton = sender as Button;
            if (selectedButton == null) return;
            switch (selectedButton.Name)
            {
                case "btnPlay":
                    string icon = IsPlaying ? "/images/videocontrols/pause.png" :
                        "/images/videocontrols/play.png";
                    playImage.Source = libs.GlobalClass.LoadImage(icon);
                    if (IsPlaying)
                        PauseAllCam();
                    else
                        PlayAllCam();

                    IsPlaying = !IsPlaying;
                    return;

                case "btnSeekBack":
                    SeekBackwardPlayers();
                    return;
                case "btnSeekForward":
                    SeekForwardPlayers();
                    break;

                case "btnPrevious":
                    CurrentRate = CurrentRate - stepRate;
                    break;               
                case "btnForward":
                    CurrentRate = CurrentRate + stepRate;
                    break;

            }
            CurrentRate = System.Math.Max(CurrentRate, 0.1f);
            CurrentRate = System.Math.Min(CurrentRate, 4.0f);        
            SetRatePlayers(CurrentRate);
        }
       
        private void PlayAllCam()
        {

            if (CurrentRate != 0)
            {
                CurrentRate = 1.0f;
                SetRatePlayers(CurrentRate);
            }

            foreach (var child in gridCameraList.Children)
            {
                ViewCamera cam = child as ViewCamera;
                if (cam != null && cam.Player != null)
                    cam.Player.Playing();
            }
        }
        private void PauseAllCam()
        {
            foreach (var child in gridCameraList.Children)
            {
                ViewCamera cam = child as ViewCamera;
                if (cam != null && cam.Player !=null )
                    cam.Player.Pause();
            }
        }

        private void SetRatePlayers(float rate)
        {
            foreach (var child in gridCameraList.Children)
            {
                ViewCamera cam = child as ViewCamera;
                if (cam != null && cam.Player != null)
                    cam.Player.SetRate(rate);
            }
        }

        private void SeekBackwardPlayers() 
        {
            foreach (var child in gridCameraList.Children)
            {
                ViewCamera cam = child as ViewCamera;
                if (cam != null && cam.Player != null)
                    cam.Player.SeekBackward();
            }

        }
        private void SeekForwardPlayers() 
        {
            foreach (var child in gridCameraList.Children)
            {
                ViewCamera cam = child as ViewCamera;
                if (cam != null && cam.Player != null)
                    cam.Player.SeekForward();
            }

        }

        private void DestroyViewCameras()
        {
            // Capture the list on the UI thread to avoid iterator invalidation
            var cameras = gridCameraList.Children.OfType<ViewCamera>().ToList();

            foreach (var cam in cameras)
            {
                try
                {
                    cam.Dispose();
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, $"Lỗi khi dispose camera {cam?.Camera?.camID}");
                }
            }
        }

        /// <summary>
        /// Renew all playback sessions after EOS without re-searching.
        /// Sessions stay alive for 5 minutes (grace period) on the server.
        /// If renewal succeeds, playback can restart from the beginning.
        /// If session expired, user must search again.
        /// </summary>
        public async Task<bool> RenewSessionsAsync()
        {
            if (_deviceSessionIds.Count == 0) return false;

            LoggerManager.LogDebug($"Đang thực hiện làm mới {_deviceSessionIds.Count} sessions playback.");
            bool anyRenewed = false;
            var expiredDevices = new List<string>();

            foreach (var kvp in _deviceSessionIds)
            {
                string deviceId = kvp.Key;
                string sessionId = kvp.Value;

                try
                {
                    string newRtspUrl = await ApiManager.Instance.RenewPlaybackSessionAsync(sessionId, CancellationToken.None);
                    if (!string.IsNullOrEmpty(newRtspUrl))
                    {
                        _camWithRtspUrls[deviceId] = newRtspUrl;
                        anyRenewed = true;
                        LoggerManager.LogDebug($"Làm mới thành công session cho {deviceId}.");
                    }
                    else
                    {
                        expiredDevices.Add(deviceId);
                    }
                }
                catch (Exception ex)
                {
                    LoggerManager.LogError($"Lỗi khi làm mới session cho {deviceId}", ex);
                    expiredDevices.Add(deviceId);
                }
            }

            if (expiredDevices.Count > 0)
            {
                LoggerManager.LogWarn($"{expiredDevices.Count} sessions đã hết hạn hoàn toàn.");
                // Remove expired devices
                foreach (var deviceId in expiredDevices)
                {
                    _camWithRtspUrls.Remove(deviceId);
                    _deviceSessionIds.Remove(deviceId);
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    _viewSearch.txtResultSearch.Inlines.Clear();
                    _viewSearch.txtResultSearch.Inlines.Add(
                        new Run($"{expiredDevices.Count} session(s) đã hết hạn. Nhấn Search để tạo lại.")
                        { Foreground = new SolidColorBrush(Colors.Orange) });
                });
            }

            if (anyRenewed)
            {
                LoggerManager.LogInfo("Đã làm mới thành công ít nhất 1 session, bắt đầu phát lại.");
                // Restart playback with renewed sessions
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_searchStartTime.HasValue)
                        _currentPlaybackTime = _searchStartTime.Value;
                    Playback();
                    _playheadTimer.Start();
                });
            }

            return anyRenewed;
        }
    }
}
