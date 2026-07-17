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
        private readonly DispatcherTimer _loadingSpinnerTimer;
        private bool _disposed;
        private bool _changingStream;
        private bool _actionsPinned;
        private bool _fullscreenMode;
        private bool _positioningBadge;
        private int _badgeGeneration;
        private Window _ownerWindow;

        public LiveTile_v3()
        {
            InitializeComponent();
            // Popup namescopes cannot reliably resolve ElementName bindings
            // across the HwndHost boundary; bind the target explicitly.
            // Match the web tile controls: compact square actions and a
            // clearly destructive red disconnect action.
            ConnectButton.Width = DisconnectButton.Width = 28;
            ConnectButton.Height = DisconnectButton.Height = 28;
            DisconnectButton.Background = (Brush)FindResource("VmsErrorBrush_v3");
            DisconnectButton.BorderBrush = (Brush)FindResource("VmsErrorBrush_v3");
            DisconnectButton.Foreground = (Brush)FindResource("VmsTextInverseBrush_v3");
            _loadingSpinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _loadingSpinnerTimer.Tick += LoadingSpinnerTimer_Tick;
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _retryTimer.Tick += RetryTimer_Tick;
            _connectTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _connectTimeoutTimer.Tick += ConnectTimeoutTimer_Tick;
            _hideActionsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _hideActionsTimer.Tick += HideActionsTimer_Tick;
            Player.PlaybackStateChanged += Player_PlaybackStateChanged;
            Player.VideoMouseEnter += Player_VideoMouseEnter;
            Player.VideoMouseMove += Player_VideoMouseMove;
            Player.VideoMouseLeave += Player_VideoMouseLeave;
            Loaded += LiveTile_Loaded;
            Unloaded += LiveTile_Unloaded;
            if (Application.Current != null)
                Application.Current.Deactivated += Application_Deactivated;
            LayoutUpdated += LiveTile_LayoutUpdated;
        }

        private void LiveTile_Loaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (ReferenceEquals(_ownerWindow, window)) return;
            if (_ownerWindow != null)
            {
                _ownerWindow.Deactivated -= OwnerWindow_Deactivated;
                _ownerWindow.Activated -= OwnerWindow_Activated;
                _ownerWindow.LocationChanged -= OwnerWindow_LocationChanged;
                _ownerWindow.SizeChanged -= OwnerWindow_SizeChanged;
            }
            _ownerWindow = window;
            if (_ownerWindow != null)
            {
                _ownerWindow.Deactivated += OwnerWindow_Deactivated;
                _ownerWindow.Activated += OwnerWindow_Activated;
                _ownerWindow.LocationChanged += OwnerWindow_LocationChanged;
                _ownerWindow.SizeChanged += OwnerWindow_SizeChanged;
                OpenCameraBadgeIfActive();
            }
        }

        private void OpenCameraBadgeIfActive()
        {
            if (_disposed || _ownerWindow == null || !_ownerWindow.IsActive ||
                Slot == null || Slot.Camera == null || !TileBorder.IsVisible ||
                Slot.State == LiveConnectionState_v3.Empty) return;
            var generation = _badgeGeneration;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed || generation != _badgeGeneration || _ownerWindow == null ||
                    !_ownerWindow.IsActive || Slot == null || Slot.Camera == null || !TileBorder.IsVisible ||
                    Slot.State == LiveConnectionState_v3.Empty)
                    return;
                if (string.IsNullOrWhiteSpace(CameraName.Text))
                    CameraName.Text = Slot.DisplayName;
                if (string.IsNullOrWhiteSpace(CameraName.Text)) return;
                StatusDot.Visibility = Visibility.Visible;
                CameraBadge.Visibility = Visibility.Visible;
                CameraBadgePopup.Visibility = Visibility.Visible;
                CameraBadgePopup.IsOpen = true;
                PositionCameraBadgePopup();
            }), DispatcherPriority.Render);
        }

        private void HideCameraBadge(bool clearText)
        {
            CameraBadgePopup.IsOpen = false;
            CameraBadgePopup.Visibility = Visibility.Collapsed;
            CameraBadge.Visibility = Visibility.Collapsed;
            StatusDot.Visibility = Visibility.Collapsed;
            if (clearText)
                CameraName.Text = string.Empty;
        }

        private void LiveTile_Unloaded(object sender, RoutedEventArgs e)
        {
            SafeStopHideActionsTimer();
            _actionsPinned = false;
            try { ActionPopup.IsOpen = false; }
            catch (System.ComponentModel.Win32Exception) { }
            if (_ownerWindow == null) return;
            _ownerWindow.Deactivated -= OwnerWindow_Deactivated;
            _ownerWindow.Activated -= OwnerWindow_Activated;
            _ownerWindow.LocationChanged -= OwnerWindow_LocationChanged;
            _ownerWindow.SizeChanged -= OwnerWindow_SizeChanged;
            _ownerWindow = null;
        }

        private void OwnerWindow_LocationChanged(object sender, EventArgs e)
        {
            if (CameraBadgePopup.IsOpen)
                PositionCameraBadgePopup();
            if (ActionPopup.IsOpen)
                PositionActionPopup();
        }

        private void OwnerWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (CameraBadgePopup.IsOpen) PositionCameraBadgePopup();
                if (ActionPopup.IsOpen) PositionActionPopup();
            }), DispatcherPriority.Render);
        }

        private void LiveTile_LayoutUpdated(object sender, EventArgs e)
        {
            if (CameraBadgePopup.IsOpen)
                PositionCameraBadgePopup();
            if (ActionPopup.IsOpen)
                PositionActionPopup();
        }

        private void OwnerWindow_Deactivated(object sender, EventArgs e)
        {
            SafeStopHideActionsTimer();
            _actionsPinned = false;
            ActionPopup.IsOpen = false;
            HideCameraBadge(false);
        }

        private void Application_Deactivated(object sender, EventArgs e)
        {
            SafeStopHideActionsTimer();
            _actionsPinned = false;
            HideCameraBadge(false);
            ActionPopup.IsOpen = false;
            if (_ownerWindow != null)
                _ownerWindow.Topmost = false;
        }

        private void OwnerWindow_Activated(object sender, EventArgs e)
        {
            if (_disposed || Slot == null || Slot.Camera == null) return;
            OpenCameraBadgeIfActive();
            if (_fullscreenMode)
            {
                if (_ownerWindow != null) _ownerWindow.Topmost = true;
                _actionsPinned = true;
                ShowActions();
            }
        }

        public LiveSlotViewModel_v3 Slot { get; private set; }
        public event EventHandler RemoveRequested;
        public event EventHandler FullscreenRequested;
        public event EventHandler StateChanged;

        public void Bind(LiveSlotViewModel_v3 slot)
        {
            _badgeGeneration++;
            HideCameraBadge(true);
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
                HideCameraBadge(false);
                return;
            }

            // Popup controls are separate native windows because the video
            // renderer uses WindowsFormsHost. Collapsing the tile alone does
            // not close them, so explicitly close overlays for hidden tiles.
            ActionPopup.IsOpen = false;
            HideCameraBadge(false);
            if (!visible) OpenCameraBadgeIfActive();
        }

        public void SetFullscreenMode(bool active)
        {
            _fullscreenMode = active;
            _actionsPinned = active;
            FullscreenButton.ToolTip = active ? "Thu nhỏ màn hình" : "Toàn màn hình";
            FullscreenIcon.Kind = active
                ? MahApps.Metro.IconPacks.PackIconMaterialKind.FullscreenExit
                : MahApps.Metro.IconPacks.PackIconMaterialKind.Fullscreen;
            if (active)
                ShowActions();
            else
            {
                _actionsPinned = false;
                HideActions();
            }
        }

        public void RefreshPopupPlacement()
        {
            if (_disposed) return;
            var reopenActions = ActionPopup.IsOpen;
            HideCameraBadge(false);
            ActionPopup.IsOpen = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (_disposed) return;
                if (reopenActions) ShowActions();
                OpenCameraBadgeIfActive();
            }));
        }

        /// <summary>Suppresses popup overlays during a layout/fullscreen transition.</summary>
        public void HideTransientOverlays()
        {
            HideCameraBadge(false);
            ActionPopup.IsOpen = false;
            ActionBar.Opacity = 0;
            ActionBar.IsHitTestVisible = false;
        }

        public void SetVideoSurfaceVisible(bool visible)
        {
            Player.SetVideoSurfaceVisible(visible);
        }

        public async System.Threading.Tasks.Task ConnectAsync()
        {
            if (_disposed || Slot == null || Slot.Camera == null) return;
            _retryTimer.Stop();
            _connectTimeoutTimer.Stop();
            _badgeGeneration++;
            HideCameraBadge(true);
            Player.SetCameraBadge(string.Empty, false, false, false);
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
            _badgeGeneration++;
            Player.Disconnect();
            Player.SetVideoSurfaceVisible(false);
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
            _badgeGeneration++;
            // GStreamer/WindowsFormsHost owns UI handles; disconnect must run on
            // the WPF dispatcher thread. The bulk API is concurrent, while this
            // UI cleanup remains thread-safe and non-blocking at the network layer.
            Player.Disconnect();
            Player.SetVideoSurfaceVisible(false);
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
            _badgeGeneration++;
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
            _badgeGeneration++;
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
            var generation = _badgeGeneration;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // A previous GStreamer pipeline can finish asynchronously
                // after Clear All/rebind. Never let that stale callback
                // mutate the newly assigned slot or its badge.
                if (_disposed || generation != _badgeGeneration || Slot == null || Slot.Camera == null)
                    return;
                if (e.State == WhepPlaybackState_v3.Connecting)
                {
                    // WindowsFormsHost is an airspace island: if its native
                    // surface remains visible it will paint above the WPF
                    // pending overlay. Keep it hidden until Playing.
                    HideCameraBadge(true);
                    Player.SetCameraBadge(string.Empty, false, false, false);
                    Player.SetVideoSurfaceVisible(false);
                    Slot.State = LiveConnectionState_v3.Connecting;
                    CameraName.Text = Slot.DisplayName;
                    if (!string.IsNullOrWhiteSpace(CameraName.Text))
                        CameraBadge.Visibility = Visibility.Visible;
                    _connectTimeoutTimer.Stop();
                    _connectTimeoutTimer.Start();
                }
                if (e.State == WhepPlaybackState_v3.Playing)
                {
                    Player.SetVideoSurfaceVisible(true);
                    Slot.State = LiveConnectionState_v3.Connected;
                    Slot.ConnectedAtUtc = DateTime.UtcNow;
                    Slot.ErrorMessage = null;
                    Slot.RetryCount = 0;
                    _retryTimer.Stop();
                    _connectTimeoutTimer.Stop();
                }
                if (e.State == WhepPlaybackState_v3.Error)
                {
                    // WindowsFormsHost is an airspace island and cannot be
                    // covered by a WPF Border. Hide the native surface first
                    // so ErrorOverlay can actually be rendered.
                    Player.SetVideoSurfaceVisible(false);
                    _connectTimeoutTimer.Stop();
                    Slot.ErrorMessage = string.IsNullOrWhiteSpace(e.UserMessage) ? e.Message : e.UserMessage;
                    if (e.IsRetryable && Slot.RetryCount < 3)
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

        private void LoadingSpinnerTimer_Tick(object sender, EventArgs e)
        {
            if (_disposed || LoadingOverlay.Visibility != Visibility.Visible)
            {
                _loadingSpinnerTimer.Stop();
                return;
            }
            StreamLoadingRotation.Angle = (StreamLoadingRotation.Angle + 12) % 360;
        }

        private void ConnectTimeoutTimer_Tick(object sender, EventArgs e)
        {
            _connectTimeoutTimer.Stop();
            if (Slot == null || Slot.Camera == null || Slot.State != LiveConnectionState_v3.Connecting) return;
            Slot.ErrorMessage = "Đã kết nối nhưng không nhận được khung hình video từ camera.";
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
            if (_disposed || !IsLoaded || _ownerWindow == null || ActionBar.Visibility != Visibility.Visible) return;
            OpenCameraBadgeIfActive();
            SafeStopHideActionsTimer();
            ActionPopup.IsOpen = true;
            ActionBar.Opacity = 1;
            ActionBar.IsHitTestVisible = true;
            Dispatcher.BeginInvoke(new Action(PositionActionPopup), DispatcherPriority.Loaded);
        }

        private void PositionCameraBadgePopup()
        {
            if (_positioningBadge || !CameraBadgePopup.IsOpen || !IsLoaded || !TileBorder.IsVisible) return;
            var source = PresentationSource.FromVisual(TileBorder);
            if (source == null || source.CompositionTarget == null) return;
            try
            {
                _positioningBadge = true;
                // PointToScreen returns device pixels. Popup offsets are WPF
                // device-independent units, so convert through the current
                // presentation source before assigning absolute coordinates.
                var screenPoint = TileBorder.PointToScreen(new Point(7, 7));
                var dipPoint = source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
                if (Math.Abs(CameraBadgePopup.HorizontalOffset - dipPoint.X) > 0.1 ||
                    Math.Abs(CameraBadgePopup.VerticalOffset - dipPoint.Y) > 0.1)
                {
                    CameraBadgePopup.HorizontalOffset = dipPoint.X;
                    CameraBadgePopup.VerticalOffset = dipPoint.Y;
                }
            }
            catch (InvalidOperationException) { }
            finally
            {
                _positioningBadge = false;
            }
        }

        private void PositionActionPopup()
        {
            if (!ActionPopup.IsOpen || !IsLoaded || !TileBorder.IsVisible ||
                TileBorder.ActualWidth <= 0 || TileBorder.ActualHeight <= 0 ||
                ActionBar.ActualWidth <= 0 || ActionBar.ActualHeight <= 0) return;
            var source = PresentationSource.FromVisual(TileBorder);
            if (source == null || source.CompositionTarget == null) return;
            try
            {
                // Anchor the action bar to the center of the tile's bottom
                // edge, independent of the tile's previous layout/fullscreen
                // size. Popup offsets are WPF DIPs, so convert from screen
                // device pixels using the current presentation source.
                var bottomCenter = TileBorder.PointToScreen(new Point(TileBorder.ActualWidth / 2.0, TileBorder.ActualHeight));
                var dipPoint = source.CompositionTarget.TransformFromDevice.Transform(bottomCenter);
                var x = dipPoint.X - (ActionBar.ActualWidth / 2.0);
                var y = dipPoint.Y - ActionBar.ActualHeight - 10.0;
                if (Math.Abs(ActionPopup.HorizontalOffset - x) > 0.1 ||
                    Math.Abs(ActionPopup.VerticalOffset - y) > 0.1)
                {
                    ActionPopup.HorizontalOffset = x;
                    ActionPopup.VerticalOffset = y;
                }
            }
            catch (InvalidOperationException) { }
        }

        private void TileBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (CameraBadgePopup.IsOpen)
                Dispatcher.BeginInvoke(new Action(PositionCameraBadgePopup), DispatcherPriority.Render);
            if (ActionPopup.IsOpen)
                Dispatcher.BeginInvoke(new Action(PositionActionPopup), DispatcherPriority.Loaded);
        }

        private void HideActions()
        {
            if (_disposed || _actionsPinned || _fullscreenMode) return;
            SafeStopHideActionsTimer();
            ActionPopup.IsOpen = false;
            ActionBar.Opacity = 0;
            ActionBar.IsHitTestVisible = false;
        }

        private void ScheduleHideActions()
        {
            if (_disposed || !IsLoaded || _ownerWindow == null || _actionsPinned) return;
            // Native GStreamer mouse events can arrive concurrently with
            // Unloaded. DispatcherTimer may then throw Win32Exception even
            // after the guards above pass, so both operations are guarded.
            SafeStopHideActionsTimer();
            SafeStartHideActionsTimer();
        }

        private void SafeStopHideActionsTimer()
        {
            try { _hideActionsTimer.Stop(); }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }

        private void SafeStartHideActionsTimer()
        {
            try
            {
                if (!_disposed && IsLoaded && _ownerWindow != null)
                    _hideActionsTimer.Start();
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }

        private void HideActionsTimer_Tick(object sender, EventArgs e)
        {
            SafeStopHideActionsTimer();
            if (_disposed || !IsLoaded || _ownerWindow == null) return;
            if (!_actionsPinned && !TileBorder.IsMouseOver && !ActionBar.IsMouseOver)
                HideActions();
        }

        private void TileBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowActions();
        }

        private void HoverSurface_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowActions();
        }

        private void TileBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            ScheduleHideActions();
        }

        private void HoverSurface_MouseLeave(object sender, MouseEventArgs e)
        {
            ScheduleHideActions();
        }

        private void Player_VideoMouseEnter(object sender, EventArgs e)
        {
            ShowActions();
        }

        private void Player_VideoMouseMove(object sender, EventArgs e)
        {
            ShowActions();
        }

        private void Player_VideoMouseLeave(object sender, EventArgs e)
        {
            ScheduleHideActions();
        }

        private void ActionBar_MouseEnter(object sender, MouseEventArgs e)
        {
            SafeStopHideActionsTimer();
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
            var showCameraId = !empty;
            Player.SetVideoSurfaceVisible(!empty && Slot.State != LiveConnectionState_v3.Error &&
                Slot.State != LiveConnectionState_v3.Retrying &&
                Slot.State != LiveConnectionState_v3.Connecting &&
                Slot.State != LiveConnectionState_v3.Disconnecting);
            Player.SetCameraBadge(empty ? string.Empty : Slot.DisplayName, showCameraId,
                !empty && Slot.State == LiveConnectionState_v3.Connected,
                !empty && Slot.HasError && Slot.State == LiveConnectionState_v3.Error);
            EmptyOverlay.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            CameraBadge.Visibility = Visibility.Collapsed;
            // Keep an in-tile fallback for error/retrying states. The native
            // video surface is hidden in those states, so the WPF badge is
            // visible and guarantees that every selected camera still shows
            // its ID even when the stream fails.
            CameraBadgeInline.Visibility = Visibility.Collapsed;
            if (!showCameraId)
                HideCameraBadge(true);
            else
            {
                CameraBadgePopup.IsOpen = false;
            }
            ActionBar.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (empty)
            {
                _actionsPinned = false;
                HideActions();
            }
            ErrorOverlay.Visibility = !empty && Slot.HasError &&
                (Slot.State == LiveConnectionState_v3.Error || Slot.State == LiveConnectionState_v3.Retrying)
                ? Visibility.Visible : Visibility.Collapsed;
            if (!empty && (Slot.State == LiveConnectionState_v3.Error || Slot.State == LiveConnectionState_v3.Retrying))
                Player.SetVideoSurfaceVisible(false);
            var loading = !empty && Slot != null &&
                (Slot.State == LiveConnectionState_v3.Connecting || Slot.State == LiveConnectionState_v3.Disconnecting);
            LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            LoadingText.Text = Slot != null && Slot.State == LiveConnectionState_v3.Disconnecting ? "Đang ngắt kết nối..." : Slot != null && Slot.State == LiveConnectionState_v3.Retrying ? "Đang thử lại " + Slot.RetryCount + "/3..." : "Đang kết nối...";
            PendingTitle.Text = Slot != null && Slot.State == LiveConnectionState_v3.Disconnecting
                ? "Đang ngắt kết nối..."
                : "Đang kết nối stream...";
            PendingStreamText.Text = empty ? string.Empty : Slot.DisplayName + " - " + Slot.StreamLabel;
            if (loading)
            {
                if (!_loadingSpinnerTimer.IsEnabled) _loadingSpinnerTimer.Start();
            }
            else
            {
                _loadingSpinnerTimer.Stop();
                StreamLoadingRotation.Angle = 0;
            }
            CameraName.Text = empty ? string.Empty : Slot.DisplayName;
            CameraNameInline.Text = empty ? string.Empty : Slot.DisplayName;
            if (showCameraId && !string.IsNullOrWhiteSpace(CameraName.Text))
            {
                StatusDot.Visibility = Visibility.Visible;
                CameraBadge.Visibility = Visibility.Visible;
            }
            if (showCameraId && !string.IsNullOrWhiteSpace(CameraNameInline.Text) &&
                (Slot.State == LiveConnectionState_v3.Error || Slot.State == LiveConnectionState_v3.Retrying))
                CameraBadgeInline.Visibility = Visibility.Visible;
            ErrorText.Text = "Kiểm tra mạng, cấu hình camera hoặc máy chủ phát trực tiếp.";
            ErrorText.Text = !string.IsNullOrWhiteSpace(Slot == null ? null : Slot.ErrorMessage)
                ? Slot.ErrorMessage
                : "Kiểm tra mạng, cấu hình camera hoặc máy chủ phát trực tiếp.";
            var statusBrush = Slot != null && Slot.State == LiveConnectionState_v3.Connected
                ? "VmsSuccessBrush_v3"
                : Slot != null && Slot.HasError && Slot.State == LiveConnectionState_v3.Error
                    ? "VmsErrorBrush_v3"
                    : Slot != null && (Slot.State == LiveConnectionState_v3.Connecting ||
                        Slot.State == LiveConnectionState_v3.Retrying ||
                        Slot.State == LiveConnectionState_v3.Disconnecting)
                        ? "VmsWarningBrush_v3" : "VmsOfflineBrush_v3";
            StatusDot.Fill = (System.Windows.Media.Brush)FindResource(statusBrush);
            StatusDotInline.Fill = (System.Windows.Media.Brush)FindResource(statusBrush);
            ConnectButton.Visibility = !empty && (Slot == null || !Slot.IsConnected) ? Visibility.Visible : Visibility.Collapsed;
            DisconnectButton.Visibility = !empty && Slot != null && (Slot.IsConnected || Slot.State == LiveConnectionState_v3.Connecting) ? Visibility.Visible : Visibility.Collapsed;
            StreamSelector.ItemsSource = empty || Slot.Camera.Streams == null ? null : Slot.Camera.Streams;
            StreamSelector.SelectedItem = empty ? null : Slot.SelectedStream;
            // Stream selection is managed by the camera/session, while the
            // tile action bar mirrors the web actions (connect, disconnect,
            // fullscreen and remove) only.
            StreamSelector.Visibility = Visibility.Collapsed;
            _changingStream = false;
            OpenCameraBadgeIfActive();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _retryTimer.Stop();
            _retryTimer.Tick -= RetryTimer_Tick;
            _loadingSpinnerTimer.Stop();
            _loadingSpinnerTimer.Tick -= LoadingSpinnerTimer_Tick;
            _connectTimeoutTimer.Stop();
            _connectTimeoutTimer.Tick -= ConnectTimeoutTimer_Tick;
            SafeStopHideActionsTimer();
            _hideActionsTimer.Tick -= HideActionsTimer_Tick;
            if (Application.Current != null)
                Application.Current.Deactivated -= Application_Deactivated;
            ActionPopup.IsOpen = false;
            CameraBadgePopup.IsOpen = false;
            Loaded -= LiveTile_Loaded;
            Unloaded -= LiveTile_Unloaded;
            LayoutUpdated -= LiveTile_LayoutUpdated;
            if (_ownerWindow != null)
            {
                _ownerWindow.Deactivated -= OwnerWindow_Deactivated;
                _ownerWindow.Activated -= OwnerWindow_Activated;
                _ownerWindow.LocationChanged -= OwnerWindow_LocationChanged;
                _ownerWindow.SizeChanged -= OwnerWindow_SizeChanged;
                _ownerWindow = null;
            }
            Player.PlaybackStateChanged -= Player_PlaybackStateChanged;
            Player.VideoMouseEnter -= Player_VideoMouseEnter;
            Player.VideoMouseMove -= Player_VideoMouseMove;
            Player.VideoMouseLeave -= Player_VideoMouseLeave;
            MetadataOverlay.Dispose();
            Player.Dispose();
        }
    }
}
