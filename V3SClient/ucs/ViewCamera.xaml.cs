using GLib;
using GMap.NET;
using Gst;
using Gst.Video;
using SharpDX.Direct3D11;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
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
using V3SClient.libs;
using V3SClient.models;
using UserControl = System.Windows.Controls.UserControl;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for UCamera.xaml
    /// </summary>
    public partial class ViewCamera : UserControl, INotifyPropertyChanged, IDisposable
    {

        bool _isExpanded = false;
        int _gridRow, _gridCol;

        private bool _initialized=true;

        private List<string> _videoFiles;
        private List<ApiManager.PlaybackVideoInfo> _segments;
        private List<double> _segmentVisualOffsets = new List<double>();
        private double _totalDurationSeconds = 0;
        private List<MetaAIResult> _aiMarkers = new List<MetaAIResult>();
        private System.DateTime _lastRenderTime = System.DateTime.MinValue;
        private readonly object _renderLock = new object();
        private System.DateTime _timelineStart;
        private System.DateTime _timelineEnd;
        private bool _isDraggingTimeline = false;
        private const int SeekThrottleMs = 150;
        private System.DateTime _lastSeekInteractionTime = System.DateTime.MinValue;
        private double _lastTimelineClickX = 0;

        public System.DateTime? SelectionStart { get; set; }
        public System.DateTime? SelectionEnd { get; set; }
        private bool _isSelectingStart = false;
        private bool _isSelectingEnd = false;


        public int OriginalRowSpan { get; set; } = 1;
        public int OriginalColSpan { get; set; } = 1;

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

        private Visibility _showVideoSlider = Visibility.Collapsed;
        public Visibility ShowVideoSlider
        {
            get { return _showVideoSlider; }
            set
            {
                if (value != _showVideoSlider)
                {
                    _showVideoSlider = value;
                    OnPropertyChanged("ShowVideoSlider");
                }
            }
        }

        private bool _isPlaybackMode = false;
        public bool IsPlaybackMode
        {
            get { return _isPlaybackMode; }
            set
            {
                _isPlaybackMode = value;
                ShowVideoSlider = _isPlaybackMode ? Visibility.Visible : Visibility.Collapsed;
                if (!_isPlaybackMode) IsFullTimeline = false;
            }
        }

        private bool _isFullTimeline = false;
        public bool IsFullTimeline
        {
            get { return _isFullTimeline; }
            set
            {
                if (_isFullTimeline != value)
                {
                    _isFullTimeline = value;
                    OnPropertyChanged("IsFullTimeline");
                    OnPropertyChanged("TimelineHeight");
                    UpdateManualMarkers();
                    RenderMiniTimeline();
                    UpdateMiniPlayhead();
                }
            }
        }

        public double TimelineHeight => IsFullTimeline ? 55.0 : 8.0;

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
        public bool isMicOn { get; set; } = false;
        private bool isZoomOut { get; set; } = true;
        public bool IsAIViewMode { get; set; } = false;
        public string PlaybackRtspUrl { get; set; } = null;
        public string HlsUrl { get; set; } = null;
        public models.Camera Camera { get; set; } = null;
        public string Camera_Name { get; set; } = null;

        private Visibility _cameraStatus = Visibility.Hidden;
       
        public Visibility CameraStatus
        {
            get { return _cameraStatus; }
            set
            {
                if (value != _cameraStatus)
                {
                    _cameraStatus = value;
                    OnPropertyChanged("CameraStatus");                   
                }
            }
            
        }
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

        // send event to parent to zoom in/ out

        // public event EventHandler<string> SendLog;
        public event EventHandler<object> EventClosed;
        public event EventHandler<GMap.NET.PointLatLng> SendGPS;
        public event EventHandler<string> StreamModeChanged;

        public event EventHandler<List<MetaAIResult>> SendMetaAIResult;

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }


        public IntPtr _videoPanelHandle = IntPtr.Zero;

       
        public models.RtspPlayer Player { get; set; } = null;
        private PlayerType playerType { get; set; }
        public void InitPipeline(string rtsp = "")
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
            if (this.playerType == PlayerType.RtspPlay)
            {
                Player = new models.RtspPlayer(rtsp, _videoPanelHandle, this.Camera.is_H264, is_nvidiagpu: libs.Counter.HasNvidiaGPU);
            }
            else if (this.playerType == PlayerType.FilesPlay)
            {
                Player = new FilesPlayer(_videoFiles, _videoPanelHandle, this.Camera.is_H264, isNvidiaGPU: libs.Counter.HasNvidiaGPU);
            }
            else if (this.playerType == PlayerType.HLSPlay)
            {
                Player = new PlaybackHLS(rtsp, _videoPanelHandle, this.Camera.is_H264, isNvidiaGPU: libs.Counter.HasNvidiaGPU);
            }

            if (IsPlaybackMode)
                ShowVideoSlider = Visibility.Visible;
            else
                ShowVideoSlider = playerType == PlayerType.RtspPlay ? Visibility.Collapsed : Visibility.Visible;
          
            Player.InitPipeline();
            if (IsPlaybackMode) Player.QueryPositionPlaying();
          
            Player.SendGPS += SendGPS2Parent;
            Player.PlayerSending += GetPlayerState;
            Player.SendWarning += SendWarning2Parent;
            Player.SendMetaAIResult += ForwardMetaAI;


            Player.ShowVideoSlider = ShowVideoSlider;
           
        }
        private void SendWarning2Parent(object sender, string e)
        {
            if (e == "abnormal")
            {
                try
                {
                    // Lấy stream của file .wav đã được nhúng dưới dạng Embedded Resource
                    // Lưu ý: Chuỗi "V3SClient.audios.abnormal_warning.wav" phụ thuộc vào [Default Namespace của Project].[Tên Thư Mục].[Tên File]
                    // Giả định Default Namespace của bạn là "V3SClient"
                    string resourceName = "V3SClient.audios.abnormal_warning.wav";
                    
                    using (System.IO.Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                    {
                        if (stream != null)
                        {
                            // Đọc trực tiếp từ Stream trong RAM
                            using (System.Media.SoundPlayer player = new System.Media.SoundPlayer(stream))
                            {
                                player.Play(); // Phát bất đồng bộ
                            }
                        }
                        else
                        {
                            // Cảnh báo nếu gõ sai tên Resource hoặc chưa chọn Build Action = Embedded Resource
                            libs.LoggerManager.LogWarn($"Không tìm thấy Embedded Resource: {resourceName}");
                            TTSManager.Instance.EnqueueWarning("Bất thường");
                        }
                    }
                }
                catch (Exception ex)
                {
                    libs.LoggerManager.LogException(ex, "Lỗi khi phát âm thanh Embedded Resource");
                    TTSManager.Instance.EnqueueWarning("Bất thường");
                }
            }
            else
            {
                TTSManager.Instance.EnqueueWarning(Camera.name);
            }
            SetWarningTemp();
        }
        private void ForwardMetaAI(object sender, List<MetaAIResult> results)
        {
            results.ForEach(r => r.CameraInfo = Camera.name);
            SendMetaAIResult?.Invoke(this, results);

            // Add to mini timeline markers
            bool hasNewMarkers = false;
            foreach (var res in results)
            {
                // Filter: face, plate, person, human, or blacklist
                string type = res.MetaType?.ToLower();
                if (type == "face" || type == "plate" || type == "person" || type == "human" || res.IsBlackList)
                {
                    lock (_aiMarkers)
                    {
                        // Check for duplicate markers for the same tracking object to avoid clutter
                        if (!_aiMarkers.Any(m => m.TrackingObjectIndex == res.TrackingObjectIndex && m.MetaType == res.MetaType))
                        {
                            _aiMarkers.Add(res);
                            hasNewMarkers = true;
                        }
                    }
                }
            }

            if (hasNewMarkers)
            {
                // Throttling: only re-render if at least 200ms have passed
                if ((System.DateTime.Now - _lastRenderTime).TotalMilliseconds > 200)
                {
                    _lastRenderTime = System.DateTime.Now; // Update here to throttle effectively
                    this.Dispatcher.BeginInvoke(new Action(() => RenderMiniTimeline()));
                }
            }
        }
        private async void SetWarningTemp()
        {
            CameraWarning = 5;
            await System.Threading.Tasks.Task.Delay(3000); // đợi 3 giây
            CameraWarning = 0;
        }

        private void GetPlayerState(object sender, PlayerInfo info)
        {
            switch (info.Key)
            {
                case PlayerStatus.Position:
                    // Use InvariantCulture for robust parsing across locales (e.g., handling comma vs dot)
                    if (double.TryParse(info.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pos))
                        VideoPosition = pos;
                    break;

                case PlayerStatus.Duration:
                    VideoDuration = long.Parse(info.Value);
                    ShowConnectButton = Visibility.Hidden;
                    break;
                case PlayerStatus.Stop:
                    ShowConnectButton = Visibility.Visible;               
                    System.Diagnostics.Debug.WriteLine("error  ----> "+info.Value);
                    break;
                case PlayerStatus.Eof:
                    System.Diagnostics.Debug.WriteLine("Reconnectiong --->");
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => ConnectedCamera()));
                    break;
            }
        }

        private void SendGPS2Parent(object sender, PointLatLng gps)
        {
           SendGPS?.Invoke(Camera.camID, gps);
        }

        private void Player_SendGPS(object sender, GMap.NET.PointLatLng e)
        {
            
        }

        private void Player_eventPlayingMessage(object sender, string msg)
        {
            if(msg.Contains("End of Stream"))
                this.CameraStatus = Visibility.Visible;
        }

        public ViewCamera(models.Camera camera, PlayerType playerType=PlayerType.RtspPlay, 
            List<string> videFiles=null)
        {

            InitializeComponent();
            DataContext = this;          
            _videoFiles = videFiles;            
             Camera = camera;
            this.playerType = playerType;
            string camName = Camera.name;
            camName = camName.Replace("Cam", "");
            camName = camName.Trim();
            txtCameraName.Text = camName;           
            _videoPanelHandle = VideoPanel.Handle;

            Loaded += ViewCamera_Loaded;

        }

        #region Update Drag,Drop control
        private Point _dragStartPoint;
        private bool _isDragging = false;

        public static event Action<ViewCamera> CameraSelected; // Ä‘á»ƒ View chÃ­nh biáº¿t cell nÃ o Ä‘ang chá»n
        public static event Action<ViewCamera, Border> CameraDropped; // Thông báo khi tháº£
        private void btnPlayCamera_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;

            // Chá»n camera (hiá»ƒn thá»‹ border vÃ ng)
            CameraSelected?.Invoke(this);
            (sender as UIElement).CaptureMouse();
            e.Handled = true;
        }

        private void btnPlayCamera_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPos = e.GetPosition(null);
                if (!_isDragging &&
                    (Math.Abs(currentPos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                     Math.Abs(currentPos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance))
                {
                    _isDragging = true;

                    // Tạo DataObject cho drag & drop
                    System.Windows.DataObject dragData = new System.Windows.DataObject("ViewCamera", this);
                    DragDrop.DoDragDrop(this, dragData, System.Windows.DragDropEffects.Move);

                    _isDragging = false;
                }
            }
        }

        private void btnPlayCamera_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            (sender as UIElement).ReleaseMouseCapture();
        }


        #endregion Update Drag,Drop control



        public void SetSelected(bool selected)
        {
            if (selected)
                this.BorderBrush = Brushes.Yellow;
            else
                this.BorderBrush = new SolidColorBrush(Colors.OrangeRed);
        }

        public void SetTextCenterButton(string text)
        {
            centerButton.Text = text;
        }
        private void ViewCamera_Loaded(object sender, RoutedEventArgs e)
        {
            //153, 163, 164
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
        public void btn_Speaker_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                img_speaker.Source = !isSpeakerOn ? Load_Image("/images/speaker_off.png")
                    : Load_Image("/images/speaker_On.png");

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
            if (isZoomOut == false)
            {
                Grid parent = this.Parent as Grid;
                if (parent != null)
                {
                    foreach (var child in parent.Children)
                    {
                        if (child is UIElement ui)
                        {
                            ui.Visibility = Visibility.Visible;
                        }
                    }
                }
            }
            EventClosed?.Invoke(this,new object());
        }


        public void btn_Zoom_Click(object sender, RoutedEventArgs e)
        {
            Grid parent = this.Parent as Grid;
            if (parent == null) return;

            if (isZoomOut == true)
            {
                // Capture current state before maximizing
                _gridRow = Grid.GetRow(this);
                _gridCol = Grid.GetColumn(this);
                OriginalRowSpan = Grid.GetRowSpan(this);
                OriginalColSpan = Grid.GetColumnSpan(this);

                // Hide all other cameras to prevent overlap and ensure full control visibility
                foreach (var child in parent.Children)
                {
                    if (child != this && child is UIElement ui)
                    {
                        ui.Visibility = Visibility.Collapsed;
                    }
                }

                int row = Math.Max(1, parent.RowDefinitions.Count);
                int col = Math.Max(1, parent.ColumnDefinitions.Count);
                
                // Bring to front using ZIndex
                System.Windows.Controls.Panel.SetZIndex(this, 99);
                
                parent.Children.Remove(this);
                parent.Children.Add(this);

                Grid.SetRow(this, 0);
                Grid.SetColumn(this, 0);
                Grid.SetRowSpan(this, row);
                Grid.SetColumnSpan(this, col);
                IsFullTimeline = true;
            }
            else
            {
                // Restore visibility for all cameras
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
                
                // Restore ZIndex
                System.Windows.Controls.Panel.SetZIndex(this, 1);
                
                IsFullTimeline = false;
            }
            isZoomOut = !isZoomOut;

            // Always reconnect to dynamically switch between Main (fullscreen) and Sub (grid) streams
            if (!IsPlaybackMode)
            {
                ConnectedCamera();
            }
        }

        public void btn_ToggleTimeline_Click(object sender, RoutedEventArgs e)
        {
            // Removed manual toggle as per user request (Zoom-only full mode)
        }
              

        private void btnLive_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ConnectedCamera();
        }

        private void btnPlayCamera_Click(object sender, RoutedEventArgs e)
        {
            ConnectedCamera();
        }
        public void ConnectedCamera()
        {
            try
            {
                ShowConnectButton = Visibility.Hidden;
                string rtspUrl = Camera?.rtps; // Default fallback

                if (IsPlaybackMode && !string.IsNullOrEmpty(HlsUrl))
                {
                    rtspUrl = HlsUrl;
                }
                else if (IsPlaybackMode && !string.IsNullOrEmpty(PlaybackRtspUrl))
                {
                    rtspUrl = PlaybackRtspUrl;
                }
                else if (Camera != null)
                {
                    bool isFullscreen = !isZoomOut; // Because isZoomOut is flipped before calling this in btn_Zoom_Click
                    
                    if (isFullscreen)
                    {
                        if (IsAIViewMode) {
                            rtspUrl = !string.IsNullOrEmpty(Camera.RtspUrlMainAI) ? Camera.RtspUrlMainAI : 
                                     (!string.IsNullOrEmpty(Camera.RtspUrlMainRaw) ? Camera.RtspUrlMainRaw : Camera.rtps);
                            this.Camera.is_H264 = !string.IsNullOrEmpty(Camera.RtspUrlMainAI) ? Camera.IsH264MainAI : 
                                                 (!string.IsNullOrEmpty(Camera.RtspUrlMainRaw) ? Camera.IsH264MainRaw : Camera.is_H264);
                        } else {
                            rtspUrl = !string.IsNullOrEmpty(Camera.RtspUrlMainRaw) ? Camera.RtspUrlMainRaw : Camera.rtps;
                            this.Camera.is_H264 = !string.IsNullOrEmpty(Camera.RtspUrlMainRaw) ? Camera.IsH264MainRaw : Camera.is_H264;
                        }
                    }
                    else
                    {
                        if (IsAIViewMode) {
                            rtspUrl = !string.IsNullOrEmpty(Camera.RtspUrlAI) ? Camera.RtspUrlAI : 
                                     (!string.IsNullOrEmpty(Camera.RtspUrlRaw) ? Camera.RtspUrlRaw : Camera.rtps);
                            this.Camera.is_H264 = !string.IsNullOrEmpty(Camera.RtspUrlAI) ? Camera.IsH264AI : 
                                                 (!string.IsNullOrEmpty(Camera.RtspUrlRaw) ? Camera.IsH264Raw : Camera.is_H264);
                        } else {
                            rtspUrl = !string.IsNullOrEmpty(Camera.RtspUrlRaw) ? Camera.RtspUrlRaw : Camera.rtps;
                            this.Camera.is_H264 = !string.IsNullOrEmpty(Camera.RtspUrlRaw) ? Camera.IsH264Raw : Camera.is_H264;
                        }
                    }
                }

                // Determine stream mode badge
                if (rtspUrl == Camera.RtspUrlMainRaw || rtspUrl == Camera.RtspUrlMainAI)
                    Camera.ActiveStreamMode = "M";
                else if (rtspUrl == Camera.RtspUrlRaw || rtspUrl == Camera.RtspUrlAI)
                    Camera.ActiveStreamMode = "S";
                else
                    Camera.ActiveStreamMode = "M"; // fallback single-stream = Main

                StreamModeChanged?.Invoke(this, Camera.ActiveStreamMode);

                InitPipeline(rtsp: rtspUrl);
                Player.player.SetState(State.Playing);
            }catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi ConnectedCamera");
                System.Diagnostics.Debug.WriteLine("Connect camera fail: " + ex.Message);
            }
        }


        public void SetSegments(List<ApiManager.PlaybackVideoInfo> segments, System.DateTime start, System.DateTime end)
        {
            if (segments == null || segments.Count == 0)
            {
                _segments = new List<ApiManager.PlaybackVideoInfo>();
                _segmentVisualOffsets = new List<double>();
                _totalDurationSeconds = 0;
                RenderMiniTimeline();
                return;
            }

            _segments = segments.OrderBy(s => s.StartTime).ToList();
            _segmentVisualOffsets = new List<double>();
            _totalDurationSeconds = 0;

            foreach (var seg in _segments)
            {
                _segmentVisualOffsets.Add(_totalDurationSeconds);
                _totalDurationSeconds += seg.Duration;
            }

            // The Search Time markers (start/end) are now ignored as per user request.
            // Timeline range is strictly the available video duration.
            _timelineStart = _segments[0].StartTime.Kind == DateTimeKind.Utc ? _segments[0].StartTime.ToLocalTime() : _segments[0].StartTime;
            _timelineEnd = _timelineStart.AddSeconds(_totalDurationSeconds);

            // Smart Jump: if playhead is at 0, jump to the start
            if (IsPlaybackMode && VideoPosition <= 0.1)
            {
                VideoPosition = 0;
            }

            RenderMiniTimeline();
        }

        private void RenderMiniTimeline()
        {
            if (_totalDurationSeconds <= 0)
            {
                this.Dispatcher.BeginInvoke(new Action(() => canvasMiniTimeline.Children.Clear()));
                return;
            }

            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                double width = canvasMiniTimeline.ActualWidth;
                double height = canvasMiniTimeline.ActualHeight;
                if (width <= 0) width = 200;

                canvasMiniTimeline.Children.Clear();

                // 1. Search window background is no longer needed since we only show video range.
                // But we draw a base track for contrast.
                if (IsFullTimeline)
                {
                    Rectangle rectTrack = new Rectangle
                    {
                        Fill = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                        Width = width, Height = 18, Opacity = 0.8
                    };
                    Canvas.SetLeft(rectTrack, 0); Canvas.SetTop(rectTrack, 22);
                    canvasMiniTimeline.Children.Add(rectTrack);
                }

                // 2. Draw Segments (Concatenated)
                for (int i = 0; i < _segments.Count; i++)
                {
                    var seg = _segments[i];
                    double visualStart = _segmentVisualOffsets[i];
                    
                    double x = (visualStart / _totalDurationSeconds) * width;
                    double w = (seg.Duration / _totalDurationSeconds) * width;

                    Rectangle rect = new Rectangle
                    {
                        Fill = new SolidColorBrush(Color.FromRgb(0, 120, 255)),
                        Width = Math.Max(1, w - 0.5), // Tiny gap to show transition
                        Height = IsFullTimeline ? 18 : height,
                        Opacity = 0.9, RadiusX = 1, RadiusY = 1,
                        ToolTip = $"Real Time: {seg.StartTime.ToLocalTime():HH:mm:ss} - {seg.EndTime.ToLocalTime():HH:mm:ss}\nDuration: {seg.Duration}s"
                    };
                    Canvas.SetLeft(rect, x); Canvas.SetTop(rect, IsFullTimeline ? 22 : 0);
                    canvasMiniTimeline.Children.Add(rect);

                    // Fragment Ticks
                    if (IsFullTimeline)
                    {
                        Line tickFile = new Line { X1 = x, X2 = x, Y1 = 20, Y2 = 40, Stroke = Brushes.White, StrokeThickness = 0.5, Opacity = 0.3 };
                        canvasMiniTimeline.Children.Add(tickFile);
                    }
                }

                // 3. Draw Axis (Relative/Duration labels)
                if (IsFullTimeline)
                {
                    int tickCount = 10;
                    for (int i = 0; i <= tickCount; i++)
                    {
                        double xPos = (width / tickCount) * i;
                        Line tick = new Line { X1 = xPos, X2 = xPos, Y1 = 12, Y2 = 22, Stroke = Brushes.Gray, StrokeThickness = 1 };
                        canvasMiniTimeline.Children.Add(tick);

                        double seconds = (_totalDurationSeconds / tickCount) * i;
                        TimeSpan ts = TimeSpan.FromSeconds(seconds);
                        TextBlock lbl = new TextBlock { 
                            Text = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss"), 
                            Foreground = Brushes.LightGray, FontSize = 8.5 
                        };
                        Canvas.SetLeft(lbl, Math.Max(2, xPos - 15)); Canvas.SetTop(lbl, 0);
                        canvasMiniTimeline.Children.Add(lbl);
                    }
                }

                // 4. Draw AI Event Markers (Must map back to visual offset)
                RenderAiMarkers(width, height);
            }));
        }

        private void RenderAiMarkers(double width, double height)
        {
            List<MetaAIResult> markersToDraw;
            lock (_aiMarkers) { markersToDraw = new List<MetaAIResult>(_aiMarkers); }

            foreach (var ai in markersToDraw)
            {
                if (System.DateTime.TryParse(ai.TimeStamp, out System.DateTime eventTime))
                {
                    double visualX = TimeToX(eventTime.ToLocalTime());
                    if (visualX >= 0 && visualX <= width)
                    {
                        Rectangle marker = new Rectangle { Fill = Brushes.Yellow, Width = 2, Height = height > 0 ? height : 14, ToolTip = $"{ai.MetaType}: {ai.Caption} @ {ai.TimeStamp}" };
                        Canvas.SetLeft(marker, visualX);
                        canvasMiniTimeline.Children.Add(marker);
                    }
                }
            }
        }


        private void UpdateMiniPlayhead()
        {
            if (_totalDurationSeconds <= 0) return;

            // VideoPosition is in seconds (converted by RtspPlayer timer)
            double currentOffsetSeconds = VideoPosition;

            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                double width = canvasMiniTimeline.ActualWidth;
                if (width <= 0) return;

                double x = (currentOffsetSeconds / _totalDurationSeconds) * width;
                Canvas.SetLeft(miniPlayhead, Math.Min(x, width - 2));
            }));
        }

        private void canvasMiniTimeline_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderMiniTimeline();
            UpdateMiniPlayhead();
            UpdateManualMarkers();
        }

        private void canvasMiniTimeline_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastTimelineClickX = e.GetPosition(canvasMiniTimeline).X;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDraggingTimeline = true;
                canvasMiniTimeline.CaptureMouse();
                HandleTimelineSeek(_lastTimelineClickX);
            }
        }

        private void canvasMiniTimeline_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingTimeline)
            {
                HandleTimelineSeek(e.GetPosition(canvasMiniTimeline).X);
            }
        }

        private void canvasMiniTimeline_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingTimeline)
            {
                _isDraggingTimeline = false;
                canvasMiniTimeline.ReleaseMouseCapture();
            }
        }

        private System.DateTime XToTime(double x)
        {
            double width = canvasMiniTimeline.ActualWidth;
            if (width <= 0 || _totalDurationSeconds <= 0 || _segments == null || _segments.Count == 0) return _timelineStart;
            
            double offset = (x / width) * _totalDurationSeconds;
            
            // Find which segment this offset falls into
            for (int i = 0; i < _segments.Count; i++)
            {
                double start = _segmentVisualOffsets[i];
                double end = start + _segments[i].Duration;
                
                if (offset >= start && offset <= end)
                {
                    return (_segments[i].StartTime.Kind == DateTimeKind.Utc ? _segments[i].StartTime.ToLocalTime() : _segments[i].StartTime)
                           .AddSeconds(offset - start);
                }
                
                if (i < _segments.Count - 1 && offset < _segmentVisualOffsets[i+1])
                {
                    // Between segments, return start of next
                    return _segments[i+1].StartTime.Kind == DateTimeKind.Utc ? _segments[i+1].StartTime.ToLocalTime() : _segments[i+1].StartTime;
                }
            }
            
            return _timelineEnd;
        }

        private double TimeToX(System.DateTime time)
        {
            double width = canvasMiniTimeline.ActualWidth;
            if (width <= 0 || _totalDurationSeconds <= 0 || _segments == null || _segments.Count == 0) return 0;

            System.DateTime localTime = time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;

            // Find segment containing this time or the one right after
            for (int i = 0; i < _segments.Count; i++)
            {
                var seg = _segments[i];
                System.DateTime segStart = seg.StartTime.Kind == DateTimeKind.Utc ? seg.StartTime.ToLocalTime() : seg.StartTime;
                System.DateTime segEnd = seg.EndTime.Kind == DateTimeKind.Utc ? seg.EndTime.ToLocalTime() : seg.EndTime;

                if (localTime >= segStart && localTime <= segEnd)
                {
                    double visualOffset = _segmentVisualOffsets[i] + (localTime - segStart).TotalSeconds;
                    return (visualOffset / _totalDurationSeconds) * width;
                }
                
                if (localTime < segStart)
                {
                     // Before this segment, snap to its start
                     return (_segmentVisualOffsets[i] / _totalDurationSeconds) * width;
                }
            }

            return width;
        }

        private void HandleTimelineSeek(double x)
        {
            double width = canvasMiniTimeline.ActualWidth;
            if (width <= 0 || Player == null || _totalDurationSeconds <= 0) return;

            double ratio = Math.Max(0, Math.Min(1, x / width));
            double seekSec = ratio * _totalDurationSeconds;

            // Throttling: only seek every 150ms during dragging
            if ((System.DateTime.Now - _lastSeekInteractionTime).TotalMilliseconds < SeekThrottleMs)
                return;

            _lastSeekInteractionTime = System.DateTime.Now;
            
            // Handle local Seek to Position mapping
            // Note: Since the player uses a concatenated stream, we simple use the linear offset.
            long seekPosNs = (long)(seekSec * 1000000000L); 

            // Seek player on background thread
            System.Threading.Tasks.Task.Run(() => {
                Player?.SeekTo(seekPosNs);
            });
            
            // Update UI immediately (must be on UI thread)
            this.Dispatcher.Invoke(() => {
                Canvas.SetLeft(miniPlayhead, Math.Max(0, Math.Min(x, width)));
            });
        }

        private void MenuSetStart_Click(object sender, RoutedEventArgs e)
        {
            SelectionStart = XToTime(_lastTimelineClickX);
            UpdateManualMarkers();
        }

        private void MenuSetEnd_Click(object sender, RoutedEventArgs e)
        {
            SelectionEnd = XToTime(_lastTimelineClickX);
            UpdateManualMarkers();
        }


        private void MenuCancelSelection_Click(object sender, RoutedEventArgs e)
        {
            SelectionStart = null;
            SelectionEnd = null;
            UpdateManualMarkers();
        }

        private void MenuDownload_Click(object sender, RoutedEventArgs e)
        {
            if (SelectionStart.HasValue && SelectionEnd.HasValue)
            {
                var start = SelectionStart.Value < SelectionEnd.Value ? SelectionStart.Value : SelectionEnd.Value;
                var end = SelectionStart.Value < SelectionEnd.Value ? SelectionEnd.Value : SelectionStart.Value;
                
                // Trigger download via SmartDownloadWindow
                var downloadWindow = new SmartDownloadWindow(new List<string> { Camera.camID }, start, end);
                downloadWindow.Owner = System.Windows.Window.GetWindow(this);
                downloadWindow.ShowDialog();
            }
            else
            {
                System.Windows.MessageBox.Show("Vui lòng chá»n Ä‘áº§y Ä‘á»§ Ä‘iá»ƒm báº¯t Ä‘áº§u vÃ  káº¿t thÃºc.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateManualMarkers()
        {
            double width = canvasMiniTimeline.ActualWidth;
            if (width <= 0 || _totalDurationSeconds <= 0) return;

            if (SelectionStart.HasValue && IsFullTimeline)
            {
                double x = TimeToX(SelectionStart.Value);
                txtStartMarker.Visibility = Visibility.Visible;
                Canvas.SetLeft(txtStartMarker, x);
                Canvas.SetTop(txtStartMarker, 0);

                txtSelectionStart.Text = SelectionStart.Value.ToString("HH:mm:ss");
                borderStart.Visibility = Visibility.Visible;
                Canvas.SetLeft(borderStart, x - 20);
                Canvas.SetTop(borderStart, 28);
            }
            else
            {
                txtStartMarker.Visibility = Visibility.Collapsed;
                borderStart.Visibility = Visibility.Collapsed;
            }

            if (SelectionEnd.HasValue && IsFullTimeline)
            {
                double x = TimeToX(SelectionEnd.Value);
                txtEndMarker.Visibility = Visibility.Visible;
                Canvas.SetLeft(txtEndMarker, x - 10);
                Canvas.SetTop(txtEndMarker, 0);

                txtSelectionEnd.Text = SelectionEnd.Value.ToString("HH:mm:ss");
                borderEnd.Visibility = Visibility.Visible;
                Canvas.SetLeft(borderEnd, x - 20);
                Canvas.SetTop(borderEnd, 28);
            }
            else
            {
                txtEndMarker.Visibility = Visibility.Collapsed;
                borderEnd.Visibility = Visibility.Collapsed;
            }

            if (SelectionStart.HasValue && SelectionEnd.HasValue)
            {
                double x1 = TimeToX(SelectionStart.Value);
                double x2 = TimeToX(SelectionEnd.Value);
                double left = Math.Min(x1, x2);
                double rectWidth = Math.Abs(x1 - x2);

                rectSelection.Visibility = Visibility.Visible;
                Canvas.SetLeft(rectSelection, left);
                rectSelection.Width = rectWidth;
                rectSelection.Height = TimelineHeight;
            }
            else
            {
                rectSelection.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Calculates the cumulative duration (in nanoseconds) from the start of the stream 
        /// up to the specified wall-clock time. Supports gaps by flattening them.
        /// </summary>
        public long GetVisualOffsetNs(System.DateTime time)
        {
            if (_totalDurationSeconds <= 0 || _segments == null || _segments.Count == 0) return 0;

            System.DateTime localTime = time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;
            double cumulativeSec = 0;

            foreach (var seg in _segments)
            {
                System.DateTime segStart = seg.StartTime.Kind == DateTimeKind.Utc ? seg.StartTime.ToLocalTime() : seg.StartTime;
                System.DateTime segEnd = seg.EndTime.Kind == DateTimeKind.Utc ? seg.EndTime.ToLocalTime() : seg.EndTime;

                if (localTime < segStart)
                {
                    // Time is before this segment, return current cumulative start
                    return (long)(cumulativeSec * 1000000000L);
                }

                if (localTime >= segStart && localTime <= segEnd)
                {
                    // Time is inside this segment
                    double offsetInSeg = (localTime - segStart).TotalSeconds;
                    return (long)((cumulativeSec + offsetInSeg) * 1000000000L);
                }

                cumulativeSec += seg.Duration;
            }

            return (long)(cumulativeSec * 1000000000L);
        }

        public void Dispose()
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

            _videoPanelHandle = IntPtr.Zero;
            _segments?.Clear();
            _aiMarkers?.Clear();
            
            // Unsubscribe from any other internal events if needed
            this.SendGPS = null;
            this.SendMetaAIResult = null;
            this.EventClosed = null;
        }
    }
}



















