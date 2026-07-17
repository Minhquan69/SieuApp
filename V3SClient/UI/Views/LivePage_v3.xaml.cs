using System;
using System.Collections.Generic;
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
using V3SClient.viewModels;

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
        private Visibility _tileSidebarVisibility;
        private GridLength _tileSidebarWidth;
        private bool _tileWindowStateSaved;
        private bool _gridFullscreen;
        private WindowState _gridWindowState;
        private bool _gridWindowStateSaved;
        private int _fullscreenTransitionVersion;
        private readonly TranslateTransform _headerActionTransform = new TranslateTransform();
        private LiveTile_v3 _dragTile;
        private Point _dragStart;

        public LivePage_v3()
        {
            InitializeComponent();
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

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CameraStatus.Text = string.Format("{0} cameras · {1} groups · {2}/{3} active", _viewModel.CameraCount, _viewModel.GroupCount, _viewModel.ActiveCameraCount, _viewModel.Slots.Count);
            EmptyState.Visibility = _viewModel.CameraCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            AllCameraFilterButton.IsEnabled = false;
            AiCameraFilterButton.IsEnabled = true;
            UpdateFilterButtonVisuals();
            NormalizeCameraSidebarLayout();
            BuildGrid();
            QueueHeaderActionCentering();
        }

        private void LivePageHeader_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueHeaderActionCentering();
        }

        private void LivePage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueHeaderActionCentering();
        }

        private void CameraGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueHeaderActionCentering();
        }

        private void QueueHeaderActionCentering()
        {
            // Grid columns reserve different widths for the title/sidebar
            // controls at narrow aspect ratios. Calculate against the real
            // header bounds after arrange so the icon group stays at the
            // actual visual centre for every window size.
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (!IsLoaded || !LivePageHeader.IsVisible ||
                    LivePageHeader.ActualWidth <= 0 || HeaderActionPanel.ActualWidth <= 0)
                    return;
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
            var dimensions = GetDimensions(_viewModel.Layout, _viewModel.Slots.Count);
            for (var row = 0; row < dimensions.Item1; row++) CameraGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
            for (var column = 0; column < dimensions.Item2; column++) CameraGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });

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
                tile.Bind(slot);
                var placement = GetPlacement(_viewModel.Layout, visualIndex++, dimensions.Item2);
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

        private static Tuple<int, int> GetDimensions(LiveLayoutMode_v3 layout, int count)
        {
            if (layout == LiveLayoutMode_v3.Layout1x1) return Tuple.Create(1, 1);
            if (layout == LiveLayoutMode_v3.Layout2x2) return Tuple.Create(2, 2);
            if (layout == LiveLayoutMode_v3.Layout5Plus1 || layout == LiveLayoutMode_v3.Layout3x3) return Tuple.Create(3, 3);
            if (layout == LiveLayoutMode_v3.Layout16Plus1) return Tuple.Create(5, 5);
            if (layout == LiveLayoutMode_v3.Layout6x6) return Tuple.Create(6, 6);
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
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
                foreach (var slot in (slots ?? Enumerable.Empty<LiveSlotViewModel_v3>()).ToList())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LiveTile_v3 tile;
                    if (!_tiles.TryGetValue(slot.SlotId, out tile)) continue;
                    if (slot.State == LiveConnectionState_v3.Connected ||
                        slot.State == LiveConnectionState_v3.Connecting)
                        continue;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    cancellationToken.ThrowIfCancellationRequested();
                    await tile.ConnectAsync();
                }
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
            foreach (var slot in slots) if (_tiles.ContainsKey(slot.SlotId)) await _tiles[slot.SlotId].ConnectAsync();
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
            foreach (var slot in slots) if (_tiles.ContainsKey(slot.SlotId)) await _tiles[slot.SlotId].ConnectAsync();
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

        private void InlineFill_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var camera = InlineCamera(sender);
            if (camera == null) return;
            _viewModel.FillFromCamera(camera);
            BuildGrid();
            _viewModel.RefreshCameraIndicators();
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
            count = Math.Max(1, Math.Min(100, count));
            _viewModel.ApplyCustomLayout(count);
            InlineCustomSlotText.Text = count.ToString();
            LayoutMenuButton.Content = "Layout " + count;
            BuildGrid(deferStaleCleanup: true);
        }

        private void VisibleCustomLayout_Click(object sender, RoutedEventArgs e)
        {
            int count;
            if (!int.TryParse(VisibleCustomSlotText.Text, out count)) count = 10;
            count = Math.Max(1, Math.Min(100, count));
            _viewModel.ApplyCustomLayout(count);
            VisibleCustomSlotText.Text = count.ToString();
            LayoutMenuButton.Content = "Layout " + count;
            BuildGrid(deferStaleCleanup: true);
        }


        private void CustomPreset_Click(object sender, RoutedEventArgs e)
        {
            int count;
            if (!int.TryParse(Convert.ToString((sender as FrameworkElement)?.Tag), out count)) return;
            _viewModel.ApplyCustomLayout(count);
            CustomSlotText.Text = count.ToString();
            LayoutMenuButton.Content = "Layout " + count;
            LayoutPopup.IsOpen = false;
            BuildGrid(deferStaleCleanup: true);
        }

        private async void ConnectAll_Click(object sender, RoutedEventArgs e)
        {
            var targets = _viewModel.Slots.Where(slot => slot.Camera != null).ToList();
            try
            {
                var connectTasks = _tiles.Values
                    .Where(tile => tile.Slot != null && tile.Slot.Camera != null)
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
                // Hide the grid while changing spans/window chrome. This keeps
                // WPF from painting an intermediate 4x4 layout before the
                // selected tile becomes fullscreen.
                CameraGrid.Visibility = Visibility.Hidden;
                foreach (UIElement child in CameraGrid.Children)
                {
                    var cameraTile = child as LiveTile_v3;
                    if (cameraTile != null)
                    {
                        var selected = ReferenceEquals(cameraTile, tile);
                        cameraTile.SetFullscreenVisibility(selected);
                        cameraTile.SetFullscreenMode(selected);
                        cameraTile.HideTransientOverlays();
                        // Keep the selected native surface alive. Hiding and
                        // showing a D3D11 RTSP sink can make it wait for the
                        // next keyframe, which looks like a long reconnect.
                        // The other surfaces are hidden before their bounds
                        // change, so they cannot flash over the selected tile.
                        cameraTile.SetVideoSurfaceVisible(selected &&
                            (cameraTile.Slot == null ||
                             cameraTile.Slot.State != LiveConnectionState_v3.Error));
                    }
                    child.Visibility = ReferenceEquals(child, tile) ? Visibility.Visible : Visibility.Collapsed;
                }
                Grid.SetRow(tile, 0); Grid.SetColumn(tile, 0);
                Grid.SetRowSpan(tile, Math.Max(1, CameraGrid.RowDefinitions.Count));
                Grid.SetColumnSpan(tile, Math.Max(1, CameraGrid.ColumnDefinitions.Count));
                tile.Opacity = 0;
                EnterTileFullscreen();
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    if (_fullscreenTile == tile && transitionVersion == _fullscreenTransitionVersion)
                    {
                        CameraGrid.UpdateLayout();
                        CameraGrid.Visibility = Visibility.Visible;
                        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                        {
                            if (_fullscreenTile != tile || transitionVersion != _fullscreenTransitionVersion) return;
                            tile.SetVideoSurfaceVisible(tile.Slot == null ||
                                tile.Slot.State != LiveConnectionState_v3.Error);
                            tile.Opacity = 1;
                            tile.RefreshPopupPlacement();
                        }));
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
            var shell = window.Content as ShellPage_v3;
            if (shell != null) shell.SetChromeVisible(false);
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.Topmost = true;
            // One maximized transition avoids the Normal -> manual resize ->
            // fullscreen sequence that causes the native GStreamer surface
            // to resize several times and visibly stutter.
            window.WindowState = WindowState.Maximized;
        }

        private void ExitTileFullscreen()
        {
            var transitionVersion = ++_fullscreenTransitionVersion;
            var window = Window.GetWindow(this);
            _fullscreenTile = null;
            CameraGrid.Visibility = Visibility.Hidden;
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
                var shell = window.Content as ShellPage_v3;
                if (shell != null) shell.SetChromeVisible(true);
                window.WindowState = _tileWindowState;
                window.WindowStyle = _tileWindowStyle;
                window.ResizeMode = _tileResizeMode;
                window.Left = _tileWindowLeft;
                window.Top = _tileWindowTop;
                window.Width = _tileWindowWidth;
                window.Height = _tileWindowHeight;
                window.Topmost = _tileTopmost;
            }
            _tileWindowStateSaved = false;
            foreach (var tile in _tiles.Values)
            {
                // Fullscreen temporarily collapses every non-selected tile.
                // Restore both the native tile visibility and its overlays
                // before rebuilding the grid, otherwise only the fullscreen
                // camera remains visible after returning.
                tile.SetFullscreenVisibility(false);
                tile.SetFullscreenMode(false);
                Panel.SetZIndex(tile, 0);
            }
            CameraSidebar.Visibility = _tileSidebarVisibility;
            UpdateSidebarOpenButtons();
            SidebarColumn.MinWidth = 240;
            SidebarColumn.MaxWidth = 420;
            SidebarColumn.Width = _tileSidebarWidth.Value > 0 ? _tileSidebarWidth : new GridLength(0.24, GridUnitType.Star);
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
            RunAfterNativeLayout(new Action(() =>
            {
                if (_fullscreenTile != null || transitionVersion != _fullscreenTransitionVersion) return;
                CameraGrid.InvalidateMeasure();
                CameraGrid.InvalidateArrange();
                CameraGrid.UpdateLayout();
                CameraGrid.Visibility = Visibility.Visible;
                RunAfterNativeLayout(new Action(() =>
                {
                    if (_fullscreenTile != null || transitionVersion != _fullscreenTransitionVersion) return;
                    foreach (var tile in _tiles.Values)
                    {
                        tile.SetVideoSurfaceVisible(tile.Slot == null ||
                            tile.Slot.State != LiveConnectionState_v3.Error);
                        tile.Opacity = 1;
                        tile.RefreshPopupPlacement();
                    }
                }));
            }));
        }

        private void RunAfterNativeLayout(Action action)
        {
            // Native D3D11 video can receive its final HWND bounds after WPF
            // has processed Render priority. ContextIdle is late enough for
            // layout to settle but does not wait for the whole application to
            // become idle (which is slow while many camera tiles update).
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, action)));
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_fullscreenTile != null)
            {
                ExitTileFullscreen();
                return;
            }

            SetGridFullscreen(!_gridFullscreen);
        }

        private void SetGridFullscreen(bool entering)
        {
            var window = Window.GetWindow(this);
            if (window == null) return;
            if (entering == _gridFullscreen) return;

            if (entering)
            {
                _gridWindowState = window.WindowState;
                _gridWindowStateSaved = true;
                window.WindowState = WindowState.Maximized;
            }
            else
            {
                window.WindowState = _gridWindowStateSaved ? _gridWindowState : WindowState.Normal;
                _gridWindowStateSaved = false;
            }

            _gridFullscreen = entering;
            var shell = window.Content as ShellPage_v3;
            if (shell != null) shell.SetChromeVisible(!entering);
            CameraSidebar.Visibility = entering ? Visibility.Collapsed : Visibility.Visible;
            UpdateSidebarOpenButtons();
            SidebarColumn.MinWidth = entering ? 0 : 240;
            SidebarColumn.MaxWidth = entering ? double.PositiveInfinity : 420;
            SidebarColumn.Width = entering ? new GridLength(0) : new GridLength(0.24, GridUnitType.Star);
            UpdateFullscreenControls();
        }

        private void UpdateFullscreenControls()
        {
            var tooltip = _gridFullscreen ? "Thu nhỏ" : "Toàn màn hình";
            var icon = _gridFullscreen
                ? MahApps.Metro.IconPacks.PackIconMaterialKind.FullscreenExit
                : MahApps.Metro.IconPacks.PackIconMaterialKind.Fullscreen;
            HeaderFullscreenButton.ToolTip = tooltip;
            HeaderFullscreenIcon.Kind = icon;
            LiveToolbarFullscreenButton.ToolTip = tooltip;
            LiveToolbarFullscreenIcon.Kind = icon;
        }

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            var hide = CameraSidebar.Visibility == Visibility.Visible;
            CameraSidebar.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            SidebarColumn.MinWidth = hide ? 0 : 240;
            SidebarColumn.MaxWidth = hide ? double.PositiveInfinity : 420;
            SidebarColumn.Width = hide ? new GridLength(0) : new GridLength(0.24, GridUnitType.Star);
            UpdateSidebarOpenButtons();
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
            LivePageHeader.SizeChanged -= LivePageHeader_SizeChanged;
            CameraGrid.SizeChanged -= CameraGrid_SizeChanged;
            SizeChanged -= LivePage_SizeChanged;
            DisposeTiles();
        }
    }
}
