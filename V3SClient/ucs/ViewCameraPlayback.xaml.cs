using GLib;
using GMap.NET;
using Gst;
using Gst.Video;
using SharpDX.Direct3D11;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using V3SClient.libs;
using V3SClient.models;
using UserControl = System.Windows.Controls.UserControl;

namespace V3SClient.ucs
{
    public partial class ViewCameraPlayback : UserControl, INotifyPropertyChanged, IDisposable
    {
        bool _isExpanded = false;
        int _gridRow, _gridCol;
        private bool _initialized = true;

        private List<string> _videoFiles;
        private List<PlaybackSegment> _segments = new List<PlaybackSegment>();
        private double _totalDurationSeconds = 0;
        private System.DateTime _searchStartTime;
        private System.DateTime _searchEndTime;
        private double _totalRealDurationSeconds = 0;
        private List<MetaAIResult> _aiMarkers = new List<MetaAIResult>();
        private System.DateTime _lastRenderTime = System.DateTime.MinValue;
        private readonly object _renderLock = new object();
        private bool _isDraggingTimeline = false;
        private const int SeekThrottleMs = 150;
        private System.DateTime _lastSeekInteractionTime = System.DateTime.MinValue;
        private double _lastTimelineClickX = 0;

        public int OriginalRowSpan { get; set; } = 1;
        public int OriginalColSpan { get; set; } = 1;

        private float _currentPlaybackRate = 1.0f;
        private bool _isPlaying = true;
        private readonly DispatcherTimer _hideHoverActionsTimer;
        private bool _aiOverlayEnabled;
        private bool _disposed;
        // This is a native-hosted card, deliberately kept inside this tile.  A
        // normal WPF overlay would be painted underneath GStreamer's HWND.
        private ElementHost _downloadProgressElementHost;
        private SmartDownloadManager.DownloadTask _downloadTask;
        private TextBlock _downloadTitleText;
        private TextBlock _downloadDetailText;
        private TextBlock _downloadPercentText;
        private TextBlock _downloadSpeedText;
        private System.Windows.Controls.ProgressBar _downloadProgressBar;
        private System.Windows.Controls.Button _downloadCancelButton;
        private readonly DispatcherTimer _downloadProgressTimer;
        private readonly DispatcherTimer _downloadProgressHideTimer;

        private Brush _viewCamBackground;
        public Brush ViewCamBackground
        {
            get { return _viewCamBackground; }
            set
            {
                if (value != _viewCamBackground)
                {
                    _viewCamBackground = value;
                    OnPropertyChanged("ViewCamBackground");
                }
            }
        }

        private double _videoPosition;
        public double VideoPosition
        {
            get { return _videoPosition; }
            set
            {
                if (Math.Abs(value - _videoPosition) > 0.05)
                {
                    _videoPosition = value;
                    OnPropertyChanged("VideoPosition");
                    UpdateMiniPlayhead();
                }
            }
        }
        
        private long _videoDuration;
        public long VideoDuration
        {
            get { return _videoDuration; }
            set
            {
                if (value != _videoDuration)
                {
                    _videoDuration = value;
                    OnPropertyChanged("VideoDuration");
                }
            }
        }

        private Visibility _showConnectButton = Visibility.Visible;
        public Visibility ShowConnectButton
        {
            get { return _showConnectButton; }
            set
            {
                if (value != _showConnectButton)
                {
                    _showConnectButton = value;
                    OnPropertyChanged("ShowConnectButton");
                }
            }
        }

        public bool isSpeakerOn { get; set; } = true;
        private bool isZoomOut { get; set; } = true;
        public string HlsUrl { get; set; } = null;
        public models.Camera Camera { get; set; } = null;
        public string Camera_Name { get; set; } = null;

        private int _cameraWarning = 0;
        public int CameraWarning
        {
            get { return _cameraWarning; }
            set
            {
                if (value != _cameraWarning)
                {
                    _cameraWarning = value;
                    OnPropertyChanged($"{nameof(CameraWarning)}");                    
                }
            }
        }

        public event EventHandler<object> EventClosed;
        public event EventHandler<GMap.NET.PointLatLng> SendGPS;
        public event EventHandler<List<MetaAIResult>> SendMetaAIResult;
        public event EventHandler<ViewCameraPlayback> SnapshotCurrentRequested;
        public event EventHandler<ViewCameraPlayback> SnapshotAllRequested;
        public event EventHandler<ViewCameraPlayback> DownloadCurrentRequested;
        public event EventHandler<ViewCameraPlayback> AiOverlayRequested;
        public event System.EventHandler PlaybackHoverEntered;
        public event System.EventHandler PlaybackHoverLeft;
        public event EventHandler<ViewCameraPlayback> PlaybackTileClicked;
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public IntPtr _videoPanelHandle = IntPtr.Zero;
        public models.RtspPlayer Player { get; set; } = null;

        // Custom segment definition from m3u8
        public class PlaybackSegment
        {
            public double Duration { get; set; }
            public string Url { get; set; }
            public double StartOffset { get; set; }
            // Offset reported by the playback API (the `ofs` URL parameter).  It
            // identifies the position in the source recording; GStreamer still
            // seeks by the contiguous media-playlist position.
            public double? SourceOffset { get; set; }
            public bool HasVideo { get; set; }
            public System.DateTime RealStartTime { get; set; }
        }

        public ViewCameraPlayback(models.Camera camera)
        {
            InitializeComponent();
            DataContext = this;          
            Camera = camera;
            
            // Playback uses the same stable logical camera identifier as Live View.
            // The display name may change, while camID is the value selected by users.
            txtCameraName.Text = string.IsNullOrWhiteSpace(Camera.camID) ? Camera.name : Camera.camID;
            _videoPanelHandle = VideoPanel.Handle;

            // GStreamer renders into a native WinForms panel.  Listen to that panel as
            // well as the WPF tile so hover works over the actual picture, not only its edge.
            VideoPanel.MouseEnter += VideoPanel_MouseEnter;
            VideoPanel.MouseLeave += VideoPanel_MouseLeave;
            VideoPanel.MouseClick += VideoPanel_MouseClick;
            _hideHoverActionsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _hideHoverActionsTimer.Tick += HideHoverActionsTimer_Tick;
            _downloadProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            _downloadProgressTimer.Tick += (s, e) => UpdateDownloadProgressVisual();
            _downloadProgressHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            _downloadProgressHideTimer.Tick += (s, e) => HideDownloadProgress();

            Loaded += ViewCameraPlayback_Loaded;
        }

        public void ShowDownloadProgress(SmartDownloadManager.DownloadTask task)
        {
            if (_disposed || task == null || !IsLoaded)
                return;

            _downloadTask = task;
            EnsureDownloadProgressHost();
            _downloadProgressHideTimer.Stop();
            UpdateDownloadProgressVisual();
            NoPlaybackDataNativeHost.Visibility = Visibility.Visible;
            _downloadProgressTimer.Start();
        }

        // The playback page uses this during a fullscreen/window geometry
        // transaction.  Hiding the native D3D11 surface prevents it from
        // painting at every intermediate WPF size while the grid is arranged.
        public void SetVideoSurfaceVisible(bool visible)
        {
            if (_disposed || VideoPanel == null || VideoPanel.IsDisposed)
                return;
            try { VideoPanel.Visible = visible; }
            catch (ObjectDisposedException) { }
        }

        private void EnsureDownloadProgressHost()
        {
            if (_downloadProgressElementHost != null)
                return;

            _downloadProgressElementHost = new ElementHost
            {
                Dock = DockStyle.Fill,
                // Keep the native overlay visually light; the video remains
                // readable behind the compact progress card.
                BackColor = System.Drawing.Color.Transparent,
                Child = BuildDownloadProgressCard()
            };
            NoPlaybackDataNativeHost.Child = _downloadProgressElementHost;
            // WindowsFormsHost is an HWND island above the GStreamer video
            // surface.  The alpha channel of a WPF child is otherwise
            // composited against the ElementHost back buffer, which makes the
            // card look solid black.  Apply opacity to the HWND island too so
            // the rendered video remains visible through the download card.
            NoPlaybackDataNativeHost.Background = Brushes.Transparent;
            NoPlaybackDataNativeHost.Opacity = 0.72;
            NoPlaybackDataNativeHost.Width = 390;
            NoPlaybackDataNativeHost.Height = 164;
        }

        private FrameworkElement BuildDownloadProgressCard()
        {
            var root = new Border
            {
                // Keep the card itself deliberately light.  Together with
                // the host opacity above this is a true visual overlay rather
                // than an opaque dialog over the camera image.
                Background = new SolidColorBrush(Color.FromArgb(138, 5, 20, 34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(24, 63, 95)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                // Opacity is owned by the native host.  Keeping this at 1
                // avoids pre-compositing the alpha against a black WinForms
                // buffer before it reaches the video surface.
                Opacity = 1.0
            };
            var rows = new Grid();
            for (var i = 0; i < 5; i++) rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var spinner = new Border { Width = 18, Height = 18, BorderBrush = new SolidColorBrush(Color.FromRgb(45, 107, 255)), BorderThickness = new Thickness(3, 3, 0, 0), CornerRadius = new CornerRadius(12), RenderTransformOrigin = new Point(0.5, 0.5), Margin = new Thickness(0, 0, 10, 0) };
            spinner.RenderTransform = new RotateTransform(0);
            var rotate = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(760))) { RepeatBehavior = RepeatBehavior.Forever };
            spinner.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotate);
            header.Children.Add(spinner);
            _downloadTitleText = new TextBlock { Text = "Đang tải video…", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_downloadTitleText, 1); header.Children.Add(_downloadTitleText);
            _downloadCancelButton = new System.Windows.Controls.Button { Content = "Hủy", Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(12, 5, 12, 5), FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand };
            _downloadCancelButton.Click += (s, e) => { if (_downloadTask != null) SmartDownloadManager.Instance.Cancel(_downloadTask); };
            Grid.SetColumn(_downloadCancelButton, 2); header.Children.Add(_downloadCancelButton);
            rows.Children.Add(header);
            _downloadDetailText = new TextBlock { Margin = new Thickness(0, 12, 0, 0), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(190, 211, 231)), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(_downloadDetailText, 1); rows.Children.Add(_downloadDetailText);
            var progressHeader = new Grid { Margin = new Thickness(0, 10, 0, 4) };
            progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); progressHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progressHeader.Children.Add(new TextBlock { Text = "Tiến trình", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(159, 184, 208)) });
            _downloadPercentText = new TextBlock { FontSize = 12, Foreground = Brushes.White };
            Grid.SetColumn(_downloadPercentText, 1); progressHeader.Children.Add(_downloadPercentText);
            Grid.SetRow(progressHeader, 2); rows.Children.Add(progressHeader);
            _downloadProgressBar = new System.Windows.Controls.ProgressBar { Height = 7, Minimum = 0, Maximum = 100, Background = new SolidColorBrush(Color.FromRgb(11, 44, 69)), Foreground = new SolidColorBrush(Color.FromRgb(45, 107, 255)), BorderThickness = new Thickness(0) };
            Grid.SetRow(_downloadProgressBar, 3); rows.Children.Add(_downloadProgressBar);
            _downloadSpeedText = new TextBlock { Margin = new Thickness(0, 9, 0, 0), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(190, 211, 231)) };
            Grid.SetRow(_downloadSpeedText, 4); rows.Children.Add(_downloadSpeedText);
            root.Child = rows;
            return root;
        }

        private void UpdateDownloadProgressVisual()
        {
            var task = _downloadTask;
            if (_disposed || task == null) return;
            var terminal = string.Equals(task.Status, "Completed", StringComparison.OrdinalIgnoreCase) || string.Equals(task.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) || string.Equals(task.Status, "Failed", StringComparison.OrdinalIgnoreCase);
            if (_downloadTitleText != null) _downloadTitleText.Text = task.Status == "Completed" ? "Đã tải xong" : task.Status == "Cancelled" ? "Đã hủy tải" : task.Status == "Failed" ? "Tải video thất bại" : "Đang tải video…";
            if (_downloadDetailText != null) _downloadDetailText.Text = (Camera?.camID ?? task.CameraNames) + " · " + task.StartTime.ToString("HH:mm:ss") + " – " + task.EndTime.ToString("HH:mm:ss");
            if (_downloadPercentText != null)
            {
                _downloadPercentText.Text = task.TotalBytes > 0
                    ? string.Format("{0} / {1} · {2:0}%", FormatDownloadBytes(task.DownloadedBytes), FormatDownloadBytes(task.TotalBytes), task.Progress)
                    : task.DownloadedBytes > 0
                        ? FormatDownloadBytes(task.DownloadedBytes) + " / không xác định"
                        : "Đang chờ dữ liệu";
            }
            if (_downloadProgressBar != null) _downloadProgressBar.Value = Math.Max(0, Math.Min(100, task.Progress));
            if (_downloadSpeedText != null) _downloadSpeedText.Text = string.IsNullOrWhiteSpace(task.Speed) ? "Đang kết nối máy chủ…" : task.Speed;
            if (_downloadCancelButton != null) _downloadCancelButton.Visibility = task.CanCancel ? Visibility.Visible : Visibility.Collapsed;
            if (!terminal) return;
            _downloadProgressTimer.Stop();
            _downloadProgressHideTimer.Stop();
            _downloadProgressHideTimer.Start();
        }

        private static string FormatDownloadBytes(long value)
        {
            if (value < 1024) return value + " B";
            if (value < 1024L * 1024L) return (value / 1024d).ToString("0.0") + " KB";
            if (value < 1024L * 1024L * 1024L) return (value / (1024d * 1024d)).ToString("0.0") + " MB";
            return (value / (1024d * 1024d * 1024d)).ToString("0.00") + " GB";
        }

        private void HideDownloadProgress()
        {
            _downloadProgressHideTimer.Stop();
            _downloadProgressTimer.Stop();
            if (NoPlaybackDataNativeHost != null) NoPlaybackDataNativeHost.Visibility = Visibility.Collapsed;
        }

        private void VideoPanel_MouseEnter(object sender, System.EventArgs e)
        {
            PlaybackHoverEntered?.Invoke(this, System.EventArgs.Empty);
        }

        private void VideoPanel_MouseLeave(object sender, System.EventArgs e)
        {
            PlaybackHoverLeft?.Invoke(this, System.EventArgs.Empty);
            ScheduleHideHoverActions();
        }

        private void VideoPanel_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                PlaybackTileClicked?.Invoke(this, this);
        }

        private void Tile_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            PlaybackTileClicked?.Invoke(this, this);
        }

        private void Tile_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            PlaybackHoverEntered?.Invoke(this, System.EventArgs.Empty);
            ShowHoverActions();
        }

        private void Tile_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            PlaybackHoverLeft?.Invoke(this, System.EventArgs.Empty);
            ScheduleHideHoverActions();
        }

        private void HoverActions_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ShowHoverActions();
        }

        private void HoverActions_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ScheduleHideHoverActions();
        }

        private void ShowHoverActions()
        {
            if (_disposed || !IsLoaded || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            _hideHoverActionsTimer.Stop();
            SetHoverActionsVisible(true);
        }

        private void ScheduleHideHoverActions()
        {
            if (_disposed || !IsLoaded || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            _hideHoverActionsTimer.Stop();
            _hideHoverActionsTimer.Start();
        }

        private void HideHoverActionsTimer_Tick(object sender, System.EventArgs e)
        {
            _hideHoverActionsTimer.Stop();
            if (_disposed || !IsLoaded || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            SetHoverActionsVisible(false);
        }

        private void SetHoverActionsVisible(bool visible)
        {
            try
            {
                // Do not set WindowsFormsHost visibility while its child ElementHost is
                // still being created. This method is called only after Loaded and is
                // deliberately guarded because native video events can arrive during unload.
                // Playback actions are hosted once by VPlaybackHLS above the
                // aggregate timeline. Keeping this native per-tile host hidden
                // prevents HWND airspace flicker and missed mouse clicks.
                if (HoverActionsHost != null)
                    HoverActionsHost.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể cập nhật toolbar playback hover");
            }
        }

        public void InitPipeline()
        {
            if (Player != null)
            {
                Player.SendGPS -= SendGPS2Parent;
                Player.PlayerSending -= GetPlayerState;
                Player.SendWarning -= SendWarning2Parent;
                Player.SendMetaAIResult -= ForwardMetaAI;
                Player.Dispose();
                Player = null;
            }
            _aiMarkers.Clear();

            Player = new PlaybackHLS(HlsUrl, _videoPanelHandle, this.Camera.is_H264, isNvidiaGPU: libs.Counter.HasNvidiaGPU);
          
            Player.InitPipeline();
            Player.QueryPositionPlaying();
          
            Player.SendGPS += SendGPS2Parent;
            Player.PlayerSending += GetPlayerState;
            Player.SendWarning += SendWarning2Parent;
            Player.SendMetaAIResult += ForwardMetaAI;
        }

        private void SendWarning2Parent(object sender, string e)
        {
            if (e == "abnormal")
            {
                TTSManager.Instance.EnqueueWarning("Báº¥t thÆ°á»ng");
            }
            else
            {
                TTSManager.Instance.EnqueueWarning(Camera.name);
            }
            SetWarningTemp();
        }

        private async void SetWarningTemp()
        {
            CameraWarning = 5;
            await System.Threading.Tasks.Task.Delay(3000);
            CameraWarning = 0;
        }

        private void ForwardMetaAI(object sender, List<MetaAIResult> results)
        {
            results.ForEach(r => r.CameraInfo = Camera.name);
            SendMetaAIResult?.Invoke(this, results);
            // In playback we can also add markers to timeline if needed.
        }

        private void GetPlayerState(object sender, PlayerInfo info)
        {
            switch (info.Key)
            {
                case PlayerStatus.Position:
                    if (double.TryParse(info.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pos))
                        VideoPosition = pos;
                    break;
                case PlayerStatus.Duration:
                    VideoDuration = long.Parse(info.Value);
                    ShowConnectButton = Visibility.Hidden;
                    break;
                case PlayerStatus.Stop:
                    // During HLS startup a temporary Stop can arrive before the
                    // first duration/state notification. Give the pipeline a short
                    // grace period; otherwise a valid camera is incorrectly marked
                    // as having no playback data.
                    if (VideoDuration <= 0)
                        ShowNoPlaybackDataAfterStartupGrace(Player);
                    else
                        ShowConnectButton = Visibility.Visible;
                    break;
                case PlayerStatus.Eof:
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => ConnectedCamera()));
                    break;
            }
        }

        private void SendGPS2Parent(object sender, PointLatLng gps)
        {
            SendGPS?.Invoke(Camera.camID, gps);
        }

        public void ConnectedCamera()
        {
            try
            {
                PreparingPlaybackOverlay.Visibility = Visibility.Collapsed;
                NoPlaybackDataOverlay.Visibility = Visibility.Collapsed;
                // A WindowsFormsHost is an HWND and paints above normal WPF controls.
                // Restore it only when a real playback pipeline is about to start.
                videoWindow.Visibility = Visibility.Visible;
                VideoPanel.Visible = true;
                ShowConnectButton = Visibility.Hidden;
                InitPipeline();
                Player.player.SetState(State.Playing);
                _isPlaying = true;
                UpdateSpeedDisplay();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lá»—i ConnectedCamera");
            }
        }

        private void ViewCameraPlayback_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => SetHoverActionsVisible(false)), DispatcherPriority.ContextIdle);
            if (_initialized)
            {
                byte deta = 5;
                byte r = 126, g = 127, b = 127;

                _initialized = false;
                _gridCol = Grid.GetColumn(this);
                _gridRow = Grid.GetRow(this);
                if(_gridRow %2 == _gridCol %2)
                    ViewCamBackground = new SolidColorBrush(Color.FromRgb(r,g,b));
                else
                    ViewCamBackground = new SolidColorBrush(Color.FromRgb((byte)(r+deta), (byte) (g+deta), (byte)(b+deta)));
            }
        }

        private ImageSource Load_Image(string image_file)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new System.Uri(image_file, UriKind.Relative);
            bitmap.EndInit();
            return bitmap;
        }

        private void btnLive_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ConnectedCamera();
        }

        private void btnPlayCamera_Click(object sender, RoutedEventArgs e)
        {
            ConnectedCamera();
        }

        public void btn_Speaker_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Player?.Speaker(isSpeakerOn);
                isSpeakerOn = !isSpeakerOn;
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        public void btn_Exit_Click(object sender, RoutedEventArgs e)
        {
            EventClosed?.Invoke(this, new object());
        }

        private void btn_AiOverlay_Click(object sender, RoutedEventArgs e)
        {
            _aiOverlayEnabled = !_aiOverlayEnabled;
            if (Player != null)
                Player.RoiInfoShow = _aiOverlayEnabled;
            AiOverlayRequested?.Invoke(this, this);
        }

        /// <summary>Invoked by the single page-level playback toolbar.</summary>
        public void ToggleAiOverlay()
        {
            btn_AiOverlay_Click(this, null);
        }

        private void btn_SnapshotCurrent_Click(object sender, RoutedEventArgs e)
        {
            SnapshotCurrentRequested?.Invoke(this, this);
        }

        private void btn_SnapshotAll_Click(object sender, RoutedEventArgs e)
        {
            SnapshotAllRequested?.Invoke(this, this);
        }

        private void btn_DownloadCurrent_Click(object sender, RoutedEventArgs e)
        {
            DownloadCurrentRequested?.Invoke(this, this);
        }

        public bool TrySaveSnapshot(out string savedPath)
        {
            savedPath = null;
            if (_disposed || VideoPanel == null || VideoPanel.Width <= 0 || VideoPanel.Height <= 0)
                return false;

            try
            {
                // Match browser download behavior: save directly to the user's
                // normal Downloads folder.
                var downloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                var folder = downloads;
                Directory.CreateDirectory(folder);
                var safeId = string.Concat((Camera?.camID ?? "camera").Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                savedPath = System.IO.Path.Combine(folder, safeId + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");

                // PlaybackHLS supplies a decoded appsink frame at the camera's
                // source resolution.  Prefer it so snapshots are Full HD when the
                // source is Full HD and never include the grid, badge or controls.
                var playback = Player as models.PlaybackHLS;
                if (playback != null)
                {
                    if (playback.TrySaveDecodedSnapshot(savedPath))
                        return true;
                }

                // No decoded frame cache is available for this pipeline. Capture only
                // the tile's native camera surface after page chrome has been hidden.
                var bitmap = CaptureRenderedVideoFrame();
                if (bitmap == null)
                    return false;
                using (bitmap)
                {
                    bitmap.Save(savedPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                return true;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể chụp ảnh playback " + (Camera?.camID ?? string.Empty));
                savedPath = null;
                return false;
            }
        }

        public void ShowPreparingPlayback(string status)
        {
            if (_disposed)
                return;

            try
            {
                if (PreparingPlaybackText != null)
                    PreparingPlaybackText.Text = string.IsNullOrWhiteSpace(status) ? "Đang chuẩn bị dữ liệu..." : status;
                NoPlaybackDataOverlay.Visibility = Visibility.Collapsed;
                VideoPanel.Visible = false;
                videoWindow.Visibility = Visibility.Collapsed;
                PreparingPlaybackOverlay.Visibility = Visibility.Visible;
                SetHoverActionsVisible(false);
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể hiển thị trạng thái chuẩn bị playback");
            }
        }

        private async void ShowNoPlaybackDataAfterStartupGrace(models.RtspPlayer playerAtStop)
        {
            await System.Threading.Tasks.Task.Delay(1800);
            if (_disposed || !ReferenceEquals(Player, playerAtStop) || VideoDuration > 0)
                return;

            if (Dispatcher.CheckAccess())
                ShowNoPlaybackData();
            else
                Dispatcher.BeginInvoke(new Action(ShowNoPlaybackData));
        }

        /// <summary>
        /// Exports one frame from the HLS source in its native dimensions.  This is
        /// intentionally a short-lived external decoder process: it is isolated from
        /// the long-running GStreamer render pipeline, so a snapshot failure cannot
        /// stop playback or close the application.
        /// </summary>
        public async Task<string> TrySaveSourceSnapshotAsync()
        {
            if (_disposed || string.IsNullOrWhiteSpace(HlsUrl))
                return null;

            var ffmpeg = ResolveFfmpegExecutable();
            if (string.IsNullOrWhiteSpace(ffmpeg))
                return null;

            var outputPath = CreateSnapshotOutputPath();
            if (string.IsNullOrWhiteSpace(outputPath))
                return null;

            try
            {
                var seekSeconds = Math.Max(0, VideoPosition);
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-hide_banner -loglevel error -y -ss " + seekSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                        " -i " + QuoteProcessArgument(HlsUrl) + " -map 0:v:0 -frames:v 1 -an -c:v png " + QuoteProcessArgument(outputPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process == null)
                        return null;

                    var errorRead = process.StandardError.ReadToEndAsync();
                    var outputRead = process.StandardOutput.ReadToEndAsync();
                    var exited = await System.Threading.Tasks.Task.Run(() => process.WaitForExit(15000));
                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        LoggerManager.LogWarn("Snapshot playback quá thời gian chờ cho " + (Camera?.camID ?? string.Empty));
                        return null;
                    }

                    await System.Threading.Tasks.Task.WhenAll(errorRead, outputRead);
                    if (process.ExitCode != 0 || !File.Exists(outputPath) || new System.IO.FileInfo(outputPath).Length == 0)
                    {
                        try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                        LoggerManager.LogWarn("Không thể xuất frame gốc playback " + (Camera?.camID ?? string.Empty) + ": " + errorRead.Result);
                        return null;
                    }
                }

                return outputPath;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể chạy snapshot frame gốc playback " + (Camera?.camID ?? string.Empty));
                return null;
            }
        }

        private string CreateSnapshotOutputPath()
        {
            try
            {
                var downloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(downloads);
                var safeId = string.Concat((Camera?.camID ?? "camera").Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                return System.IO.Path.Combine(downloads, safeId + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveFfmpegExecutable()
        {
            var configured = Environment.GetEnvironmentVariable("FFMPEG_PATH");
            var candidates = new[]
            {
                configured,
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                @"C:\ffmpeg-8.1-essentials_build\bin\ffmpeg.exe"
            };
            return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        }

        private static string QuoteProcessArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// The title badge is hosted in a separate native HWND so it otherwise gets
        /// included by CopyFromScreen.  The page temporarily hides this chrome while
        /// exporting a frame and restores it immediately afterwards.
        /// </summary>
        public void SetSnapshotChromeVisible(bool visible)
        {
            if (_disposed || LeftOnTopWindow == null)
                return;

            try
            {
                LeftOnTopWindow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "KhÃ´ng thá»ƒ cáº­p nháº­t nhÃ£n camera khi chá»¥p playback");
            }
        }

        private System.Drawing.Bitmap CaptureRenderedVideoFrame()
        {
            System.Drawing.Bitmap bitmap = null;
            try
            {
                // Retain the simple path for software sinks; it costs almost
                // nothing and does not require the application to be foreground.
                bitmap = new System.Drawing.Bitmap(VideoPanel.Width, VideoPanel.Height);
                VideoPanel.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
                if (!IsNearlyBlack(bitmap))
                    return CropLetterboxedViewport(bitmap);

                bitmap.Dispose();
                bitmap = new System.Drawing.Bitmap(VideoPanel.Width, VideoPanel.Height);

                // The GStreamer D3D11 child surface is present in the desktop
                // compositor. CopyFromScreen captures that actual rendered frame,
                // unlike DrawToBitmap which returns an empty parent panel.
                var source = VideoPanel.PointToScreen(System.Drawing.Point.Empty);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(source, System.Drawing.Point.Empty, bitmap.Size,
                        System.Drawing.CopyPixelOperation.SourceCopy);
                }

                if (!IsNearlyBlack(bitmap))
                    return CropLetterboxedViewport(bitmap);

                bitmap.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                bitmap?.Dispose();
                LoggerManager.LogException(ex, "KhÃ´ng thá»ƒ capture frame playback " + (Camera?.camID ?? string.Empty));
                return null;
            }
        }

        /// <summary>
        /// Playback deliberately keeps the source aspect ratio.  The D3D sink therefore
        /// paints black letterbox bands inside the native panel.  A screenshot is an
        /// export of the camera frame, not a screenshot of its WPF grid slot, so remove
        /// only fully-black outer bands before persisting it.
        /// </summary>
        private static System.Drawing.Bitmap CropLetterboxedViewport(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width < 32 || bitmap.Height < 32)
                return bitmap;

            var top = FindBlackBand(bitmap, true, true);
            var bottom = FindBlackBand(bitmap, true, false);
            var left = FindBlackBand(bitmap, false, true);
            var right = FindBlackBand(bitmap, false, false);

            var width = bitmap.Width - left - right;
            var height = bitmap.Height - top - bottom;

            // Never crop a genuine dark scene.  Letterbox removal is valid only when
            // enough of the source remains after scanning the four outer edges.
            if (width < bitmap.Width / 3 || height < bitmap.Height / 3 ||
                (top == 0 && bottom == 0 && left == 0 && right == 0))
                return bitmap;

            try
            {
                var cropped = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using (var graphics = System.Drawing.Graphics.FromImage(cropped))
                {
                    graphics.DrawImage(bitmap,
                        new System.Drawing.Rectangle(0, 0, width, height),
                        new System.Drawing.Rectangle(left, top, width, height),
                        System.Drawing.GraphicsUnit.Pixel);
                }

                bitmap.Dispose();
                return cropped;
            }
            catch
            {
                // Saving the unmodified rendered frame is preferable to losing a
                // snapshot when a graphics driver rejects a bitmap conversion.
                return bitmap;
            }
        }

        private static int FindBlackBand(System.Drawing.Bitmap bitmap, bool scanRows, bool fromStart)
        {
            var outerCount = scanRows ? bitmap.Height : bitmap.Width;
            var innerCount = scanRows ? bitmap.Width : bitmap.Height;
            var maxBand = Math.Min(outerCount / 3, 600);
            var band = 0;

            for (var outer = 0; outer < maxBand; outer++)
            {
                var coordinate = fromStart ? outer : outerCount - 1 - outer;
                var blackPixels = 0;
                // Sample the edge densely enough to reject a real night scene while
                // avoiding thousands of GetPixel calls on a 4K snapshot.
                var step = Math.Max(1, innerCount / 96);
                var samples = 0;

                for (var inner = 0; inner < innerCount; inner += step)
                {
                    var pixel = scanRows ? bitmap.GetPixel(inner, coordinate) : bitmap.GetPixel(coordinate, inner);
                    samples++;
                    if (pixel.R <= 10 && pixel.G <= 10 && pixel.B <= 10)
                        blackPixels++;
                }

                // A letterbox row/column is virtually solid black.  Do not treat
                // ordinary dark image content as padding.
                if (samples == 0 || blackPixels * 100 < samples * 97)
                    break;

                band++;
            }

            // Ignore tiny dark borders from the native video sink.
            return band >= 4 ? band : 0;
        }

        private static bool IsNearlyBlack(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0)
                return true;

            var stepX = Math.Max(1, bitmap.Width / 32);
            var stepY = Math.Max(1, bitmap.Height / 18);
            var samples = 0;
            var visible = 0;
            for (var y = 0; y < bitmap.Height; y += stepY)
            {
                for (var x = 0; x < bitmap.Width; x += stepX)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    samples++;
                    if (pixel.R > 8 || pixel.G > 8 || pixel.B > 8)
                        visible++;
                }
            }

            // A real night camera can be dark, but a DrawToBitmap failure is an
            // almost perfectly black frame. Require at least 2% visible samples.
            return samples == 0 || visible * 50 < samples;
        }

        public void ShowNoPlaybackData()
        {
            ShowConnectButton = Visibility.Hidden;
            // Hide the complete native host, not only its child panel. A hidden
            // WinForms panel can still leave its D3D/GStreamer HWND painted gray
            // above WPF. Collapsing the host makes the WPF error state and camera
            // identifier the only visual layers for an empty playlist.
            VideoPanel.Visible = false;
            videoWindow.Visibility = Visibility.Collapsed;
            PreparingPlaybackOverlay.Visibility = Visibility.Collapsed;
            NoPlaybackDataOverlay.Visibility = Visibility.Visible;
            SetHoverActionsVisible(false);
        }

        public void btn_Zoom_Click(object sender, RoutedEventArgs e)
        {
            Grid parent = this.Parent as Grid;
            if (parent == null) return;

            if (isZoomOut == true)
            {
                _gridRow = Grid.GetRow(this);
                _gridCol = Grid.GetColumn(this);
                OriginalRowSpan = Grid.GetRowSpan(this);
                OriginalColSpan = Grid.GetColumnSpan(this);

                foreach (var child in parent.Children)
                {
                    if (child != this && child is UIElement ui)
                    {
                        ui.Visibility = Visibility.Collapsed;
                    }
                }

                int row = Math.Max(1, parent.RowDefinitions.Count);
                int col = Math.Max(1, parent.ColumnDefinitions.Count);
                
                System.Windows.Controls.Panel.SetZIndex(this, 99);
                parent.Children.Remove(this);
                parent.Children.Add(this);

                Grid.SetRow(this, 0);
                Grid.SetColumn(this, 0);
                Grid.SetRowSpan(this, row);
                Grid.SetColumnSpan(this, col);
            }
            else
            {
                foreach (var child in parent.Children)
                {
                    if (child is UIElement ui)
                    {
                        ui.Visibility = Visibility.Visible;
                    }
                }

                Grid.SetRow(this, _gridRow);
                Grid.SetColumn(this, _gridCol);
                Grid.SetRowSpan(this, OriginalRowSpan);
                Grid.SetColumnSpan(this, OriginalColSpan);
                System.Windows.Controls.Panel.SetZIndex(this, 1);
            }
            isZoomOut = !isZoomOut;
            RenderMiniTimeline();
        }

        // --- Playback Speed Controls ---
        private void btn_Slower_Click(object sender, RoutedEventArgs e)
        {
            _currentPlaybackRate = Math.Max(0.1f, _currentPlaybackRate - 0.2f);
            ApplyPlaybackRate();
        }

        private void btn_Faster_Click(object sender, RoutedEventArgs e)
        {
            _currentPlaybackRate = Math.Min(4.0f, _currentPlaybackRate + 0.2f);
            ApplyPlaybackRate();
        }

        private void btn_PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (Player == null || Player.player == null) return;

            if (_isPlaying)
            {
                Player.Pause();
            }
            else
            {
                if (_currentPlaybackRate != 1.0f)
                {
                    _currentPlaybackRate = 1.0f;
                    ApplyPlaybackRate();
                }
                Player.Playing();
            }
            _isPlaying = !_isPlaying;
        }

        private void ApplyPlaybackRate()
        {
            if (Player != null)
            {
                Player.SetRate(_currentPlaybackRate);
                UpdateSpeedDisplay();
            }
        }

        private void UpdateSpeedDisplay()
        {
            // The speed text is rendered by the page-level toolbar.
        }

        // --- M3U8 Parsing & Timeline ---
        public void ParseM3U8AndRenderTimeline(string m3u8Content, System.DateTime searchStart, System.DateTime searchEnd)
        {
            _segments.Clear();
            _totalDurationSeconds = 0;
            _searchStartTime = searchStart;
            _searchEndTime = searchEnd;
            _totalRealDurationSeconds = (_searchEndTime - _searchStartTime).TotalSeconds;

            if (string.IsNullOrWhiteSpace(m3u8Content))
            {
                RenderMiniTimeline();
                return;
            }

            string[] lines = m3u8Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            double currentDuration = 0;
            
            // The playback API gives the authoritative wall-clock timestamp in
            // the segment file name, including its optional microsecond suffix.
            // Do not reduce this to whole seconds: doing so makes independent
            // camera playlists visibly drift while playing or seeking together.
            var dateRegex = new System.Text.RegularExpressions.Regex(
                @"(?<timestamp>\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(?:-\d{1,6})?)",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            var sourceOffsetRegex = new System.Text.RegularExpressions.Regex(
                @"(?:[?&])ofs=(?<offset>-?\d+(?:\.\d+)?)",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var videoSegments = new List<PlaybackSegment>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("#EXTINF:"))
                {
                    string durStr = line.Substring(8).TrimEnd(',');
                    if (double.TryParse(durStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedDur))
                    {
                        currentDuration = parsedDur;
                    }
                }
                else if (!line.StartsWith("#") && !string.IsNullOrEmpty(line))
                {
                    System.DateTime realStartTime = System.DateTime.MinValue;
                    var match = dateRegex.Match(line);
                    if (match.Success)
                    {
                        string timestamp = match.Groups["timestamp"].Value;
                        var timestampFormats = new[]
                        {
                            "yyyy-MM-dd_HH-mm-ss-FFFFFF",
                            "yyyy-MM-dd_HH-mm-ss-FFFFF",
                            "yyyy-MM-dd_HH-mm-ss-FFFF",
                            "yyyy-MM-dd_HH-mm-ss-FFF",
                            "yyyy-MM-dd_HH-mm-ss-FF",
                            "yyyy-MM-dd_HH-mm-ss-F",
                            "yyyy-MM-dd_HH-mm-ss"
                        };
                        System.DateTime.TryParseExact(timestamp, timestampFormats,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out realStartTime);
                    }

                    double parsedSourceOffset;
                    var sourceOffsetMatch = sourceOffsetRegex.Match(line);
                    double? sourceOffset = sourceOffsetMatch.Success &&
                        double.TryParse(sourceOffsetMatch.Groups["offset"].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out parsedSourceOffset)
                        ? (double?)parsedSourceOffset
                        : null;

                    // This is a segment URL
                    videoSegments.Add(new PlaybackSegment
                    {
                        Duration = currentDuration,
                        Url = line,
                        StartOffset = 0, // Will be calculated
                        SourceOffset = sourceOffset,
                        HasVideo = true,
                        RealStartTime = realStartTime != System.DateTime.MinValue ? realStartTime : _searchStartTime.AddSeconds(_totalDurationSeconds)
                    });
                    _totalDurationSeconds += currentDuration;
                    currentDuration = 0;
                }
            }
            
            // Sort segments by RealStartTime just in case
            videoSegments = videoSegments.OrderBy(s => s.RealStartTime).ToList();
            
            // Build the timeline directly from the recording timestamps.  The old
            // implementation ignored gaps shorter than ten seconds, which shifted
            // all following segments and broke multi-camera real-time sync.
            System.DateTime currentRealTime = _searchStartTime;
            foreach (var vSeg in videoSegments)
            {
                if (vSeg.RealStartTime < _searchStartTime)
                {
                    // A playlist may start mid-segment.  Preserve only the part
                    // that overlaps the queried interval.
                    double clippedSeconds = (_searchStartTime - vSeg.RealStartTime).TotalSeconds;
                    if (clippedSeconds >= vSeg.Duration)
                        continue;
                    vSeg.RealStartTime = _searchStartTime;
                    vSeg.Duration -= Math.Max(0, clippedSeconds);
                }

                if (vSeg.RealStartTime >= _searchEndTime || vSeg.Duration <= 0)
                    continue;

                if (vSeg.RealStartTime > currentRealTime)
                {
                    double gapDuration = (vSeg.RealStartTime - currentRealTime).TotalSeconds;
                    _segments.Add(new PlaybackSegment
                    {
                        Duration = gapDuration,
                        StartOffset = (currentRealTime - _searchStartTime).TotalSeconds,
                        HasVideo = false,
                        RealStartTime = currentRealTime
                    });
                }

                vSeg.StartOffset = Math.Max(0, (vSeg.RealStartTime - _searchStartTime).TotalSeconds);
                _segments.Add(vSeg);

                var segmentEnd = vSeg.RealStartTime.AddSeconds(vSeg.Duration);
                if (segmentEnd > currentRealTime)
                    currentRealTime = segmentEnd;
            }
            
            if (currentRealTime < _searchEndTime)
            {
                double finalGap = (_searchEndTime - currentRealTime).TotalSeconds;
                if (finalGap > 0)
                {
                    _segments.Add(new PlaybackSegment
                    {
                        Duration = finalGap,
                        StartOffset = Math.Max(0, (currentRealTime - _searchStartTime).TotalSeconds),
                        HasVideo = false,
                        RealStartTime = currentRealTime
                    });
                }
            }

            RenderMiniTimeline();
        }

        public List<PlaybackSegment> GetTimelineSegments()
        {
            return _segments.Select(seg => new PlaybackSegment
            {
                Duration = seg.Duration,
                Url = seg.Url,
                StartOffset = seg.StartOffset,
                SourceOffset = seg.SourceOffset,
                HasVideo = seg.HasVideo,
                RealStartTime = seg.RealStartTime
            }).ToList();
        }

        public void SeekToRealTime(System.DateTime targetTime, bool forceSeek = false)
        {
            if (_totalRealDurationSeconds <= 0)
            {
                return;
            }

            double realTimeOffset = (targetTime - _searchStartTime).TotalSeconds;
            realTimeOffset = Math.Max(0, Math.Min(realTimeOffset, _totalRealDurationSeconds));
            double gstTargetTime = MapRealTimeOffsetToVideoPosition(realTimeOffset);

            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                double width = canvasMiniTimeline.ActualWidth;
                if (width > 0 && !double.IsNaN(width) && miniPlayhead != null)
                {
                    double x = (realTimeOffset / _totalRealDurationSeconds) * width;
                    Canvas.SetLeft(miniPlayhead, Math.Max(0, Math.Min(x, width)));
                    txtCurrentTime.Text = _searchStartTime.AddSeconds(realTimeOffset).ToString("HH:mm:ss");

                    if (currentTimeBorder != null)
                    {
                        currentTimeBorder.Visibility = Visibility.Visible;
                        Canvas.SetLeft(currentTimeBorder, Math.Max(0, x - 25));
                    }
                }
            }));

            if (forceSeek || (System.DateTime.Now - _lastSeekInteractionTime).TotalMilliseconds > SeekThrottleMs)
            {
                _lastSeekInteractionTime = System.DateTime.Now;
                if (Player != null && Player.player != null)
                {
                    Player.SeekAbsolute((long)(gstTargetTime * Gst.Constants.SECOND));
                }
            }
        }

        public System.DateTime GetCurrentPlaybackRealTime()
        {
            if (_searchStartTime == System.DateTime.MinValue)
                return System.DateTime.MinValue;

            return _searchStartTime.AddSeconds(MapVideoPositionToRealTimeOffset(VideoPosition));
        }

        public void SetSnapshotPickVisual(bool active, bool highlighted)
        {
            if (_disposed || videoWindow == null || VideoPanel == null)
                return;

            // Do not hide/recreate the native video HWND while picking: that made
            // tiles flash each time the pointer crossed an edge. Native HWNDs cannot
            // alpha-compose safely in WPF, therefore keep the video intact and use
            // a light WPF dim/border cue instead of an opaque black host.
            videoWindow.Visibility = Visibility.Visible;
            VideoPanel.Visible = true;
            // Keep the native video surface intact. The GStreamer overlay paints
            // the 20% dim cue directly on the texture so there is no black WPF/HWND
            // airspace layer; the hovered tile is restored to normal brightness.
            videoWindow.Opacity = 1.0;
            DragOverlay.Opacity = 1.0;
            if (Player != null)
                Player.DimForCaptureSelection = active && !highlighted;
            NoPlaybackDataHost.Visibility = Visibility.Collapsed;
        }

        public double TimelineHeight => 35.0;

        private void RenderMiniTimeline()
        {
            if (_totalRealDurationSeconds <= 0)
            {
                this.Dispatcher.BeginInvoke(new Action(() => canvasMiniTimeline.Children.Clear()));
                return;
            }

            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                double width = canvasMiniTimeline.ActualWidth;
                double height = canvasMiniTimeline.ActualHeight;
                if (width <= 0 || double.IsNaN(width)) width = 200;
                if (height <= 0 || double.IsNaN(height)) height = 20;

                canvasMiniTimeline.Children.Clear();

                // Base track
                Rectangle rectTrack = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    Width = width, Height = height, Opacity = 0.8
                };
                Canvas.SetLeft(rectTrack, 0); Canvas.SetTop(rectTrack, 0);
                canvasMiniTimeline.Children.Add(rectTrack);

                // Draw Segments
                foreach (var seg in _segments)
                {
                    double x = (seg.StartOffset / _totalRealDurationSeconds) * width;
                    double w = (seg.Duration / _totalRealDurationSeconds) * width;
                    if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
                    if (double.IsNaN(w) || double.IsInfinity(w)) w = 0;

                    Rectangle rect = new Rectangle
                    {
                        Fill = seg.HasVideo ? new SolidColorBrush(Color.FromRgb(0, 210, 255)) : new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                        Width = Math.Max(0, w - 0.5),
                        Height = Math.Max(1, height),
                        Opacity = 0.9, RadiusX = 0, RadiusY = 0,
                        ToolTip = $"Offset: {TimeSpan.FromSeconds(seg.StartOffset):hh\\:mm\\:ss}\nDuration: {seg.Duration}s"
                    };
                    Canvas.SetLeft(rect, x); Canvas.SetTop(rect, 0);
                    canvasMiniTimeline.Children.Add(rect);
                }
                
                txtTimeStart.Text = _searchStartTime.ToString("HH:mm");
                txtTimeEnd.Text = _searchEndTime.ToString("HH:mm");
                
                System.DateTime mid = _searchStartTime.AddSeconds(_totalRealDurationSeconds / 2);
                txtTimeMiddle.Text = mid.ToString("HH:mm");
                
                UpdateMiniPlayhead();
            }));
        }
        
        private double MapVideoPositionToRealTimeOffset(double videoPos)
        {
            double currentVideoPos = 0;
            foreach (var seg in _segments)
            {
                if (seg.HasVideo)
                {
                    if (videoPos >= currentVideoPos && videoPos <= currentVideoPos + seg.Duration)
                    {
                        double offsetInSegment = videoPos - currentVideoPos;
                        return seg.StartOffset + offsetInSegment;
                    }
                    currentVideoPos += seg.Duration;
                }
            }
            return _totalRealDurationSeconds; // Default to end
        }

        private double MapRealTimeOffsetToVideoPosition(double realTimeOffset)
        {
            double currentVideoPos = 0;
            double lastValidVideoPos = 0;

            foreach (var seg in _segments)
            {
                if (realTimeOffset >= seg.StartOffset && realTimeOffset <= seg.StartOffset + seg.Duration)
                {
                    if (seg.HasVideo)
                    {
                        double offsetInSegment = realTimeOffset - seg.StartOffset;
                        return currentVideoPos + offsetInSegment;
                    }
                    else
                    {
                        // User clicked in gap -> seek to the start of the next valid video segment
                        return currentVideoPos; 
                    }
                }
                if (seg.HasVideo)
                {
                    currentVideoPos += seg.Duration;
                    lastValidVideoPos = currentVideoPos;
                }
            }
            return lastValidVideoPos;
        }

        private void UpdateMiniPlayhead()
        {
            if (_totalRealDurationSeconds <= 0 || _isDraggingTimeline || miniPlayhead == null) return;
            
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                double width = canvasMiniTimeline.ActualWidth;
                if (width <= 0 || double.IsNaN(width)) return;
                
                double realTimeOffsetSeconds = MapVideoPositionToRealTimeOffset(VideoPosition);
                double x = (realTimeOffsetSeconds / _totalRealDurationSeconds) * width;
                if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
                
                double playheadWidth = double.IsNaN(miniPlayhead.Width) ? 2 : miniPlayhead.Width;
                double targetX = Math.Min(x, Math.Max(0, width - playheadWidth));
                
                Canvas.SetLeft(miniPlayhead, targetX);
                
                System.DateTime currentRealTime = _searchStartTime.AddSeconds(realTimeOffsetSeconds);
                txtCurrentTime.Text = currentRealTime.ToString("HH:mm:ss");
                
                if (currentTimeBorder != null)
                {
                    currentTimeBorder.Visibility = Visibility.Visible;
                    Canvas.SetLeft(currentTimeBorder, Math.Max(0, targetX - 25));
                }
            }));
        }

        private void canvasMiniTimeline_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_totalRealDurationSeconds <= 0) return;
            _isDraggingTimeline = true;
            canvasMiniTimeline.CaptureMouse();
            HandleTimelineSeek(e.GetPosition(canvasMiniTimeline));
        }

        private void canvasMiniTimeline_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingTimeline)
            {
                HandleTimelineSeek(e.GetPosition(canvasMiniTimeline));
            }
        }

        private void canvasMiniTimeline_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingTimeline)
            {
                _isDraggingTimeline = false;
                canvasMiniTimeline.ReleaseMouseCapture();
                HandleTimelineSeek(e.GetPosition(canvasMiniTimeline));
            }
        }

        private void HandleTimelineSeek(Point p)
        {
            double width = canvasMiniTimeline.ActualWidth;
            if (width <= 0 || double.IsNaN(width) || _totalRealDurationSeconds <= 0) return;
            
            double x = Math.Max(0, Math.Min(p.X, width));
            if (double.IsNaN(x)) x = 0;
            
            double realTimeOffset = (x / width) * _totalRealDurationSeconds;
            double gstTargetTime = MapRealTimeOffsetToVideoPosition(realTimeOffset);
            
            Canvas.SetLeft(miniPlayhead, x);
            
            System.DateTime currentRealTime = _searchStartTime.AddSeconds(realTimeOffset);
            txtCurrentTime.Text = currentRealTime.ToString("HH:mm:ss");
            if (currentTimeBorder != null)
            {
                currentTimeBorder.Visibility = Visibility.Visible;
                Canvas.SetLeft(currentTimeBorder, Math.Max(0, x - 25));
            }

            if ((System.DateTime.Now - _lastSeekInteractionTime).TotalMilliseconds > SeekThrottleMs || !_isDraggingTimeline)
            {
                _lastSeekInteractionTime = System.DateTime.Now;
                if (Player != null && Player.player != null)
                {
                    Player.SeekAbsolute((long)(gstTargetTime * Gst.Constants.SECOND));
                }
            }
        }

        private void canvasMiniTimeline_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderMiniTimeline();
        }

        public void Dispose()
        {
            _disposed = true;
            VideoPanel.MouseEnter -= VideoPanel_MouseEnter;
            VideoPanel.MouseLeave -= VideoPanel_MouseLeave;
            _hideHoverActionsTimer.Stop();
            _hideHoverActionsTimer.Tick -= HideHoverActionsTimer_Tick;
            _downloadProgressTimer.Stop();
            _downloadProgressHideTimer.Stop();
            if (_downloadProgressElementHost != null)
            {
                NoPlaybackDataNativeHost.Child = null;
                _downloadProgressElementHost.Dispose();
                _downloadProgressElementHost = null;
            }
            if (Player != null)
            {
                Player.Dispose();
                Player = null;
            }
        }
    }
}
