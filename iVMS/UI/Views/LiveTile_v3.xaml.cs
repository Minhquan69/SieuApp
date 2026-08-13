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
using V3SClient.models;
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
        private bool _isMuted;
        private bool _usingMainPresentation;
        private bool _gridStreamWarmedForRestore;
        private int _mainPresentationGeneration;
        private int _mainSwitchScheduledGeneration = -1;
        private CameraStreamInfo _pendingMainStream;
        private bool _positioningBadge;
        private bool _badgeRelocationQueued;
        private bool _popupPlacementSuspended;
        // Initial Offline is a normal queued-startup state. Only an explicit
        // operator disconnect is allowed to keep the action popup visible.
        private bool _showDisconnectedActions;
        private int _badgeGeneration;
        private bool _badgeOpenQueued;
        private Window _ownerWindow;
        private IDisposable _metadataSubscription;
        // Player.Camera may be cleared while its native host is arranging.
        // Keep the logical binding identity so a layout-only rebuild does not
        // rebind every active tile.
        private Camera _boundCamera;

        public LiveTile_v3()
        {
            InitializeComponent();
            // Match the compact four-action grid toolbar.
            ConnectButton.Width = DisconnectButton.Width = 28;
            ConnectButton.Height = DisconnectButton.Height = 28;
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
            // Camera badges are native children of their own video host.
            // Unlike the former Popup they need no screen-coordinate update.
        }

        /// <summary>
        /// Opens an ID badge only while its owner is the foreground iVista
        /// window.  A generation token prevents a queued layout callback from
        /// reopening the native Popup after Alt+Tab or a grid transition.
        /// </summary>
        private void OpenCameraBadgeNow(int generation)
        {
            // Retained as a no-op for existing layout callers.  The badge is
            // now automatically clipped by the native video host.
        }

        private void HideCameraBadge(bool clearText)
        {
            _badgeGeneration++;
            _badgeOpenQueued = false;
        }

        private void SafeCloseActionPopup()
        {
            try
            {
                if (ActionPopup != null && ActionPopup.IsOpen)
                    ActionPopup.IsOpen = false;
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }

        private void LiveTile_Unloaded(object sender, RoutedEventArgs e)
        {
            SafeStopHideActionsTimer();
            _actionsPinned = false;
            SafeCloseActionPopup();
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
            // A WPF Popup is a separate HWND. Placement="Relative" avoids
            // DPI errors across monitors, but it does not always receive a
            // native move notification while a borderless owner is dragged.
            // Coalesce move events into one render update and force the popup
            // placement to be recalculated from this tile's current HWND.
            if (_badgeRelocationQueued)
                return;

            _badgeRelocationQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _badgeRelocationQueued = false;
                if (_disposed || _popupPlacementSuspended || _ownerWindow == null || !_ownerWindow.IsActive)
                    return;
                if (ActionPopup.IsOpen)
                    PositionActionPopup();
            }), DispatcherPriority.Render);
        }

        private void OwnerWindow_Deactivated(object sender, EventArgs e)
        {
            SafeStopHideActionsTimer();
            _actionsPinned = false;
            SafeCloseActionPopup();
            // Popup owns a native HWND and can otherwise remain above a
            // different foreground application.  Keep the badge scoped to
            // the iVista window; OwnerWindow_Activated restores it instantly.
            HideCameraBadge(false);
        }

        private void Application_Deactivated(object sender, EventArgs e)
        {
            SafeStopHideActionsTimer();
            _actionsPinned = false;
            HideCameraBadge(false);
            SafeCloseActionPopup();
            if (_ownerWindow != null)
                _ownerWindow.Topmost = false;
        }

        private void OwnerWindow_Activated(object sender, EventArgs e)
        {
            if (_disposed || Slot == null || Slot.Camera == null) return;
            var generation = _badgeGeneration;
            // Activation already causes a layout pass; open in that same pass
            // instead of queuing a second Render cycle for every grid tile.
            Dispatcher.BeginInvoke(new Action(() => OpenCameraBadgeNow(generation)), DispatcherPriority.Render);
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
        private bool _compactDashboardMode;
        public bool CompactDashboardMode
        {
            get { return _compactDashboardMode; }
            set { _compactDashboardMode = value; if (IsLoaded) ApplyCompactDashboardMode(); }
        }

        private void ApplyCompactDashboardMode()
        {
            if (!_compactDashboardMode) return;
            // Keep the four compact dashboard actions available when
            // ShowActions reapplies this style during hover. Only hide the
            // legacy controls that are not part of the compact toolbar.
            ConnectButton.Visibility = StreamSelector.Visibility = Visibility.Collapsed;
            MuteButton.Visibility = SnapshotButton.Visibility = Visibility.Collapsed;
            // Dashboard quick view keeps only the lightweight spinner. The
            // full live page retains its connection/status messages.
            LoadingText.Visibility = Visibility.Collapsed;
            PendingTitle.Visibility = Visibility.Collapsed;
            PendingStreamText.Visibility = Visibility.Collapsed;
            CameraBadgeInline.Padding = new Thickness(4, 2, 4, 2);
            CameraBadgeInline.Margin = new Thickness(5);
            CameraNameInline.FontSize = 9;
            CameraNameInline.MaxWidth = 86;
            HideOverlayContent(OfflineOverlay);
            HideOverlayContent(ErrorOverlay);
            ShowCompactConnectionStatus(OfflineOverlay);
            ShowCompactConnectionStatus(ErrorOverlay);
            RemoveButton.Content = "×";
            RemoveButton.FontSize = 18;
            RemoveButton.Foreground = Brushes.White;
            RemoveButton.Width = 22;
            RemoveButton.Height = 22;
            RemoveButton.Margin = new Thickness(0);
            RemoveButton.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            RemoveButton.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
            ActionBar.Background = Brushes.Transparent;
        }

        private static void HideOverlayContent(Border overlay)
        {
            var panel = overlay == null ? null : overlay.Child as Panel;
            if (panel == null) return;
            foreach (UIElement child in panel.Children) child.Visibility = Visibility.Collapsed;
        }

        private static void ShowCompactConnectionStatus(Border overlay)
        {
            var panel = overlay == null ? null : overlay.Child as Panel;
            var title = panel != null && panel.Children.Count > 1 ? panel.Children[1] as TextBlock : null;
            if (title == null) return;
            title.Text = "Mất kết nối";
            title.FontSize = 11;
            title.Foreground = Brushes.White;
            title.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// A slot is a mutable view-model: selecting a camera fills the same
        /// previously-empty slot object.  Compare the player camera too, so
        /// layout optimization never skips the required bind for that case.
        /// </summary>
        public bool RequiresBind(LiveSlotViewModel_v3 slot)
        {
            return !ReferenceEquals(Slot, slot) ||
                   !ReferenceEquals(_boundCamera, slot == null ? null : slot.Camera);
        }

        public event EventHandler RemoveRequested;
        public event EventHandler FullscreenRequested;
        public event EventHandler SnapshotRequested;
        public event EventHandler StateChanged;
        public event EventHandler AudioRequested;
        public event EventHandler AudioStateChanged;

        public void Bind(LiveSlotViewModel_v3 slot)
        {
            _badgeGeneration++;
            _showDisconnectedActions = false;
            HideCameraBadge(true);
            var previousCamera = _boundCamera;
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
            _boundCamera = nextCamera;
            DataContext = slot;
            RefreshVisuals();
            ApplyCompactDashboardMode();
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

        /// <summary>Closes every native overlay before this tile is hidden or expanded.</summary>
        public void HideForFullscreen()
        {
            // Popup controls are separate native windows because the video
            // renderer uses WindowsFormsHost. Collapsing or expanding a tile
            // alone does not close them, so do so explicitly.
            SafeCloseActionPopup();
            HideCameraBadge(false);
        }

        /// <summary>Restores the ID only after the normal camera grid is back.</summary>
        public void RestoreAfterFullscreen()
        {
            SafeCloseActionPopup();
            HideCameraBadge(false);
            OpenCameraBadgeIfActive();
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

        public void SetMuted(bool muted)
        {
            _isMuted = muted;
            if (Player != null) Player.SetMuted(muted);
            if (MainPlayer != null) MainPlayer.SetMuted(muted);
            MuteButton.ToolTip = muted ? "Bật âm thanh" : "Tắt âm thanh";
            MuteIcon.Kind = muted ? MahApps.Metro.IconPacks.PackIconMaterialKind.VolumeOff : MahApps.Metro.IconPacks.PackIconMaterialKind.VolumeHigh;
            AudioPlayingIcon.Visibility = !muted && Slot != null && Slot.Camera != null
                ? Visibility.Visible : Visibility.Collapsed;
        }

        public bool HasAudio => Player != null && Player.HasAudio;
        public bool IsMuted => _isMuted;


        public bool TrySaveSnapshot(out string savedPath) { return Player.TrySaveSnapshot(out savedPath); }
        public System.Threading.Tasks.Task<string> TrySaveSourceSnapshotAsync() { return Player.TrySaveSourceSnapshotAsync(); }

        public void RefreshPopupPlacement()
        {
            if (_disposed || _popupPlacementSuspended) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (_disposed) return;
                // A layout rebuild may move a tile, but it must not close and
                // recreate every badge Popup. Reposition open overlays in
                // place; this keeps IDs stable when a new camera is added.
                if (ActionPopup.IsOpen) PositionActionPopup();
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
            SafeCloseActionPopup();
        }

        /// <summary>Restores overlays after the grid receives stable bounds.</summary>
        public void ResumePopupPlacementAfterResize()
        {
            if (_disposed) return;
            _popupPlacementSuspended = false;
            // The caller has already completed CameraGrid.UpdateLayout().
            // Reopen synchronously so IDs move with the sidebar, not seconds
            // later behind video rendering work.
            OpenCameraBadgeNow(_badgeGeneration);
            if (_actionsPinned || _fullscreenMode) ShowActions();
        }

        /// <summary>Suppresses popup overlays during a layout/fullscreen transition.</summary>
        public void HideTransientOverlays()
        {
            HideCameraBadge(false);
            SafeCloseActionPopup();
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

        /// <summary>Synchronizes both possible native hosts to this tile's final bounds.</summary>
        public void SynchronizeNativeVideoSurfaces()
        {
            if (_disposed) return;
            Player.SynchronizeNativeVideoHost();
            MainPlayer.SynchronizeNativeVideoHost();
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
            _ = MainPlayer.DisconnectPipelineAsync();
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
            // a predictive frame that lacks its reference data. Wait for real
            // decoded frames instead of an unconditional 850 ms delay: a
            // healthy camera promotes sooner, while a slow camera keeps sub1
            // visible until its main decoder is genuinely ready.
            await MainPlayer.WaitForStableVideoFramesAsync();
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
            _showDisconnectedActions = false;
            // `false` is authoritative from /devices/status/batch.  Do not
            // start a decoder/pipeline for a camera known to be offline; a
            // stream failure is reserved for cameras reported online.
            if (Slot.Camera.is_online == false)
            {
                _retryTimer.Stop();
                _connectTimeoutTimer.Stop();
                Slot.State = LiveConnectionState_v3.Offline;
                Slot.ErrorMessage = "Camera đang ngoại tuyến theo trạng thái hệ thống.";
                Player.Disconnect();
                MainPlayer.Disconnect();
                Player.SetVideoSurfaceVisible(false);
                MainPlayer.SetPresentationVisible(false);
                RefreshVisuals();
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
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
            // Wait for every pending native connection to finish cancelling
            // before the tile can be reused. This prevents the previous
            // camera wall from completing Parse.Launch after a new wall starts.
            await Player.DisconnectPipelineAsync();
            await MainPlayer.DisconnectPipelineAsync();
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
                    ScheduleReconnectAfterStreamError();
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

        private void ScheduleReconnectAfterStreamError()
        {
            if (Slot == null || Slot.Camera == null) return;
            if (Slot.Camera.is_online == false)
            {
                Slot.State = LiveConnectionState_v3.Offline;
                return;
            }

            // Keep retrying reported-online cameras.  The device-status poll
            // will stop this flow as soon as the camera becomes offline.
            Slot.RetryCount++;
            Slot.State = LiveConnectionState_v3.Retrying;
            _retryTimer.Stop();
            _retryTimer.Interval = TimeSpan.FromSeconds(_compactDashboardMode ? 5 : 15);
            _retryTimer.Start();
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
            Player.Disconnect();
            ScheduleReconnectAfterStreamError();
            RefreshVisuals();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void Connect_Click(object sender, RoutedEventArgs e) { await ConnectAsync(); }
        private async void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            if (Slot == null || Slot.Camera == null) return;
            if (Slot.State == LiveConnectionState_v3.Connected ||
                Slot.State == LiveConnectionState_v3.Connecting)
            {
                _showDisconnectedActions = true;
                Disconnect();
            }
            else
                await ConnectAsync();
        }
        private void CollapseActions_Click(object sender, RoutedEventArgs e) { HideActions(); }
        private void Remove_Click(object sender, RoutedEventArgs e) { RemoveRequested?.Invoke(this, EventArgs.Empty); }
        private void Fullscreen_Click(object sender, RoutedEventArgs e) { FullscreenRequested?.Invoke(this, EventArgs.Empty); }
        private void Snapshot_Click(object sender, RoutedEventArgs e) { SnapshotRequested?.Invoke(this, EventArgs.Empty); }
        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            if (MuteButton == null) return;
            var wantsAudio = _isMuted;
            if (wantsAudio) AudioRequested?.Invoke(this, EventArgs.Empty);
            SetMuted(wantsAudio);
            AudioStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void EnsureCompactActionsVisible()
        {
            if (!_compactDashboardMode || _disposed || !IsLoaded || Slot == null || Slot.Camera == null)
                return;
            ShowActions();
            RemoveButton.Visibility = Visibility.Visible;
            RemoveButton.IsHitTestVisible = true;
            Panel.SetZIndex(RemoveButton, 100);
        }

        private void Ptz_Click(object sender, RoutedEventArgs e) { LoggerManager.LogInfo($"PTZ requested for camera {Slot?.Camera?.camID ?? "unknown"}."); }
        private void CameraControl_Click(object sender, RoutedEventArgs e)
        {
            var open = CameraControlPanel.Visibility != Visibility.Visible;
            // Pin the popup while its secondary controls are open. Otherwise
            // the tile hover timer can close the native Popup before the
            // volume button receives the mouse click.
            _actionsPinned = open;
            CameraControlPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            MuteButton.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            CameraControlIcon.Kind = open
                ? MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown
                : MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp;
            CameraControlButton.ToolTip = open ? "Đóng điều khiển camera" : "Mở điều khiển camera";
            if (open) RefreshPopupPlacement();
            else ScheduleHideActions();
        }

        private void ShowActions()
        {
            // The action bar is a native WPF Popup so it is not clipped by the
            // WindowsFormsHost video surface.  Native video mouse messages can
            // arrive a little late after Alt+Tab; never let one reopen a popup
            // while the iVista window is inactive.
            if (_disposed || _popupPlacementSuspended || !IsLoaded ||
                ActionBar.Visibility != Visibility.Visible) return;
            if (_ownerWindow == null)
            {
                _ownerWindow = Window.GetWindow(this);
                if (_ownerWindow == null) return;
            }
            if (!_compactDashboardMode && !_ownerWindow.IsActive) return;
            ApplyCompactDashboardMode();
            if (_compactDashboardMode && Slot != null && Slot.Camera != null)
            {
                CameraControlButton.Visibility = Visibility.Visible;
                FullscreenButton.Visibility = Visibility.Visible;
                DisconnectButton.Visibility = Visibility.Visible;
                RemoveButton.Visibility = Visibility.Visible;
                SafeCloseActionPopup();
            }
            OpenCameraBadgeIfActive();
            SafeStopHideActionsTimer();
            ActionPopup.IsOpen = true;
            ActionBar.Opacity = 1;
            ActionBar.IsHitTestVisible = true;
            Dispatcher.BeginInvoke(new Action(PositionActionPopup), DispatcherPriority.Loaded);
        }

        private void ConstrainCameraBadgeToTile()
        {
            var tileWidth = Math.Max(0d, TileBorder.ActualWidth);
            if (tileWidth <= 0d) return;

            const double horizontalInset = 6d;
            const double badgeChromeWidth = 30d;
            var badgeMaxWidth = Math.Max(1d, tileWidth - horizontalInset);
            var nameMaxWidth = Math.Max(0d, badgeMaxWidth - badgeChromeWidth);
            if (Math.Abs(CameraNameInline.MaxWidth - nameMaxWidth) > 0.1)
                CameraNameInline.MaxWidth = nameMaxWidth;
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
                var anchor = _compactDashboardMode
                    ? TileBorder.PointToScreen(new Point(TileBorder.ActualWidth, 0))
                    : TileBorder.PointToScreen(new Point(TileBorder.ActualWidth / 2.0, TileBorder.ActualHeight));
                var dipPoint = source.CompositionTarget.TransformFromDevice.Transform(anchor);
                var x = _compactDashboardMode
                    ? dipPoint.X - ActionBar.ActualWidth - 1.0
                    : dipPoint.X - (ActionBar.ActualWidth / 2.0);
                var y = _compactDashboardMode
                    ? dipPoint.Y + 1.0
                    : dipPoint.Y - ActionBar.ActualHeight - 10.0;
                if (Math.Abs(ActionPopup.HorizontalOffset - x) > 0.1 ||
                    Math.Abs(ActionPopup.VerticalOffset - y) > 0.1)
                {
                    ActionPopup.HorizontalOffset = x;
                    ActionPopup.VerticalOffset = y;
                }
            }
            catch (InvalidOperationException) { }
        }

        private void ActionBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ActionPopup.IsOpen)
                Dispatcher.BeginInvoke(new Action(PositionActionPopup), DispatcherPriority.Loaded);
        }

        private void TileBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateErrorLayoutForTileSize();
        }

        private void UpdateErrorLayoutForTileSize()
        {
            var details = ErrorOverlay == null ? null : ErrorOverlay.Child as StackPanel;
            if (details == null || details.Children.Count < 4 ||
                TileBorder.ActualWidth <= 0 || TileBorder.ActualHeight <= 0)
                return;

            var compact = TileBorder.ActualWidth < 220 || TileBorder.ActualHeight < 145;
            ErrorOverlay.Padding = compact ? new Thickness(4) : new Thickness(8);
            var icon = details.Children[0] as UIElement;
            var title = details.Children[1] as UIElement;
            var message = details.Children[2] as TextBlock;
            var retry = details.Children[3] as UIElement;
            if (icon != null) icon.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            if (title != null) title.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            if (retry != null) retry.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            if (message == null) return;

            message.Text = compact
                ? "Lỗi luồng camera"
                : (!string.IsNullOrWhiteSpace(Slot == null ? null : Slot.ErrorMessage)
                    ? Slot.ErrorMessage
                    : "Kiểm tra mạng, cấu hình camera hoặc máy chủ phát trực tiếp.");
            message.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
            message.TextTrimming = compact ? TextTrimming.CharacterEllipsis : TextTrimming.None;
            message.FontSize = compact ? 10 : 11;
            message.Margin = compact ? new Thickness(0) : new Thickness(0, 4, 0, 8);
        }

        private void HideActions()
        {
            if (_disposed || _actionsPinned || _fullscreenMode) return;
            SafeStopHideActionsTimer();
            SafeCloseActionPopup();
            CameraControlPanel.Visibility = Visibility.Collapsed;
            CameraControlIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp;
            CameraControlButton.ToolTip = "Mở điều khiển camera";
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
            // AI overlay is carried inside the RTSP stream as SEI metadata.
            // RtspPlayer extracts that metadata through its pipeline identity
            // callback and draws it on d3d11overlay, matching the original app.
            // Do not subscribe to the external metadata/Kafka bridge here.
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
            // A tile is reused when the user removes one camera and selects
            // another. Restore the four visible actions on every bind/refresh
            // so a previous camera's Collapsed state cannot leak forward.
            CameraControlButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            FullscreenButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            DisconnectButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            RemoveButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            var reportedOffline = !empty && Slot.Camera.is_online == false;
            var manuallyDisconnected = !empty && !reportedOffline && Slot.State == LiveConnectionState_v3.Offline;
            var showVideo = !empty && !reportedOffline && !manuallyDisconnected && Slot.State != LiveConnectionState_v3.Error &&
                Slot.State != LiveConnectionState_v3.Retrying &&
                Slot.State != LiveConnectionState_v3.Connecting &&
                Slot.State != LiveConnectionState_v3.Disconnecting;
            // There are two native HWND hosts. Exactly one may render: the
            // persistent sub1 host in grid mode, or the warmed-up main host
            // in selected-camera fullscreen mode.
            Player.SetVideoSurfaceVisible(showVideo && !_usingMainPresentation);
            MainPlayer.SetPresentationVisible(showVideo && _usingMainPresentation);
            EmptyOverlay.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            OfflineOverlay.Visibility = reportedOffline ? Visibility.Visible : Visibility.Collapsed;
            DisconnectedOverlay.Visibility = manuallyDisconnected ? Visibility.Visible : Visibility.Collapsed;
            DisconnectedCameraText.Text = manuallyDisconnected
                ? Slot.DisplayName + " - " + (string.IsNullOrWhiteSpace(Slot.StreamLabel) ? "main" : Slot.StreamLabel)
                : string.Empty;
            DisconnectedGroupText.Text = manuallyDisconnected ? GetCameraGroupName(Slot.Camera) : string.Empty;
            // Keep an in-tile fallback for error/retrying states. The native
            // video surface is hidden in those states, so the WPF badge is
            // visible and guarantees that every selected camera still shows
            // its ID even when the stream fails.
            // The native video host is an HWND and paints above WPF. Use the
            // inline badge only while there is no video surface; once video
            // is active, the player-owned badge is the visible one.
            CameraBadgeInline.Visibility = showCameraId && !showVideo
                ? Visibility.Visible : Visibility.Collapsed;
            if (!showCameraId)
                HideCameraBadge(true);
            ActionBar.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (!empty && _compactDashboardMode)
            {
                SafeStopHideActionsTimer();
                ActionPopup.IsOpen = true;
                ActionBar.Opacity = 1;
                ActionBar.IsHitTestVisible = true;
                Dispatcher.BeginInvoke(new Action(PositionActionPopup), DispatcherPriority.Loaded);
            }
            else if (empty)
            {
                _actionsPinned = false;
                HideActions();
            }
            else if (manuallyDisconnected && _showDisconnectedActions)
            {
                _actionsPinned = true;
                ActionPopup.IsOpen = true;
                ActionBar.Opacity = 1;
                ActionBar.IsHitTestVisible = true;
                Dispatcher.BeginInvoke(new Action(PositionActionPopup), DispatcherPriority.Loaded);
            }
            else if (!_actionsPinned && !_fullscreenMode &&
                !TileBorder.IsMouseOver && !ActionBar.IsMouseOver)
            {
                HideActions();
            }
            else if (ActionPopup.IsOpen)
            {
                Dispatcher.BeginInvoke(new Action(PositionActionPopup), DispatcherPriority.Loaded);
            }
            ErrorOverlay.Visibility = !empty && !reportedOffline && Slot.HasError &&
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
            cameraBadgeText = empty ? string.Empty : Slot.DisplayName;
            CameraNameInline.Text = cameraBadgeText;
            AudioPlayingIcon.Visibility = _isMuted || empty ? Visibility.Collapsed : Visibility.Visible;
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
                        ? "VmsWarningBrush_v3"
                        : "VmsOfflineBrush_v3";
            UpdateErrorLayoutForTileSize();
            StatusDotInline.Stroke = (System.Windows.Media.Brush)FindResource(statusBrush);
            Player.SetCameraBadge(cameraBadgeText, showCameraId && showVideo && !_usingMainPresentation,
                Slot != null && Slot.State == LiveConnectionState_v3.Connected,
                Slot != null && Slot.HasError && Slot.State == LiveConnectionState_v3.Error);
            MainPlayer.SetCameraBadge(cameraBadgeText, showCameraId && showVideo && _usingMainPresentation,
                Slot != null && Slot.State == LiveConnectionState_v3.Connected,
                Slot != null && Slot.HasError && Slot.State == LiveConnectionState_v3.Error);
            // The visible grid bar is fixed to four actions. Keep the legacy
            // connect control hidden and retain the broken-link action for
            // every populated slot, including a camera that is reconnecting.
            ConnectButton.Visibility = Visibility.Collapsed;
            DisconnectButton.Visibility = !empty ? Visibility.Visible : Visibility.Collapsed;
            var streamIsActive = Slot != null &&
                (Slot.State == LiveConnectionState_v3.Connected || Slot.State == LiveConnectionState_v3.Connecting);
            StreamConnectionIcon.Kind = streamIsActive
                ? MahApps.Metro.IconPacks.PackIconMaterialKind.PowerPlugOff
                : MahApps.Metro.IconPacks.PackIconMaterialKind.PowerPlug;
            DisconnectButton.Foreground = (Brush)FindResource(streamIsActive
                ? "VmsErrorBrush_v3"
                : "VmsSuccessBrush_v3");
            DisconnectButton.ToolTip = streamIsActive ? "Ngắt kết nối camera" : "Kết nối camera";
            PtzButton.Visibility = !empty && Slot.Camera != null && Slot.Camera.ptz_available == true ? Visibility.Visible : Visibility.Collapsed;
            var hasAudio = !empty && Player != null && Player.HasAudio;
            // Keep the control clickable while the pipeline is reconnecting;
            // HasAudio can briefly be false during a refresh and disabling the
            // button makes it appear to work only on the first click.
            MuteButton.IsEnabled = true;
            MuteButton.IsHitTestVisible = true;
            MuteButton.Visibility = empty || CameraControlPanel.Visibility != Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            // Do not reset mute on every visual/status refresh. The page
            // initializes new tiles muted; subsequent refreshes must preserve
            // the user's per-camera audio choice.
            CameraControlButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            // Do not reset the expanded control panel on every stream/status
            // refresh. RefreshVisuals is called frequently while connecting;
            // collapsing it here makes the popup lose buttons mid-interaction.
            if (!ActionPopup.IsOpen)
                CameraControlPanel.Visibility = Visibility.Collapsed;
            else
            {
                CameraControlButton.Visibility = Visibility.Visible;
                MuteButton.Visibility = Visibility.Visible;
                MuteButton.IsEnabled = true;
                MuteButton.IsHitTestVisible = true;
                PtzButton.Visibility = !empty && Slot.Camera != null && Slot.Camera.ptz_available == true
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            StreamSelector.ItemsSource = empty || Slot.Camera.Streams == null ? null : Slot.Camera.Streams;
            StreamSelector.SelectedItem = empty ? null : Slot.SelectedStream;
            // Stream selection is managed by the camera/session, while the
            // tile action bar mirrors the web actions (connect, disconnect,
            // fullscreen and remove) only.
            StreamSelector.Visibility = Visibility.Collapsed;
            if (_compactDashboardMode)
            {
                // Keep the four visible grid actions stable in compact mode.
                // Only legacy controls remain hidden; hiding the visible
                // actions here made them disappear after every refresh.
                ConnectButton.Visibility = Visibility.Collapsed;
                MuteButton.Visibility = Visibility.Collapsed;
                SnapshotButton.Visibility = Visibility.Collapsed;
                PtzButton.Visibility = Visibility.Collapsed;
                CameraControlPanel.Visibility = Visibility.Collapsed;
                DisconnectButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
                FullscreenButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
                RemoveButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
                CameraControlButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            }
            _changingStream = false;
            OpenCameraBadgeIfActive();
        }

        private static string GetCameraGroupName(Camera camera)
        {
            if (camera == null) return string.Empty;
            var groups = GlobalSystem.Instance == null || GlobalSystem.Instance.CameraGroups == null
                ? null
                : GlobalSystem.Instance.CameraGroups.CamGroupList;
            var group = EnumerateCameraGroups(groups).FirstOrDefault(item => item.Cameras != null &&
                item.Cameras.Any(member => ReferenceEquals(member, camera) ||
                    (!string.IsNullOrWhiteSpace(member == null ? null : member.camID) &&
                     string.Equals(member.camID, camera.camID, StringComparison.OrdinalIgnoreCase))));
            // Camera.groupID is an internal numeric Group_Id, while the live
            // sidebar is grouped by API Unit_Name. Resolve via membership so
            // users see the real unit/group label (for example, “Hà Nội”).
            return !string.IsNullOrWhiteSpace(group == null ? null : group.name)
                ? group.name
                : "Chưa phân nhóm";
        }

        private static System.Collections.Generic.IEnumerable<VMTalkGroup> EnumerateCameraGroups(
            System.Collections.Generic.IEnumerable<VMTalkGroup> groups)
        {
            if (groups == null) yield break;
            foreach (var group in groups)
            {
                if (group == null) continue;
                yield return group;
                foreach (var child in EnumerateCameraGroups(group.SubGroups))
                    yield return child;
            }
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
            SafeCloseActionPopup();
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
