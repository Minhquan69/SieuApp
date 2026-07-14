using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class LiveTile_v3 : UserControl, IDisposable
    {
        private readonly DispatcherTimer _retryTimer;
        private readonly DispatcherTimer _connectTimeoutTimer;
        private bool _disposed;
        private bool _changingStream;
        private Storyboard _loadingStoryboard;

        public LiveTile_v3()
        {
            InitializeComponent();
            _loadingStoryboard = new Storyboard();
            var rotation = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1))) { RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(rotation, LoadingRotation);
            Storyboard.SetTargetProperty(rotation, new PropertyPath(RotateTransform.AngleProperty));
            _loadingStoryboard.Children.Add(rotation);
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _retryTimer.Tick += RetryTimer_Tick;
            _connectTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _connectTimeoutTimer.Tick += ConnectTimeoutTimer_Tick;
            Player.PlaybackStateChanged += Player_PlaybackStateChanged;
        }

        public LiveSlotViewModel_v3 Slot { get; private set; }
        public event EventHandler RemoveRequested;
        public event EventHandler FullscreenRequested;
        public event EventHandler StateChanged;

        public void Bind(LiveSlotViewModel_v3 slot)
        {
            var previousCamera = Slot == null ? null : Slot.Camera;
            var nextCamera = slot == null ? null : slot.Camera;
            if (previousCamera != null && !ReferenceEquals(previousCamera, nextCamera))
                Player.Disconnect();
            Slot = slot;
            DataContext = slot;
            RefreshVisuals();
            if (slot == null || slot.Camera == null)
            {
                Player.Camera = null;
                UpdateMetadataSubscription();
                return;
            }
            Player.SelectedStream = slot.SelectedStream;
            Player.Camera = slot.Camera;
            UpdateMetadataSubscription();
        }

        public async System.Threading.Tasks.Task ConnectAsync()
        {
            if (_disposed || Slot == null || Slot.Camera == null) return;
            _retryTimer.Stop();
            _connectTimeoutTimer.Stop();
            Slot.State = LiveConnectionState_v3.Connecting;
            Slot.ErrorMessage = null;
            RefreshVisuals();
            StateChanged?.Invoke(this, EventArgs.Empty);
            Player.SelectedStream = Slot.SelectedStream;
            await Player.ReconnectAsync();
        }

        public void Disconnect()
        {
            _retryTimer.Stop();
            _connectTimeoutTimer.Stop();
            Player.Disconnect();
            if (Slot != null)
            {
                Slot.State = Slot.Camera == null ? LiveConnectionState_v3.Empty : LiveConnectionState_v3.Offline;
                Slot.ErrorMessage = null;
                Slot.RetryCount = 0;
            }
            RefreshVisuals();
        }

        public System.Threading.Tasks.Task DisconnectAsync()
        {
            _retryTimer.Stop();
            _connectTimeoutTimer.Stop();
            // GStreamer/WindowsFormsHost owns UI handles; disconnect must run on
            // the WPF dispatcher thread. The bulk API is concurrent, while this
            // UI cleanup remains thread-safe and non-blocking at the network layer.
            Player.Disconnect();
            if (Slot != null)
            {
                Slot.State = Slot.Camera == null ? LiveConnectionState_v3.Empty : LiveConnectionState_v3.Offline;
                Slot.ErrorMessage = null;
                Slot.RetryCount = 0;
            }
            RefreshVisuals();
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void RequestDisconnect()
        {
            _retryTimer.Stop();
            _connectTimeoutTimer.Stop();
            Player.RequestDisconnect();
            if (Slot != null) Slot.State = Slot.Camera == null ? LiveConnectionState_v3.Empty : LiveConnectionState_v3.Offline;
        }

        private void Player_PlaybackStateChanged(object sender, WhepPlaybackStateChangedEventArgs_v3 e)
        {
            if (Slot == null || _disposed) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.State == WhepPlaybackState_v3.Connecting)
                {
                    Slot.State = LiveConnectionState_v3.Connecting;
                    _connectTimeoutTimer.Stop();
                    _connectTimeoutTimer.Start();
                }
                if (e.State == WhepPlaybackState_v3.Playing)
                {
                    Slot.State = LiveConnectionState_v3.Connected;
                    Slot.ErrorMessage = null;
                    Slot.RetryCount = 0;
                    _retryTimer.Stop();
                    _connectTimeoutTimer.Stop();
                }
                if (e.State == WhepPlaybackState_v3.Error)
                {
                    _connectTimeoutTimer.Stop();
                    Slot.ErrorMessage = e.Message;
                    if (Slot.RetryCount < 3)
                    {
                        Slot.RetryCount++;
                        Slot.State = LiveConnectionState_v3.Retrying;
                        _retryTimer.Interval = TimeSpan.FromSeconds(Math.Min(8, Math.Pow(2, Slot.RetryCount)));
                        _retryTimer.Start();
                    }
                    else Slot.State = LiveConnectionState_v3.Error;
                }
                RefreshVisuals();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }));
        }

        private async void RetryTimer_Tick(object sender, EventArgs e)
        {
            _retryTimer.Stop();
            await ConnectAsync();
        }

        private void ConnectTimeoutTimer_Tick(object sender, EventArgs e)
        {
            _connectTimeoutTimer.Stop();
            if (Slot == null || Slot.Camera == null || Slot.State != LiveConnectionState_v3.Connecting) return;
            Slot.ErrorMessage = "The stream connected but no video frame was received.";
            Slot.State = LiveConnectionState_v3.Error;
            Player.Disconnect();
            RefreshVisuals();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void Connect_Click(object sender, RoutedEventArgs e) { await ConnectAsync(); }
        private void Disconnect_Click(object sender, RoutedEventArgs e) { Disconnect(); }
        private void Remove_Click(object sender, RoutedEventArgs e) { RemoveRequested?.Invoke(this, EventArgs.Empty); }
        private void Fullscreen_Click(object sender, RoutedEventArgs e) { FullscreenRequested?.Invoke(this, EventArgs.Empty); }
        private async void Retry_Click(object sender, RoutedEventArgs e)
        {
            if (Slot != null) { Slot.RetryCount = 0; Slot.ErrorMessage = null; }
            await ConnectAsync();
        }

        private async void StreamSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_changingStream || Slot == null || Slot.Camera == null || StreamSelector.SelectedItem == null) return;
            Slot.SelectedStream = StreamSelector.SelectedItem as CameraStreamInfo;
            Player.SelectedStream = Slot.SelectedStream;
            UpdateMetadataSubscription();
            await ConnectAsync();
        }

        private void UpdateMetadataSubscription()
        {
            var isAi = Slot != null && Slot.Camera != null &&
                ((Slot.SelectedStream != null && Slot.SelectedStream.IsAiMode == true) ||
                 string.Equals(Slot.Camera.type, "ai_processed", StringComparison.OrdinalIgnoreCase));
            MetadataOverlay.Visibility = isAi ? Visibility.Visible : Visibility.Collapsed;
            // Metadata messages are keyed by the logical camera ID; playback
            // may use a different relay path when it connects to the broker.
            MetadataOverlay.Subscribe(isAi ? Slot.Camera.camID : null);
        }

        private void RefreshVisuals()
        {
            _changingStream = true;
            var empty = Slot == null || Slot.Camera == null;
            EmptyOverlay.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            CameraBadge.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            ActionBar.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            ErrorOverlay.Visibility = !empty && Slot.HasError && Slot.State == LiveConnectionState_v3.Error ? Visibility.Visible : Visibility.Collapsed;
            var loading = !empty && Slot != null && (Slot.State == LiveConnectionState_v3.Connecting || Slot.State == LiveConnectionState_v3.Retrying);
            LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            LoadingText.Text = Slot != null && Slot.State == LiveConnectionState_v3.Retrying ? "Đang thử lại " + Slot.RetryCount + "/3..." : "Đang kết nối...";
            if (loading) _loadingStoryboard.Begin(this, true); else _loadingStoryboard.Remove(this);
            CameraName.Text = empty ? string.Empty : Slot.DisplayName + " · " + Slot.StreamLabel;
            ErrorText.Text = "Kiểm tra mạng, cấu hình camera hoặc máy chủ phát trực tiếp.";
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource(Slot != null && Slot.State == LiveConnectionState_v3.Connected
                ? "VmsSuccessBrush_v3" : Slot != null && Slot.HasError ? "VmsErrorBrush_v3" : "VmsOfflineBrush_v3");
            ConnectButton.Visibility = !empty && (Slot == null || !Slot.IsConnected) ? Visibility.Visible : Visibility.Collapsed;
            DisconnectButton.Visibility = !empty && Slot != null && (Slot.IsConnected || Slot.State == LiveConnectionState_v3.Connecting) ? Visibility.Visible : Visibility.Collapsed;
            StreamSelector.ItemsSource = empty || Slot.Camera.Streams == null ? null : Slot.Camera.Streams;
            StreamSelector.SelectedItem = empty ? null : Slot.SelectedStream;
            StreamSelector.Visibility = !empty && Slot.Camera.Streams != null && Slot.Camera.Streams.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            _changingStream = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _retryTimer.Stop();
            _retryTimer.Tick -= RetryTimer_Tick;
            _connectTimeoutTimer.Stop();
            _connectTimeoutTimer.Tick -= ConnectTimeoutTimer_Tick;
            Player.PlaybackStateChanged -= Player_PlaybackStateChanged;
            MetadataOverlay.Dispose();
            Player.Dispose();
        }
    }
}
