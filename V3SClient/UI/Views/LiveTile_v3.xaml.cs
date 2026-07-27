using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using V3SClient.libs;
using V3SClient.viewModels;
using V3SClient.Services;

namespace V3SClient.UI.Views
{
    public partial class LiveTile_v3 : UserControl, IDisposable
    {
        private sealed class SubscriptionGroup : IDisposable
        {
            private IDisposable _first;
            private IDisposable _second;

            public SubscriptionGroup(IDisposable first, IDisposable second)
            {
                _first = first;
                _second = second;
            }

            public void Dispose()
            {
                var first = System.Threading.Interlocked.Exchange(ref _first, null);
                var second = System.Threading.Interlocked.Exchange(ref _second, null);
                first?.Dispose();
                second?.Dispose();
            }
        }

        private readonly DispatcherTimer _retryTimer;
        private readonly DispatcherTimer _connectTimeoutTimer;
        private readonly DispatcherTimer _hideActionsTimer;
        private readonly DispatcherTimer _loadingSpinnerTimer;
        private bool _disposed;
        private bool _changingStream;
        private bool _actionsPinned;
        private bool _fullscreenMode;
        private bool _usingMainPresentation;
        private bool _gridStreamWarmedForRestore;
        private int _mainPresentationGeneration;
        private int _mainSwitchScheduledGeneration = -1;
        private CameraStreamInfo _pendingMainStream;
        private bool _positioningBadge;
        private bool _popupPlacementSuspended;
        private int _badgeGeneration;
        private Window _ownerWindow;
        private IDisposable _metadataSubscription;

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
            MainPlayer.PlaybackStateChanged += MainPlayer_PlaybackStateChanged;
            MainPlayer.SetPresentationVisible(false);
            Player.VideoMouseEnter += Player_VideoMouseEnter;
            Player.VideoMouseMove += Player_VideoMouseMove;
            Player.VideoMouseLeave += Player_VideoMouseLeave;
            Loaded += LiveTile_Loaded;
            Unloaded += LiveTile_Unloaded;
            if (Application.Current != null)
                Application.Current.Deactivated += Application_Deactivated;
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
            }
            _ownerWindow = window;
            if (_ownerWindow != null)
            {
                _ownerWindow.Deactivated += OwnerWindow_Deactivated;
                _ownerWindow.Activated += OwnerWindow_Activated;
                _ownerWindow.LocationChanged += OwnerWindow_LocationChanged;
                OpenCameraBadgeIfActive();
            }
        }

        private void OpenCameraBadgeIfActive()
        {
            if (_disposed || _popupPlacementSuspended || _ownerWindow == null || !_ownerWindow.IsActive ||
                Slot == null || Slot.Camera == null || !TileBorder.IsVisible ||
                Slot.State == LiveConnectionState_v3.Empty) return;
            var generation = _badgeGeneration;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed || _popupPlacementSuspended || generation != _badgeGeneration || _ownerWindow == null ||
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
            _ownerWindow = null;
        }

        private void OwnerWindow_LocationChanged(object sender, EventArgs e)
        {
            if (_popupPlacementSuspended)
                return;
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
                // Fullscreen is deliberately scoped to the application window.
                // Setting Topmost here also promotes the native Popup HWNDs used
                // by the action bar, making those buttons appear above other
                // applications after Alt+Tab.
                _actionsPinned = true;
                ShowActions();
            }
        }

        public LiveSlotViewModel_v3 Slot { get; private set; }

        /// <summary>
        /// A slot is a mutable view-model: selecting a camera fills the same
        /// previously-empty slot object.  Compare the player camera too, so
        /// layout optimization never skips the required bind for that case.
        /// </summary>
        public bool RequiresBind(LiveSlotViewModel_v3 slot)
        {
            return !ReferenceEquals(Slot, slot) ||
                   !ReferenceEquals(Player.Camera, slot == null ? null : slot.Camera);
        }

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
            {
                Player.Disconnect();
                MainPlayer.Disconnect();
            }
            _usingMainPresentation = false;
            _pendingMainStream = null;
            _mainPresentationGeneration++;
            _mainSwitchScheduledGeneration = -1;
            Slot = slot;
            DataContext = slot;
            RefreshVisuals();
            if (slot == null || slot.Camera == null)
            {
                Player.Camera = null;
                MainPlayer.Camera = null;
                UpdateMetadataSubscription();
                return;
            }
            Player.SelectedStream = slot.SelectedStream;
            Player.Camera = slot.Camera;
            MainPlayer.SetPresentationVisible(false);
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
            if (_disposed || _popupPlacementSuspended) return;
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

        /// <summary>
        /// Popup overlays are native HWNDs. During a parent-grid resize they
        /// are closed so PointToScreen is not recalculated per tile/pass.
        /// </summary>
        public void SuspendPopupPlacementForResize()
        {
            if (_disposed || _popupPlacementSuspended) return;
            _popupPlacementSuspended = true;
            SafeStopHideActionsTimer();
            HideCameraBadge(false);
            ActionPopup.IsOpen = false;
        }

        /// <summary>Restores overlays after the grid receives stable bounds.</summary>
        public void ResumePopupPlacementAfterResize()
        {
            if (_disposed) return;
            _popupPlacementSuspended = false;
            OpenCameraBadgeIfActive();
            if (_actionsPinned || _fullscreenMode) ShowActions();
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
            if (_usingMainPresentation)
                MainPlayer.SetPresentationVisible(visible);
            else
                Player.SetVideoSurfaceVisible(visible);
        }

        /// <summary>
        /// Starts the main stream in a second native host while the sub1
        /// player keeps rendering.  The visible source is swapped only when
        /// GStreamer has produced a real video overlay for main.
        /// </summary>
        public async System.Threading.Tasks.Task PrepareFullscreenMainStreamAsync(CameraStreamInfo mainStream)
        {
            if (_disposed || Slot == null || Slot.Camera == null || mainStream == null) return;
            if (SameStream(Slot.SelectedStream, mainStream)) return;

            _pendingMainStream = mainStream;
            _mainPresentationGeneration++;
            MainPlayer.Camera = Slot.Camera;
            MainPlayer.SelectedStream = mainStream;
            MainPlayer.SetPresentationVisible(false);
            await MainPlayer.ReconnectAsync();
        }

        /// <summary>
        /// Immediately restores the already-running grid/sub1 pipeline.  The
        /// main pipeline is then torn down in the background, so leaving
        /// fullscreen never waits for a new RTSP handshake.
        /// </summary>
        public void RestoreGridStream(CameraStreamInfo gridStream)
        {
            _mainPresentationGeneration++;
            _mainSwitchScheduledGeneration = -1;
            _pendingMainStream = null;
            if (_usingMainPresentation)
            {
                MainPlayer.SetPresentationVisible(false);
                if (!_gridStreamWarmedForRestore)
                    Player.SetPipelinePaused(false);
                Player.SetVideoSurfaceVisible(true);
            }
            _usingMainPresentation = false;
            _gridStreamWarmedForRestore = false;
            if (gridStream != null)
            {
                Slot.SelectedStream = gridStream;
                Player.SelectedStream = gridStream;
            }
            MainPlayer.RequestDisconnect();
            _ = System.Threading.Tasks.Task.Run(() => MainPlayer.DisposePipelineInBackground());
            UpdateMetadataSubscription();
            RefreshVisuals();
        }

        /// <summary>
        /// Starts the already-open sub1 pipeline before the fullscreen window
        /// begins shrinking. It can decode a clean keyframe while main is
        /// still displayed, avoiding the bright/corrupt first frame on the
        /// returned grid.
        /// </summary>
        public void WarmGridStreamForRestore(CameraStreamInfo gridStream)
        {
            if (_disposed || !_usingMainPresentation || Slot == null || Slot.Camera == null) return;
            if (gridStream != null) Player.SelectedStream = gridStream;
            _gridStreamWarmedForRestore = true;
            Player.SetPipelinePaused(false);
        }

        /// <summary>
        /// Switches between the lightweight grid stream and the main stream
        /// without replacing the tile or its WindowsFormsHost.  Keeping the
        /// native host alive is essential for a smooth fullscreen transition.
        /// </summary>
        public async System.Threading.Tasks.Task UseStreamAsync(CameraStreamInfo stream)
        {
            if (_disposed || Slot == null || Slot.Camera == null) return;
            if (ReferenceEquals(Slot.SelectedStream, stream)) return;

            Slot.SelectedStream = stream;
            Player.SelectedStream = stream;
            UpdateMetadataSubscription();
            RefreshVisuals();

            // An offline/error slot only needs its preferred source updated.
            // A running tile reconnects asynchronously; ConnectAsync already
            // serializes pipeline teardown/startup and never blocks the UI.
            if (Slot.State == LiveConnectionState_v3.Connected ||
                Slot.State == LiveConnectionState_v3.Connecting ||
                Slot.State == LiveConnectionState_v3.Retrying)
                await ConnectAsync();
        }

        private void MainPlayer_PlaybackStateChanged(object sender, WhepPlaybackStateChangedEventArgs_v3 e)
        {
            if (_disposed || Slot == null || Slot.Camera == null) return;
            var generation = _mainPresentationGeneration;
            LoggerManager.LogInfo("Live View _v3 fullscreen main state for " +
                Slot.DisplayName + ": " + e.State);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed || generation != _mainPresentationGeneration ||
                    Slot == null || Slot.Camera == null || _pendingMainStream == null)
                    return;

                if (e.State == WhepPlaybackState_v3.Playing)
                {
                    if (_mainSwitchScheduledGeneration == generation) return;
                    _mainSwitchScheduledGeneration = generation;
                    _ = SwitchToStableMainAsync(generation);
                }
                else if (e.State == WhepPlaybackState_v3.Error)
                {
                    LoggerManager.LogWarn("Live View _v3 kept sub1 visible because main failed for " +
                        Slot.DisplayName + ": " + (e.Message ?? "unknown error"));
                }
                // A main-stream failure deliberately leaves the live sub1
                // pipeline on screen. The user keeps video instead of seeing
                // a fullscreen error caused only by the optional upgrade.
            }), DispatcherPriority.Render);
        }

        private async System.Threading.Tasks.Task SwitchToStableMainAsync(int generation)
        {
            // The first frame after an RTSP pipeline reaches Playing can be
            // a predictive frame that lacks its reference data. Keep it
            // hidden long enough for the main decoder to receive an IDR and
            // a few complete frames; sub1 remains visible throughout.
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(850));
            if (_disposed || generation != _mainPresentationGeneration || _usingMainPresentation ||
                Slot == null || Slot.Camera == null || _pendingMainStream == null)
                return;

            _usingMainPresentation = true;
            Slot.SelectedStream = _pendingMainStream;
            Player.SetVideoSurfaceVisible(false);
            // Preserve the established sub1 RTSP session, but pause it while
            // main is visible so it stops decoding/rendering and does not
            // compete for CPU/GPU resources.
            Player.SetPipelinePaused(true);
            MainPlayer.SetPresentationVisible(true);
            UpdateMetadataSubscription();
            RefreshVisuals();
        }

        private static bool SameStream(CameraStreamInfo first, CameraStreamInfo second)
        {
            return ReferenceEquals(first, second) ||
                (first != null && second != null &&
                 string.Equals(first.StreamType, second.StreamType, StringComparison.OrdinalIgnoreCase) &&
                 first.IsAiMode == second.IsAiMode);
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
            MainPlayer.Disconnect();
            _usingMainPresentation = false;
            _gridStreamWarmedForRestore = false;
            _pendingMainStream = null;
            _mainPresentationGeneration++;
            _mainSwitchScheduledGeneration = -1;
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
            MainPlayer.Disconnect();
            _usingMainPresentation = false;
            _gridStreamWarmedForRestore = false;
            _pendingMainStream = null;
            _mainPresentationGeneration++;
            _mainSwitchScheduledGeneration = -1;
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
            MainPlayer.RequestDisconnect();
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
            MainPlayer.RequestDisconnect();
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
            // The action bar is a native WPF Popup so it is not clipped by the
            // WindowsFormsHost video surface.  Native video mouse messages can
            // arrive a little late after Alt+Tab; never let one reopen a popup
            // while the iVista window is inactive.
            if (_disposed || _popupPlacementSuspended || !IsLoaded || _ownerWindow == null ||
                !_ownerWindow.IsActive || ActionBar.Visibility != Visibility.Visible) return;
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
            // LivePage_v3 owns the debounced resize coordinator. Updating a
            // native popup here would run once for every camera tile.
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
            // WebApp subscribes per logical camera, not per stream currently
            // selected. A camera can expose its raw stream while AI metadata
            // is produced by its paired AI stream.
            var isAi = Slot != null && Slot.Camera != null &&
                (Slot.Camera.HasAIStream ||
                 (Slot.SelectedStream != null && Slot.SelectedStream.IsAiMode == true) ||
                 (Slot.Camera.Streams != null && Slot.Camera.Streams.Any(stream => stream != null && stream.IsAiMode == true)) ||
                 string.Equals(Slot.Camera.type, "ai_processed", StringComparison.OrdinalIgnoreCase));

            // This runs before ConnectAsync creates either player pipeline.
            Player.AiOverlayEnabled = isAi;
            MainPlayer.AiOverlayEnabled = isAi;
            Player.ClearAiMetadata();
            MainPlayer.ClearAiMetadata();
            _metadataSubscription?.Dispose();
            _metadataSubscription = null;
            if (!isAi || string.IsNullOrWhiteSpace(Slot.Camera.camID)) return;

            var cameraId = Slot.Camera.camID;
            var streamCameraId = Slot.SelectedStream == null ? null : Slot.SelectedStream.RtspRelayRaw;
            var primarySubscription = MetadataSocketService_v3.Instance.Subscribe(cameraId, OnAiMetadataFrame);
            // WebApp changes its logical camera ID to relayRawPath when a
            // stream is selected. Subscribe to that alias too, otherwise a
            // camera can play normally but silently drop its ai_metadata.
            _metadataSubscription = string.IsNullOrWhiteSpace(streamCameraId) ||
                string.Equals(cameraId, streamCameraId, StringComparison.OrdinalIgnoreCase)
                ? primarySubscription
                : new SubscriptionGroup(primarySubscription,
                    MetadataSocketService_v3.Instance.Subscribe(streamCameraId, OnAiMetadataFrame));
        }

        private void OnAiMetadataFrame(AiMetadataFrame_v3 frame)
        {
            if (_disposed || frame == null || Slot == null || Slot.Camera == null ||
                !IsCurrentMetadataCameraId(frame.CameraId))
                return;
            Player.Send2Draw(frame);
            MainPlayer.Send2Draw(frame);
        }

        private bool IsCurrentMetadataCameraId(string cameraId)
        {
            if (string.IsNullOrWhiteSpace(cameraId) || Slot == null || Slot.Camera == null) return false;
            if (string.Equals(cameraId, Slot.Camera.camID, StringComparison.OrdinalIgnoreCase)) return true;
            var streamCameraId = Slot.SelectedStream == null ? null : Slot.SelectedStream.RtspRelayRaw;
            return !string.IsNullOrWhiteSpace(streamCameraId) &&
                string.Equals(cameraId, streamCameraId, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshVisuals()
        {
            _changingStream = true;
            var empty = Slot == null || Slot.Camera == null;
            var showCameraId = !empty;
            var showVideo = !empty && Slot.State != LiveConnectionState_v3.Error &&
                Slot.State != LiveConnectionState_v3.Retrying &&
                Slot.State != LiveConnectionState_v3.Connecting &&
                Slot.State != LiveConnectionState_v3.Disconnecting;
            // There are two native HWND hosts. Exactly one may render: the
            // persistent sub1 host in grid mode, or the warmed-up main host
            // in selected-camera fullscreen mode.
            Player.SetVideoSurfaceVisible(showVideo && !_usingMainPresentation);
            MainPlayer.SetPresentationVisible(showVideo && _usingMainPresentation);
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
                if (_usingMainPresentation) MainPlayer.SetPresentationVisible(false);
                else Player.SetVideoSurfaceVisible(false);
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
            // Show the source actually selected for this tile so operators
            // can immediately confirm grid=sub and fullscreen=main.
            var cameraBadgeText = empty
                ? string.Empty
                : Slot.DisplayName + " · " + (string.IsNullOrWhiteSpace(Slot.StreamLabel) ? "main" : Slot.StreamLabel);
            CameraName.Text = cameraBadgeText;
            CameraNameInline.Text = cameraBadgeText;
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
            if (_ownerWindow != null)
            {
                _ownerWindow.Deactivated -= OwnerWindow_Deactivated;
                _ownerWindow.Activated -= OwnerWindow_Activated;
                _ownerWindow.LocationChanged -= OwnerWindow_LocationChanged;
                _ownerWindow = null;
            }
            Player.PlaybackStateChanged -= Player_PlaybackStateChanged;
            MainPlayer.PlaybackStateChanged -= MainPlayer_PlaybackStateChanged;
            Player.VideoMouseEnter -= Player_VideoMouseEnter;
            Player.VideoMouseMove -= Player_VideoMouseMove;
            Player.VideoMouseLeave -= Player_VideoMouseLeave;
            _metadataSubscription?.Dispose();
            _metadataSubscription = null;
            Player.Dispose();
            MainPlayer.Dispose();
        }
    }
}
