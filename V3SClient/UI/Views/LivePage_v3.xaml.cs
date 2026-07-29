using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using V3SClient.libs;
using V3SClient.models;
using V3SClient.ucs;
using V3SClient.viewModels;
using V3SClient.window;
using FormsScreen = System.Windows.Forms.Screen;

namespace V3SClient.UI.Views
{
    public partial class LivePage_v3 : UserControl, IDisposable
    {
        private readonly LiveViewModel_v3 _viewModel;
        private readonly Dictionary<int, LiveTile_v3> _tiles = new Dictionary<int, LiveTile_v3>();
        private CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource _cameraOperation = new CancellationTokenSource();
        private bool _disposed;
        private LiveTile_v3 _fullscreenTile;
        private Camera _pendingCameraClick;
        private int _cameraClickVersion;
        private Button _removeErrorsHeaderButton;
        private WindowStyle _tileWindowStyle;
        private ResizeMode _tileResizeMode;
        private WindowState _tileWindowState;
        private bool _tileTopmost;
        private double _tileWindowLeft;
        private double _tileWindowTop;
        private double _tileWindowWidth;
        private double _tileWindowHeight;
        private bool _tileUsesVirtualDesktop;
        private Visibility _tileSidebarVisibility;
        private GridLength _tileSidebarWidth;
        private bool _tileWindowStateSaved;
        private bool _gridFullscreen;
        private WindowState _gridWindowState;
        private double _gridWindowLeft;
        private double _gridWindowTop;
        private double _gridWindowWidth;
        private double _gridWindowHeight;
        private bool _gridWindowStateSaved;
        private int _fullscreenTransitionVersion;
        private int _geometryTransitionVersion;
        private readonly TranslateTransform _headerActionTransform = new TranslateTransform();
        private readonly DispatcherTimer _resizeSettledTimer;
        private readonly DispatcherTimer _deviceStatusRefreshTimer;
        private int _deviceStatusRefreshInProgress;
        private readonly Stopwatch _resizeStopwatch = new Stopwatch();
        private bool _resizeOverlaysSuspended;
        private bool _geometryTransitionInProgress;
        private bool _headerActionCenteringQueued;
        private int _resizeEventCount;
        private int _resizeSettlementVersion;
        private LiveTile_v3 _dragTile;
        private Point _dragStart;
        private int _customLayoutRows;
        private int _customLayoutColumns;
        private List<CustomLayoutCell_v3> _customLayoutCells = new List<CustomLayoutCell_v3>();

        public LivePage_v3()
        {
            InitializeComponent();
            // Keep resize feedback responsive while still coalescing the many
            // SizeChanged events raised by WindowsFormsHost/D3D surfaces.
            _resizeSettledTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(85) };
            _resizeSettledTimer.Tick += ResizeSettledTimer_Tick;
            // The status gateway is authoritative, but polling it too often
            // creates needless requests when large camera lists are open.
            // Refresh immediately on page load/Connect all, then every 30 s.
            _deviceStatusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _deviceStatusRefreshTimer.Tick += DeviceStatusRefreshTimer_Tick;
            // The action icons are centred against the complete header at all
            // window sizes, not against the space left between side controls.
            HeaderActionPanel.RenderTransform = _headerActionTransform;
            Grid.SetColumn(HeaderActionPanel, 0);
            Grid.SetColumnSpan(HeaderActionPanel, 3);
            Panel.SetZIndex(HeaderActionPanel, 10);
            LivePageHeader.SizeChanged += LivePageHeader_SizeChanged;
            CameraGrid.SizeChanged += CameraGrid_SizeChanged;
            SizeChanged += LivePage_SizeChanged;
            _removeErrorsHeaderButton = new Button
            {
                Style = (Style)FindResource("LiveHeaderActionStyle_v3"),
                Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(2, 2, 2, 2),
                ToolTip = "Xóa camera lỗi",
                Content = new MahApps.Metro.IconPacks.PackIconMaterial
                {
                    Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.CloseCircleOutline,
                    Width = 14,
                    Height = 14
                },
                Visibility = Visibility.Collapsed
            };
            _removeErrorsHeaderButton.Click += RemoveErrors_Click;
            HeaderActionPanel.Children.Insert(2, _removeErrorsHeaderButton);
            _viewModel = new LiveViewModel_v3();
            DataContext = _viewModel;
            Loaded += OnLoaded;
            Unloaded += (s, e) => Dispose();
        }

        private static ShellPage_v3 GetShellPage(Window window)
        {
            var shellWindow = window as ShellWindow_v3;
            if (shellWindow != null)
                return shellWindow.ShellPage;
            return window == null ? null : window.Content as ShellPage_v3;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            CameraStatus.Text = string.Format("{0} cameras · {1} groups · {2}/{3} active", _viewModel.CameraCount, _viewModel.GroupCount, _viewModel.ActiveCameraCount, _viewModel.Slots.Count);
            EmptyState.Visibility = _viewModel.CameraCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            AllCameraFilterButton.IsEnabled = false;
            AiCameraFilterButton.IsEnabled = true;
            UpdateFilterButtonVisuals();
            NormalizeCameraSidebarLayout();
            BuildGrid();
            QueueHeaderActionCentering();
            await RefreshDeviceStatusesAsync();
            if (!_disposed) _deviceStatusRefreshTimer.Start();
        }

        private async void DeviceStatusRefreshTimer_Tick(object sender, EventArgs e)
        {
            await RefreshDeviceStatusesAsync();
        }

        private async Task<bool> RefreshDeviceStatusesAsync()
        {
            if (_disposed || Interlocked.Exchange(ref _deviceStatusRefreshInProgress, 1) != 0)
                return false;

            try
            {
                var deviceIds = _viewModel.CameraGroups
                    .SelectMany(group => group.Cameras ?? Enumerable.Empty<Camera>())
                    .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID))
                    .Select(camera => camera.camID.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (deviceIds.Count == 0) return false;

                var statuses = await ApiManager.Instance.GetDeviceStatusBatchAsync(deviceIds, _lifetime.Token);
                if (_disposed || statuses == null || statuses.Count == 0) return false;

                _viewModel.ApplyDeviceStatuses(statuses);
                UpdateStatus();
                UpdateOnlineCameraStatusLabel();
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Live View _v3 device-status refresh failed");
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref _deviceStatusRefreshInProgress, 0);
            }
        }

        private void UpdateOnlineCameraStatusLabel()
        {
            CameraStatus.Text = string.Format("{0} cameras · {1} groups · {2} online · {3}/{4} active",
                _viewModel.CameraCount, _viewModel.GroupCount, _viewModel.OnlineCameraCount,
                _viewModel.ActiveCameraCount, _viewModel.Slots.Count);
        }

        private void LivePageHeader_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleResizeSettle();
        }

        private void LivePage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleResizeSettle();
        }

        private void CameraGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleResizeSettle();
        }

        /// <summary>
        /// A native D3D video surface follows every WPF arrange pass while a
        /// window is being resized. Do not force a transaction for every
        /// SizeChanged event; restore native popups only after final bounds.
        /// </summary>
        private void ScheduleResizeSettle()
        {
            if (_disposed || _fullscreenTile != null || _geometryTransitionInProgress)
                return;

            _resizeEventCount++;
            _resizeSettlementVersion++;
            if (!_resizeOverlaysSuspended)
            {
                // Camera IDs/actions are native Popup HWNDs. Repositioning
                // one for every WPF arrange pass is the expensive part of a
                // live resize, especially on a large wall. Hide those small
                // overlays only while the pointer is actively resizing; the
                // D3D video itself continues rendering without interruption.
                _resizeOverlaysSuspended = true;
                foreach (var tile in _tiles.Values)
                    tile.SuspendPopupPlacementForResize();
                _resizeStopwatch.Restart();
            }

            _resizeSettledTimer.Stop();
            _resizeSettledTimer.Start();
        }

        private void ResizeSettledTimer_Tick(object sender, EventArgs e)
        {
            _resizeSettledTimer.Stop();
            var settlementVersion = _resizeSettlementVersion;
            RunAfterNativeLayout(new Action(() =>
            {
                if (_disposed || settlementVersion != _resizeSettlementVersion ||
                    _fullscreenTile != null || _geometryTransitionInProgress)
                    return;

                foreach (var tile in _tiles.Values)
                    tile.ResumePopupPlacementAfterResize();

                _resizeOverlaysSuspended = false;
                QueueHeaderActionCentering();

                var elapsed = _resizeStopwatch.IsRunning ? _resizeStopwatch.ElapsedMilliseconds : 0;
                _resizeStopwatch.Reset();
                if (elapsed >= 16 || _resizeEventCount > 1)
                {
                    LoggerManager.LogDebug(string.Format(
                        "Live View _v3 resize settled: {0} layout events, {1} tiles, {2} active cameras, {3} ms.",
                        _resizeEventCount, _tiles.Count, _viewModel.ActiveCameraCount, elapsed));
                }
                _resizeEventCount = 0;
            }));
        }

        private void CancelPendingResizeSettle()
        {
            _resizeSettledTimer.Stop();
            _resizeSettlementVersion++;
            _resizeEventCount = 0;
            _resizeStopwatch.Reset();
            _resizeOverlaysSuspended = false;
        }

        /// <summary>
        /// Atomically applies a shell or camera-list geometry change.  A
        /// WindowsFormsHost owns a native D3D surface, so it can otherwise
        /// repaint at every intermediate WPF width while the grid is still
        /// being arranged.  Keep the complete grid hidden until the final
        /// arrange pass, then expose all native surfaces together.
        /// </summary>
        public void BeginGeometryTransition()
        {
            // ShellSidebar raises this event immediately before its Width is
            // changed.  The next Render pass therefore has the final bounds.
            // Recreate the native badge popups in that pass; relying on the
            // ContextIdle resize coordinator made IDs lag several seconds
            // behind the left navigation pane on a busy video wall.
            if (_disposed || _fullscreenTile != null)
                return;

            CancelPendingResizeSettle();
            var transitionVersion = ++_geometryTransitionVersion;
            foreach (var tile in _tiles.Values)
                tile.SuspendPopupPlacementForResize();

            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (_disposed || transitionVersion != _geometryTransitionVersion || _fullscreenTile != null)
                    return;

                CameraGrid.InvalidateMeasure();
                CameraGrid.InvalidateArrange();
                CameraGrid.UpdateLayout();
                foreach (var tile in _tiles.Values)
                    tile.ResumePopupPlacementAfterResize();
                QueueHeaderActionCentering();
            }));
        }

        private void RunGridGeometryTransition(Action applyLayout, bool suspendVideoSurfaces = false)
        {
            if (_disposed)
                return;

            // Per-camera fullscreen owns its own transfer sequence.  Do not
            // let a nested sidebar/layout event reveal an intermediate grid.
            if (_fullscreenTile != null)
            {
                if (applyLayout != null) applyLayout();
                return;
            }

            CancelPendingResizeSettle();
            _geometryTransitionInProgress = true;
            var transitionVersion = ++_geometryTransitionVersion;
            // A sidebar changes the position of every tile.  Badge popups are
            // separate native windows, so intentionally recreate them around
            // this one atomic layout transaction instead of leaving stale IDs
            // on screen until the D3D idle queue becomes free.
            var reloadCameraBadges = applyLayout != null && !suspendVideoSurfaces;
            if (reloadCameraBadges)
            {
                foreach (var tile in _tiles.Values)
                    tile.SuspendPopupPlacementForResize();
            }
            if (suspendVideoSurfaces)
            {
                CameraGrid.Visibility = Visibility.Hidden;
                foreach (var tile in _tiles.Values)
                {
                    tile.SuspendPopupPlacementForResize();
                    tile.SetVideoSurfaceVisible(false);
                }
            }

            if (applyLayout != null) applyLayout();

            if (reloadCameraBadges)
            {
                // Force the final WPF bounds now. ResumePopupPlacement...
                // queues each popup at Render priority, after these bounds
                // have been committed but without waiting for D3D ContextIdle.
                CameraGrid.InvalidateMeasure();
                CameraGrid.InvalidateArrange();
                CameraGrid.UpdateLayout();
                foreach (var tile in _tiles.Values)
                    tile.ResumePopupPlacementAfterResize();
            }

            RunAfterNativeLayout(new Action(() =>
            {
                if (_disposed || transitionVersion != _geometryTransitionVersion || _fullscreenTile != null)
                {
                    if (transitionVersion == _geometryTransitionVersion)
                        _geometryTransitionInProgress = false;
                    return;
                }

                try
                {
                    CameraGrid.InvalidateMeasure();
                    CameraGrid.InvalidateArrange();
                    CameraGrid.UpdateLayout();
                    if (suspendVideoSurfaces)
                    {
                        CameraGrid.Visibility = Visibility.Visible;
                        foreach (var tile in _tiles.Values)
                        {
                            tile.SynchronizeNativeVideoSurfaces();
                            tile.SetVideoSurfaceVisible(tile.Slot == null ||
                                tile.Slot.State != LiveConnectionState_v3.Error);
                            tile.ResumePopupPlacementAfterResize();
                        }
                    }
                    QueueHeaderActionCentering();
                }
                finally
                {
                    if (transitionVersion == _geometryTransitionVersion)
                        _geometryTransitionInProgress = false;
                }
            }));
        }

        private void QueueHeaderActionCentering()
        {
            if (_disposed || _headerActionCenteringQueued)
                return;

            _headerActionCenteringQueued = true;
            // Grid columns reserve different widths for the title/sidebar
            // controls at narrow aspect ratios. Calculate against the real
            // header bounds after arrange so the icon group stays at the
            // actual visual centre for every window size.
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _headerActionCenteringQueued = false;
                if (!IsLoaded || !LivePageHeader.IsVisible ||
                    LivePageHeader.ActualWidth <= 0 || HeaderActionPanel.ActualWidth <= 0)
                    return;
                var shellWindow = Window.GetWindow(this) as ShellWindow_v3;
                if (shellWindow != null && shellWindow.IsVirtualDesktopMode &&
                    FormsScreen.AllScreens.Length > 1 && CameraGrid.ActualWidth > 0)
                {
                    // The centre of the virtual desktop is often exactly at
                    // the bezel between two monitors. Keep the header actions
                    // together on the left display instead of splitting the
                    // controls across the physical screen boundary.
                    // Preserve the complete title/status block; actions begin
                    // immediately after it instead of covering its text.
                    var desiredLeft = HeaderTitlePanel.TranslatePoint(
                        new Point(HeaderTitlePanel.ActualWidth + 16, 0), this).X;
                    var currentLeft = HeaderActionPanel.TranslatePoint(new Point(0, 0), this).X;
                    _headerActionTransform.X = desiredLeft - (currentLeft - _headerActionTransform.X);
                    return;
                }
                // Centre against the camera grid itself. The surrounding
                // Shell navigation and camera sidebar are intentionally not
                // included in this visual centre.
                var target = CameraGrid.ActualWidth > 0
                    ? (FrameworkElement)CameraGrid
                    : LivePageHeader;
                var headerCenter = target.TranslatePoint(
                    new Point(target.ActualWidth / 2.0, target.ActualHeight / 2.0), this).X;
                var actionsCenter = HeaderActionPanel.TranslatePoint(
                    new Point(HeaderActionPanel.ActualWidth / 2.0, HeaderActionPanel.ActualHeight / 2.0), this).X;
                // TranslatePoint includes the transform already applied by a
                // previous queued resize callback. Remove it first, otherwise
                // successive callbacks alternately over-correct the centre.
                var untransformedActionsCenter = actionsCenter - _headerActionTransform.X;
                _headerActionTransform.X = headerCenter - untransformedActionsCenter;
            }));
        }

        private void NormalizeCameraSidebarLayout()
        {
            var panel = CameraSidebar.Child as Grid;
            if (panel == null) return;
            foreach (UIElement child in panel.Children)
            {
                if (child is ScrollViewer)
                    Grid.SetRow(child, 5);
                else if (child is TextBox && Grid.GetRow(child) == 2)
                    child.Visibility = Visibility.Collapsed;
                else if (child is Grid && Grid.GetRow(child) == 3)
                    child.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateFilterButtonVisuals()
        {
            var selected = (System.Windows.Media.Brush)FindResource("VmsPrimarySoftBrush_v3");
            var normal = (System.Windows.Media.Brush)FindResource("VmsSurface2Brush_v3");
            var selectedBorder = (System.Windows.Media.Brush)FindResource("VmsPrimaryBrush_v3");
            var normalBorder = (System.Windows.Media.Brush)FindResource("VmsBorderBrush_v3");
            AllCameraFilterButton.Background = _viewModel.AiOnly ? normal : selected;
            AllCameraFilterButton.BorderBrush = _viewModel.AiOnly ? normalBorder : selectedBorder;
            AiCameraFilterButton.Background = _viewModel.AiOnly ? selected : normal;
            AiCameraFilterButton.BorderBrush = _viewModel.AiOnly ? selectedBorder : normalBorder;
            AllCameraFilterButton.IsEnabled = true;
            AiCameraFilterButton.IsEnabled = true;
        }

        private void BuildGrid(bool deferStaleCleanup = false, Action staleCleanupCompleted = null)
        {
            // Rebuild only the layout chrome. Keep tile/player instances for
            // unchanged slots so selecting another camera does not reconnect
            // or reload cameras that are already running.
            var previousTiles = new Dictionary<int, LiveTile_v3>(_tiles);
            _tiles.Clear();
            CameraGrid.RowDefinitions.Clear();
            CameraGrid.ColumnDefinitions.Clear();
            var hasMergedCustomLayout = _viewModel.Layout == LiveLayoutMode_v3.Custom &&
                                        _customLayoutRows > 0 && _customLayoutColumns > 0 &&
                                        _customLayoutCells.Count > 0 &&
                                        _customLayoutCells.Count == _viewModel.Slots.Count;
            var dimensions = hasMergedCustomLayout
                ? Tuple.Create(_customLayoutRows, _customLayoutColumns)
                : GetDimensions(_viewModel.Layout, _viewModel.Slots.Count);
            var compactGrid = dimensions.Item1 >= 5 || dimensions.Item2 >= 5;
            // Use the current viewport to keep every requested custom slot
            // inside the live panel.  This is intentionally not a fixed 6x6
            // cap: installations with many cameras can use their full grid.
            var viewportWidth = Math.Max(1d, CameraGrid.ActualWidth);
            var viewportHeight = Math.Max(1d, CameraGrid.ActualHeight);
            var minimumTileHeight = compactGrid ? Math.Max(32d, Math.Min(92d, viewportHeight / dimensions.Item1)) : 120d;
            var minimumTileWidth = compactGrid ? Math.Max(44d, Math.Min(118d, viewportWidth / dimensions.Item2)) : 150d;
            for (var row = 0; row < dimensions.Item1; row++) CameraGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = minimumTileHeight });
            for (var column = 0; column < dimensions.Item2; column++)
            {
                CameraGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = minimumTileWidth });
            }

            var visualIndex = 0;
            foreach (var slot in _viewModel.Slots)
            {
                LiveTile_v3 tile;
                if (!previousTiles.TryGetValue(slot.SlotId, out tile) || tile.Slot == null)
                {
                    tile = new LiveTile_v3();
                    tile.RemoveRequested += Tile_RemoveRequested;
                    tile.FullscreenRequested += Tile_FullscreenRequested;
                    tile.StateChanged += Tile_StateChanged;
                    tile.AllowDrop = true;
                    tile.PreviewMouseLeftButtonDown += Tile_PreviewMouseLeftButtonDown;
                    tile.PreviewMouseMove += Tile_PreviewMouseMove;
                    tile.Drop += Tile_Drop;
                }
                // A layout-only rebuild must not rebind every existing tile:
                // Bind refreshes native WindowsFormsHost surfaces and, with
                // several cameras, makes all decoders resize at once. Keep
                // the established pipelines untouched unless this tile was
                // newly created or its slot instance genuinely changed.
                if (tile.RequiresBind(slot))
                    tile.Bind(slot);
                var customCell = hasMergedCustomLayout ? _customLayoutCells[visualIndex] : null;
                var placement = customCell == null
                    ? GetPlacement(_viewModel.Layout, visualIndex, dimensions.Item2)
                    : Tuple.Create(customCell.Row, customCell.Column, customCell.RowSpan, customCell.ColumnSpan);
                visualIndex++;
                Grid.SetRow(tile, placement.Item1);
                Grid.SetColumn(tile, placement.Item2);
                Grid.SetRowSpan(tile, placement.Item3);
                Grid.SetColumnSpan(tile, placement.Item4);
                if (!CameraGrid.Children.Contains(tile)) CameraGrid.Children.Add(tile);
                _tiles[slot.SlotId] = tile;
                previousTiles.Remove(slot.SlotId);
            }
            var staleTiles = previousTiles.Values.ToArray();
            foreach (var stale in staleTiles)
            {
                CameraGrid.Children.Remove(stale);
                stale.RemoveRequested -= Tile_RemoveRequested;
                stale.FullscreenRequested -= Tile_FullscreenRequested;
                stale.StateChanged -= Tile_StateChanged;
                stale.PreviewMouseLeftButtonDown -= Tile_PreviewMouseLeftButtonDown;
                stale.PreviewMouseMove -= Tile_PreviewMouseMove;
                stale.Drop -= Tile_Drop;
            }
            if (deferStaleCleanup)
                ScheduleTileCleanup(staleTiles, disposeTiles: true, completed: staleCleanupCompleted);
            else
                foreach (var stale in staleTiles) stale.Dispose();
            _viewModel.RefreshCameraIndicators();
            UpdateStatus();
            var popupTiles = _tiles.Values.ToArray();
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                foreach (var popupTile in popupTiles)
                    popupTile.RefreshPopupPlacement();
            }));
        }

        private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragTile = sender as LiveTile_v3;
            _dragStart = e.GetPosition(CameraGrid);
        }

        private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragTile == null || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(CameraGrid);
            if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var source = _dragTile;
            _dragTile = null;
            if (source.Slot != null && source.Slot.Camera != null)
                DragDrop.DoDragDrop(source, source, DragDropEffects.Move);
        }

        private void Tile_Drop(object sender, DragEventArgs e)
        {
            var target = sender as LiveTile_v3;
            var source = e.Data.GetData(typeof(LiveTile_v3)) as LiveTile_v3;
            if (source == null) source = _dragTile;
            if (source == null || target == null || source.Slot == null || target.Slot == null || ReferenceEquals(source, target)) return;
            _viewModel.SwapSlots(source.Slot, target.Slot);
            BuildGrid();
            _viewModel.RefreshCameraIndicators();
            e.Handled = true;
        }

        private Tuple<int, int> GetDimensions(LiveLayoutMode_v3 layout, int count)
        {
            if (layout == LiveLayoutMode_v3.Layout1x1) return Tuple.Create(1, 1);
            if (layout == LiveLayoutMode_v3.Layout2x2) return Tuple.Create(2, 2);
            if (layout == LiveLayoutMode_v3.Layout5Plus1 || layout == LiveLayoutMode_v3.Layout3x3) return Tuple.Create(3, 3);
            if (layout == LiveLayoutMode_v3.Layout16Plus1) return Tuple.Create(5, 5);
            if (layout == LiveLayoutMode_v3.Layout6x6) return Tuple.Create(6, 6);
            // Fit the grid to the viewport aspect ratio instead of using a
            // square-only grid.  A wide camera wall therefore uses more
            // columns and fewer rows, avoiding the 10x10 overflow seen when
            // a large group is selected.
            var width = CameraGrid.ActualWidth > 0 ? CameraGrid.ActualWidth : 16d;
            var height = CameraGrid.ActualHeight > 0 ? CameraGrid.ActualHeight : 9d;
            var aspectRatio = Math.Max(1d, width / Math.Max(1d, height));
            var columns = Math.Max(1, Math.Min(count, (int)Math.Ceiling(Math.Sqrt(count * aspectRatio))));
            return Tuple.Create((int)Math.Ceiling((double)count / columns), columns);
        }

        private static Tuple<int, int, int, int> GetPlacement(LiveLayoutMode_v3 layout, int position, int columns)
        {
            if (layout == LiveLayoutMode_v3.Layout5Plus1)
            {
                var places = new[] { Tuple.Create(0, 0, 2, 2), Tuple.Create(0, 2, 1, 1), Tuple.Create(1, 2, 1, 1), Tuple.Create(2, 0, 1, 1), Tuple.Create(2, 1, 1, 1), Tuple.Create(2, 2, 1, 1) };
                return places[position];
            }
            if (layout == LiveLayoutMode_v3.Layout16Plus1)
            {
                if (position == 0) return Tuple.Create(1, 1, 3, 3);
                var ring = new[] { Tuple.Create(0,0),Tuple.Create(0,1),Tuple.Create(0,2),Tuple.Create(0,3),Tuple.Create(0,4),Tuple.Create(1,4),Tuple.Create(2,4),Tuple.Create(3,4),Tuple.Create(4,4),Tuple.Create(4,3),Tuple.Create(4,2),Tuple.Create(4,1),Tuple.Create(4,0),Tuple.Create(3,0),Tuple.Create(2,0),Tuple.Create(1,0) };
                var point = ring[position - 1];
                return Tuple.Create(point.Item1, point.Item2, 1, 1);
            }
            return Tuple.Create(position / columns, position % columns, 1, 1);
        }

        private async void Camera_Click(object sender, RoutedEventArgs e)
        {
            var camera = (sender as FrameworkElement)?.Tag as Camera;
            var slot = _viewModel.ToggleCamera(camera);
            if (slot == null) return;
            BuildGrid();
            if (slot.Camera != null && _tiles.ContainsKey(slot.SlotId))
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                await _tiles[slot.SlotId].ConnectAsync();
            }
        }

        private async void Camera_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_disposed || !IsLoaded) return;
            if (e.ChangedButton != MouseButton.Left) return;
            var camera = (sender as FrameworkElement)?.Tag as Camera;
            if (camera == null) return;

            if (e.ClickCount == 2)
            {
                // One owner for this gesture: the second downstroke cancels
                // the pending single-click before it can toggle this camera.
                e.Handled = true;
                ++_cameraClickVersion;
                _pendingCameraClick = null;
                var slots = _viewModel.FillFromCamera(camera);
                BuildGrid();
                _ = ConnectSlotsDeferredAsync(slots, BeginCameraOperation());
                return;
            }

            if (e.ClickCount != 1) return;
            // No Click/MouseUp handler is allowed to toggle the same row.
            // A single click is committed only after the system double-click
            // interval has expired.
            e.Handled = true;
            var version = ++_cameraClickVersion;
            _pendingCameraClick = camera;
            var doubleClickDelay = Math.Max(500, System.Windows.Forms.SystemInformation.DoubleClickTime + 50);
            await Task.Delay(doubleClickDelay);
            if (_disposed || !IsLoaded) return;
            if (version != _cameraClickVersion || !ReferenceEquals(_pendingCameraClick, camera)) return;
            _pendingCameraClick = null;
            var activeSlot = _viewModel.Slots.FirstOrDefault(item =>
                item.Camera != null && string.Equals(item.Camera.camID, camera.camID, StringComparison.OrdinalIgnoreCase));
            if (activeSlot != null && activeSlot.IsConnected &&
                DateTime.UtcNow - activeSlot.ConnectedAtUtc < TimeSpan.FromSeconds(1))
                return;
            var slot = _viewModel.ToggleCamera(camera);
            if (slot == null) return;
            BuildGrid();
            _viewModel.RefreshCameraIndicators();
            if (slot.Camera != null && _tiles.ContainsKey(slot.SlotId))
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                await _tiles[slot.SlotId].ConnectAsync();
            }
        }

        private CancellationToken BeginCameraOperation()
        {
            _cameraOperation.Cancel();
            _cameraOperation.Dispose();
            _cameraOperation = new CancellationTokenSource();
            return _cameraOperation.Token;
        }

        private async Task ConnectSlotsDeferredAsync(IEnumerable<LiveSlotViewModel_v3> slots, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var targets = (slots ?? Enumerable.Empty<LiveSlotViewModel_v3>())
                    .Where(slot => slot != null &&
                        (slot.Camera == null || slot.Camera.is_online != false) &&
                        slot.State != LiveConnectionState_v3.Connected &&
                        slot.State != LiveConnectionState_v3.Connecting)
                    .Select(slot =>
                    {
                        LiveTile_v3 tile;
                        return _tiles.TryGetValue(slot.SlotId, out tile) ? tile : null;
                    })
                    .Where(tile => tile != null)
                    .ToArray();
                cancellationToken.ThrowIfCancellationRequested();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                cancellationToken.ThrowIfCancellationRequested();
                // Start every pipeline in one batch and deliberately do not
                // await the whole batch here.  A tile exposes its surface on
                // its own Playing event, so a fast camera is shown instantly
                // while slow/offline cameras continue independently.
                var connectTasks = targets.Select(tile => tile.ConnectAsync()).ToArray();
                _ = Task.WhenAll(connectTasks).ContinueWith(task =>
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (task.IsFaulted)
                            LoggerManager.LogException(task.Exception, "Live View _v3 batch camera connection failed");
                        if (!_disposed) UpdateStatus();
                    })));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 deferred camera connection failed"); }
        }

        private void AllCameraFilter_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AiOnly = false;
            UpdateFilterButtonVisuals();
        }

        private void AiCameraFilter_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AiOnly = true;
            UpdateFilterButtonVisuals();
        }

        private async void Camera_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var camera = (sender as FrameworkElement)?.Tag as Camera;
            var slots = _viewModel.FillFromCamera(camera);
            BuildGrid();
            await ConnectSlotsDeferredAsync(slots, BeginCameraOperation());
        }

        private Camera ContextCamera(object sender)
        {
            var item = (sender as FrameworkElement)?.DataContext as LiveCameraItemViewModel_v3;
            return item == null ? null : item.Camera;
        }

        private void ContextSelect_Click(object sender, RoutedEventArgs e)
        {
            var camera = ContextCamera(sender);
            if (camera == null) return;
            _viewModel.ToggleCamera(camera);
            BuildGrid();
        }

        private async void ContextFill_Click(object sender, RoutedEventArgs e)
        {
            var camera = ContextCamera(sender);
            if (camera == null) return;
            var slots = _viewModel.FillFromCamera(camera);
            BuildGrid();
            await ConnectSlotsDeferredAsync(slots, BeginCameraOperation());
        }

        private async void ContextConnect_Click(object sender, RoutedEventArgs e)
        {
            var camera = ContextCamera(sender);
            var slot = camera == null ? null : _viewModel.Slots.FirstOrDefault(item => SameCamera(item.Camera, camera));
            if (slot == null)
            {
                slot = _viewModel.ToggleCamera(camera);
                BuildGrid();
            }
            if (slot != null && _tiles.ContainsKey(slot.SlotId)) await _tiles[slot.SlotId].ConnectAsync();
        }

        private async void ContextDisconnect_Click(object sender, RoutedEventArgs e)
        {
            var camera = ContextCamera(sender);
            var slot = camera == null ? null : _viewModel.Slots.FirstOrDefault(item => SameCamera(item.Camera, camera));
            if (slot != null && _tiles.ContainsKey(slot.SlotId))
            {
                _tiles[slot.SlotId].Disconnect();
            }
        }

        private async void ContextRemove_Click(object sender, RoutedEventArgs e)
        {
            var camera = ContextCamera(sender);
            var slot = camera == null ? null : _viewModel.Slots.FirstOrDefault(item => SameCamera(item.Camera, camera));
            if (slot == null) return;
            if (_tiles.ContainsKey(slot.SlotId)) _tiles[slot.SlotId].Disconnect();
            _viewModel.ClearSlot(slot);
            BuildGrid();
        }

        private static bool SameCamera(Camera left, Camera right)
        {
            return left != null && right != null && string.Equals(left.camID, right.camID, StringComparison.OrdinalIgnoreCase);
        }

        private Camera InlineCamera(object sender)
        {
            return (sender as FrameworkElement)?.Tag as Camera;
        }

        private async void InlineFill_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var camera = InlineCamera(sender);
            if (camera == null) return;
            var slots = _viewModel.FillFromCamera(camera);
            BuildGrid();
            _viewModel.RefreshCameraIndicators();
            await ConnectSlotsDeferredAsync(slots, BeginCameraOperation());
        }

        private void CameraGridViewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // A Grid measured by a vertical ScrollViewer receives infinite
            // height.  Consequently, an error overlay with wrapped text can
            // dictate the desired height of its entire row.  Make the camera
            // wall exactly the viewport size so star rows/columns stay equal
            // regardless of a tile's connection state.
            if (CameraGridViewport.ActualWidth <= 0 || CameraGridViewport.ActualHeight <= 0)
                return;

            var width = CameraGridViewport.ActualWidth;
            var height = CameraGridViewport.ActualHeight;
            if (double.IsNaN(CameraGrid.Width) || Math.Abs(CameraGrid.Width - width) > 0.5)
                CameraGrid.Width = width;
            if (double.IsNaN(CameraGrid.Height) || Math.Abs(CameraGrid.Height - height) > 0.5)
                CameraGrid.Height = height;

            // Do not call BuildGrid from SizeChanged. Rebuilding creates and
            // arranges native video hosts, which raises SizeChanged again and
            // can spiral into an unbounded layout/repaint loop. The existing
            // tiles remain intact; only their row/column limits are updated.
            UpdateGridCellMinimums();
            ScheduleResizeSettle();
        }

        private void UpdateGridCellMinimums()
        {
            var rows = CameraGrid.RowDefinitions.Count;
            var columns = CameraGrid.ColumnDefinitions.Count;
            if (rows == 0 || columns == 0 ||
                CameraGridViewport.ActualWidth <= 0 || CameraGridViewport.ActualHeight <= 0)
                return;

            var compactGrid = rows >= 5 || columns >= 5;
            var minimumHeight = compactGrid
                ? Math.Max(32d, Math.Min(92d, CameraGridViewport.ActualHeight / rows))
                : 120d;
            var minimumWidth = compactGrid
                ? Math.Max(44d, Math.Min(118d, CameraGridViewport.ActualWidth / columns))
                : 150d;

            // Avoid invalidating the complete Grid for every pixel dragged
            // when the effective minimum did not actually change.
            foreach (var row in CameraGrid.RowDefinitions)
                if (Math.Abs(row.MinHeight - minimumHeight) > 0.5)
                    row.MinHeight = minimumHeight;
            foreach (var column in CameraGrid.ColumnDefinitions)
                if (Math.Abs(column.MinWidth - minimumWidth) > 0.5)
                    column.MinWidth = minimumWidth;
        }

        private async void InlineConnect_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var camera = InlineCamera(sender);
            if (camera == null) return;
            var slot = _viewModel.Slots.FirstOrDefault(item => SameCamera(item.Camera, camera)) ?? _viewModel.ToggleCamera(camera);
            BuildGrid();
            _viewModel.RefreshCameraIndicators();
            if (slot != null && _tiles.ContainsKey(slot.SlotId)) await _tiles[slot.SlotId].ConnectAsync();
        }

        private void LayoutButton_Click(object sender, RoutedEventArgs e)
        {
            LiveLayoutMode_v3 layout;
            if (!Enum.TryParse(Convert.ToString((sender as FrameworkElement)?.Tag), out layout)) return;
            _viewModel.SetLayout(layout);
            LayoutMenuButton.Content = "▦ " + LayoutLabel(layout);
            LayoutPopup.IsOpen = false;
            BuildGrid(deferStaleCleanup: true);
        }

        private void LayoutMenuButton_Click(object sender, RoutedEventArgs e) { LayoutPopup.IsOpen = !LayoutPopup.IsOpen; }
        private void LayoutMenu_MouseEnter(object sender, MouseEventArgs e) { LayoutPopup.IsOpen = true; }
        private void LayoutPopup_MouseLeave(object sender, MouseEventArgs e) { LayoutPopup.IsOpen = false; }

        private void DisplayMenuButton_Click(object sender, RoutedEventArgs e)
        {
            var shellWindow = Window.GetWindow(this) as ShellWindow_v3;
            if (shellWindow != null)
            {
                shellWindow.ToggleVirtualDesktopMode();
                // ShellWindow has now committed its virtual-screen bounds.
                // Reapply only the Grid placement (never the camera bindings)
                // so preset layouts become monitor-safe immediately.
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    if (!_disposed && _fullscreenTile == null)
                    {
                        BuildGrid(deferStaleCleanup: true);
                        QueueHeaderActionCentering();
                    }
                }));
            }
        }

        private void DisplayMode_Click(object sender, RoutedEventArgs e)
        {
            var mode = Convert.ToString((sender as FrameworkElement)?.Tag);
            if (string.IsNullOrWhiteSpace(mode)) return;

            try
            {
                // DisplaySwitch is the supported Windows command behind the
                // Win+P options: internal, clone, extend and external.
                Process.Start(new ProcessStartInfo
                {
                    FileName = System.IO.Path.Combine(Environment.SystemDirectory, "DisplaySwitch.exe"),
                    Arguments = mode,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Không thể thay đổi chế độ nhiều màn hình", ex);
            }
        }

        private void RebuildDisplayScreenList()
        {
            DisplayScreenList.Children.Clear();
            var screens = FormsScreen.AllScreens;
            for (var index = 0; index < screens.Length; index++)
            {
                var screen = screens[index];
                var displayNumber = index + 1;
                var label = string.Format(
                    "Màn hình {0}{1} · {2} × {3}",
                    displayNumber,
                    screen.Primary ? " (chính)" : string.Empty,
                    screen.WorkingArea.Width,
                    screen.WorkingArea.Height);
                var button = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new MahApps.Metro.IconPacks.PackIconMaterial
                            {
                                Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.Monitor,
                                Width = 14,
                                Height = 14,
                                Margin = new Thickness(0, 0, 6, 0)
                            },
                            new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 10 }
                        }
                    },
                    Style = (Style)FindResource("SecondaryButtonStyle_v3"),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 1, 0, 1),
                    Padding = new Thickness(7, 5, 7, 5),
                    ToolTip = "Mở lưới camera toàn màn hình tại màn hình này"
                };
                button.Click += (s, e) =>
                {
                    DisplayPopup.IsOpen = false;
                    SetGridFullscreen(true, screen);
                };
                DisplayScreenList.Children.Add(button);
            }
        }

        private static string LayoutLabel(LiveLayoutMode_v3 layout)
        {
            switch (layout)
            {
                case LiveLayoutMode_v3.Layout1x1: return "1x1";
                case LiveLayoutMode_v3.Layout2x2: return "2x2";
                case LiveLayoutMode_v3.Layout3x3: return "3x3";
                case LiveLayoutMode_v3.Layout5Plus1: return "5+1";
                case LiveLayoutMode_v3.Layout16Plus1: return "16+1";
                case LiveLayoutMode_v3.Layout6x6: return "6x6";
                default: return "Custom";
            }
        }

        private void CustomLayout_Click(object sender, RoutedEventArgs e)
        {
            int count;
            if (!int.TryParse(CustomSlotText.Text, out count)) count = 10;
            count = Math.Max(1, count);
            _viewModel.ApplyCustomLayout(count);
            CustomSlotText.Text = _viewModel.CustomSlotCount.ToString();
            LayoutMenuButton.Content = "▦ Custom";
            LayoutPopup.IsOpen = false;
            BuildGrid(deferStaleCleanup: true);
        }

        private void InlineCustomLayout_Click(object sender, RoutedEventArgs e)
        {
            int count;
            if (!int.TryParse(InlineCustomSlotText.Text, out count)) count = 10;
            count = Math.Max(1, count);
            _viewModel.ApplyCustomLayout(count);
            InlineCustomSlotText.Text = _viewModel.CustomSlotCount.ToString();
            LayoutMenuButton.Content = "Layout " + _viewModel.CustomSlotCount;
            BuildGrid(deferStaleCleanup: true);
        }

        private void VisibleCustomLayout_Click(object sender, RoutedEventArgs e)
        {
            int count;
            if (!int.TryParse(VisibleCustomSlotText.Text, out count)) count = 10;
            count = Math.Max(1, count);
            _viewModel.ApplyCustomLayout(count);
            VisibleCustomSlotText.Text = _viewModel.CustomSlotCount.ToString();
            LayoutMenuButton.Content = "Layout " + _viewModel.CustomSlotCount;
            BuildGrid(deferStaleCleanup: true);
        }

        private void OpenCustomLayoutEditor_Click(object sender, RoutedEventArgs e)
        {
            OpenCustomLayoutEditor();
        }

        private void OpenCustomLayoutEditor()
        {
            int rows, columns;
            List<CustomLayoutCell_v3> currentCells;
            GetLayoutForEditor(out rows, out columns, out currentCells);
            var dialog = new CustomLayoutDialog_v3(rows, columns, currentCells)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true) return;

            _customLayoutRows = dialog.Rows;
            _customLayoutColumns = dialog.Columns;
            _customLayoutCells = dialog.LayoutCells.Select(cell => cell.Clone()).ToList();
            _viewModel.ApplyCustomLayout(_customLayoutCells.Count);
            CustomSlotText.Text = _customLayoutCells.Count.ToString();
            if (InlineCustomSlotText != null) InlineCustomSlotText.Text = _customLayoutCells.Count.ToString();
            if (VisibleCustomSlotText != null) VisibleCustomSlotText.Text = _customLayoutCells.Count.ToString();
            LayoutMenuButton.Content = "Custom";
            LayoutPopup.IsOpen = false;
            BuildGrid(deferStaleCleanup: true);
        }

        private void GetLayoutForEditor(out int rows, out int columns, out List<CustomLayoutCell_v3> cells)
        {
            if (_viewModel.Layout == LiveLayoutMode_v3.Custom && _customLayoutRows > 0 &&
                _customLayoutColumns > 0 && _customLayoutCells.Count == _viewModel.Slots.Count)
            {
                rows = _customLayoutRows;
                columns = _customLayoutColumns;
                cells = _customLayoutCells.Select(cell => cell.Clone()).ToList();
                return;
            }

            var dimensions = GetDimensions(_viewModel.Layout, _viewModel.Slots.Count);
            rows = dimensions.Item1;
            columns = dimensions.Item2;
            var editorColumns = columns;
            cells = _viewModel.Slots.Select((slot, index) =>
            {
                var placement = GetPlacement(_viewModel.Layout, index, editorColumns);
                return new CustomLayoutCell_v3
                {
                    Row = placement.Item1,
                    Column = placement.Item2,
                    RowSpan = placement.Item3,
                    ColumnSpan = placement.Item4
                };
            }).ToList();
        }

        private void CustomPreset_Click(object sender, RoutedEventArgs e)
        {
            int count;
            if (!int.TryParse(Convert.ToString((sender as FrameworkElement)?.Tag), out count)) return;
            _customLayoutRows = 0;
            _customLayoutColumns = 0;
            _customLayoutCells.Clear();
            _viewModel.ApplyCustomLayout(count);
            CustomSlotText.Text = _viewModel.CustomSlotCount.ToString();
            LayoutMenuButton.Content = "Layout " + _viewModel.CustomSlotCount;
            LayoutPopup.IsOpen = false;
            BuildGrid(deferStaleCleanup: true);
        }

        private async void ConnectAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Fetch immediately before the bulk operation.  This avoids
                // spending decoder/network slots on cameras the backend has
                // already marked offline.
                await RefreshDeviceStatusesAsync();
                foreach (var offlineSlot in _viewModel.Slots.Where(slot => slot.Camera != null && slot.Camera.is_online == false))
                {
                    if (offlineSlot.State != LiveConnectionState_v3.Connected)
                        offlineSlot.State = LiveConnectionState_v3.Offline;
                }
                var connectTasks = _tiles.Values
                    .Where(tile => tile.Slot != null && tile.Slot.Camera != null && tile.Slot.Camera.is_online != false)
                    .Select(tile => tile.ConnectAsync())
                    .ToArray();
                await Task.WhenAll(connectTasks);
            }
            finally
            {
                UpdateStatus();
            }
        }

        private async void DisconnectAll_Click(object sender, RoutedEventArgs e)
        {
            var tiles = _tiles.Values.ToArray();
            foreach (var tile in tiles) tile.RequestDisconnect();
            CameraGrid.Visibility = Visibility.Visible;
            var cleanupTasks = tiles.Select(tile => tile.DisconnectInBackgroundAsync()).ToArray();
            _ = Task.WhenAll(cleanupTasks).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(UpdateStatus)));
        }

        private async void RemoveAll_Click(object sender, RoutedEventArgs e)
        {
            var tiles = _tiles.Values.ToArray();
            foreach (var tile in tiles) tile.RequestDisconnect();
            CameraGrid.Visibility = Visibility.Visible;
            var cleanupTasks = tiles.Select(tile => tile.DisconnectInBackgroundAsync()).ToArray();
            try { await Task.WhenAll(cleanupTasks); }
            catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 background remove cleanup failed"); }
            _viewModel.ClearAll();
            BuildGrid();
            UpdateStatus();
        }

        /// <summary>
        /// GStreamer owns WPF/WinForms handles, so pipeline disposal must remain
        /// on the dispatcher thread. The bulk command schedules one dispatcher
        /// batch, so all local stops begin in the same UI turn rather than being
        /// visibly staggered camera-by-camera.
        /// </summary>
        private void ScheduleTileCleanup(IEnumerable<LiveTile_v3> tiles, bool disposeTiles = false, Action completed = null)
        {
            var queue = new Queue<LiveTile_v3>((tiles ?? Enumerable.Empty<LiveTile_v3>()).Where(tile => tile != null));
            Action cleanupBatch = () =>
            {
                if (_disposed) return;
                if (disposeTiles)
                {
                    var staleTiles = queue.ToArray();
                    var backgroundDisposals = staleTiles.Select(tile => tile.DisconnectInBackgroundAsync()).ToArray();
                    _ = Task.WhenAll(backgroundDisposals).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        foreach (var tile in staleTiles)
                            try { tile.Dispose(); } catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 stale tile dispose failed"); }
                        completed?.Invoke();
                    })));
                    return;
                }
                while (queue.Count > 0)
                {
                    try
                    {
                        var tile = queue.Dequeue();
                        tile.Disconnect();
                    }
                    catch (Exception ex) { LoggerManager.LogException(ex, "Live View _v3 deferred tile cleanup failed"); }
                }
                completed?.Invoke();
            };
            if (queue.Count > 0)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, cleanupBatch);
            else
                completed?.Invoke();
        }

        private void RemoveErrors_Click(object sender, RoutedEventArgs e)
        {
            foreach (var slot in _viewModel.Slots.Where(slot => slot.HasError).ToList()) _viewModel.ClearSlot(slot);
            BuildGrid();
        }

        private void Tile_RemoveRequested(object sender, EventArgs e)
        {
            var tile = sender as LiveTile_v3;
            if (tile == null || tile.Slot == null) return;
            tile.Disconnect();
            _viewModel.ClearSlot(tile.Slot);
            BuildGrid();
        }

        private void Tile_FullscreenRequested(object sender, EventArgs e)
        {
            var tile = sender as LiveTile_v3;
            if (tile == null) return;
            if (_fullscreenTile == null)
            {
                var transitionVersion = ++_fullscreenTransitionVersion;
                _fullscreenTile = tile;
                // Keep the selected tile visible through the span/window
                // transition. Its existing sub-stream scales immediately;
                // hiding it here caused the noticeable black flash/stutter
                // before the fullscreen surface reappeared.
                CameraGrid.Visibility = Visibility.Visible;
                foreach (UIElement child in CameraGrid.Children)
                {
                    var cameraTile = child as LiveTile_v3;
                    if (cameraTile != null)
                    {
                        var selected = ReferenceEquals(cameraTile, tile);
                        cameraTile.HideForFullscreen();
                        cameraTile.SetFullscreenMode(selected);
                        cameraTile.HideTransientOverlays();
                        // A D3D sink retains its previous HWND rectangle for
                        // one render pass. Hide it during that pass so its old
                        // small grid-sized surface cannot flash in the centre
                        // of the fullscreen camera.
                        cameraTile.SetVideoSurfaceVisible(false);
                    }
                    child.Visibility = ReferenceEquals(child, tile) ? Visibility.Visible : Visibility.Collapsed;
                }
                Grid.SetRow(tile, 0); Grid.SetColumn(tile, 0);
                Grid.SetRowSpan(tile, Math.Max(1, CameraGrid.RowDefinitions.Count));
                Grid.SetColumnSpan(tile, Math.Max(1, CameraGrid.ColumnDefinitions.Count));
                tile.Opacity = 1;
                EnterTileFullscreen();
                // WindowState, shell chrome, and the grid spans all change
                // in this transition. Render priority occurs before the
                // shell's final arrange on small windows, producing the
                // visible left-aligned grid frame. Wait for stable layout
                // before exposing any WPF/native tile content.
                RunAfterNativeLayout(new Action(() =>
                {
                    if (_fullscreenTile == tile && transitionVersion == _fullscreenTransitionVersion)
                    {
                        CameraGrid.InvalidateMeasure();
                        CameraGrid.InvalidateArrange();
                        CameraGrid.UpdateLayout();
                        tile.SynchronizeNativeVideoSurfaces();
                        // Start the heavier main stream only after the WPF
                        // window and the already-playing grid stream have
                        // reached their final fullscreen bounds.
                        tile.SetVideoSurfaceVisible(tile.Slot == null ||
                            tile.Slot.State != LiveConnectionState_v3.Error);
                        if (tile.Slot != null && tile.Slot.Camera != null)
                            _ = tile.PrepareFullscreenMainStreamAsync(
                                LiveViewModel_v3.SelectFullscreenStream(tile.Slot.Camera));
                        tile.RefreshPopupPlacement();
                    }
                }));
            }
            else
            {
                ExitTileFullscreen();
            }
        }

        private void EnterTileFullscreen()
        {
            var window = Window.GetWindow(this);
            if (window == null) return;
            var shellWindow = window as ShellWindow_v3;
            _tileUsesVirtualDesktop = shellWindow != null && shellWindow.IsVirtualDesktopMode;
            // If the user is already viewing the complete grid fullscreen,
            // transfer directly to the selected-camera fullscreen state. Do
            // not restore the normal/small window first: doing so resizes all
            // native video surfaces once and produces a visible stutter.
            var transferFromGridFullscreen = _gridFullscreen;
            var windowStateToRestore = transferFromGridFullscreen && _gridWindowStateSaved
                ? _gridWindowState
                : window.WindowState;
            _tileWindowStyle = window.WindowStyle;
            _tileResizeMode = window.ResizeMode;
            _tileWindowState = windowStateToRestore;
            _tileTopmost = window.Topmost;
            _tileWindowLeft = window.Left;
            _tileWindowTop = window.Top;
            _tileWindowWidth = window.Width;
            _tileWindowHeight = window.Height;
            _tileSidebarVisibility = transferFromGridFullscreen ? Visibility.Visible : CameraSidebar.Visibility;
            _tileSidebarWidth = transferFromGridFullscreen
                ? new GridLength(0.24, GridUnitType.Star)
                : SidebarColumn.Width;
            _tileWindowStateSaved = true;
            if (transferFromGridFullscreen)
            {
                _gridFullscreen = false;
                _gridWindowStateSaved = false;
                UpdateFullscreenControls();
            }
            CameraSidebar.Visibility = Visibility.Collapsed;
            UpdateSidebarOpenButtons();
            SidebarColumn.MinWidth = 0;
            SidebarColumn.MaxWidth = double.PositiveInfinity;
            SidebarColumn.Width = new GridLength(0);
            LivePageHeader.Visibility = Visibility.Collapsed;
            var shell = GetShellPage(window);
            if (shell != null) shell.SetChromeVisible(false);
            // A selected camera is easier to observe on one monitor. Keep the
            // multi-monitor wall state in ShellWindow, but temporarily place
            // this presentation on the primary display. Exiting restores the
            // virtual desktop wall.
            if (_tileUsesVirtualDesktop)
            {
                shellWindow.EnterPrimaryScreenPresentation();
            }
            else
            {
                if (shellWindow != null)
                    shellWindow.EnterCurrentScreenFullscreen();
                else
                    window.WindowState = WindowState.Maximized;
            }
        }

        private void ExitTileFullscreen()
        {
            var transitionVersion = ++_fullscreenTransitionVersion;
            var window = Window.GetWindow(this);
            var fullscreenTile = _fullscreenTile;
            var fullscreenCamera = fullscreenTile == null || fullscreenTile.Slot == null
                ? null
                : fullscreenTile.Slot.Camera;
            _fullscreenTile = null;
            CameraGrid.Visibility = Visibility.Hidden;
            // Resume sub1 while main is still the visible fullscreen source.
            // This gives its decoder time to receive a clean frame before
            // the grid's native surfaces are revealed.
            if (fullscreenTile != null && fullscreenCamera != null)
                fullscreenTile.WarmGridStreamForRestore(LiveViewModel_v3.SelectGridStream(fullscreenCamera));
            // Keep every native video surface hidden while the window returns
            // from maximized mode. This mirrors the enter transition and
            // prevents all cameras from resizing in a visible intermediate
            // frame.
            foreach (var tile in _tiles.Values)
            {
                tile.Visibility = Visibility.Collapsed;
                tile.Opacity = 0;
                tile.SetVideoSurfaceVisible(false);
            }
            if (window != null && _tileWindowStateSaved)
            {
                var shell = GetShellPage(window);
                if (shell != null) shell.SetChromeVisible(true);
                var shellWindow = window as ShellWindow_v3;
                if (_tileUsesVirtualDesktop && shellWindow != null)
                {
                    shellWindow.RestoreVirtualDesktopPresentation();
                }
                else
                {
                    window.WindowState = _tileWindowState;
                    if (_tileWindowState == WindowState.Normal)
                    {
                        window.Left = _tileWindowLeft;
                        window.Top = _tileWindowTop;
                        window.Width = _tileWindowWidth;
                        window.Height = _tileWindowHeight;
                    }
                }
                window.ResizeMode = _tileResizeMode;
                window.Topmost = _tileTopmost;
            }
            _tileWindowStateSaved = false;
            _tileUsesVirtualDesktop = false;
            foreach (var tile in _tiles.Values)
            {
                // Fullscreen temporarily collapses every non-selected tile.
                // Restore both the native tile visibility and its overlays
                // before rebuilding the grid, otherwise only the fullscreen
                // camera remains visible after returning.
                tile.RestoreAfterFullscreen();
                tile.SetFullscreenMode(false);
                Panel.SetZIndex(tile, 0);
            }
            CameraSidebar.Visibility = _tileSidebarVisibility;
            UpdateSidebarOpenButtons();
            SidebarColumn.MinWidth = 210;
            SidebarColumn.MaxWidth = 320;
            SidebarColumn.Width = _tileSidebarWidth.Value > 0 ? _tileSidebarWidth : new GridLength(0.20, GridUnitType.Star);
            LivePageHeader.Visibility = Visibility.Visible;
            BuildGrid();
            foreach (var tile in _tiles.Values)
            {
                tile.Visibility = Visibility.Visible;
                tile.Opacity = 0;
                // BuildGrid may refresh a tile and re-enable its native
                // surface. Hide it again before yielding to WPF so it cannot
                // flash at (0,0) while row/column bounds are changing.
                tile.SetVideoSurfaceVisible(false);
            }
            // Do not switch main back to sub1 yet.  Switching the source while
            // the window is still restoring gives the native sink an old
            // fullscreen-sized HWND for one frame, then makes it jump into
            // the small grid.  First settle the *final* WPF bounds; only then
            // change presentation and reveal every tile as one frame.
            RunAfterNativeLayout(new Action(() =>
            {
                if (_fullscreenTile != null || transitionVersion != _fullscreenTransitionVersion) return;
                CameraGrid.InvalidateMeasure();
                CameraGrid.InvalidateArrange();
                CameraGrid.UpdateLayout();
                if (fullscreenTile != null && fullscreenCamera != null)
                    fullscreenTile.RestoreGridStream(LiveViewModel_v3.SelectGridStream(fullscreenCamera));
                CameraGrid.Visibility = Visibility.Visible;
                foreach (var tile in _tiles.Values)
                {
                    tile.SynchronizeNativeVideoSurfaces();
                    tile.SetVideoSurfaceVisible(tile.Slot == null ||
                        tile.Slot.State != LiveConnectionState_v3.Error);
                    tile.Opacity = 1;
                    tile.RefreshPopupPlacement();
                }
            }));
        }

        private void RunAfterNativeLayout(Action action)
        {
            // Two render turns are enough for WPF to commit the final HWND
            // bounds. ContextIdle can be delayed for seconds by active video
            // rendering, which made fullscreen/resize feel like it was
            // loading even though all decoder pipelines were already ready.
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                Dispatcher.BeginInvoke(DispatcherPriority.Render, action)));
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_fullscreenTile != null)
            {
                ExitTileFullscreen();
                return;
            }

            // The original client treats fullscreen as a virtual-desktop
            // wall: when Windows is in Extend mode, one camera wall spans
            // every display instead of choosing a single target screen.
            var shellWindow = Window.GetWindow(this) as ShellWindow_v3;
            if (shellWindow != null)
            {
                shellWindow.ToggleVirtualDesktopMode();
                QueueHeaderActionCentering();
            }
        }

        private void SetGridFullscreen(bool entering, FormsScreen targetScreen = null)
        {
            var window = Window.GetWindow(this);
            if (window == null) return;
            if (entering == _gridFullscreen && targetScreen == null) return;

                RunGridGeometryTransition(new Action(() =>
            {
                if (entering && !_gridFullscreen)
                {
                    _gridWindowState = window.WindowState;
                    // WindowState alone is not enough.  When a maximized WPF
                    // window is restored, its implicit RestoreBounds may be
                    // applied one dispatcher tick late, visibly placing the
                    // grid in a temporary corner position.  Save the exact
                    // normal bounds and restore them in the same transaction.
                    var bounds = window.WindowState == WindowState.Normal
                        ? new Rect(window.Left, window.Top, window.Width, window.Height)
                        : window.RestoreBounds;
                    _gridWindowLeft = bounds.Left;
                    _gridWindowTop = bounds.Top;
                    _gridWindowWidth = bounds.Width;
                    _gridWindowHeight = bounds.Height;
                    _gridWindowStateSaved = true;
                }
                _gridFullscreen = entering;
                var shell = GetShellPage(window);
                if (shell != null) shell.SetChromeVisible(!entering);
                CameraSidebar.Visibility = entering ? Visibility.Collapsed : Visibility.Visible;
                UpdateSidebarOpenButtons();
                SidebarColumn.MinWidth = entering ? 0 : 210;
                SidebarColumn.MaxWidth = entering ? double.PositiveInfinity : 320;
                SidebarColumn.Width = entering ? new GridLength(0) : new GridLength(0.20, GridUnitType.Star);
                // Apply all WPF-only chrome changes first, then perform exactly
                // one native window resize.
                if (entering)
                {
                    if (targetScreen != null)
                    {
                        // A maximized WPF window remains on its old monitor
                        // until it has normal bounds on the target monitor.
                        // Move it first, then maximize in the same native
                        // layout transaction so D3D video surfaces do not
                        // flash on the previous display.
                        window.WindowState = WindowState.Normal;
                        window.Left = targetScreen.WorkingArea.Left;
                        window.Top = targetScreen.WorkingArea.Top;
                        window.Width = targetScreen.WorkingArea.Width;
                        window.Height = targetScreen.WorkingArea.Height;
                    }
                    window.WindowState = WindowState.Maximized;
                }
                else
                {
                    var stateToRestore = _gridWindowStateSaved ? _gridWindowState : WindowState.Normal;
                    window.WindowState = stateToRestore;
                    if (stateToRestore == WindowState.Normal && _gridWindowWidth > 0 && _gridWindowHeight > 0)
                    {
                        // Set the final bounds immediately after leaving
                        // Maximized, while the camera grid is still hidden.
                        // This prevents the native video HWNDs from ever
                        // receiving the transient RestoreBounds rectangle.
                        window.Left = _gridWindowLeft;
                        window.Top = _gridWindowTop;
                        window.Width = _gridWindowWidth;
                        window.Height = _gridWindowHeight;
                    }
                }
                if (!entering) _gridWindowStateSaved = false;
                UpdateFullscreenControls();
                }), suspendVideoSurfaces: true);
        }

        private void UpdateFullscreenControls()
        {
            var tooltip = _gridFullscreen ? "Thu nhỏ" : "Toàn màn hình";
            var icon = _gridFullscreen
                ? MahApps.Metro.IconPacks.PackIconMaterialKind.FullscreenExit
                : MahApps.Metro.IconPacks.PackIconMaterialKind.Fullscreen;
        }

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            var hide = CameraSidebar.Visibility == Visibility.Visible;
            RunGridGeometryTransition(new Action(() =>
            {
                CameraSidebar.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
                SidebarColumn.MinWidth = hide ? 0 : 210;
                SidebarColumn.MaxWidth = hide ? double.PositiveInfinity : 320;
                SidebarColumn.Width = hide ? new GridLength(0) : new GridLength(0.20, GridUnitType.Star);
                UpdateSidebarOpenButtons();
            }), suspendVideoSurfaces: false);
        }

        private void UpdateSidebarOpenButtons()
        {
            // Keep a single camera-list tab.  The old in-grid opener and the
            // header opener were both made visible after switching fullscreen,
            // producing the duplicated chevrons shown in the UI.
            SidebarOpenButton.Visibility = Visibility.Collapsed;
            SidebarOpenHeaderButton.Visibility = LivePageHeader.Visibility == Visibility.Visible &&
                CameraSidebar.Visibility != Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateStatus()
        {
            if (_removeErrorsHeaderButton != null)
                _removeErrorsHeaderButton.Visibility = _viewModel.Slots.Any(slot => slot.HasError)
                    ? Visibility.Visible : Visibility.Collapsed;
            CameraStatus.Text = string.Format("{0} cameras · {1} groups · {2}/{3} active", _viewModel.CameraCount, _viewModel.GroupCount, _viewModel.ActiveCameraCount, _viewModel.Slots.Count);
            _viewModel.RefreshCameraIndicators();
        }

        private void DisposeTiles()
        {
            foreach (var tile in _tiles.Values)
            {
                tile.RemoveRequested -= Tile_RemoveRequested;
                tile.FullscreenRequested -= Tile_FullscreenRequested;
                tile.StateChanged -= Tile_StateChanged;
                tile.Dispose();
            }
            _tiles.Clear();
        }

        private void Tile_StateChanged(object sender, EventArgs e)
        {
            _viewModel.RefreshCameraIndicators();
            UpdateStatus();
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_fullscreenTile != null) ExitTileFullscreen();
            _disposed = true;
            _cameraOperation.Cancel();
            _cameraOperation.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _resizeSettledTimer.Stop();
            _resizeSettledTimer.Tick -= ResizeSettledTimer_Tick;
            _deviceStatusRefreshTimer.Stop();
            _deviceStatusRefreshTimer.Tick -= DeviceStatusRefreshTimer_Tick;
            LivePageHeader.SizeChanged -= LivePageHeader_SizeChanged;
            CameraGrid.SizeChanged -= CameraGrid_SizeChanged;
            SizeChanged -= LivePage_SizeChanged;
            DisposeTiles();
        }
    }
}
