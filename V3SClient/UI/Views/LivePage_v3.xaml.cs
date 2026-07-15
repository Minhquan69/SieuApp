using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private bool _disposed;
        private LiveTile_v3 _fullscreenTile;
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
        private LiveTile_v3 _dragTile;
        private Point _dragStart;

        public LivePage_v3()
        {
            InitializeComponent();
            _viewModel = new LiveViewModel_v3();
            DataContext = _viewModel;
            Loaded += OnLoaded;
            Unloaded += (s, e) => Dispose();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CameraStatus.Text = string.Format("{0} cameras · {1} groups · {2}/{3} active", _viewModel.CameraCount, _viewModel.GroupCount, _viewModel.ActiveCameraCount, _viewModel.Slots.Count);
            EmptyState.Visibility = _viewModel.CameraCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildGrid();
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
                var placement = GetPlacement(_viewModel.Layout, slot.SlotId, dimensions.Item2);
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
            UpdateStatus();
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

        private static Tuple<int, int, int, int> GetPlacement(LiveLayoutMode_v3 layout, int slotId, int columns)
        {
            if (layout == LiveLayoutMode_v3.Layout5Plus1)
            {
                var places = new[] { Tuple.Create(0, 0, 2, 2), Tuple.Create(0, 2, 1, 1), Tuple.Create(1, 2, 1, 1), Tuple.Create(2, 0, 1, 1), Tuple.Create(2, 1, 1, 1), Tuple.Create(2, 2, 1, 1) };
                return places[slotId - 1];
            }
            if (layout == LiveLayoutMode_v3.Layout16Plus1)
            {
                if (slotId == 1) return Tuple.Create(1, 1, 3, 3);
                var ring = new[] { Tuple.Create(0,0),Tuple.Create(0,1),Tuple.Create(0,2),Tuple.Create(0,3),Tuple.Create(0,4),Tuple.Create(1,4),Tuple.Create(2,4),Tuple.Create(3,4),Tuple.Create(4,4),Tuple.Create(4,3),Tuple.Create(4,2),Tuple.Create(4,1),Tuple.Create(4,0),Tuple.Create(3,0),Tuple.Create(2,0),Tuple.Create(1,0) };
                var point = ring[slotId - 2];
                return Tuple.Create(point.Item1, point.Item2, 1, 1);
            }
            var index = slotId - 1;
            return Tuple.Create(index / columns, index % columns, 1, 1);
        }

        private async void Camera_Click(object sender, RoutedEventArgs e)
        {
            var camera = (sender as FrameworkElement)?.Tag as Camera;
            var slot = _viewModel.ToggleCamera(camera);
            if (slot == null) return;
            BuildGrid();
            if (slot.Camera != null && _tiles.ContainsKey(slot.SlotId)) await _tiles[slot.SlotId].ConnectAsync();
        }

        private void Camera_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || e.ClickCount > 1) return;
            var camera = (sender as FrameworkElement)?.Tag as Camera;
            var slot = _viewModel.ToggleCamera(camera);
            if (slot == null) return;
            BuildGrid();
            _viewModel.RefreshCameraIndicators();
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
                _fullscreenTile = tile;
                EnterTileFullscreen();
                foreach (UIElement child in CameraGrid.Children)
                {
                    var cameraTile = child as LiveTile_v3;
                    if (cameraTile != null)
                    {
                        var selected = ReferenceEquals(cameraTile, tile);
                        cameraTile.SetFullscreenVisibility(selected);
                        cameraTile.SetFullscreenMode(selected);
                    }
                    child.Visibility = ReferenceEquals(child, tile) ? Visibility.Visible : Visibility.Collapsed;
                }
                Grid.SetRow(tile, 0); Grid.SetColumn(tile, 0); Grid.SetRowSpan(tile, Math.Max(1, CameraGrid.RowDefinitions.Count)); Grid.SetColumnSpan(tile, Math.Max(1, CameraGrid.ColumnDefinitions.Count));
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
            _tileWindowStyle = window.WindowStyle;
            _tileResizeMode = window.ResizeMode;
            _tileWindowState = window.WindowState;
            _tileTopmost = window.Topmost;
            _tileWindowLeft = window.Left;
            _tileWindowTop = window.Top;
            _tileWindowWidth = window.Width;
            _tileWindowHeight = window.Height;
            _tileSidebarVisibility = CameraSidebar.Visibility;
            _tileSidebarWidth = SidebarColumn.Width;
            _tileWindowStateSaved = true;
            CameraSidebar.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
            LivePageHeader.Visibility = Visibility.Collapsed;
            FullscreenToolbar.Visibility = Visibility.Collapsed;
            var shell = window.Content as ShellPage_v3;
            if (shell != null) shell.SetChromeVisible(false);
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowState = WindowState.Normal;
            window.Left = 0;
            window.Top = 0;
            window.Width = SystemParameters.PrimaryScreenWidth;
            window.Height = SystemParameters.PrimaryScreenHeight;
            window.Topmost = true;
        }

        private void ExitTileFullscreen()
        {
            var window = Window.GetWindow(this);
            _fullscreenTile = null;
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
                tile.SetFullscreenMode(false);
            CameraSidebar.Visibility = _tileSidebarVisibility;
            SidebarColumn.Width = _tileSidebarWidth.Value > 0 ? _tileSidebarWidth : new GridLength(300);
            LivePageHeader.Visibility = Visibility.Visible;
            FullscreenToolbar.Visibility = Visibility.Collapsed;
            BuildGrid();
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_fullscreenTile != null)
            {
                ExitTileFullscreen();
                return;
            }
            var window = Window.GetWindow(this);
            if (window == null) return;
            var entering = window.WindowState != WindowState.Maximized;
            window.WindowState = entering ? WindowState.Maximized : WindowState.Normal;
            var shell = window.Content as ShellPage_v3;
            if (shell != null) shell.SetChromeVisible(!entering);
            CameraSidebar.Visibility = entering ? Visibility.Collapsed : Visibility.Visible;
            SidebarColumn.Width = entering ? new GridLength(0) : new GridLength(300);
            FullscreenToolbar.Visibility = entering ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            var hide = CameraSidebar.Visibility == Visibility.Visible;
            CameraSidebar.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            SidebarColumn.Width = hide ? new GridLength(0) : new GridLength(300);
            SidebarOpenButton.Visibility = hide ? Visibility.Visible : Visibility.Collapsed;
            SidebarOpenHeaderButton.Visibility = hide ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateStatus()
        {
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
            UpdateStatus();
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_fullscreenTile != null) ExitTileFullscreen();
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            DisposeTiles();
        }
    }
}
