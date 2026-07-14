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
            public bool HasVideo { get; set; }
            public System.DateTime RealStartTime { get; set; }
        }

        public ViewCameraPlayback(models.Camera camera)
        {
            InitializeComponent();
            DataContext = this;          
            Camera = camera;
            
            string camName = Camera.name;
            camName = camName.Replace("Cam", "");
            camName = camName.Trim();
            txtCameraName.Text = camName;           
            _videoPanelHandle = VideoPanel.Handle;

            Loaded += ViewCameraPlayback_Loaded;
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
                TTSManager.Instance.EnqueueWarning("Bất thường");
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
                ShowConnectButton = Visibility.Hidden;
                InitPipeline();
                Player.player.SetState(State.Playing);
                _isPlaying = true;
                txtPlayPauseIcon.Text = "\uE769"; // Pause icon
                UpdateSpeedDisplay();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi ConnectedCamera");
            }
        }

        private void ViewCameraPlayback_Loaded(object sender, RoutedEventArgs e)
        {
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
                img_speaker.Source = !isSpeakerOn ? Load_Image("/images/speaker_off.png") : Load_Image("/images/speaker_On.png");
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
                txtPlayPauseIcon.Text = "\uE768"; // Play icon
            }
            else
            {
                if (_currentPlaybackRate != 1.0f)
                {
                    _currentPlaybackRate = 1.0f;
                    ApplyPlaybackRate();
                }
                Player.Playing();
                txtPlayPauseIcon.Text = "\uE769"; // Pause icon
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
            if (txtSpeed != null)
            {
                txtSpeed.Text = $"{_currentPlaybackRate:F1}x";
            }
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
            
            System.Text.RegularExpressions.Regex dateRegex = new System.Text.RegularExpressions.Regex(@"(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})");

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
                        if (System.DateTime.TryParseExact(match.Value, "yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out System.DateTime parsedTime))
                        {
                            realStartTime = parsedTime;
                        }
                    }

                    // This is a segment URL
                    videoSegments.Add(new PlaybackSegment
                    {
                        Duration = currentDuration,
                        Url = line,
                        StartOffset = 0, // Will be calculated
                        HasVideo = true,
                        RealStartTime = realStartTime != System.DateTime.MinValue ? realStartTime : _searchStartTime.AddSeconds(_totalDurationSeconds)
                    });
                    _totalDurationSeconds += currentDuration;
                    currentDuration = 0;
                }
            }
            
            // Sort segments by RealStartTime just in case
            videoSegments = videoSegments.OrderBy(s => s.RealStartTime).ToList();
            
            // Create gaps
            double currentRealOffset = 0;
            System.DateTime currentRealTime = _searchStartTime;
            
            foreach (var vSeg in videoSegments)
            {
                if (vSeg.RealStartTime > currentRealTime)
                {
                    double gapDuration = (vSeg.RealStartTime - currentRealTime).TotalSeconds;
                    if (gapDuration > 10) // 10 seconds threshold for a gap
                    {
                        _segments.Add(new PlaybackSegment
                        {
                            Duration = gapDuration,
                            StartOffset = currentRealOffset,
                            HasVideo = false,
                            RealStartTime = currentRealTime
                        });
                        currentRealOffset += gapDuration;
                        currentRealTime = currentRealTime.AddSeconds(gapDuration);
                    }
                }
                
                vSeg.StartOffset = currentRealOffset;
                _segments.Add(vSeg);
                
                currentRealOffset += vSeg.Duration;
                currentRealTime = vSeg.RealStartTime.AddSeconds(vSeg.Duration);
            }
            
            if (currentRealTime < _searchEndTime)
            {
                double finalGap = (_searchEndTime - currentRealTime).TotalSeconds;
                if (finalGap > 0)
                {
                    _segments.Add(new PlaybackSegment
                    {
                        Duration = finalGap,
                        StartOffset = currentRealOffset,
                        HasVideo = false,
                        RealStartTime = currentRealTime
                    });
                }
            }

            RenderMiniTimeline();
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
            if (Player != null)
            {
                Player.Dispose();
                Player = null;
            }
        }
    }
}
