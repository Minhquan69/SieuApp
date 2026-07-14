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
    /// Interaction logic for Playback_HLS_page.xaml
    /// </summary>
    public partial class VPlaybackHLS : Page, INotifyPropertyChanged
    {
        System.Timers.Timer _timerGPS;

        public event EventHandler<List<models.Camera>> ActiveCamerasChanged;
        public event EventHandler<Dictionary<string, GMap.NET.PointLatLng>> ForwardGPSBuffer;
        Dictionary<string, GMap.NET.PointLatLng> _gpsBuffer { get; set; } = new Dictionary<string, GMap.NET.PointLatLng>();

        public event PropertyChangedEventHandler PropertyChanged;

        private const int InactivityThreshold = 2000;

        public bool IsPlaying { get; set; } = true;

        private const float stepRate = 0.2f;

        private float _currentRate;
        private Dictionary<string, string> _camWithHlsUrls = new Dictionary<string, string>();
        
        private System.DateTime? _searchStartTime;
        private System.DateTime? _searchEndTime;
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

        public VPlaybackHLS(ObservableCollection<VMTalkGroup> cam_group_list)
        {
            InitializeComponent();
            DataContext = this;

            Unloaded += Playback_page_Unloaded;
            CurrentRate = 1.0f;
            IsObsolete = Visibility.Hidden;
            CamGroupList = cam_group_list;
            AllowSelectingCamera();

            leftMenu = new LeftMenu(CamGroupList, heightZoneCameraList: 350);
            RightMenu = new UI.Pages.RightPlayback();

            leftMenu.Event_Camera_Selected_Changed += Add_Remove_SelectedCameraList;
            leftMenu.Event_Nodes_Camera_Selected_Changed += LeftMenu_Event_Nodes_Camera_Selected_Changed;
            SelecedCameraList.CollectionChanged += Camera_Selected_Changed;

            leftMenu.frameBottom.Navigate(_viewSearch);
            _viewSearch.EventSeachClick += btnSearch_Click;
            frmLeftMenu.Navigate(leftMenu);
            logPage = new UI.Pages.RightPlayback();
            

            frmRightSide.Content = logPage;

            Loaded += Page_Loaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
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

        private void btnSearch_Click(object sender, List<System.DateTime?> e)
        {
            if (_isSearching) return;
            _isSearching = true;

            try
            {
                if (e[0] == null || e[1] == null) return;
                System.DateTime fromdate = (System.DateTime)e[0];
                System.DateTime todate = (System.DateTime)e[1];

                LoggerManager.LogDebug($"Bắt đầu tìm kiếm video từ {fromdate} đến {todate}");

                TimeSpan duration = todate - fromdate;
                if (duration.TotalHours > 168)
                {
                    LoggerManager.LogWarn("Khoảng thời gian tìm kiếm quá lớn (> 7 day)");
                    MessageBox.Show("Khoảng thời gian tìm kiếm khuyên dùng dưới 7 day!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                List<string> cameraIds = SelecedCameraList?.Select(cam => cam.camID).ToList() ?? new List<string>();
                if (cameraIds.Count == 0)
                {
                    LoggerManager.LogWarn("Người dùng chưa chọn thiết bị để tìm kiếm .");
                    MessageBox.Show("Bạn chưa chọn thiết bị", "Tìm kiếm video", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _viewSearch.txtResultSearch.Inlines.Clear();
                _viewSearch.txtResultSearch.Inlines.Add(new Run("Chuẩn bị dữ liệu ...") { Foreground = new SolidColorBrush(Colors.LightBlue) });

                _camWithHlsUrls.Clear();
                btnDownload.Visibility = Visibility.Collapsed;
                _searchStartTime = fromdate;
                _searchEndTime = todate;
                 
                //Playback voi HLS server
                string hlsServer = ApiManager.Instance.GetEndpointUrl("_playback");
                
                foreach (var cam in SelecedCameraList)
                {
                    string startStr = fromdate.ToString("yyyy-MM-ddTHH:mm:ss");
                    string endStr = todate.ToString("yyyy-MM-ddTHH:mm:ss");
                    string hlsUrl = $"{hlsServer}/playlist.m3u8?device_id={cam.camID}&start_time={startStr}&end_time={endStr}";
                    _camWithHlsUrls[cam.camID] = hlsUrl;
                    LoggerManager.LogDebug($"Đã hoàn thành link xem lại playback cho {cam.camID}: {hlsUrl}");
                }

                _viewSearch.txtResultSearch.Inlines.Clear();
                foreach (var cam in SelecedCameraList)
                {
                    bool found = _camWithHlsUrls.ContainsKey(cam.camID);
                    var run = new Run(found ? $"{cam.name} --> Ready \n" : $"{cam.name} -->Not found\n");
                    run.Foreground = new SolidColorBrush(found ? Colors.WhiteSmoke : Colors.OrangeRed);
                    _viewSearch.txtResultSearch.Inlines.Add(run);
                }

                if (_camWithHlsUrls.Count > 0)
                {
                    LoggerManager.LogInfo($"Bắt đầu phát lại  cho {_camWithHlsUrls.Count} camera.");
                    PlaybackHLS();
                }
                else
                {
                    LoggerManager.LogInfo("Không có URL  nào được tạo.");
                    MessageBox.Show("Không có URL  nào được tạo.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi tìm kiếm video playback");
                MessageBox.Show("Có lỗi xảy ra. Vui lòng thử lại sau.", "Lỗi hệ thống", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isSearching = false;
            }
        }

        private void btnDownload_Click(object sender, EventArgs e) { }

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

            DestroyViewCameras();
        }

        private void ConfigGrid(int rows, int columns)
        {
            DestroyViewCameras();

            gridCameraList.Children.Clear();
            gridCameraList.ColumnDefinitions.Clear();
            gridCameraList.RowDefinitions.Clear();

            for (int i = 0; i < rows; i++)
            {
                RowDefinition row = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
                gridCameraList.RowDefinitions.Add(row);
            }

            for (int j = 0; j < columns; j++)
            {
                ColumnDefinition col = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
                gridCameraList.ColumnDefinitions.Add(col);
            }
        }

        private async void ShowCamera(int rows, int columns)
        {
            var camListPlayed = SelecedCameraList.Where(x => _camWithHlsUrls.ContainsKey(x.camID)).ToList();
            int camidx = 0;
            
            // Lấy token nếu có (có thể được định nghĩa trong GlobalSystem)
            string token = ApiManager.Instance.GetEndpointToken("_playback") ?? "";
            
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < columns; j++)
                {
                    if (camidx >= camListPlayed.Count) break;

                    var camModel = camListPlayed[camidx];

                    ViewCameraPlayback cam = new ViewCameraPlayback(camModel);
                    string hlsUrl = _camWithHlsUrls[camModel.camID];
                    cam.HlsUrl = hlsUrl;

                    Grid.SetRow(cam, i); Grid.SetColumn(cam, j);
                    gridCameraList.Children.Add(cam);
                    camidx++;
                    cam.SendGPS += GpsReceiver;
                    cam.SendMetaAIResult += ShowAIResult;

                    // Fetch m3u8 content
                    try
                    {
                        using (var client = new System.Net.Http.HttpClient())
                        {
                            if (!string.IsNullOrEmpty(token))
                            {
                                client.DefaultRequestHeaders.Add("X-Playback-Token", token);
                            }
                            string m3u8Content = await client.GetStringAsync(hlsUrl);
                            if (_searchStartTime.HasValue && _searchEndTime.HasValue)
                            {
                                cam.ParseM3U8AndRenderTimeline(m3u8Content, _searchStartTime.Value, _searchEndTime.Value);
                            }
                            else
                            {
                                cam.ParseM3U8AndRenderTimeline(m3u8Content,System. DateTime.Now,System. DateTime.Now);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerManager.LogException(ex, $"Lỗi tải m3u8 cho camera {camModel.name}");
                    }

                    // Tự động kết nối luồng khi load lên grid
                    cam.ConnectedCamera();
                }
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
        }

        private void GpsReceiver(object sender, PointLatLng gps)
        {
            string camid = sender as string;
            _gpsBuffer[camid] = gps;
        }

        public void PlaybackHLS()
        {
            this.CurrentRate = 1.0f;
            int rows, columns;

            int n = _camWithHlsUrls.Count;
            LoggerManager.LogDebug($"Khởi tạo lưới playback cho {n} camera.");
            DestroyViewCameras();

            rows = (int)Math.Ceiling(Math.Sqrt(n));
            columns = (int)Math.Ceiling((double)n / rows);

            ConfigGrid(rows, columns);
            ShowCamera(rows, columns);
            txtTotalCam.Text = string.Format("Cam: {0} / {1}", _camWithHlsUrls.Count, SelecedCameraList.Count);
            System.Threading.Tasks.Task.Run(() => {
                List<models.Camera> camList = SelecedCameraList.Where(cam => _camWithHlsUrls.ContainsKey(cam.camID)).ToList();
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
            txtTotalCam.Text = string.Format("Cam: {0} / {1}", _camWithHlsUrls.Count, SelecedCameraList.Count);
        }

        private void LeftMenu_Event_Nodes_Camera_Selected_Changed(object sender, List<models.Camera> cameras)
        {
            SelecedCameraList.Clear();
            if (cameras != null)
            {
                foreach (var cam in cameras)
                {
                    SelecedCameraList.Add(cam);
                }
            }
            txtTotalCam.Text = string.Format("Cam: {0} / {1}", _camWithHlsUrls.Count, SelecedCameraList.Count);
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
                    c.IsChecked = false;
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
                    c.IsChecked = false;
                }
            }
            if (unit.SubUnits != null)
            {
                foreach (var s in unit.SubUnits) EnableSelectionRecursive(s);
            }
        }

        public void PlaybackControl(object sender, RoutedEventArgs e)
        {
            Button selectedButton = sender as Button;
            if (selectedButton == null) return;
            switch (selectedButton.Name)
            {
                case "btnPlay":
                    string icon = IsPlaying ? "/images/videocontrols/pause.png" : "/images/videocontrols/play.png";
                    playImage.Source = libs.GlobalClass.LoadImage(icon);
                    if (IsPlaying) PauseAllCam(); else PlayAllCam();
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
                if (cam != null && cam.Player != null)
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
    }
}
