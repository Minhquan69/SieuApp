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
using System.Windows.Controls.Primitives;
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
using MahApps.Metro.IconPacks;

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
        private bool _isPlaybackSidebarCollapsed;
        private readonly DispatcherTimer _playbackToastTimer;
        private readonly DispatcherTimer _globalPlaybackToolbarHideTimer;
        private readonly DispatcherTimer _playbackHoverProbeTimer;
        private readonly DispatcherTimer _playbackToolbarTimeTimer;
        // A transparent WPF Popup is its own top-level HWND, allowing it to
        // render over GStreamer's WinForms HWND without the opaque rectangular
        // background imposed by WindowsFormsHost.
        private Popup _nativePlaybackToolbarPopup;
        private FrameworkElement _nativePlaybackToolbarVisual;
        // Retained only for the legacy creation block below. New toolbars use
        // the Popup fields above.
        private System.Windows.Forms.Integration.WindowsFormsHost _nativePlaybackToolbarHost;
        private System.Windows.Forms.Integration.ElementHost _nativePlaybackToolbarElementHost;
        private System.Windows.Forms.Panel _nativePlaybackToolbarPanel;
        private PackIconMaterial _nativePlaybackPlayIcon;
        private PackIconMaterial _nativePlaybackFullscreenIcon;
        private Button _nativePlaybackRateButton;
        private TextBlock _nativePlaybackTimeText;
        private System.Windows.Forms.Integration.WindowsFormsHost _nativePlaybackDownloadHost;
        private System.Windows.Forms.Integration.ElementHost _nativePlaybackDownloadElementHost;
        private SmartDownloadManager.DownloadTask _activePlaybackDownload;
        private readonly List<SmartDownloadManager.DownloadTask> _activePlaybackDownloads = new List<SmartDownloadManager.DownloadTask>();
        private readonly Dictionary<SmartDownloadManager.DownloadTask, ViewCameraPlayback> _playbackDownloadTiles = new Dictionary<SmartDownloadManager.DownloadTask, ViewCameraPlayback>();
        private TextBlock _playbackDownloadTitleText;
        private TextBlock _playbackDownloadFileText;
        private TextBlock _playbackDownloadProgressText;
        private TextBlock _playbackDownloadSizeText;
        private ProgressBar _playbackDownloadProgressBar;
        private Button _playbackDownloadCancelButton;
        private readonly DispatcherTimer _playbackDownloadProgressTimer;
        private enum CameraPickAction { None, Snapshot, Download }
        private enum PlaybackDownloadMode { MergeIntoSingleVideo, SaveApiSegments }
        private CameraPickAction _cameraPickAction;
        private ViewCameraPlayback _snapshotHoverTile;
        private Button _snapshotSelectedButton;
        private Button _downloadSelectedButton;
        private PackIconMaterial _snapshotSelectedIcon;
        private PackIconMaterial _downloadSelectedIcon;
        private Border _aggregatePlayheadLabel;
        private Rectangle _aggregatePlayheadLine;
        private Window _playbackOwnerWindow;
        private bool _isPlaybackFullscreen;
        private WindowState _playbackRestoreWindowState;
        private WindowStyle _playbackRestoreWindowStyle;
        private ResizeMode _playbackRestoreResizeMode;
        private bool _playbackRestoreTopmost;
        private int _playbackGeometryTransitionVersion;
        private bool _isPlaybackGeometryTransitioning;

        private enum PlaybackToastKind { Info, Warning, Error }


        public bool IsPlaying { get; set; } = true;
        private System.DateTime _lastRealtimePlaylistSynchronizationUtc = System.DateTime.MinValue;
        private const double PlaylistSyncDriftThresholdSeconds = 1.25;
        private const int PlaylistSyncIntervalMilliseconds = 2000;

        private void TogglePlaybackSidebar_Click(object sender, RoutedEventArgs e)
        {
            _isPlaybackSidebarCollapsed = !_isPlaybackSidebarCollapsed;
            PlaybackSidebar.Visibility = _isPlaybackSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            PlaybackSidebarColumn.Width = _isPlaybackSidebarCollapsed ? new GridLength(0) : new GridLength(280);
            PlaybackSidebarOpenButton.Visibility = _isPlaybackSidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowPlaybackToast(string title, string message, PlaybackToastKind kind = PlaybackToastKind.Info)
        {
            var brushKey = kind == PlaybackToastKind.Error
                ? "VmsErrorBrush_v3"
                : kind == PlaybackToastKind.Warning ? "VmsWarningBrush_v3" : "VmsInfoBrush_v3";
            var accent = FindResource(brushKey) as Brush;
            if (accent != null)
            {
                PlaybackToastAccent.Background = accent;
                PlaybackToastIcon.Foreground = accent;
            }

            PlaybackToastTitle.Text = title;
            PlaybackToastMessage.Text = message;
            PlaybackToast.Visibility = Visibility.Visible;
            _playbackToastTimer.Stop();
            _playbackToastTimer.Start();
        }

        private void ClosePlaybackToast_Click(object sender, RoutedEventArgs e)
        {
            _playbackToastTimer.Stop();
            PlaybackToast.Visibility = Visibility.Collapsed;
        }

        private void PlaybackToastTimer_Tick(object sender, EventArgs e)
        {
            _playbackToastTimer.Stop();
            PlaybackToast.Visibility = Visibility.Collapsed;
        }

        private const float stepRate = 0.2f;

        private float _currentRate;
        private Dictionary<string, string> _camWithHlsUrls = new Dictionary<string, string>();
        // The exact playlist text that passed validation. Reusing it to initialise
        // each tile prevents a second request from racing the playback server and
        // producing a false "no data" state for a valid camera.
        private readonly Dictionary<string, string> _camWithPlaylistContent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        private System.DateTime? _searchStartTime;
        private System.DateTime? _searchEndTime;
        private System.DateTime? _renderedPlaybackStart;
        private System.DateTime? _renderedPlaybackEnd;
        private bool _isSearching = false;
        private bool _refreshQueued;
        private System.DateTime _lastSeekInteractionTime = System.DateTime.MinValue;
        private readonly List<AggregateTimelineRow> _aggregateTimelineRows = new List<AggregateTimelineRow>();
        private bool _isDraggingAggregateTimeline = false;
        private System.DateTime? _aggregateCurrentTime = null;
        private System.DateTime? _aggregateHoverTime;
        private System.DateTime? _aggregatePendingSeekTime;
        private System.DateTime _aggregateSeekHoldUntil = System.DateTime.MinValue;
        private const int AggregateTimelineSeekThrottleMs = 150;
        private const double AggregateLabelWidth = 72;
        private const double AggregateAxisHeight = 28;
        private const double AggregateRowHeight = 20;
        private const double AggregateLegendHeight = 22;
        private const double AggregateMinHeight = 78;
        private const double AggregateMaxHeight = 132;
        private Border _aggregateHoverLabel;
        private Rectangle _aggregateHoverLine;
        private const double AggregateTimeLabelWidth = 74;

        private double CenterTimelineLabel(double x, double width)
        {
            double canvasWidth = aggregateTimelineCanvas == null ? 0 : aggregateTimelineCanvas.ActualWidth;
            double left = x - width / 2;
            if (canvasWidth > 0)
                left = Math.Max(AggregateLabelWidth, Math.Min(left, canvasWidth - width - 2));
            return Math.Max(0, left);
        }
        private Color TimelineColor(string resourceKey)
        {
            var value = TryFindResource(resourceKey);
            var brush = value as SolidColorBrush;
            if (brush != null) return brush.Color;
            if (value is Color) return (Color)value;
            return Colors.Transparent;
        }

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

            _playbackToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _playbackToastTimer.Tick += PlaybackToastTimer_Tick;
            _globalPlaybackToolbarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _globalPlaybackToolbarHideTimer.Tick += GlobalPlaybackToolbarHideTimer_Tick;
            _playbackHoverProbeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _playbackHoverProbeTimer.Tick += PlaybackHoverProbeTimer_Tick;
            _playbackToolbarTimeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _playbackToolbarTimeTimer.Tick += PlaybackToolbarTimeTimer_Tick;
            _playbackDownloadProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _playbackDownloadProgressTimer.Tick += PlaybackDownloadProgressTimer_Tick;

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


            // Keep the existing ViewSearch instance/event contract, but host
            // it in the web-style toolbar rather than below the camera tree.
            // This is presentation-only: camera selection and HLS search
            // continue to use exactly the same objects and handlers.
            frmSearchToolbar.Navigate(_viewSearch);
            _viewSearch.EventSeachClick += btnSearch_Click;
            // Playback now uses the same VMS camera-list presentation and
            // source structure as Live View. Existing selection/playback
            // handlers remain the single source of truth for playback.
            PlaybackCameraList.SetCameraGroups(CamGroupList);
            PlaybackCameraList.CameraSelectionRequested += PlaybackCameraList_CameraSelectionRequested;
            PlaybackCameraList.SetSelectedCameras(SelecedCameraList);
            logPage = new UI.Pages.RightPlayback();
            

            frmRightSide.Content = logPage;

            Loaded += Page_Loaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            CreateNativePlaybackToolbar();
            CreateNativePlaybackDownloadOverlay();
            _playbackHoverProbeTimer.Start();
            _playbackToolbarTimeTimer.Start();
            AttachPlaybackFullscreenShortcut();
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

        private void AttachPlaybackFullscreenShortcut()
        {
            var owner = Window.GetWindow(this);
            if (ReferenceEquals(owner, _playbackOwnerWindow)) return;
            if (_playbackOwnerWindow != null)
                _playbackOwnerWindow.PreviewKeyDown -= PlaybackOwnerWindow_PreviewKeyDown;
            _playbackOwnerWindow = owner;
            if (_playbackOwnerWindow != null)
                _playbackOwnerWindow.PreviewKeyDown += PlaybackOwnerWindow_PreviewKeyDown;
        }

        private void PlaybackOwnerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || !_isPlaybackFullscreen) return;
            TogglePlaybackFullscreen();
            e.Handled = true;
        }

        private void CreateNativePlaybackToolbar()
        {
            if (_nativePlaybackToolbarPopup != null || gridCameraList == null)
                return;

            _nativePlaybackToolbarVisual = BuildNativePlaybackToolbarVisual();
            _nativePlaybackToolbarVisual.MouseEnter += NativePlaybackToolbar_MouseEnter;
            _nativePlaybackToolbarVisual.MouseLeave += NativePlaybackToolbar_MouseLeave;
            _nativePlaybackToolbarPopup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = true,
                PlacementTarget = gridCameraList,
                Placement = PlacementMode.Custom,
                CustomPopupPlacementCallback = PlaybackToolbarPlacement,
                Child = _nativePlaybackToolbarVisual,
                IsOpen = false
            };
            return;

#pragma warning disable CS0162 // Legacy WinForms host kept as a reference during migration.
            if (_nativePlaybackToolbarHost != null || NativePlaybackToolbarLayer == null)
                return;

            _nativePlaybackToolbarPanel = new System.Windows.Forms.Panel
            {
                Width = 610,
                Height = 50,
                // Only the controls themselves are styled. The host must not
                // draw a second opaque strip behind the playback video.
                BackColor = System.Drawing.Color.Transparent
            };
            _nativePlaybackToolbarPanel.MouseEnter += NativePlaybackToolbar_MouseEnter;
            _nativePlaybackToolbarPanel.MouseLeave += NativePlaybackToolbar_MouseLeave;

            /* Native WinForms buttons are deliberately disabled: their glyph
             * rendering differs from the VMS web design. The WPF visual below is
             * hosted above the GStreamer HWND by this same native panel. */
            /*
            var commands = new[]
            {
                new { Text = "↶10", Command = "Slower", Tip = "Giảm tốc độ" },
                new { Text = "|◀", Command = "Back", Tip = "Lùi" },
                new { Text = "▶", Command = "Play", Tip = "Phát / tạm dừng" },
                new { Text = "▶|", Command = "Forward", Tip = "Tiến" },
                new { Text = "10↷", Command = "Faster", Tip = "Tăng tốc độ" },
                new { Text = "AI", Command = "AiAll", Tip = "Bật / tắt AI cho tất cả camera" },
                new { Text = "📷", Command = "SnapshotAll", Tip = "Chụp ảnh tất cả camera" },
                new { Text = "⇩", Command = "DownloadAll", Tip = "Tải video tất cả camera" }
            };

            var toolTip = new System.Windows.Forms.ToolTip();
            var x = 5;
            foreach (var command in commands)
            {
                var button = new System.Windows.Forms.Button
                {
                    Text = command.Text,
                    Tag = command.Command,
                    Width = command.Command == "Play" ? 46 : 42,
                    Height = 32,
                    Left = x,
                    Top = 5,
                    FlatStyle = System.Windows.Forms.FlatStyle.Flat,
                    BackColor = System.Drawing.Color.FromArgb(14, 43, 70),
                    ForeColor = System.Drawing.Color.FromArgb(222, 232, 244),
                    Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold),
                    TabStop = false
                };
                button.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(39, 76, 108);
                button.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(24, 70, 119);
                button.Click += NativePlaybackToolbarButton_Click;
                button.MouseEnter += NativePlaybackToolbar_MouseEnter;
                button.MouseLeave += NativePlaybackToolbar_MouseLeave;
                toolTip.SetToolTip(button, command.Tip);
                _nativePlaybackToolbarPanel.Controls.Add(button);
                x += button.Width + 3;
            }
            */

            _nativePlaybackToolbarElementHost = new System.Windows.Forms.Integration.ElementHost
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                BackColor = System.Drawing.Color.Transparent,
                Child = BuildNativePlaybackToolbarVisual()
            };
            _nativePlaybackToolbarPanel.Controls.Add(_nativePlaybackToolbarElementHost);

            _nativePlaybackToolbarHost = new System.Windows.Forms.Integration.WindowsFormsHost
            {
                Child = _nativePlaybackToolbarPanel,
                Width = 610,
                Height = 50,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 12),
                Visibility = Visibility.Collapsed
            };
            NativePlaybackToolbarLayer.Children.Add(_nativePlaybackToolbarHost);
#pragma warning restore CS0162
        }

        private CustomPopupPlacement[] PlaybackToolbarPlacement(Size popupSize, Size targetSize, Point offset)
        {
            return new[]
            {
                new CustomPopupPlacement(
                    new Point((targetSize.Width - popupSize.Width) / 2d, targetSize.Height - popupSize.Height - 12d),
                    PopupPrimaryAxis.None)
            };
        }

        private bool IsNativePlaybackToolbarOpen => _nativePlaybackToolbarPopup != null && _nativePlaybackToolbarPopup.IsOpen;

        private void SetNativePlaybackToolbarOpen(bool isOpen)
        {
            if (_nativePlaybackToolbarPopup == null)
                return;
            _nativePlaybackToolbarPopup.PlacementTarget = gridCameraList;
            _nativePlaybackToolbarPopup.IsOpen = isOpen;
        }

        private void CreateNativePlaybackDownloadOverlay()
        {
            if (_nativePlaybackDownloadHost != null || NativePlaybackToolbarLayer == null)
                return;

            var panel = new System.Windows.Forms.Panel
            {
                Width = 450,
                Height = 166,
                BackColor = System.Drawing.Color.FromArgb(7, 24, 39)
            };

            _nativePlaybackDownloadElementHost = new System.Windows.Forms.Integration.ElementHost
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                BackColor = System.Drawing.Color.FromArgb(7, 24, 39),
                Child = BuildNativePlaybackDownloadVisual()
            };
            panel.Controls.Add(_nativePlaybackDownloadElementHost);

            _nativePlaybackDownloadHost = new System.Windows.Forms.Integration.WindowsFormsHost
            {
                Child = panel,
                Width = 450,
                Height = 166,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            Panel.SetZIndex(_nativePlaybackDownloadHost, 90);
            NativePlaybackToolbarLayer.Children.Add(_nativePlaybackDownloadHost);
        }

        private FrameworkElement BuildNativePlaybackDownloadVisual()
        {
            var root = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(5, 20, 34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(24, 63, 95)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(18)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var spinner = new PackIconMaterial
            {
                Kind = PackIconMaterialKind.Loading,
                Width = 19,
                Height = 19,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 107, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0)
            };
            header.Children.Add(spinner);
            _playbackDownloadTitleText = new TextBlock
            {
                Text = "Đang chuẩn bị tải video…",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_playbackDownloadTitleText, 1);
            header.Children.Add(_playbackDownloadTitleText);
            _playbackDownloadCancelButton = new Button
            {
                Content = "Hủy",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 6, 12, 6),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            _playbackDownloadCancelButton.Click += PlaybackDownloadCancel_Click;
            Grid.SetColumn(_playbackDownloadCancelButton, 2);
            header.Children.Add(_playbackDownloadCancelButton);
            grid.Children.Add(header);

            _playbackDownloadFileText = new TextBlock
            {
                Margin = new Thickness(0, 14, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(190, 211, 231)),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = "export.mp4"
            };
            Grid.SetRow(_playbackDownloadFileText, 1);
            grid.Children.Add(_playbackDownloadFileText);

            var progressHeader = new Grid { Margin = new Thickness(0, 12, 0, 5) };
            progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sentText = new TextBlock { Text = "Tiến trình", Foreground = new SolidColorBrush(Color.FromRgb(159, 184, 208)), FontSize = 12 };
            progressHeader.Children.Add(sentText);
            _playbackDownloadProgressText = new TextBlock { Text = "0%", Foreground = Brushes.White, FontSize = 12 };
            Grid.SetColumn(_playbackDownloadProgressText, 1);
            progressHeader.Children.Add(_playbackDownloadProgressText);
            Grid.SetRow(progressHeader, 2);
            grid.Children.Add(progressHeader);

            _playbackDownloadProgressBar = new ProgressBar
            {
                Height = 7,
                Minimum = 0,
                Maximum = 100,
                Background = new SolidColorBrush(Color.FromRgb(11, 44, 69)),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 107, 255)),
                BorderThickness = new Thickness(0)
            };
            Grid.SetRow(_playbackDownloadProgressBar, 3);
            grid.Children.Add(_playbackDownloadProgressBar);

            _playbackDownloadSizeText = new TextBlock
            {
                Margin = new Thickness(0, 11, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(190, 211, 231)),
                FontSize = 12,
                Text = "Lưu vào Downloads"
            };
            Grid.SetRow(_playbackDownloadSizeText, 4);
            grid.Children.Add(_playbackDownloadSizeText);
            root.Child = grid;
            return root;
        }

        private void ShowPlaybackDownloadProgress(IList<SmartDownloadManager.DownloadTask> tasks)
        {
            if (tasks == null || tasks.Count == 0)
                return;

            _activePlaybackDownloads.Clear();
            _activePlaybackDownloads.AddRange(tasks.Where(task => task != null));
            _activePlaybackDownload = _activePlaybackDownloads.FirstOrDefault();
            _playbackDownloadTiles.Clear();
            foreach (var task in _activePlaybackDownloads)
            {
                var tile = GetPlaybackCameras().FirstOrDefault(item =>
                    string.Equals(item.Camera?.camID, task.CameraNames, StringComparison.OrdinalIgnoreCase));
                if (tile == null)
                    continue;
                _playbackDownloadTiles[task] = tile;
                tile.ShowDownloadProgress(task);
            }
            // Progress belongs to the camera tile, as on the web page.  Keep the
            // former page-wide native host hidden so it cannot cover another feed.
            if (_nativePlaybackDownloadHost != null)
                _nativePlaybackDownloadHost.Visibility = Visibility.Collapsed;
            UpdatePlaybackDownloadProgress();
            _playbackDownloadProgressTimer.Stop();
            _playbackDownloadProgressTimer.Start();
        }

        private void PlaybackDownloadCancel_Click(object sender, RoutedEventArgs e)
        {
            foreach (var task in _activePlaybackDownloads.Where(task => task != null && task.CanCancel).ToArray())
                SmartDownloadManager.Instance.Cancel(task);
            UpdatePlaybackDownloadProgress();
        }

        private void PlaybackDownloadProgressTimer_Tick(object sender, EventArgs e)
        {
            UpdatePlaybackDownloadProgress();
        }

        private void UpdatePlaybackDownloadProgress()
        {
            var tasks = _activePlaybackDownloads.Where(download => download != null).ToList();
            var task = tasks.FirstOrDefault();
            if (task == null)
                return;

            int completedCount = tasks.Count(item => string.Equals(item.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            int cancelledCount = tasks.Count(item => string.Equals(item.Status, "Cancelled", StringComparison.OrdinalIgnoreCase));
            int failedCount = tasks.Count(item => string.Equals(item.Status, "Failed", StringComparison.OrdinalIgnoreCase));
            bool completed = completedCount == tasks.Count;
            bool cancelled = cancelledCount > 0 && cancelledCount + completedCount == tasks.Count;
            bool failed = failedCount > 0;
            double progress = tasks.Average(item => item.Progress);
            if (_playbackDownloadTitleText != null)
                _playbackDownloadTitleText.Text = completed ? "Đã tải xong" : cancelled ? "Đã hủy tải" : failed ? "Có video tải thất bại" : "Đang gửi file…";
            if (_playbackDownloadFileText != null)
                _playbackDownloadFileText.Text = tasks.Count == 1
                    ? string.Format("{0} · {1:HH:mm:ss} – {2:HH:mm:ss}", task.CameraNames, task.StartTime, task.EndTime)
                    : string.Format("export_{0:yyyy-MM-ddTHH-mm-ss}_{1:yyyy-MM-ddTHH-mm-ss}.mp4", task.StartTime, task.EndTime);
            if (_playbackDownloadProgressText != null)
                _playbackDownloadProgressText.Text = string.Format("{0}/{1} tệp", completedCount, tasks.Count);
            if (_playbackDownloadProgressBar != null)
                _playbackDownloadProgressBar.Value = progress;
            if (_playbackDownloadSizeText != null)
                _playbackDownloadSizeText.Text = string.IsNullOrWhiteSpace(task.Speed) ? "Lưu vào Downloads" : task.Speed;
            if (_playbackDownloadCancelButton != null)
            {
                bool canCancel = tasks.Any(item => item.CanCancel);
                _playbackDownloadCancelButton.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
                _playbackDownloadCancelButton.IsEnabled = canCancel;
            }

            foreach (var pair in _playbackDownloadTiles.ToArray())
            {
                if (pair.Key == null || pair.Value == null)
                    continue;
                pair.Value.ShowDownloadProgress(pair.Key);
            }

            if (completed || cancelled || failed)
                _playbackDownloadProgressTimer.Stop();
        }

        private FrameworkElement BuildNativePlaybackToolbarVisual()
        {
            var border = new SolidColorBrush(Color.FromRgb(40, 68, 94));
            var buttonBackground = new SolidColorBrush(Color.FromRgb(13, 34, 54));
            var buttonHover = new SolidColorBrush(Color.FromRgb(25, 65, 105));
            var foreground = new SolidColorBrush(Color.FromRgb(224, 234, 246));

            var root = new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                SnapsToDevicePixels = true
            };
            root.MouseEnter += (s, e) => _globalPlaybackToolbarHideTimer.Stop();
            root.MouseLeave += (s, e) =>
            {
                _globalPlaybackToolbarHideTimer.Stop();
                _globalPlaybackToolbarHideTimer.Start();
            };

            var commands = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            commands.Children.Add(CreateNativeToolbarButton(PackIconMaterialKind.Pause, "Play", "Tạm dừng", buttonBackground, buttonHover, foreground, out _nativePlaybackPlayIcon));
            commands.Children.Add(CreateNativeToolbarButton(PackIconMaterialKind.SkipPrevious, "BackLong", "<< 1 phút", buttonBackground, buttonHover, foreground));
            commands.Children.Add(CreateNativeToolbarButton(PackIconMaterialKind.Rewind, "Back", "<< 10 s", buttonBackground, buttonHover, foreground));

            _nativePlaybackTimeText = new TextBlock
            {
                Text = "00:00 / 00:00",
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground
            };
            commands.Children.Add(new Border
            {
                Width = 116,
                Height = 30,
                Margin = new Thickness(7, 0, 7, 0),
                Background = buttonBackground,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = _nativePlaybackTimeText
            });
            commands.Children.Add(CreateNativeToolbarButton(PackIconMaterialKind.FastForward, "Forward", ">> 10 s", buttonBackground, buttonHover, foreground));
            commands.Children.Add(CreateNativeToolbarButton(PackIconMaterialKind.SkipNext, "ForwardLong", ">> 1 phút", buttonBackground, buttonHover, foreground));
            commands.Children.Add(CreateNativeToolbarSeparator(border));

            _nativePlaybackRateButton = CreateNativeToolbarTextButton("1x ▾", "RateMenu", "Chọn tốc độ phát", buttonBackground, buttonHover, foreground);
            _nativePlaybackRateButton.Width = 62;
            commands.Children.Add(_nativePlaybackRateButton);
            commands.Children.Add(CreateNativeToolbarSeparator(border));
            commands.Children.Add(CreateNativeToolbarTextButton("AI", "AiAll", "Bật / tắt AI", buttonBackground, buttonHover, foreground));
            commands.Children.Add(CreateNativeToolbarButton(PackIconMaterialKind.CameraOutline, "SnapshotAll", "Chụp ảnh tất cả", buttonBackground, buttonHover, foreground));
            _snapshotSelectedButton = CreateNativeToolbarButton(PackIconMaterialKind.Camera, "SnapshotSelected", "Chụp ảnh", buttonBackground, buttonHover, foreground, out _snapshotSelectedIcon);
            commands.Children.Add(_snapshotSelectedButton);
            _downloadSelectedButton = CreateNativeToolbarButton(PackIconMaterialKind.DownloadOutline, "DownloadSelected", "Tải video camera hiện tại", buttonBackground, buttonHover, foreground, out _downloadSelectedIcon);
            commands.Children.Add(_downloadSelectedButton);
            commands.Children.Add(CreateNativeToolbarButton(PackIconMaterialKind.Fullscreen, "Fullscreen", "Toàn màn hình", buttonBackground, buttonHover, foreground, out _nativePlaybackFullscreenIcon));

            root.Child = commands;
            return root;
        }

        private static Border CreateNativeToolbarSeparator(Brush color)
        {
            return new Border
            {
                Width = 1,
                Height = 22,
                Background = color,
                Margin = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private Button CreateNativeToolbarButton(PackIconMaterialKind kind, string command, string toolTip, Brush background, Brush hoverBackground, Brush foreground)
        {
            PackIconMaterial ignored;
            return CreateNativeToolbarButton(kind, command, toolTip, background, hoverBackground, foreground, out ignored);
        }

        private Button CreateNativeToolbarButton(PackIconMaterialKind kind, string command, string toolTip, Brush background, Brush hoverBackground, Brush foreground, out PackIconMaterial icon)
        {
            icon = new PackIconMaterial { Kind = kind, Width = 16, Height = 16, Foreground = foreground };
            var button = CreateNativeToolbarButtonCore(command, toolTip, background, hoverBackground, foreground);
            button.Content = icon;
            return button;
        }

        private Button CreateNativeToolbarTextButton(string text, string command, string toolTip, Brush background, Brush hoverBackground, Brush foreground)
        {
            var button = CreateNativeToolbarButtonCore(command, toolTip, background, hoverBackground, foreground);
            button.Width = 40;
            button.Content = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return button;
        }

        private Button CreateNativeToolbarButtonCore(string command, string toolTip, Brush background, Brush hoverBackground, Brush foreground)
        {
            var button = new Button
            {
                Width = 32,
                Height = 30,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Tag = command,
                ToolTip = toolTip,
                Background = background,
                Foreground = foreground,
                BorderBrush = new SolidColorBrush(Color.FromRgb(41, 73, 102)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, background));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, button.BorderBrush));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, button.BorderThickness));
            var template = new ControlTemplate(typeof(Button));
            var roundedBorder = new FrameworkElementFactory(typeof(Border));
            roundedBorder.SetBinding(Border.BackgroundProperty, new Binding("Background")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            roundedBorder.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            roundedBorder.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            roundedBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            roundedBorder.AppendChild(content);
            template.VisualTree = roundedBorder;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, hoverBackground));
            style.Triggers.Add(hover);
            button.Style = style;
            button.Click += NativePlaybackToolbarButton_Click;
            return button;
        }

        private void ShowNativePlaybackRateMenu()
        {
            if (_nativePlaybackRateButton == null)
                return;

            var popupBackground = new SolidColorBrush(Color.FromRgb(10, 28, 45));
            var popupBorder = new SolidColorBrush(Color.FromRgb(40, 68, 94));
            var textBrush = new SolidColorBrush(Color.FromRgb(224, 234, 246));
            var menu = new ContextMenu
            {
                Background = popupBackground,
                BorderBrush = popupBorder,
                BorderThickness = new Thickness(1),
                PlacementTarget = _nativePlaybackRateButton,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                StaysOpen = false
            };
            // Do not inherit the Windows light MenuItem template. The playback
            // toolbar sits above a native video HWND and must retain the VMS dark
            // contrast even when the desktop theme is light.
            var itemStyle = new Style(typeof(System.Windows.Controls.MenuItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, popupBackground));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, textBrush));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 7, 24, 7)));
            itemStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            var itemTemplate = new ControlTemplate(typeof(System.Windows.Controls.MenuItem));
            var itemBorder = new FrameworkElementFactory(typeof(Border));
            itemBorder.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var itemContent = new FrameworkElementFactory(typeof(ContentPresenter));
            itemContent.SetBinding(ContentPresenter.ContentProperty, new Binding("Header") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            itemContent.SetBinding(ContentPresenter.MarginProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            itemBorder.AppendChild(itemContent);
            itemTemplate.VisualTree = itemBorder;
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));
            var itemHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            itemHover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(25, 65, 105))));
            itemStyle.Triggers.Add(itemHover);
            menu.Resources.Add(typeof(System.Windows.Controls.MenuItem), itemStyle);
            foreach (var rate in new[] { 0.5f, 1.0f, 1.5f, 2.0f })
            {
                var value = rate;
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.') + "x",
                    Style = itemStyle
                };
                item.Click += (s, e) =>
                {
                    CurrentRate = value;
                    SetRatePlayers(CurrentRate);
                    _nativePlaybackRateButton.Content = (item.Header as string) + " ▾";
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void BringNativePlaybackToolbarToFront()
        {
            // Every video tile owns an HWND.  HWNDs created after the toolbar paint
            // above it, regardless of WPF Panel.ZIndex. Recreate the single toolbar
            // after the last tile so its native handle is the topmost sibling.
            var shouldRemainVisible = IsNativePlaybackToolbarOpen;
            var shouldKeepDownloadVisible = _nativePlaybackDownloadHost != null && _nativePlaybackDownloadHost.Visibility == Visibility.Visible;
            if (_nativePlaybackToolbarPopup != null)
            {
                _nativePlaybackToolbarPopup.IsOpen = false;
                _nativePlaybackToolbarPopup.Child = null;
                _nativePlaybackToolbarPopup = null;
                _nativePlaybackToolbarVisual = null;
                _nativePlaybackPlayIcon = null;
                _nativePlaybackRateButton = null;
            }

            if (_nativePlaybackDownloadHost != null)
            {
                NativePlaybackToolbarLayer.Children.Remove(_nativePlaybackDownloadHost);
                _nativePlaybackDownloadHost.Dispose();
                _nativePlaybackDownloadHost = null;
                _nativePlaybackDownloadElementHost = null;
            }

            CreateNativePlaybackToolbar();
            CreateNativePlaybackDownloadOverlay();
            if (shouldRemainVisible)
                SetNativePlaybackToolbarOpen(true);
            if (shouldKeepDownloadVisible && _nativePlaybackDownloadHost != null)
            {
                _nativePlaybackDownloadHost.Visibility = Visibility.Visible;
                UpdatePlaybackDownloadProgress();
            }
        }

        private void NativePlaybackToolbar_MouseEnter(object sender, System.EventArgs e)
        {
            _globalPlaybackToolbarHideTimer.Stop();
        }

        private void NativePlaybackToolbar_MouseLeave(object sender, System.EventArgs e)
        {
            _globalPlaybackToolbarHideTimer.Stop();
            _globalPlaybackToolbarHideTimer.Start();
        }

        private void NativePlaybackToolbarButton_Click(object sender, RoutedEventArgs e)
        {
            var action = (sender as FrameworkElement)?.Tag as string;
            switch (action)
            {
                case "Play":
                    if (IsPlaying) PauseAllCam(); else PlayAllCam();
                    IsPlaying = !IsPlaying;
                    if (_nativePlaybackPlayIcon != null)
                        _nativePlaybackPlayIcon.Kind = IsPlaying ? PackIconMaterialKind.Pause : PackIconMaterialKind.Play;
                    break;
                case "Back":
                    SeekBackwardPlayers();
                    break;
                case "BackLong":
                    SeekPlayersBySeconds(-60);
                    break;
                case "Forward":
                    SeekForwardPlayers();
                    break;
                case "ForwardLong":
                    SeekPlayersBySeconds(60);
                    break;
                case "RateMenu":
                    ShowNativePlaybackRateMenu();
                    break;
                case "SnapshotSelected":
                    ToggleCameraPickMode(CameraPickAction.Snapshot);
                    break;
                case "DownloadSelected":
                    ToggleCameraPickMode(CameraPickAction.Download);
                    break;
                case "Fullscreen":
                    TogglePlaybackFullscreen();
                    break;
                default:
                    _ = ExecuteGlobalPlaybackActionAsync(action);
                    break;
            }
        }

        // Keep the grid and aggregate timeline mounted; only the containing
        // application window changes, so no player is recreated on toggle.
        // This mirrors LivePage_v3's native-surface transaction: D3D video
        // surfaces are hidden while WPF settles its final bounds, then shown
        // together.  Without this, every intermediate WindowStyle/sidebar
        // layout causes a visible resize frame in each playback tile.
        private void TogglePlaybackFullscreen()
        {
            var owner = Window.GetWindow(this);
            if (owner == null || _isPlaybackGeometryTransitioning) return;

            var entering = !_isPlaybackFullscreen;
            RunPlaybackGeometryTransition(new Action(() =>
            {
                if (entering)
                {
                    _playbackRestoreWindowState = owner.WindowState;
                    _playbackRestoreWindowStyle = owner.WindowStyle;
                    _playbackRestoreResizeMode = owner.ResizeMode;
                    _playbackRestoreTopmost = owner.Topmost;
                }

                _isPlaybackFullscreen = entering;
                var shell = owner.Content as ShellPage_v3 ?? FindParentShell();
                if (shell != null) shell.SetChromeVisible(!entering);

                // Fullscreen Playback intentionally keeps only camera grid,
                // aggregate timeline and the hover toolbar.  Apply all WPF
                // chrome changes before the single window geometry update.
                PlaybackControlHeader.Visibility = entering ? Visibility.Collapsed : Visibility.Visible;
                PlaybackSidebar.Visibility = entering || _isPlaybackSidebarCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                PlaybackSidebarColumn.Width = entering || _isPlaybackSidebarCollapsed
                    ? new GridLength(0)
                    : new GridLength(280);
                PlaybackSidebarOpenButton.Visibility = entering
                    ? Visibility.Collapsed
                    : (_isPlaybackSidebarCollapsed ? Visibility.Visible : Visibility.Collapsed);

                if (entering)
                {
                    // Do not restore a maximized window to Normal here. That
                    // was the visible shrink-to-small-window frame before the
                    // fullscreen expansion. Borderless + maximized applies
                    // directly from the current geometry in one transition.
                    owner.WindowStyle = WindowStyle.None;
                    owner.ResizeMode = ResizeMode.NoResize;
                    owner.Topmost = true;
                    owner.WindowState = WindowState.Maximized;
                }
                else
                {
                    // Restore directly to the target state. Forcing Normal
                    // first makes Windows paint RestoreBounds (a small window)
                    // before it applies the final state, which was the exit
                    // fullscreen shrink/expand flash.
                    owner.WindowStyle = _playbackRestoreWindowStyle;
                    owner.ResizeMode = _playbackRestoreResizeMode;
                    owner.Topmost = _playbackRestoreTopmost;
                    owner.WindowState = _playbackRestoreWindowState;
                }

                if (_nativePlaybackFullscreenIcon != null)
                    _nativePlaybackFullscreenIcon.Kind = _isPlaybackFullscreen
                        ? PackIconMaterialKind.FullscreenExit
                        : PackIconMaterialKind.Fullscreen;
            }));
        }

        private void RunPlaybackGeometryTransition(Action applyLayout)
        {
            if (_isPlaybackGeometryTransitioning)
                return;

            _isPlaybackGeometryTransitioning = true;
            var transitionVersion = ++_playbackGeometryTransitionVersion;
            var toolbarWasVisible = IsNativePlaybackToolbarOpen;

            // The WinForms/D3D player is not clipped by WPF while its HWND is
            // receiving new bounds. Hide it for the transaction so no
            // intermediate grid size can ever be presented to the user.
            SetNativePlaybackToolbarOpen(false);
            gridCameraList.Visibility = Visibility.Hidden;
            foreach (var tile in GetPlaybackCameras())
                tile.SetVideoSurfaceVisible(false);

            applyLayout?.Invoke();

            RunAfterPlaybackNativeLayout(new Action(() =>
            {
                if (transitionVersion != _playbackGeometryTransitionVersion)
                {
                    _isPlaybackGeometryTransitioning = false;
                    return;
                }

                try
                {
                    gridCameraList.InvalidateMeasure();
                    gridCameraList.InvalidateArrange();
                    gridCameraList.UpdateLayout();
                    aggregateTimelineCanvas.InvalidateMeasure();
                    aggregateTimelineCanvas.InvalidateArrange();
                    aggregateTimelineCanvas.UpdateLayout();

                    gridCameraList.Visibility = Visibility.Visible;
                    foreach (var tile in GetPlaybackCameras())
                        tile.SetVideoSurfaceVisible(true);

                    if (toolbarWasVisible)
                        SetNativePlaybackToolbarOpen(true);

                    RenderAggregateTimeline();
                }
                finally
                {
                    _isPlaybackGeometryTransitioning = false;
                }
            }));
        }

        private void RunAfterPlaybackNativeLayout(Action action)
        {
            // Match LivePage_v3: two ContextIdle passes wait for both WPF's
            // measure/arrange and the hosted WinForms surface bounds update.
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, action)));
        }

        private ShellPage_v3 FindParentShell()
        {
            DependencyObject current = this;
            while (current != null)
            {
                var shell = current as ShellPage_v3;
                if (shell != null) return shell;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void PlaybackToolbarTimeTimer_Tick(object sender, EventArgs e)
        {
            if (_nativePlaybackTimeText == null)
                return;

            var tile = GetPlaybackCameras().FirstOrDefault(cam => cam.VideoDuration > 0)
                       ?? GetPlaybackCameras().FirstOrDefault();
            if (tile == null || tile.VideoDuration <= 0)
            {
                _nativePlaybackTimeText.Text = "00:00 / 00:00";
                return;
            }

            _nativePlaybackTimeText.Text = FormatPlaybackTime(tile.VideoPosition) + " / " + FormatPlaybackTime(tile.VideoDuration);

            if (!_isDraggingAggregateTimeline && _searchStartTime.HasValue)
            {
                var realTime = tile.GetCurrentPlaybackRealTime();
                if (realTime != System.DateTime.MinValue)
                {
                    // GStreamer reports the old position for a short period after
                    // a seek. Keep the user-selected playhead until the pipeline
                    // reaches the requested time instead of snapping it back.
                    if (_aggregatePendingSeekTime.HasValue)
                    {
                        if (Math.Abs((realTime - _aggregatePendingSeekTime.Value).TotalSeconds) <= 3 ||
                            System.DateTime.Now >= _aggregateSeekHoldUntil)
                        {
                            _aggregatePendingSeekTime = null;
                            _aggregateCurrentTime = realTime;
                        }
                    }
                    else
                    {
                        _aggregateCurrentTime = realTime;
                    }
                    UpdateAggregateTimelinePlayhead();
                }
            }

            SynchronizePlaybackCamerasToPlaylistClock();
        }

        // Every player reports a media-playlist position.  That position is not
        // comparable between cameras because their playlists can begin at
        // different real timestamps.  ViewCameraPlayback translates it through
        // the API segment timestamps before we compare/correct it here.
        private void SynchronizePlaybackCamerasToPlaylistClock()
        {
            if (!IsPlaying || (System.DateTime.UtcNow - _lastRealtimePlaylistSynchronizationUtc).TotalMilliseconds < PlaylistSyncIntervalMilliseconds)
                return;

            var cameras = GetPlaybackCameras()
                .Where(camera => camera != null && camera.Player != null && camera.VideoDuration > 0)
                .ToList();
            if (cameras.Count < 2)
                return;

            var master = cameras[0];
            var targetTime = master.GetCurrentPlaybackRealTime();
            if (targetTime == System.DateTime.MinValue)
                return;

            _lastRealtimePlaylistSynchronizationUtc = System.DateTime.UtcNow;
            foreach (var camera in cameras.Skip(1))
            {
                var currentTime = camera.GetCurrentPlaybackRealTime();
                if (currentTime == System.DateTime.MinValue ||
                    Math.Abs((currentTime - targetTime).TotalSeconds) > PlaylistSyncDriftThresholdSeconds)
                {
                    camera.SeekToRealTime(targetTime, true);
                }
            }
        }

        private static string FormatPlaybackTime(double seconds)
        {
            var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return value.TotalHours >= 1 ? value.ToString("hh\\:mm\\:ss") : value.ToString("mm\\:ss");
        }

        private void NativePlaybackToolbarButton_Click(object sender, System.EventArgs e)
        {
            var action = (sender as System.Windows.Forms.Control)?.Tag as string;
            switch (action)
            {
                case "Play":
                    if (IsPlaying) PauseAllCam(); else PlayAllCam();
                    IsPlaying = !IsPlaying;
                    (sender as System.Windows.Forms.Button).Text = IsPlaying ? "❚❚" : "▶";
                    break;
                case "Back": SeekBackwardPlayers(); break;
                case "Forward": SeekForwardPlayers(); break;
                case "Slower":
                    CurrentRate = System.Math.Max(0.1f, CurrentRate - stepRate);
                    SetRatePlayers(CurrentRate);
                    break;
                case "Faster":
                    CurrentRate = System.Math.Min(4.0f, CurrentRate + stepRate);
                    SetRatePlayers(CurrentRate);
                    break;
                default:
                    _ = ExecuteGlobalPlaybackActionAsync(action);
                    break;
            }
        }

        private void PlaybackHoverProbeTimer_Tick(object sender, EventArgs e)
        {
            if (_nativePlaybackToolbarPopup == null || gridCameraList == null || !gridCameraList.IsVisible)
                return;

            try
            {
                var cursor = System.Windows.Forms.Cursor.Position;
                var point = gridCameraList.PointFromScreen(new Point(cursor.X, cursor.Y));
                var insideGrid = point.X >= 0 && point.Y >= 0 && point.X <= gridCameraList.ActualWidth && point.Y <= gridCameraList.ActualHeight;
                var hasPlayers = GetPlaybackCameras().Any();
                SetNativePlaybackToolbarOpen(insideGrid && hasPlayers);
            }
            catch
            {
                // The page can be unloading while the native cursor probe runs.
            }
        }

        private void CamGroupList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            AllowSelectingCamera();
        }

        private void AreaTree_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            AllowSelectingCamera();
        }

        private async void btnSearch_Click(object sender, List<System.DateTime?> e)
        {
            // A user can select cameras faster than the playlist validation calls.
            // Queue one refresh instead of discarding the later selection change.
            if (_isSearching)
            {
                _refreshQueued = true;
                return;
            }
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
                    ShowPlaybackToast("Khoảng thời gian lớn", "Nên tìm kiếm trong khoảng thời gian không quá 7 ngày.", PlaybackToastKind.Warning);
                }

                var selectedForSearch = SelecedCameraList?.Where(cam => cam != null).ToList() ?? new List<models.Camera>();
                List<string> cameraIds = selectedForSearch.Select(cam => cam.camID).ToList();
                if (cameraIds.Count == 0)
                {
                    LoggerManager.LogWarn("NgÆ°á»i dÃ¹ng chÆ°a chá»n thiáº¿t bá»‹ Ä‘á»ƒ tÃ¬m kiáº¿m .");
                    ShowPlaybackToast("Chưa chọn camera", "Chọn ít nhất một camera trong danh sách trước khi tìm kiếm.");
                    return;
                }

                _viewSearch.txtResultSearch.Inlines.Clear();
                _viewSearch.txtResultSearch.Inlines.Add(new Run("ChuÃ¢Ì‰n biÌ£ dÆ°Ìƒ liÃªÌ£u ...") { Foreground = new SolidColorBrush(Colors.LightBlue) });

                // Do not discard the aggregate rows when the user only adds a
                // camera to the currently-rendered interval.  Existing native
                // players keep their segments; clearing the rows here left the
                // bottom timeline with only the newly-added camera.
                bool reuseCurrentPlayback = _renderedPlaybackStart == fromdate &&
                                            _renderedPlaybackEnd == todate &&
                                            gridCameraList.Children.OfType<ViewCameraPlayback>().Any();

                _camWithHlsUrls.Clear();
                _camWithPlaylistContent.Clear();
                btnDownload.Visibility = Visibility.Collapsed;
                _searchStartTime = fromdate;
                _searchEndTime = todate;
                // Move the aggregate timeline to the newly requested interval
                // immediately.  It must never keep showing the preceding search
                // range while playlist validation is still in progress.
                if (reuseCurrentPlayback)
                    SynchronizeAggregateTimelineWithSelection();
                else
                    ResetAggregateTimeline();
                 
                //Playback voi HLS server
                string hlsServer = ApiManager.Instance.GetEndpointUrl("_playback");
                string playbackToken = ApiManager.Instance.GetEndpointToken("_playback") ?? "";
                
                // Generate and validate all requested playlists concurrently. The
                // server may need a moment to materialise a playlist; retrying here
                // avoids treating that transient state as missing video data.
                var playlistTasks = selectedForSearch.Select(async cam => new
                {
                    Camera = cam,
                    Url = BuildPlaylistUrl(hlsServer, cam.camID, fromdate, todate, playbackToken),
                    Playlist = await GetPlaybackPlaylistWithRetryAsync(
                        BuildPlaylistUrl(hlsServer, cam.camID, fromdate, todate, playbackToken), playbackToken)
                }).ToArray();
                var playlistResults = await System.Threading.Tasks.Task.WhenAll(playlistTasks);
                foreach (var result in playlistResults)
                {
                    if (string.IsNullOrWhiteSpace(result.Playlist))
                        continue;

                    _camWithHlsUrls[result.Camera.camID] = result.Url;
                    _camWithPlaylistContent[result.Camera.camID] = result.Playlist;
                    LoggerManager.LogDebug($"Playback playlist synchronized for {result.Camera.camID}");
                }

                _viewSearch.txtResultSearch.Inlines.Clear();
                foreach (var cam in selectedForSearch)
                {
                    bool found = _camWithHlsUrls.ContainsKey(cam.camID);
                    var run = new Run(found ? $"{cam.name} --> Ready \n" : $"{cam.name} -->Not found\n");
                    run.Foreground = new SolidColorBrush(found ? Colors.WhiteSmoke : Colors.OrangeRed);
                    _viewSearch.txtResultSearch.Inlines.Add(run);
                }

                if (SelecedCameraList.Count > 0)

                {
                    LoggerManager.LogInfo($"Báº¯t Ä‘áº§u phÃ¡t láº¡i  cho {_camWithHlsUrls.Count} camera.");
                    PlaybackHLS();
                }
                else
                {
                    LoggerManager.LogInfo("KhÃ´ng cÃ³ URL  nÃ o Ä‘Æ°á»£c táº¡o.");
                    ShowPlaybackToast("Không có dữ liệu phát lại", "Không tạo được đường dẫn video cho camera đã chọn.", PlaybackToastKind.Warning);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lá»—i khi tÃ¬m kiáº¿m video playback");
                ShowPlaybackToast("Không thể tìm kiếm video", "Có lỗi khi kết nối máy chủ. Vui lòng thử lại sau.", PlaybackToastKind.Error);
            }
            finally
            {
                _isSearching = false;
                if (_refreshQueued)
                {
                    _refreshQueued = false;
                    Dispatcher.BeginInvoke(new Action(() => btnSearch_Click(this,
                        new List<System.DateTime?> { _viewSearch.datetimeFrom.Value, _viewSearch.datetimeTo.Value })), DispatcherPriority.Background);
                }
            }
        }

        private async System.Threading.Tasks.Task<bool> HasPlaybackSegmentsAsync(string hlsUrl, string playbackToken)
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(12);
                    if (!string.IsNullOrWhiteSpace(playbackToken))
                        client.DefaultRequestHeaders.Add("X-Playback-Token", playbackToken);

                    var response = await client.GetAsync(hlsUrl);
                    if (!response.IsSuccessStatusCode)
                        return false;

                    var playlist = await response.Content.ReadAsStringAsync();
                    return !string.IsNullOrWhiteSpace(playlist)
                        && playlist.IndexOf("#EXTINF", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Could not validate playback playlist");
                return false;
            }
        }

        private void btnDownload_Click(object sender, EventArgs e)
        {
            ShowPlaybackDownloadModeMenu(GetDownloadableCameras(), sender as UIElement);
        }

        private void ShowPlaybackDownloadModeMenu(IList<models.Camera> cameras, UIElement placementTarget = null)
        {
            var targets = cameras?
                .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID))
                .GroupBy(camera => camera.camID, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList() ?? new List<models.Camera>();
            if (targets.Count == 0)
            {
                ShowPlaybackToast("Tải video", "Chưa có camera xem lại để tải video.", PlaybackToastKind.Warning);
                return;
            }

            var menu = new ContextMenu
            {
                PlacementTarget = placementTarget ?? aggregateTimelineCanvas,
                Placement = PlacementMode.Top,
                Style = TryFindResource("PlaybackDownloadContextMenuStyle") as Style
            };
            var mergeItem = new System.Windows.Controls.MenuItem
            {
                Header = CreateDownloadMenuHeader("Gộp thành một video"),
                ToolTip = "Tải các đoạn rồi ghép thành một MP4",
                Style = TryFindResource("PlaybackDownloadMenuItemStyle") as Style
            };
            mergeItem.Click += (s, e) => QueuePlaybackDownloads(targets, PlaybackDownloadMode.MergeIntoSingleVideo);
            var segmentsItem = new System.Windows.Controls.MenuItem
            {
                Header = CreateDownloadMenuHeader("Lưu từng đoạn video", 0.82),
                ToolTip = "Lưu riêng từng đoạn dữ liệu do API trả về",
                Style = TryFindResource("PlaybackDownloadMenuItemStyle") as Style
            };
            segmentsItem.Click += (s, e) => QueuePlaybackDownloads(targets, PlaybackDownloadMode.SaveApiSegments);
            menu.Items.Add(mergeItem);
            menu.Items.Add(segmentsItem);
            menu.IsOpen = true;
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
                ShowPlaybackToast("Tải video", "Chưa có camera xem lại để tải video.", PlaybackToastKind.Warning);
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
            downloadAllItem.Click += (s, e) => ShowPlaybackDownloadModeMenu(cameras, aggregateTimelineCanvas);
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
                item.Click += (s, e) => ShowPlaybackDownloadModeMenu(new List<models.Camera> { camera }, aggregateTimelineCanvas);
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

        private void QueuePlaybackDownloads(IList<models.Camera> cameras, PlaybackDownloadMode downloadMode = PlaybackDownloadMode.MergeIntoSingleVideo)
        {
            try
            {
                System.DateTime exportStart;
                System.DateTime exportEnd;
                if (!TryGetActivePlaybackRange(out exportStart, out exportEnd))
                {
                    ShowPlaybackToast("Tải video", "Chọn khoảng thời gian hợp lệ trước khi tải.", PlaybackToastKind.Warning);
                    return;
                }

                var validCameras = cameras?
                    .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID))
                    .GroupBy(camera => camera.camID, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                if (validCameras == null || validCameras.Count == 0)
                {
                    ShowPlaybackToast("Tải video", "Chọn ít nhất một camera có dữ liệu xem lại.", PlaybackToastKind.Warning);
                    return;
                }

                string playbackServer = ApiManager.Instance.GetEndpointUrl("_playback");
                if (string.IsNullOrWhiteSpace(playbackServer))
                {
                    ShowPlaybackToast("Tải video", "Máy chủ xem lại hiện không khả dụng.", PlaybackToastKind.Error);
                    return;
                }

                string token = ApiManager.Instance.GetEndpointToken("_playback") ?? string.Empty;
                string savePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(savePath);
                var queuedDownloads = new List<SmartDownloadManager.DownloadTask>();
                foreach (var camera in validCameras)
                {
                    string fileName = BuildExportFileName(camera.camID, exportStart, exportEnd);
                    string destinationPath = System.IO.Path.Combine(savePath, fileName);
                    var exportChunks = SplitPlaybackExportRange(exportStart, exportEnd)
                        .Select(range => new SmartDownloadManager.DirectDownloadChunk
                        {
                            StartTime = range.Item1,
                            EndTime = range.Item2,
                            Url = BuildExportUrl(playbackServer, camera.camID, range.Item1, range.Item2,
                                BuildExportFileName(camera.camID, range.Item1, range.Item2), token)
                        })
                        .ToList();
                    if (exportChunks.Count == 1)
                    {
                        // A normal short export is streamed directly by the
                        // playback service without an intermediate playlist.
                        queuedDownloads.Add(SmartDownloadManager.Instance.QueueDirectDownload(
                            exportChunks[0].Url, destinationPath, camera.camID, exportStart, exportEnd, token, "X-Playback-Token"));
                        continue;
                    }

                    queuedDownloads.Add(SmartDownloadManager.Instance.QueueChunkedDirectDownload(
                        exportChunks, destinationPath, camera.camID, exportStart, exportEnd, token, "X-Playback-Token",
                        downloadMode == PlaybackDownloadMode.MergeIntoSingleVideo));
                }

                if (queuedDownloads.Count > 0)
                    ShowPlaybackDownloadProgress(queuedDownloads);
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Playback download queue failed");
                ShowPlaybackToast("Tải video", "Không thể tạo tác vụ tải. Vui lòng thử lại.", PlaybackToastKind.Error);
            }
        }

        private static IEnumerable<Tuple<System.DateTime, System.DateTime>> SplitPlaybackExportRange(System.DateTime start, System.DateTime end)
        {
            // Keep a margin below the one-hour server limit. A six-hour export
            // is eight independently retryable, bounded HTTP requests.
            var cursor = start;
            var maximumChunk = System.TimeSpan.FromMinutes(45);
            while (cursor < end)
            {
                var next = cursor.Add(maximumChunk);
                if (next > end) next = end;
                yield return Tuple.Create(cursor, next);
                cursor = next;
            }
        }

        private async System.Threading.Tasks.Task<string> GetPlaybackPlaylistWithRetryAsync(string hlsUrl, string playbackToken)
        {
            const int attempts = 4;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    using (var client = new System.Net.Http.HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);
                        if (!string.IsNullOrWhiteSpace(playbackToken))
                            client.DefaultRequestHeaders.Add("X-Playback-Token", playbackToken);

                        var response = await client.GetAsync(hlsUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            var playlist = await response.Content.ReadAsStringAsync();
                            if (!string.IsNullOrWhiteSpace(playlist) &&
                                playlist.IndexOf("#EXTINF", StringComparison.OrdinalIgnoreCase) >= 0)
                                return playlist;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggerManager.LogDebug("Chờ playlist playback lần " + (attempt + 1) + ": " + ex.Message);
                }

                if (attempt < attempts - 1)
                    await System.Threading.Tasks.Task.Delay(400 * (attempt + 1));
            }

            return null;
        }

        private bool TryGetActivePlaybackRange(out System.DateTime start, out System.DateTime end)
        {
            start = _searchStartTime ?? System.DateTime.MinValue;
            end = _searchEndTime ?? System.DateTime.MinValue;
            return start != System.DateTime.MinValue && end > start;
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
            if (_isPlaybackFullscreen)
                TogglePlaybackFullscreen();
            if (_playbackOwnerWindow != null)
            {
                _playbackOwnerWindow.PreviewKeyDown -= PlaybackOwnerWindow_PreviewKeyDown;
                _playbackOwnerWindow = null;
            }
            _playbackToastTimer.Stop();
            _globalPlaybackToolbarHideTimer.Stop();
            _playbackHoverProbeTimer.Stop();
            _playbackToolbarTimeTimer.Stop();
            _playbackDownloadProgressTimer.Stop();
            if (_nativePlaybackToolbarPopup != null)
            {
                _nativePlaybackToolbarPopup.IsOpen = false;
                _nativePlaybackToolbarPopup.Child = null;
                _nativePlaybackToolbarPopup = null;
                _nativePlaybackToolbarVisual = null;
                _nativePlaybackPlayIcon = null;
                _nativePlaybackFullscreenIcon = null;
                _nativePlaybackRateButton = null;
            }
            if (_nativePlaybackDownloadHost != null)
            {
                NativePlaybackToolbarLayer?.Children.Remove(_nativePlaybackDownloadHost);
                _nativePlaybackDownloadHost.Dispose();
                _nativePlaybackDownloadHost = null;
                _nativePlaybackDownloadElementHost = null;
                _activePlaybackDownload = null;
            }
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

        private void ConfigGrid(int rows, int columns, bool clearExistingPlayers = true)
        {
            if (clearExistingPlayers)
            {
                DestroyViewCameras();
                gridCameraList.Children.Clear();
            }
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
            var camListPlayed = SelecedCameraList.ToList();
            int camidx = 0;
            
            // Lấy token trong GlobalSystem)
            string token = ApiManager.Instance.GetEndpointToken("_playback") ?? "";
            
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < columns; j++)
                {
                    if (camidx >= camListPlayed.Count) break;

                    var camModel = camListPlayed[camidx];

                    // ShowCamera is asynchronous because each playlist is
                    // inspected.  A user may remove a camera while a prior
                    // invocation is still awaiting HTTP.  Never add that
                    // stale camera back into the grid.
                    if (!IsCameraStillSelected(camModel.camID))
                    {
                        camidx++;
                        continue;
                    }

                    // A refresh caused by selecting another camera must not recreate
                    // streams which are already playing. Keep their pipeline and only
                    // move the existing tile into its new grid slot.
                    var existingTile = gridCameraList.Children
                        .OfType<ViewCameraPlayback>()
                        .FirstOrDefault(tile => string.Equals(tile.Camera?.camID, camModel.camID, StringComparison.OrdinalIgnoreCase));
                    if (existingTile != null)
                    {
                        Grid.SetRow(existingTile, i);
                        Grid.SetColumn(existingTile, j);
                        camidx++;
                        continue;
                    }

                    ViewCameraPlayback cam = new ViewCameraPlayback(camModel);
                    // The page renders one aggregate timeline below the
                    // playback grid. Hiding each tile's mini timeline avoids
                    // duplicate, differently-scaled time axes.
                    cam.TimelineArea.Visibility = Visibility.Collapsed;
                    string hlsUrl;
                    if (!_camWithHlsUrls.TryGetValue(camModel.camID, out hlsUrl))
                    {
                        cam.ShowNoPlaybackData();
                        Grid.SetRow(cam, i); Grid.SetColumn(cam, j);
                        gridCameraList.Children.Add(cam);
                        // A selected camera without recorded video is still a
                        // first-class timeline lane.  It renders as the No
                        // Data track rather than disappearing from the list.
                        RegisterAggregateTimelineRow(camModel, new List<ViewCameraPlayback.PlaybackSegment>());
                        camidx++;
                        continue;
                    }
                    cam.HlsUrl = hlsUrl;
                    cam.ShowPreparingPlayback("Đang chuẩn bị dữ liệu phát lại...");

                    Grid.SetRow(cam, i); Grid.SetColumn(cam, j);
                    gridCameraList.Children.Add(cam);
                    camidx++;

                    cam.SendGPS += GpsReceiver;
                    cam.SendMetaAIResult += ShowAIResult;
                    cam.SnapshotCurrentRequested += PlaybackTile_SnapshotCurrentRequested;
                    cam.SnapshotAllRequested += PlaybackTile_SnapshotAllRequested;
                    cam.DownloadCurrentRequested += PlaybackTile_DownloadCurrentRequested;
                    cam.AiOverlayRequested += PlaybackTile_AiOverlayRequested;
                    cam.PlaybackHoverEntered += PlaybackTile_PlaybackHoverEntered;
                    cam.PlaybackHoverLeft += PlaybackTile_PlaybackHoverLeft;
                    cam.PlaybackTileClicked += PlaybackTile_PlaybackTileClicked;
                    cam.EventClosed += PlaybackTile_EventClosed;

                    var playlistSynchronized = false;
                    // Fetch m3u8 content
                    try
                    {
                        using (var client = new System.Net.Http.HttpClient())
                        {
                            if (!string.IsNullOrEmpty(token))
                            {
                                client.DefaultRequestHeaders.Add("X-Playback-Token", token);
                            }
                            cam.ShowPreparingPlayback("Đang đồng bộ dữ liệu phát lại...");
                            string m3u8Content;
                            if (!_camWithPlaylistContent.TryGetValue(camModel.camID, out m3u8Content))
                                m3u8Content = await GetPlaybackPlaylistWithRetryAsync(hlsUrl, token);
                            if (string.IsNullOrWhiteSpace(m3u8Content))
                                throw new InvalidOperationException("Playlist playback chưa sẵn sàng.");
                            if (_searchStartTime.HasValue && _searchEndTime.HasValue)
                            {
                                cam.ParseM3U8AndRenderTimeline(m3u8Content, _searchStartTime.Value, _searchEndTime.Value);
                                if (IsPlaybackTileStillSelected(cam))
                                    RegisterAggregateTimelineRow(camModel, cam.GetTimelineSegments());
                            }
                            else
                            {
                                cam.ParseM3U8AndRenderTimeline(m3u8Content,System. DateTime.Now,System. DateTime.Now);
                                if (IsPlaybackTileStillSelected(cam))
                                    RegisterAggregateTimelineRow(camModel, cam.GetTimelineSegments());
                            }

                            playlistSynchronized = cam.GetTimelineSegments().Any(segment => segment.HasVideo);
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerManager.LogException(ex, $"Lá»—i táº£i m3u8 cho camera {camModel.name}");
                    }

                    // Tá»± Ä‘á»™ng káº¿t ná»‘i luá»“ng khi load lÃªn grid
                    if (IsPlaybackTileStillSelected(cam) && playlistSynchronized)
                        cam.ConnectedCamera();
                    else if (IsPlaybackTileStillSelected(cam))
                    {
                        cam.ShowNoPlaybackData();
                        RegisterAggregateTimelineRow(camModel, new List<ViewCameraPlayback.PlaybackSegment>());
                    }
                }
            }

            SynchronizeAggregateTimelineWithSelection();
            ReflowPlaybackGrid();
            RenderAggregateTimeline();

            if (_timerGPS != null)
                _timerGPS.Stop();

            _timerGPS = new System.Timers.Timer(1000);
            _timerGPS.Elapsed += (s, smg) =>
            {
                ForwardGPSBuffer?.Invoke("Playback", _gpsBuffer);
            };
            _timerGPS.Start();
            Dispatcher.BeginInvoke(new Action(BringNativePlaybackToolbarToFront), DispatcherPriority.ContextIdle);
        }

        private void ResetAggregateTimeline()
        {
            _aggregateTimelineRows.Clear();
            _aggregateCurrentTime = _searchStartTime;
            _aggregatePlayheadLabel = null;
            _aggregatePlayheadLine = null;

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

            OrderAggregateTimelineRowsBySelection();
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
                    Fill = new SolidColorBrush(TimelineColor("VmsBackgroundColor_v3")),
                    Width = width,
                    Height = height
                };
                aggregateTimelineCanvas.Children.Add(background);


                var panel = new Rectangle
                {
                    Fill = new SolidColorBrush(TimelineColor("VmsSurfaceColor_v3")),
                    Width = Math.Max(0, width - 2),
                    Height = Math.Max(0, height - 2),
                    Opacity = 0.98
                };
                Canvas.SetLeft(panel, 0);
                Canvas.SetTop(panel, 1);
                aggregateTimelineCanvas.Children.Add(panel);

                string totalText = string.Format("Cam: {0} / {1}", _camWithHlsUrls.Count, SelecedCameraList.Count);
                AddTimelineText(totalText, 12, 4, 12, TimelineColor("VmsTextPrimaryColor_v3"), FontWeights.SemiBold);

                if (!_searchStartTime.HasValue || !_searchEndTime.HasValue || _searchEndTime <= _searchStartTime)
                {
                    AddTimelineText("Chưa có dữ liệu video", AggregateLabelWidth + 8, 24, 11, TimelineColor("VmsTextTertiaryColor_v3"), FontWeights.Normal);
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
                double fontSize = rowHeight < 10 ? 9 : 11;

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

                    AddTimelineText(label, 16, y + Math.Max(0, (rowHeight - fontSize - 2) / 2), fontSize, TimelineColor("VmsTextSecondaryColor_v3"), FontWeights.Normal);

                    var lossTrack = new Rectangle

                    {
                        Fill = new SolidColorBrush(TimelineColor("VmsBorderStrongColor_v3")),
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
                            Fill = new SolidColorBrush(TimelineColor("VmsSuccessColor_v3")),
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
                DrawAggregateHoverMarker(laneLeft, laneWidth, totalSeconds, height);
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
                    Fill = new SolidColorBrush(TimelineColor("VmsBorderColor_v3")),
                    Width = 1,
                    Height = Math.Max(10, height - AggregateAxisHeight - AggregateLegendHeight)
                };
                Canvas.SetLeft(tickLine, x);
                Canvas.SetTop(tickLine, AggregateAxisHeight);
                aggregateTimelineCanvas.Children.Add(tickLine);

                AddTimelineText(tickTime.ToString("HH:mm"), Math.Max(laneLeft, x - 20), 3, 11, TimelineColor("VmsTextPrimaryColor_v3"), FontWeights.Normal);
            }
        }

        private void DrawAggregatePlayhead(double laneLeft, double laneWidth, double totalSeconds, double height)
        {
            System.DateTime currentTime = _aggregateCurrentTime ?? _searchStartTime.Value;
            double offset = Math.Max(0, Math.Min((currentTime - _searchStartTime.Value).TotalSeconds, totalSeconds));
            double x = laneLeft + (offset / totalSeconds) * laneWidth;

            _aggregatePlayheadLabel = new Border
            {
                Background = new SolidColorBrush(TimelineColor("VmsPrimaryBrush_v3")),
                Width = AggregateTimeLabelWidth,
                Height = 22,
                Padding = new Thickness(5, 2, 5, 2),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock
                {
                    Text = currentTime.ToString("HH:mm:ss"),
                    Width = 64,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center
                }
            };
            Canvas.SetLeft(_aggregatePlayheadLabel, CenterTimelineLabel(x, AggregateTimeLabelWidth));
            Canvas.SetTop(_aggregatePlayheadLabel, 1);

            aggregateTimelineCanvas.Children.Add(_aggregatePlayheadLabel);

            _aggregatePlayheadLine = new Rectangle
            {
                Fill = new SolidColorBrush(TimelineColor("VmsPrimaryBrush_v3")),
                Width = 2,
                Height = Math.Max(10, height - AggregateLegendHeight - AggregateAxisHeight + 4)
            };
            Canvas.SetLeft(_aggregatePlayheadLine, x);
            Canvas.SetTop(_aggregatePlayheadLine, AggregateAxisHeight - 1);
            aggregateTimelineCanvas.Children.Add(_aggregatePlayheadLine);
        }

        private void DrawAggregateHoverMarker(double laneLeft, double laneWidth, double totalSeconds, double height)
        {
            if (!_aggregateHoverTime.HasValue || !_searchStartTime.HasValue)
                return;

            double offset = Math.Max(0, Math.Min((_aggregateHoverTime.Value - _searchStartTime.Value).TotalSeconds, totalSeconds));
            double x = laneLeft + (offset / totalSeconds) * laneWidth;
            Color hoverColor = TimelineColor("VmsInfoBrush_v3");

            _aggregateHoverLine = new Rectangle
            {
                Fill = new SolidColorBrush(hoverColor),
                Width = 1.5,
                Height = Math.Max(10, height - AggregateLegendHeight - AggregateAxisHeight + 4),
                Opacity = 0.9,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_aggregateHoverLine, x);
            Canvas.SetTop(_aggregateHoverLine, AggregateAxisHeight - 1);
            Panel.SetZIndex(_aggregateHoverLine, 130);
            aggregateTimelineCanvas.Children.Add(_aggregateHoverLine);

            _aggregateHoverLabel = new Border
            {
                Background = new SolidColorBrush(hoverColor),
                Width = AggregateTimeLabelWidth,
                Padding = new Thickness(5, 2, 5, 2),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock
                {
                    Text = _aggregateHoverTime.Value.ToString("HH:mm:ss"),
                    Width = 64,
                    Foreground = new SolidColorBrush(TimelineColor("VmsTextInverseColor_v3")),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center
                },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_aggregateHoverLabel, CenterTimelineLabel(x, AggregateTimeLabelWidth));
            Canvas.SetTop(_aggregateHoverLabel, 1);
            Panel.SetZIndex(_aggregateHoverLabel, 140);
            aggregateTimelineCanvas.Children.Add(_aggregateHoverLabel);
        }

        private void DrawAggregateLegend(double height)
        {
            double y = Math.Max(AggregateAxisHeight + 12, height - AggregateLegendHeight + 1);
            AddLegendItem(66, y, TimelineColor("VmsSuccessColor_v3"), "Video Data");
            AddLegendItem(144, y, TimelineColor("VmsBorderStrongColor_v3"), "No Data");
        }

        private void UpdateAggregateTimelinePlayhead()
        {
            if (_aggregatePlayheadLabel == null || _aggregatePlayheadLine == null ||
                !_searchStartTime.HasValue || !_searchEndTime.HasValue ||
                _searchEndTime <= _searchStartTime || aggregateTimelineCanvas == null)
                return;

            double width = aggregateTimelineCanvas.ActualWidth;
            if (width <= 0 || double.IsNaN(width))
                return;

            double totalSeconds = (_searchEndTime.Value - _searchStartTime.Value).TotalSeconds;
            double laneLeft = AggregateLabelWidth;
            double laneWidth = Math.Max(100, width - laneLeft - 10);
            System.DateTime currentTime = _aggregateCurrentTime ?? _searchStartTime.Value;
            double offset = Math.Max(0, Math.Min((currentTime - _searchStartTime.Value).TotalSeconds, totalSeconds));
            double x = laneLeft + (offset / totalSeconds) * laneWidth;

            Canvas.SetLeft(_aggregatePlayheadLine, x);
            Canvas.SetLeft(_aggregatePlayheadLabel, CenterTimelineLabel(x, AggregateTimeLabelWidth));
            var text = _aggregatePlayheadLabel.Child as TextBlock;
            if (text != null)
                text.Text = currentTime.ToString("HH:mm:ss");
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
            AddTimelineText(text, x + 16, y, 10, TimelineColor("VmsTextTertiaryColor_v3"), FontWeights.Normal);
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
            UpdateAggregateTimelineHover(e.GetPosition(aggregateTimelineCanvas));
            if (_isDraggingAggregateTimeline)
            {
                HandleAggregateTimelineSeek(e.GetPosition(aggregateTimelineCanvas), false);
            }
        }

        private void AggregateTimelineCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _aggregateHoverTime = null;
            _aggregateHoverLabel = null;
            _aggregateHoverLine = null;
            RenderAggregateTimeline();
        }

        private void UpdateAggregateTimelineHover(Point point)
        {
            if (!_searchStartTime.HasValue || !_searchEndTime.HasValue || _searchEndTime <= _searchStartTime)
                return;

            double width = aggregateTimelineCanvas.ActualWidth;
            double laneLeft = AggregateLabelWidth;
            double laneWidth = Math.Max(100, width - laneLeft - 10);
            if (point.X < laneLeft || point.X > laneLeft + laneWidth)
            {
                if (_aggregateHoverTime.HasValue)
                {
                    _aggregateHoverTime = null;
                    RenderAggregateTimeline();
                }
                return;
            }

            double ratio = Math.Max(0, Math.Min(1, (point.X - laneLeft) / laneWidth));
            var hoverTime = _searchStartTime.Value.AddSeconds((_searchEndTime.Value - _searchStartTime.Value).TotalSeconds * ratio);
            if (!_aggregateHoverTime.HasValue || Math.Abs((hoverTime - _aggregateHoverTime.Value).TotalMilliseconds) >= 250)
            {
                _aggregateHoverTime = hoverTime;
                RenderAggregateTimeline();
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
            _aggregatePendingSeekTime = targetTime;
            // A multi-camera seek can take several seconds before GStreamer reports
            // the new position. Keep the user-selected playhead stable until the
            // stream catches up instead of flashing back to the old position.
            _aggregateSeekHoldUntil = System.DateTime.Now.AddSeconds(8);
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

        private void PlaybackTile_AiOverlayRequested(object sender, ViewCameraPlayback tile)
        {
            ShowPlaybackToast("AI", "Đã cập nhật hiển thị AI cho " + (tile?.Camera?.name ?? "camera"));
        }

        private void PlaybackTile_DownloadCurrentRequested(object sender, ViewCameraPlayback tile)
        {
            if (tile?.Camera == null)
                return;

            ShowPlaybackDownloadModeMenu(new List<models.Camera> { tile.Camera }, NativePlaybackToolbarLayer);
        }

        private async void PlaybackTile_SnapshotCurrentRequested(object sender, ViewCameraPlayback tile)
        {
            await SavePlaybackSnapshotAsync(tile);
        }

        private async void PlaybackTile_SnapshotAllRequested(object sender, ViewCameraPlayback tile)
        {
            var cameras = GetPlaybackCameras().ToList();
            var count = await SavePlaybackSnapshotsAsync(cameras);

            ShowPlaybackToast("Chụp ảnh", count > 0
                ? "Đã lưu " + count + " ảnh camera."
                : "Chưa thể chụp ảnh từ các camera đang phát.",
                count > 0 ? PlaybackToastKind.Info : PlaybackToastKind.Warning);
        }

        private async System.Threading.Tasks.Task<bool> SavePlaybackSnapshotAsync(ViewCameraPlayback tile, bool notify = true)
        {
            if (tile == null)
            {
                if (notify)
                    ShowPlaybackToast("Chụp ảnh", "Không thể chụp ảnh camera hiện tại.", PlaybackToastKind.Warning);
                return false;
            }

            // Prefer a one-shot source decode. Unlike CopyFromScreen this is the
            // camera's native frame (Full HD when the source is 1080p) and cannot
            // contain the capture-selection dim overlay, badge or grid chrome.
            var sourcePath = await tile.TrySaveSourceSnapshotAsync();
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                if (notify)
                    ShowPlaybackToast("Chụp ảnh", "Đã lưu ảnh " + (tile.Camera?.name ?? "camera") + ".");
                return true;
            }

            // CopyFromScreen sees every native HWND painted over the video surface.
            // Hide the badge and global toolbar for one compositor frame so the
            // exported PNG is the camera frame, rather than a screenshot of its tile.
            var toolbarWasVisible = IsNativePlaybackToolbarOpen;
            var saved = false;
            string path = null;
            try
            {
                SetNativePlaybackToolbarOpen(false);
                tile.SetSnapshotChromeVisible(false);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await System.Threading.Tasks.Task.Delay(34);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                // The appsink branch is throttled to one source frame per second.
                // Allow a newly-opened camera a short non-blocking window to publish
                // that frame; never replace it with a screenshot of the WPF grid.
                for (var attempt = 0; attempt < 6 && !saved; attempt++)
                {
                    saved = tile.TrySaveSnapshot(out path);
                    if (!saved && attempt < 5)
                        await System.Threading.Tasks.Task.Delay(180);
                }
            }
            finally
            {
                tile.SetSnapshotChromeVisible(true);
                if (toolbarWasVisible)
                    SetNativePlaybackToolbarOpen(true);
            }

            if (!saved)
            {
                if (notify)
                    ShowPlaybackToast("Chụp ảnh", "Không thể chụp ảnh camera hiện tại.", PlaybackToastKind.Warning);
                return false;
            }

            if (notify)
                ShowPlaybackToast("Chụp ảnh", "Đã lưu ảnh " + (tile.Camera?.name ?? "camera") + ".");
            return true;
        }

        /// <summary>
        /// Source-frame snapshots do not touch the WPF/WinForms video surfaces, so
        /// they can be safely exported in parallel.  This makes "chụp tất cả" finish
        /// near the time of a single camera snapshot while preserving native source
        /// dimensions.  Only failed source exports use the UI capture fallback.
        /// </summary>
        private async System.Threading.Tasks.Task<int> SavePlaybackSnapshotsAsync(IList<ViewCameraPlayback> tiles)
        {
            if (tiles == null || tiles.Count == 0)
                return 0;

            var sourceTasks = tiles.Select(async tile => new
            {
                Tile = tile,
                Path = tile == null ? null : await tile.TrySaveSourceSnapshotAsync()
            }).ToArray();
            var results = await System.Threading.Tasks.Task.WhenAll(sourceTasks);
            var saved = results.Count(result => !string.IsNullOrWhiteSpace(result.Path));

            // Do not run several compositor/screen captures at once. This is only a
            // fallback for an unavailable source decoder; normal captures are done
            // above concurrently at the camera's original resolution.
            foreach (var failed in results.Where(result => string.IsNullOrWhiteSpace(result.Path)))
            {
                if (await SavePlaybackSnapshotAsync(failed.Tile, false))
                    saved++;
            }

            return saved;
        }

        private async void PlaybackTile_PlaybackTileClicked(object sender, ViewCameraPlayback tile)
        {
            if (_cameraPickAction == CameraPickAction.None || tile == null)
                return;

            var action = _cameraPickAction;
            ExitSnapshotPickMode();
            if (action == CameraPickAction.Snapshot)
                await SavePlaybackSnapshotAsync(tile);
            else if (action == CameraPickAction.Download && tile.Camera != null)
                QueuePlaybackDownloads(new List<models.Camera> { tile.Camera });
        }

        private void UpdateSnapshotPickVisual()
        {
            foreach (var tile in GetPlaybackCameras())
                tile.SetSnapshotPickVisual(_cameraPickAction != CameraPickAction.None, tile == _snapshotHoverTile);
        }

        private void ExitSnapshotPickMode()
        {
            _cameraPickAction = CameraPickAction.None;
            _snapshotHoverTile = null;
            UpdateSnapshotPickVisual();
            UpdateCameraPickButtons();
        }

        private void ToggleCameraPickMode(CameraPickAction action)
        {
            if (_cameraPickAction == action)
            {
                ExitSnapshotPickMode();
                return;
            }

            _cameraPickAction = action;
            _snapshotHoverTile = null;
            UpdateSnapshotPickVisual();
            UpdateCameraPickButtons();
        }

        private void UpdateCameraPickButtons()
        {
            var selected = new SolidColorBrush(Color.FromRgb(38, 98, 235));
            var normal = new SolidColorBrush(Color.FromRgb(13, 34, 54));
            if (_snapshotSelectedButton != null)
            {
                _snapshotSelectedButton.Background = _cameraPickAction == CameraPickAction.Snapshot ? selected : normal;
                _snapshotSelectedButton.Width = _cameraPickAction == CameraPickAction.Snapshot ? 44 : 32;
                _snapshotSelectedButton.Content = _cameraPickAction == CameraPickAction.Snapshot
                    ? (object)new TextBlock { Text = "Hủy", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold }
                    : _snapshotSelectedIcon;
                _snapshotSelectedButton.ToolTip = _cameraPickAction == CameraPickAction.Snapshot ? "Hủy chọn camera" : "Chụp ảnh một camera";
            }
            if (_downloadSelectedButton != null)
            {
                _downloadSelectedButton.Background = _cameraPickAction == CameraPickAction.Download ? selected : normal;
                _downloadSelectedButton.Width = _cameraPickAction == CameraPickAction.Download ? 44 : 32;
                _downloadSelectedButton.Content = _cameraPickAction == CameraPickAction.Download
                    ? (object)new TextBlock { Text = "Hủy", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold }
                    : _downloadSelectedIcon;
                _downloadSelectedButton.ToolTip = _cameraPickAction == CameraPickAction.Download ? "Hủy chọn camera" : "Tải video một camera";
            }
            if (_snapshotSelectedIcon != null)
                _snapshotSelectedIcon.Kind = _cameraPickAction == CameraPickAction.Snapshot ? PackIconMaterialKind.Close : PackIconMaterialKind.Camera;
            if (_downloadSelectedIcon != null)
                _downloadSelectedIcon.Kind = _cameraPickAction == CameraPickAction.Download ? PackIconMaterialKind.Close : PackIconMaterialKind.DownloadOutline;
        }

        private void PlaybackTile_EventClosed(object sender, object e)
        {
            var tile = sender as ViewCameraPlayback;
            if (tile == null)
                return;

            gridCameraList.Children.Remove(tile);
            tile.Dispose();
            ResetAggregateTimeline();
        }

        private void PlaybackTile_PlaybackHoverEntered(object sender, EventArgs e)
        {
            _globalPlaybackToolbarHideTimer.Stop();
            SetNativePlaybackToolbarOpen(true);

            if (_cameraPickAction != CameraPickAction.None)
            {
                _snapshotHoverTile = sender as ViewCameraPlayback;
                UpdateSnapshotPickVisual();
            }
        }

        private void PlaybackTile_PlaybackHoverLeft(object sender, EventArgs e)
        {
            if (_cameraPickAction != CameraPickAction.None && _snapshotHoverTile == sender)
            {
                _snapshotHoverTile = null;
                UpdateSnapshotPickVisual();
            }

            if (_nativePlaybackToolbarVisual != null && _nativePlaybackToolbarVisual.IsMouseOver)
                return;

            _globalPlaybackToolbarHideTimer.Stop();
            _globalPlaybackToolbarHideTimer.Start();
        }

        private void GlobalPlaybackToolbarHideTimer_Tick(object sender, EventArgs e)
        {
            _globalPlaybackToolbarHideTimer.Stop();
            SetNativePlaybackToolbarOpen(false);
        }

        private async void GlobalPlaybackAction_Click(object sender, RoutedEventArgs e)
        {
            var action = (sender as FrameworkElement)?.Tag as string;
            await ExecuteGlobalPlaybackActionAsync(action);
        }

        private async System.Threading.Tasks.Task ExecuteGlobalPlaybackActionAsync(string action)
        {
            var tiles = GetPlaybackCameras().ToList();
            if (tiles.Count == 0)
                return;

            switch (action)
            {
                case "AiAll":
                    foreach (var tile in tiles)
                        tile.ToggleAiOverlay();
                    ShowPlaybackToast("AI", "Đã cập nhật hiển thị AI cho tất cả camera.");
                    break;
                case "SnapshotAll":
                    var count = await SavePlaybackSnapshotsAsync(tiles);
                    ShowPlaybackToast("Chụp ảnh", count > 0 ? "Đã lưu " + count + " ảnh camera." : "Chưa thể chụp ảnh từ các camera đang phát.", count > 0 ? PlaybackToastKind.Info : PlaybackToastKind.Warning);
                    break;
                case "DownloadAll":
                    ShowPlaybackDownloadModeMenu(tiles.Where(tile => tile.Camera != null).Select(tile => tile.Camera).ToList(), NativePlaybackToolbarLayer);
                    break;
            }
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

            int n = SelecedCameraList.Count;
            LoggerManager.LogDebug($"Khá»Ÿi táº¡o lÆ°á»›i playback cho {n} camera.");
            bool sameInterval = _renderedPlaybackStart == _searchStartTime &&
                                _renderedPlaybackEnd == _searchEndTime;
            bool preserveExistingPlayers = sameInterval &&
                                           gridCameraList.Children.OfType<ViewCameraPlayback>().Any();

            if (!preserveExistingPlayers)
                ResetAggregateTimeline();

            GetPlaybackGridDimensions(n, out rows, out columns);

            ConfigGrid(rows, columns, clearExistingPlayers: !preserveExistingPlayers);
            ShowCamera(rows, columns);
            _renderedPlaybackStart = _searchStartTime;
            _renderedPlaybackEnd = _searchEndTime;
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
            if (PlaybackCameraList != null) PlaybackCameraList.SetSelectedCameras(SelecedCameraList);
        }

        private void PlaybackCameraList_CameraSelectionRequested(object sender, models.Camera camera)
        {
            if (camera == null || string.IsNullOrWhiteSpace(camera.camID))
                return;

            // The sidebar can supply a different Camera instance for the same
            // logical camera, so selection must be keyed by camID, not reference.
            var wasSelected = SelecedCameraList.Any(item =>
                string.Equals(item?.camID, camera.camID, StringComparison.OrdinalIgnoreCase));
            Add_Remove_SelectedCameraList(sender, camera);
            if (!wasSelected)
            {
                Dispatcher.BeginInvoke(new Action(() => btnSearch_Click(this,
                    new List<System.DateTime?> { _viewSearch.datetimeFrom.Value, _viewSearch.datetimeTo.Value })), DispatcherPriority.Background);
            }
        }

        private static void GetPlaybackGridDimensions(int count, out int rows, out int columns)
        {
            if (count <= 1)
            {
                rows = 1;
                columns = 1;
            }
            else if (count == 2)
            {
                rows = 1;
                columns = 2;
            }
            else
            {
                rows = (int)Math.Ceiling(Math.Sqrt(count));
                columns = (int)Math.Ceiling((double)count / rows);
            }
        }

        // Repositions already-running native players only. It does not destroy or
        // reconnect them, so removal feels immediate and the remaining video stays
        // continuous while the grid contracts.
        private void ReflowPlaybackGrid()
        {
            var cameraOrder = SelecedCameraList
                .Where(cam => cam != null && !string.IsNullOrWhiteSpace(cam.camID))
                .Select(cam => cam.camID)
                .ToList();

            var tiles = gridCameraList.Children
                .OfType<ViewCameraPlayback>()
                .Where(tile => tile.Camera != null && cameraOrder.Any(id =>
                    string.Equals(id, tile.Camera.camID, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(tile => cameraOrder.FindIndex(id =>
                    string.Equals(id, tile.Camera.camID, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            GetPlaybackGridDimensions(Math.Max(1, tiles.Count), out var rows, out var columns);
            gridCameraList.RowDefinitions.Clear();
            gridCameraList.ColumnDefinitions.Clear();
            for (var row = 0; row < rows; row++)
                gridCameraList.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            for (var column = 0; column < columns; column++)
                gridCameraList.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (var index = 0; index < tiles.Count; index++)
            {
                Grid.SetRow(tiles[index], index / columns);
                Grid.SetColumn(tiles[index], index % columns);
            }
        }

        private bool IsCameraStillSelected(string cameraId)
        {
            return !string.IsNullOrWhiteSpace(cameraId) && SelecedCameraList.Any(cam =>
                string.Equals(cam?.camID, cameraId, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsPlaybackTileStillSelected(ViewCameraPlayback tile)
        {
            return tile != null &&
                   gridCameraList.Children.Contains(tile) &&
                   IsCameraStillSelected(tile.Camera?.camID);
        }

        // Keeps timeline state exactly aligned with the visible selection. It
        // deliberately retains rows for existing tiles, so adding one camera
        // cannot collapse the aggregate timeline to a single lane.
        private void SynchronizeAggregateTimelineWithSelection()
        {
            var selectedIds = new HashSet<string>(SelecedCameraList
                .Where(cam => cam != null && !string.IsNullOrWhiteSpace(cam.camID))
                .Select(cam => cam.camID), StringComparer.OrdinalIgnoreCase);
            _aggregateTimelineRows.RemoveAll(row => !selectedIds.Contains(row.CameraId));
            OrderAggregateTimelineRowsBySelection();
        }

        // Playlist HTTP calls finish in arbitrary order.  The aggregate
        // timeline must instead follow the camera order selected by the user
        // (which is also the grid placement order).
        private void OrderAggregateTimelineRowsBySelection()
        {
            var order = SelecedCameraList
                .Where(cam => cam != null && !string.IsNullOrWhiteSpace(cam.camID))
                .Select((cam, index) => new { cam.camID, index })
                .ToDictionary(item => item.camID, item => item.index, StringComparer.OrdinalIgnoreCase);

            _aggregateTimelineRows.Sort((left, right) =>
            {
                int leftIndex;
                int rightIndex;
                bool leftFound = order.TryGetValue(left.CameraId, out leftIndex);
                bool rightFound = order.TryGetValue(right.CameraId, out rightIndex);
                if (leftFound && rightFound) return leftIndex.CompareTo(rightIndex);
                return leftFound ? -1 : rightFound ? 1 : string.Compare(left.CameraId, right.CameraId, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void Add_Remove_SelectedCameraList(object sender, models.Camera cam)
        {
            if (cam == null || string.IsNullOrWhiteSpace(cam.camID))
                return;

            var selected = SelecedCameraList.FirstOrDefault(item =>
                string.Equals(item?.camID, cam.camID, StringComparison.OrdinalIgnoreCase));

            if (selected != null)
            {
                // Removing a camera from the sidebar must immediately remove its
                // own player. Do not rebuild the remaining grid: rebuilding would
                // resize/reconnect video that the user did not change.
                SelecedCameraList.Remove(selected);
                _camWithHlsUrls.Remove(selected.camID);
                RemovePlaybackTile(selected.camID);
                PublishActivePlaybackCameras();
            }
            else
            {
                SelecedCameraList.Add(cam);
            }
            txtTotalCam.Text = string.Format("Cam: {0} / {1}", _camWithHlsUrls.Count, SelecedCameraList.Count);
        }

        private void RemovePlaybackTile(string cameraId)
        {
            if (string.IsNullOrWhiteSpace(cameraId))
                return;

            var tiles = gridCameraList.Children
                .OfType<ViewCameraPlayback>()
                .Where(tile => string.Equals(tile.Camera?.camID, cameraId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var tile in tiles)
            {
                gridCameraList.Children.Remove(tile);
                tile.Dispose();
            }

            _aggregateTimelineRows.RemoveAll(row =>
                string.Equals(row.CameraId, cameraId, StringComparison.OrdinalIgnoreCase));
            // Removing one selected camera must also remove its grid slot. The
            // remaining native players are kept alive and only moved into the
            // compact grid (two cameras -> one camera becomes 1x1).
            ReflowPlaybackGrid();
            RenderAggregateTimeline();
        }

        private void PublishActivePlaybackCameras()
        {
            var cameras = SelecedCameraList
                .Where(cam => cam != null && _camWithHlsUrls.ContainsKey(cam.camID))
                .ToList();
            ActiveCamerasChanged?.Invoke(this, cameras);
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
            // Resume must only change pipeline state. Calling SetRate here performs
            // a flushing seek, which made resume visibly jump back to the start.
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

        private void SeekPlayersBySeconds(long seconds)
        {
            foreach (var cam in GetPlaybackCameras())
            {
                if (cam != null && cam.Player != null)
                    cam.Player.SeekBySeconds(seconds);
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
