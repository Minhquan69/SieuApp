using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
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
        private readonly List<AggregateTimelineRow> _aggregateTimelineRows = new List<AggregateTimelineRow>();
        private bool _isDraggingAggregateTimeline = false;
        private System.DateTime? _aggregateCurrentTime = null;
        private const int AggregateTimelineSeekThrottleMs = 150;
        private const double AggregateLabelWidth = 72;
        private const double AggregateAxisHeight = 18;
        private const double AggregateRowHeight = 13;
        private const double AggregateLegendHeight = 16;
        private const double AggregateMinHeight = 48;
        private const double AggregateMaxHeight = 92;
        private static readonly Color AggregateBackgroundColor = Color.FromRgb(17, 19, 24);
        private static readonly Color AggregatePanelColor = Color.FromRgb(13, 15, 18);
        private static readonly Color AggregateTextColor = Color.FromRgb(202, 214, 224);
        private static readonly Color AggregateMutedTextColor = Color.FromRgb(142, 158, 172);
        private static readonly Color AggregateGridLineColor = Color.FromArgb(135, 72, 92, 108);
        private static readonly Color AggregateVideoColor = Color.FromRgb(0, 224, 176);
        private static readonly Color AggregateLossColor = Color.FromRgb(54, 68, 78);
        private static readonly Color AggregatePlayheadColor = Color.FromRgb(255, 132, 38);

        private class AggregateTimelineRow
        {
            public string CameraId { get; set; }
            public string CameraName { get; set; }
            public List<ViewCameraPlayback.PlaybackSegment> Segments { get; set; } = new List<ViewCameraPlayback.PlaybackSegment>();
        }

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

                LoggerManager.LogDebug($"Báº¯t Ä‘áº§u tÃ¬m kiáº¿m video tá»« {fromdate} Ä‘áº¿n {todate}");

                TimeSpan duration = todate - fromdate;
                if (duration.TotalHours > 168)
                {
                    LoggerManager.LogWarn("Khoáº£ng thá»i gian tÃ¬m kiáº¿m quÃ¡ lá»›n (> 7 day)");
                    MessageBox.Show("Khoáº£ng thá»i gian tiÌ€m kiÃªÌm khuyÃªn dÃ¹ng dÆ°á»›i 7 day!", "Cáº£nh bÃ¡o", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                List<string> cameraIds = SelecedCameraList?.Select(cam => cam.camID).ToList() ?? new List<string>();
                if (cameraIds.Count == 0)
                {
                    LoggerManager.LogWarn("NgÆ°á»i dÃ¹ng chÆ°a chá»n thiáº¿t bá»‹ Ä‘á»ƒ tÃ¬m kiáº¿m .");
                    MessageBox.Show("Báº¡n chÆ°a chá»n thiáº¿t bá»‹", "TÃ¬m kiáº¿m video", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _viewSearch.txtResultSearch.Inlines.Clear();
                _viewSearch.txtResultSearch.Inlines.Add(new Run("ChuÃ¢Ì‰n biÌ£ dÆ°Ìƒ liÃªÌ£u ...") { Foreground = new SolidColorBrush(Colors.LightBlue) });

                _camWithHlsUrls.Clear();
                btnDownload.Visibility = Visibility.Collapsed;
                _searchStartTime = fromdate;
                _searchEndTime = todate;
                 
                //Playback voi HLS server
                string hlsServer = ApiManager.Instance.GetEndpointUrl("_playback");
                string playbackToken = ApiManager.Instance.GetEndpointToken("_playback") ?? "";
                
                foreach (var cam in SelecedCameraList)
                {
                    string hlsUrl = BuildPlaylistUrl(hlsServer, cam.camID, fromdate, todate, playbackToken);
                    _camWithHlsUrls[cam.camID] = hlsUrl;
                    LoggerManager.LogDebug($"ÄaÌƒ hoaÌ€n thaÌ€nh link xem laÌ£i playback cho {cam.camID}: {hlsUrl}");
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
                    LoggerManager.LogInfo($"Báº¯t Ä‘áº§u phÃ¡t láº¡i  cho {_camWithHlsUrls.Count} camera.");
                    PlaybackHLS();
                }
                else
                {
                    LoggerManager.LogInfo("KhÃ´ng cÃ³ URL  nÃ o Ä‘Æ°á»£c táº¡o.");
                    MessageBox.Show("KhÃ´ng cÃ³ URL  nÃ o Ä‘Æ°á»£c táº¡o.", "Lá»—i", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lá»—i khi tÃ¬m kiáº¿m video playback");
                MessageBox.Show("CÃ³ lá»—i xáº£y ra. Vui lÃ²ng thá»­ láº¡i sau.", "Lá»—i há»‡ thá»‘ng", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isSearching = false;
            }
        }

        private void btnDownload_Click(object sender, EventArgs e)
        {
            QueuePlaybackDownloads(GetDownloadableCameras());
        }

        private void AggregateTimelineCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowPlaybackDownloadMenu();
        }

        private void ShowPlaybackDownloadMenu()
        {
            var cameras = GetDownloadableCameras();
            if (cameras.Count == 0)
            {
                MessageBox.Show("ChÆ°a coÌ camera playback Ä‘ÃªÌ‰ taÌ‰i video.", "TaÌ‰i video", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var menu = new ContextMenu
            {
                PlacementTarget = aggregateTimelineCanvas,
                Style = TryFindResource("PlaybackDownloadContextMenuStyle") as Style
            };

            var downloadAllItem = new System.Windows.Controls.MenuItem
            {
                Header = CreateDownloadMenuHeader($"Download all ({cameras.Count})"),

                Style = TryFindResource("PlaybackDownloadMenuItemStyle") as Style
            };
            downloadAllItem.Click += (s, e) => QueuePlaybackDownloads(cameras);
            menu.Items.Add(downloadAllItem);
            menu.Items.Add(new Separator
            {
                Style = TryFindResource("PlaybackDownloadSeparatorStyle") as Style
            });

            foreach (var cam in cameras)
            {
                var camera = cam;
                string cameraText = string.IsNullOrWhiteSpace(camera.name) ? camera.camID : $"{camera.name} ({camera.camID})";
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = CreateDownloadMenuHeader(cameraText, 0.82),
                    Style = TryFindResource("PlaybackDownloadMenuItemStyle") as Style
                };
                item.Click += (s, e) => QueuePlaybackDownloads(new List<models.Camera> { camera });
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

        private static StackPanel CreateDownloadMenuHeader(string text, double iconOpacity = 1.0)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = new SolidColorBrush(Color.FromRgb(27, 32, 41))
            };
            panel.Children.Add(CreateDownloadMenuIcon(iconOpacity));
            panel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(232, 236, 241)),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            return panel;
        }

        private static Border CreateDownloadMenuIcon(double opacity = 1.0)
        {
            return new Border
            {
                Width = 20,
                Height = 20,
                Opacity = opacity,

                Background = new SolidColorBrush(Color.FromRgb(27, 32, 41)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(2),
                Child = new System.Windows.Controls.Image
                {
                    Source = new BitmapImage(new System.Uri("pack://application:,,,/images/playback/download.png", System.UriKind.Absolute)),
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true
                }
            };
        }

        private List<models.Camera> GetDownloadableCameras()
        {
            return SelecedCameraList?
                .Where(cam => cam != null && !string.IsNullOrWhiteSpace(cam.camID) && _camWithHlsUrls.ContainsKey(cam.camID))
                .ToList() ?? new List<models.Camera>();
        }

        private void QueuePlaybackDownloads(IList<models.Camera> cameras)
        {
            try
            {
                if (!_searchStartTime.HasValue || !_searchEndTime.HasValue || _searchEndTime <= _searchStartTime)
                {
                    MessageBox.Show("ChÆ°a coÌ khoaÌ‰ng thÆ¡Ì€i gian hÆ¡Ì£p lÃªÌ£ taÌ‰i video.", "TaÌ‰i video", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (cameras == null || cameras.Count == 0)
                {
                    MessageBox.Show("ChÆ°a coÌ camera xem laÌ£i Ä‘ÃªÌ‰ taÌ‰i video.", "TaÌ‰i video", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string playbackServer = ApiManager.Instance.GetEndpointUrl("_playback");
                if (string.IsNullOrWhiteSpace(playbackServer))
                {
                    MessageBox.Show("MaÌy chuÌ‰ xem laÌ£i khÃ´ng khaÌ‰ duÌ£ng.", "TaÌ‰i video", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string token = ApiManager.Instance.GetEndpointToken("_playback") ?? "";
                string savePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iVista_Downloads");
                Directory.CreateDirectory(savePath);

                foreach (var cam in cameras)

                {
                    string fileName = BuildExportFileName(cam.camID, _searchStartTime.Value, _searchEndTime.Value);
                    string exportUrl = BuildExportUrl(playbackServer, cam.camID, _searchStartTime.Value, _searchEndTime.Value, fileName, token);
                    string destinationPath = System.IO.Path.Combine(savePath, fileName);
                    string cameraName = string.IsNullOrWhiteSpace(cam.name) ? cam.camID : cam.name;

                    SmartDownloadManager.Instance.QueueDirectDownload(
                        exportUrl,
                        destinationPath,
                        cameraName,
                        _searchStartTime.Value,
                        _searchEndTime.Value,
                        token,
                        "X-Playback-Token");
                }

                ShowDownloadManagerWindow();
                MessageBox.Show($"ÄaÌƒ thÃªm {cameras.Count} video vaÌ€o haÌ€ng Ä‘Æ¡Ì£i.", "TaÌ‰i video", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "LÃ´Ìƒi thao taÌc khi download video");
                MessageBox.Show("KhÃ´ng thÃªÌ‰ taÌ£o taÌc vuÌ£ taÌ‰i video. Vui loÌ€ng thÆ°Ì‰ laÌ£i.", "TaÌ‰i video", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string BuildExportFileName(string deviceId, System.DateTime start, System.DateTime end)
        {
            string safeDeviceId = MakeSafeFileName(deviceId);
            return $"export_{safeDeviceId}_{start:yyyyMMddHHmmss}_{end:yyyyMMddHHmmss}.mp4";
        }

        private static string BuildPlaylistUrl(string playbackServer, string deviceId, System.DateTime start, System.DateTime end, string token)
        {
            string url = playbackServer.TrimEnd('/') + "/playlist.m3u8";
            var query = new List<string>
            {
                "device_id=" + System.Uri.EscapeDataString(deviceId ?? ""),
                "start_time=" + System.Uri.EscapeDataString(start.ToString("yyyy-MM-ddTHH:mm:ss")),
                "end_time=" + System.Uri.EscapeDataString(end.ToString("yyyy-MM-ddTHH:mm:ss")),
                "playback=fmp4"
            };

            if (!string.IsNullOrWhiteSpace(token))
            {
                query.Add("token=" + System.Uri.EscapeDataString(token));
            }

            return url + "?" + string.Join("&", query);
        }

        private static string BuildExportUrl(string playbackServer, string deviceId, System.DateTime start, System.DateTime end, string fileName, string token)
        {
            string url = playbackServer.TrimEnd('/') + "/export.mp4";
            var query = new List<string>
            {
                "device_id=" + System.Uri.EscapeDataString(deviceId ?? ""),
                "start_time=" + System.Uri.EscapeDataString(start.ToString("yyyy-MM-ddTHH:mm:ss")),
                "end_time=" + System.Uri.EscapeDataString(end.ToString("yyyy-MM-ddTHH:mm:ss")),
                "mode=fast",
                "filename=" + System.Uri.EscapeDataString(fileName)
            };

            if (!string.IsNullOrWhiteSpace(token))
            {
                query.Add("token=" + System.Uri.EscapeDataString(token));
            }

            return url + "?" + string.Join("&", query);

        }

        private static string MakeSafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "camera";
            }

            string safe = value;
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(invalid, '_');
            }

            return safe;
        }

        private void ShowDownloadManagerWindow()
        {
            var existing = System.Windows.Application.Current.Windows.OfType<DownloadManagerWindow>().FirstOrDefault();
            if (existing != null)
            {
                existing.Activate();
                return;
            }

            var window = new DownloadManagerWindow();
            var owner = Window.GetWindow(this);
            if (owner != null)
            {
                window.Owner = owner;
            }

            window.Show();
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

            DestroyViewCameras();
            ResetAggregateTimeline();
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
            
            // Lấy token trong GlobalSystem)
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
                                RegisterAggregateTimelineRow(camModel, cam.GetTimelineSegments());
                            }
                            else
                            {
                                cam.ParseM3U8AndRenderTimeline(m3u8Content,System. DateTime.Now,System. DateTime.Now);
                                RegisterAggregateTimelineRow(camModel, cam.GetTimelineSegments());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerManager.LogException(ex, $"Lá»—i táº£i m3u8 cho camera {camModel.name}");
                    }

                    // Tá»± Ä‘á»™ng káº¿t ná»‘i luá»“ng khi load lÃªn grid
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

        private void ResetAggregateTimeline()
        {
            _aggregateTimelineRows.Clear();
            _aggregateCurrentTime = _searchStartTime;

            RenderAggregateTimeline();
        }

        private void RegisterAggregateTimelineRow(models.Camera camera, List<ViewCameraPlayback.PlaybackSegment> segments)
        {
            if (camera == null)
            {
                return;
            }

            string cameraName = string.IsNullOrWhiteSpace(camera.name) ? camera.camID : camera.name;
            cameraName = cameraName.Replace("Cam", "").Trim();

            _aggregateTimelineRows.RemoveAll(x => x.CameraId == camera.camID);
            _aggregateTimelineRows.Add(new AggregateTimelineRow
            {
                CameraId = camera.camID,
                CameraName = string.IsNullOrWhiteSpace(cameraName) ? camera.camID : cameraName,
                Segments = segments ?? new List<ViewCameraPlayback.PlaybackSegment>()
            });

            RenderAggregateTimeline();
        }

        private void RenderAggregateTimeline()
        {
            if (aggregateTimelineCanvas == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateAggregateTimelineHeight();

                double width = aggregateTimelineCanvas.ActualWidth;
                double height = aggregateTimelineCanvas.ActualHeight;
                if (width <= 0 || double.IsNaN(width)) width = 800;
                if (height <= 0 || double.IsNaN(height)) height = AggregateTimelineRowDefinition.ActualHeight;
                if (height <= 0 || double.IsNaN(height)) height = GetDesiredAggregateTimelineHeight();

                aggregateTimelineCanvas.Children.Clear();

                var background = new Rectangle
                {
                    Fill = new SolidColorBrush(AggregateBackgroundColor),
                    Width = width,
                    Height = height
                };
                aggregateTimelineCanvas.Children.Add(background);


                var panel = new Rectangle
                {
                    Fill = new SolidColorBrush(AggregatePanelColor),
                    Width = Math.Max(0, width - 2),
                    Height = Math.Max(0, height - 2),
                    Opacity = 0.98
                };
                Canvas.SetLeft(panel, 0);
                Canvas.SetTop(panel, 1);
                aggregateTimelineCanvas.Children.Add(panel);

                string totalText = string.Format("Cam: {0} / {1}", _camWithHlsUrls.Count, SelecedCameraList.Count);
                AddTimelineText(totalText, 12, 3, 10, AggregateTextColor, FontWeights.SemiBold);

                if (!_searchStartTime.HasValue || !_searchEndTime.HasValue || _searchEndTime <= _searchStartTime)
                {
                    AddTimelineText("ChÆ°a coÌ dÆ°Ìƒ liÃªÌ£u video", AggregateLabelWidth + 8, 24, 11, AggregateMutedTextColor, FontWeights.Normal);
                    return;
                }

                double totalSeconds = (_searchEndTime.Value - _searchStartTime.Value).TotalSeconds;
                if (totalSeconds <= 0)
                {
                    return;
                }

                double laneLeft = AggregateLabelWidth;
                double laneWidth = Math.Max(100, width - laneLeft - 10);
                double rowCount = Math.Max(1, _aggregateTimelineRows.Count);
                double availableRowsHeight = Math.Max(10, height - AggregateAxisHeight - AggregateLegendHeight - 4);
                double rowHeight = Math.Max(6, Math.Min(AggregateRowHeight, availableRowsHeight / rowCount));
                double trackHeight = Math.Max(4, Math.Min(8, rowHeight - 3));
                double fontSize = rowHeight < 9 ? 8 : 10;

                DrawAggregateTimeAxis(laneLeft, laneWidth, height);

                for (int i = 0; i < _aggregateTimelineRows.Count; i++)
                {
                    var row = _aggregateTimelineRows[i];
                    double y = AggregateAxisHeight + i * rowHeight;
                    string label = row.CameraName;
                    if (label.Length > 10)
                    {
                        label = label.Substring(0, 10);
                    }

                    AddTimelineText(label, 16, y + Math.Max(0, (rowHeight - fontSize - 2) / 2), fontSize, AggregateMutedTextColor, FontWeights.Normal);

                    var lossTrack = new Rectangle

                    {
                        Fill = new SolidColorBrush(AggregateLossColor),
                        Width = laneWidth,
                        Height = trackHeight,
                        Opacity = 0.95,
                        RadiusX = 0,
                        RadiusY = 0
                    };
                    Canvas.SetLeft(lossTrack, laneLeft);
                    Canvas.SetTop(lossTrack, y + Math.Max(2, (rowHeight - trackHeight) / 2));
                    aggregateTimelineCanvas.Children.Add(lossTrack);

                    foreach (var range in GetMergedVideoRanges(row.Segments, totalSeconds))
                    {
                        double x = laneLeft + (range.Item1 / totalSeconds) * laneWidth;
                        double segmentWidth = ((range.Item2 - range.Item1) / totalSeconds) * laneWidth;
                        var videoRect = new Rectangle
                        {
                            Fill = new SolidColorBrush(AggregateVideoColor),
                            Width = Math.Max(2, segmentWidth),
                            Height = trackHeight,
                            Opacity = 0.95
                        };
                        Canvas.SetLeft(videoRect, x);
                        Canvas.SetTop(videoRect, y + Math.Max(2, (rowHeight - trackHeight) / 2));
                        aggregateTimelineCanvas.Children.Add(videoRect);
                    }
                }

                DrawAggregatePlayhead(laneLeft, laneWidth, totalSeconds, height);
                DrawAggregateLegend(height);
            }));
        }

        private void UpdateAggregateTimelineHeight()
        {
            if (AggregateTimelineRowDefinition == null)
            {
                return;
            }

            double desiredHeight = GetDesiredAggregateTimelineHeight();
            if (Math.Abs(AggregateTimelineRowDefinition.Height.Value - desiredHeight) > 0.5)
            {
                AggregateTimelineRowDefinition.Height = new GridLength(desiredHeight);
            }
        }

        private double GetDesiredAggregateTimelineHeight()
        {

            int rowCount = Math.Max(1, _aggregateTimelineRows.Count);
            double desiredHeight = AggregateAxisHeight + AggregateLegendHeight + 8 + Math.Min(rowCount, 6) * AggregateRowHeight;
            if (rowCount > 6)
            {
                desiredHeight += Math.Min(12, (rowCount - 6) * 2);
            }

            return Math.Max(AggregateMinHeight, Math.Min(AggregateMaxHeight, desiredHeight));
        }

        private IEnumerable<Tuple<double, double>> GetMergedVideoRanges(List<ViewCameraPlayback.PlaybackSegment> segments, double totalSeconds)
        {
            if (segments == null || !_searchStartTime.HasValue)
            {
                yield break;
            }

            var ranges = segments
                .Where(s => s.HasVideo && s.Duration > 0)
                .Select(s =>
                {
                    double start = Math.Max(0, (s.RealStartTime - _searchStartTime.Value).TotalSeconds);
                    double end = Math.Min(totalSeconds, start + s.Duration);
                    return Tuple.Create(start, end);
                })
                .Where(r => r.Item2 > 0 && r.Item1 < totalSeconds && r.Item2 > r.Item1)
                .OrderBy(r => r.Item1)
                .ToList();

            if (ranges.Count == 0)
            {
                yield break;
            }

            double currentStart = ranges[0].Item1;
            double currentEnd = ranges[0].Item2;
            const double mergeGapSeconds = 2.0;

            for (int i = 1; i < ranges.Count; i++)
            {
                double nextStart = ranges[i].Item1;
                double nextEnd = ranges[i].Item2;
                if (nextStart <= currentEnd + mergeGapSeconds)
                {
                    currentEnd = Math.Max(currentEnd, nextEnd);
                    continue;
                }

                yield return Tuple.Create(currentStart, currentEnd);
                currentStart = nextStart;

                currentEnd = nextEnd;
            }

            yield return Tuple.Create(currentStart, currentEnd);
        }

        private void DrawAggregateTimeAxis(double laneLeft, double laneWidth, double height)
        {
            int tickCount = 4;
            for (int i = 0; i <= tickCount; i++)
            {
                double ratio = (double)i / tickCount;
                System.DateTime tickTime = _searchStartTime.Value.AddSeconds((_searchEndTime.Value - _searchStartTime.Value).TotalSeconds * ratio);
                double x = laneLeft + laneWidth * ratio;

                var tickLine = new Rectangle
                {
                    Fill = new SolidColorBrush(AggregateGridLineColor),
                    Width = 1,
                    Height = Math.Max(10, height - AggregateAxisHeight - AggregateLegendHeight)
                };
                Canvas.SetLeft(tickLine, x);
                Canvas.SetTop(tickLine, AggregateAxisHeight);
                aggregateTimelineCanvas.Children.Add(tickLine);

                AddTimelineText(tickTime.ToString("HH:mm"), Math.Max(laneLeft, x - 18), 2, 10, AggregateTextColor, FontWeights.Normal);
            }
        }

        private void DrawAggregatePlayhead(double laneLeft, double laneWidth, double totalSeconds, double height)
        {
            System.DateTime currentTime = _aggregateCurrentTime ?? _searchStartTime.Value;
            double offset = Math.Max(0, Math.Min((currentTime - _searchStartTime.Value).TotalSeconds, totalSeconds));
            double x = laneLeft + (offset / totalSeconds) * laneWidth;

            var markerLabel = new Border
            {
                Background = new SolidColorBrush(AggregatePlayheadColor),
                Padding = new Thickness(4, 1, 4, 1),
                CornerRadius = new CornerRadius(1),
                Child = new TextBlock
                {
                    Text = currentTime.ToString("HH:mm:ss"),
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold
                }
            };
            Canvas.SetLeft(markerLabel, Math.Max(AggregateLabelWidth, Math.Min(x - 18, aggregateTimelineCanvas.ActualWidth - 58)));
            Canvas.SetTop(markerLabel, 18);

            aggregateTimelineCanvas.Children.Add(markerLabel);

            var markerLine = new Rectangle
            {
                Fill = new SolidColorBrush(AggregatePlayheadColor),
                Width = 2,
                Height = Math.Max(10, height - AggregateLegendHeight - AggregateAxisHeight + 4)
            };
            Canvas.SetLeft(markerLine, x);
            Canvas.SetTop(markerLine, AggregateAxisHeight - 2);
            aggregateTimelineCanvas.Children.Add(markerLine);
        }

        private void DrawAggregateLegend(double height)
        {
            double y = Math.Max(AggregateAxisHeight + 12, height - AggregateLegendHeight + 1);
            AddLegendItem(66, y, AggregateVideoColor, "Video Data");
            AddLegendItem(144, y, AggregateLossColor, "No Data");
        }

        private void AddLegendItem(double x, double y, Color color, string text)
        {
            var swatch = new Rectangle
            {
                Fill = new SolidColorBrush(color),
                Width = 10,
                Height = 8
            };
            Canvas.SetLeft(swatch, x);
            Canvas.SetTop(swatch, y + 3);
            aggregateTimelineCanvas.Children.Add(swatch);
            AddTimelineText(text, x + 16, y, 10, AggregateMutedTextColor, FontWeights.Normal);
        }

        private void AddTimelineText(string text, double x, double y, double fontSize, Color color, FontWeight weight)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = weight,
                Foreground = new SolidColorBrush(color)
            };
            Canvas.SetLeft(textBlock, x);
            Canvas.SetTop(textBlock, y);
            aggregateTimelineCanvas.Children.Add(textBlock);
        }

        private void AggregateTimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {

            RenderAggregateTimeline();
        }

        private void AggregateTimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingAggregateTimeline = true;
            aggregateTimelineCanvas.CaptureMouse();
            HandleAggregateTimelineSeek(e.GetPosition(aggregateTimelineCanvas), false);
        }

        private void AggregateTimelineCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingAggregateTimeline)
            {
                HandleAggregateTimelineSeek(e.GetPosition(aggregateTimelineCanvas), false);
            }
        }

        private void AggregateTimelineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingAggregateTimeline)
            {
                return;
            }

            _isDraggingAggregateTimeline = false;
            aggregateTimelineCanvas.ReleaseMouseCapture();
            HandleAggregateTimelineSeek(e.GetPosition(aggregateTimelineCanvas), true);
        }

        private void HandleAggregateTimelineSeek(Point point, bool forceSeek)
        {
            if (!_searchStartTime.HasValue || !_searchEndTime.HasValue || _searchEndTime <= _searchStartTime)
            {
                return;
            }

            double width = aggregateTimelineCanvas.ActualWidth;
            double laneLeft = AggregateLabelWidth;
            double laneWidth = Math.Max(100, width - laneLeft - 10);
            if (point.X < laneLeft || laneWidth <= 0)
            {
                return;
            }

            double totalSeconds = (_searchEndTime.Value - _searchStartTime.Value).TotalSeconds;
            double ratio = Math.Max(0, Math.Min(1, (point.X - laneLeft) / laneWidth));
            System.DateTime targetTime = _searchStartTime.Value.AddSeconds(totalSeconds * ratio);
            _aggregateCurrentTime = targetTime;
            RenderAggregateTimeline();


            if (forceSeek || (System.DateTime.Now - _lastSeekInteractionTime).TotalMilliseconds > AggregateTimelineSeekThrottleMs)
            {
                _lastSeekInteractionTime = System.DateTime.Now;
                SeekAllPlaybackCamerasTo(targetTime, forceSeek);
            }
        }

        private IEnumerable<ViewCameraPlayback> GetPlaybackCameras()
        {
            return gridCameraList.Children.OfType<ViewCameraPlayback>();
        }

        private void SeekAllPlaybackCamerasTo(System.DateTime targetTime, bool forceSeek)
        {
            foreach (var cam in GetPlaybackCameras())
            {
                cam.SeekToRealTime(targetTime, forceSeek);
            }
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
            LoggerManager.LogDebug($"Khá»Ÿi táº¡o lÆ°á»›i playback cho {n} camera.");
            DestroyViewCameras();
            ResetAggregateTimeline();

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

            foreach (var cam in GetPlaybackCameras())
            {
                if (cam != null && cam.Player != null)
                    cam.Player.Playing();
            }
        }
        private void PauseAllCam()
        {
            foreach (var cam in GetPlaybackCameras())
            {
                if (cam != null && cam.Player != null)
                    cam.Player.Pause();
            }
        }

        private void SetRatePlayers(float rate)
        {
            foreach (var cam in GetPlaybackCameras())
            {
                if (cam != null && cam.Player != null)
                    cam.Player.SetRate(rate);
            }
        }


        private void SeekBackwardPlayers()
        {
            foreach (var cam in GetPlaybackCameras())
            {
                if (cam != null && cam.Player != null)
                    cam.Player.SeekBackward();
            }
        }
        private void SeekForwardPlayers()
        {
            foreach (var cam in GetPlaybackCameras())
            {
                if (cam != null && cam.Player != null)
                    cam.Player.SeekForward();
            }
        }

        private void DestroyViewCameras()
        {
            var cameras = gridCameraList.Children.OfType<ViewCameraPlayback>().ToList();

            foreach (var cam in cameras)
            {
                try
                {
                    cam.Dispose();
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, $"Lá»—i khi dispose camera {cam?.Camera?.camID}");
                }
            }
        }
    }
}
