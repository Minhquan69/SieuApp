using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class LiveTile_v3 : UserControl, IDisposable
    {
        private readonly DispatcherTimer _retryTimer;
        private readonly DispatcherTimer _connectTimeoutTimer;
        private readonly DispatcherTimer _hideActionsTimer;
        private bool _disposed;
        private bool _changingStream;
        private bool _actionsPinned;
        private bool _fullscreenMode;
        private Storyboard _loadingStoryboard;

        public LiveTile_v3()
        {
            InitializeComponent();
            // Match the web tile controls: compact square actions and a
            // clearly destructive red disconnect action.
            ConnectButton.Width = DisconnectButton.Width = 32;
            ConnectButton.Height = DisconnectButton.Height = 32;
            DisconnectButton.Background = (Brush)FindResource("VmsErrorBrush_v3");
            DisconnectButton.BorderBrush = (Brush)FindResource("VmsErrorBrush_v3");
            DisconnectButton.Foreground = (Brush)FindResource("VmsTextInverseBrush_v3");
            _loadingStoryboard = new Storyboard();
            var rotation = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1))) { RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(rotation, LoadingRotation);
            Storyboard.SetTargetProperty(rotation, new PropertyPath(RotateTransform.AngleProperty));
            _loadingStoryboard.Children.Add(rotation);
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _retryTimer.Tick += RetryTimer_Tick;
            _connectTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _connectTimeoutTimer.Tick += ConnectTimeoutTimer_Tick;
            _hideActionsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _hideActionsTimer.Tick += HideActionsTimer_Tick;
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

        public void SetFullscreenVisibility(bool visible)
        {
            if (visible)
            {
                CameraBadgePopup.IsOpen = Slot != null && Slot.Camera != null;
                return;
            }

            // Popup controls are separate native windows because the video
            // renderer uses WindowsFormsHost. Collapsing the tile alone does
            // not close them, so explicitly close overlays for hidden tiles.
            ActionPopup.IsOpen = false;
            CameraBadgePopup.IsOpen = false;
        }

        public void SetFullscreenMode(bool active)
        {
            _fullscreenMode = active;
            _actionsPinned = active;
            if (active)
                ShowActions();
            else
            {
                _actionsPinned = false;
                HideActions();
            }
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
            StateChanged?.Invoke(this, EventArgs.Empty);
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
            StateChanged?.Invoke(this, EventArgs.Empty);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void RequestDisconnect()
        {
            _retryTimer.Stop();
            _connectTimeoutTimer.Stop();
            Player.RequestDisconnect();
            if (Slot != null)
            {
                Slot.State = Slot.Camera == null ? LiveConnectionState_v3.Empty : LiveConnectionState_v3.Disconnecting;
                Slot.ErrorMessage = null;
                Slot.RetryCount = 0;
            }
            RefreshVisuals();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async System.Threading.Tasks.Task DisconnectInBackgroundAsync()
        {
            _retryTimer.Stop();
            _connectTimeoutTimer.Stop();
            Player.RequestDisconnect();
            await System.Threading.Tasks.Task.Run(() => Player.DisposePipelineInBackground());
            if (_disposed) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Slot != null)
                {
                    Slot.State = Slot.Camera == null ? LiveConnectionState_v3.Empty : LiveConnectionState_v3.Offline;
                    Slot.ErrorMessage = null;
                    Slot.RetryCount = 0;
                }
                Player.SetDisconnectedStatus();
                RefreshVisuals();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }));
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

        private void ShowActions()
        {
            if (ActionBar.Visibility != Visibility.Visible) return;
            _hideActionsTimer.Stop();
            ActionPopup.IsOpen = true;
            ActionBar.Opacity = 1;
            ActionBar.IsHitTestVisible = true;
            Dispatcher.BeginInvoke(new Action(PositionActionPopup), DispatcherPriority.Loaded);
        }

        private void PositionActionPopup()
        {
            if (!ActionPopup.IsOpen || TileBorder.ActualWidth <= 0 || TileBorder.ActualHeight <= 0) return;
            var left = Math.Max(0, (TileBorder.ActualWidth - ActionBar.ActualWidth) / 2.0);
            var top = Math.Max(0, TileBorder.ActualHeight - ActionBar.ActualHeight - 10.0);
            ActionPopup.HorizontalOffset = left;
            ActionPopup.VerticalOffset = top;
        }

        private void TileBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ActionPopup.IsOpen)
                Dispatcher.BeginInvoke(new Action(PositionActionPopup), DispatcherPriority.Loaded);
        }

        private void HideActions()
        {
            if (_actionsPinned || _fullscreenMode) return;
            _hideActionsTimer.Stop();
            ActionPopup.IsOpen = false;
            ActionBar.Opacity = 0;
            ActionBar.IsHitTestVisible = false;
        }

        private void ScheduleHideActions()
        {
            if (_actionsPinned) return;
            _hideActionsTimer.Stop();
            _hideActionsTimer.Start();
        }

        private void HideActionsTimer_Tick(object sender, EventArgs e)
        {
            _hideActionsTimer.Stop();
            if (!_actionsPinned && !TileBorder.IsMouseOver && !ActionBar.IsMouseOver)
                HideActions();
        }

        private void TileBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowActions();
        }

        private void TileBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            ScheduleHideActions();
        }

        private void ActionBar_MouseEnter(object sender, MouseEventArgs e)
        {
            _hideActionsTimer.Stop();
            ShowActions();
        }

        private void ActionBar_MouseLeave(object sender, MouseEventArgs e)
        {
            ScheduleHideActions();
        }

        private void TileBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Clicking the video pins the web-style action bar. Clicking a
            // button must remain reserved for that button's command.
            var current = e.OriginalSource as DependencyObject;
            while (current != null && current != TileBorder)
            {
                if (current is ButtonBase || current is ComboBox)
                    return;
                current = VisualTreeHelper.GetParent(current);
            }

            _actionsPinned = !_actionsPinned;
            if (_actionsPinned) ShowActions();
            else HideActions();
        }
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
            CameraBadgePopup.IsOpen = !empty;
            ActionBar.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (empty)
            {
                _actionsPinned = false;
                HideActions();
            }
            ErrorOverlay.Visibility = !empty && Slot.HasError && Slot.State == LiveConnectionState_v3.Error ? Visibility.Visible : Visibility.Collapsed;
            var loading = !empty && Slot != null && (Slot.State == LiveConnectionState_v3.Connecting || Slot.State == LiveConnectionState_v3.Disconnecting || Slot.State == LiveConnectionState_v3.Retrying);
            LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            LoadingText.Text = Slot != null && Slot.State == LiveConnectionState_v3.Disconnecting ? "Đang ngắt kết nối..." : Slot != null && Slot.State == LiveConnectionState_v3.Retrying ? "Đang thử lại " + Slot.RetryCount + "/3..." : "Đang kết nối...";
            if (loading) _loadingStoryboard.Begin(this, true); else _loadingStoryboard.Remove(this);
            CameraName.Text = empty ? string.Empty : Slot.DisplayName;
            ErrorText.Text = "Kiểm tra mạng, cấu hình camera hoặc máy chủ phát trực tiếp.";
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource(Slot != null && Slot.State == LiveConnectionState_v3.Connected
                ? "VmsSuccessBrush_v3" : Slot != null && Slot.HasError ? "VmsErrorBrush_v3" : "VmsOfflineBrush_v3");
            ConnectButton.Visibility = !empty && (Slot == null || !Slot.IsConnected) ? Visibility.Visible : Visibility.Collapsed;
            DisconnectButton.Visibility = !empty && Slot != null && (Slot.IsConnected || Slot.State == LiveConnectionState_v3.Connecting) ? Visibility.Visible : Visibility.Collapsed;
            StreamSelector.ItemsSource = empty || Slot.Camera.Streams == null ? null : Slot.Camera.Streams;
            StreamSelector.SelectedItem = empty ? null : Slot.SelectedStream;
            // Stream selection is managed by the camera/session, while the
            // tile action bar mirrors the web actions (connect, disconnect,
            // fullscreen and remove) only.
            StreamSelector.Visibility = Visibility.Collapsed;
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
            _hideActionsTimer.Stop();
            _hideActionsTimer.Tick -= HideActionsTimer_Tick;
            ActionPopup.IsOpen = false;
            CameraBadgePopup.IsOpen = false;
            Player.PlaybackStateChanged -= Player_PlaybackStateChanged;
            MetadataOverlay.Dispose();
            Player.Dispose();
        }
    }
}
