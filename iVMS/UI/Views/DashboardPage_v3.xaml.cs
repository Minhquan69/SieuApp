using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using V3SClient.libs;
using V3SClient.models;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public sealed class DashboardCameraHistoryItem
    {
        public string Label { get; set; }
        public string TimestampText { get; set; }
        public int Online { get; set; }
        public int Offline { get; set; }
        public double UptimePercent { get; set; }
        public double OnlineHeight { get; set; }
        public double OfflineHeight { get; set; }
        public double UptimeHeight { get; set; }
    }

    public sealed class DashboardVehicleItem : INotifyPropertyChanged
    {
        public string Identifier { get; set; }
        public string VehicleType { get; set; }
        public string CameraId { get; set; }
        public string TimeText { get; set; }
        public string AssetId { get; set; }
        public string EventKey { get; set; }
        private ImageSource _thumbnail;
        public ImageSource Thumbnail
        {
            get { return _thumbnail; }
            set
            {
                if (ReferenceEquals(_thumbnail, value)) return;
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed class DashboardCameraPickerItem : INotifyPropertyChanged
    {
        public Camera Camera { get; set; }
        private bool _isSelected;
        public bool IsSelected { get { return _isSelected; } set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
        private bool _canSelect = true;
        public bool CanSelect { get { return _canSelect; } set { if (_canSelect == value) return; _canSelect = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelect))); } }
        public string CameraName => Camera == null ? "Camera" : (Camera.camID ?? Camera.name ?? "Camera");
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class DashboardPage_v3 : UserControl
    {
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        private readonly DispatcherTimer _latestVehicleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<LiveTile_v3> _previewTiles = new List<LiveTile_v3>();
        private readonly Dictionary<LiveTile_v3, Grid> _previewHosts = new Dictionary<LiveTile_v3, Grid>();
        private readonly List<Camera> _selectedPreviewCameras = new List<Camera>();
        private const double QuickViewTileWidth = 124;
        private const double QuickViewTileSpacing = 6;
        private const int QuickViewPageSize = 5;
        private int _quickViewStartIndex;
        private int _refreshing;
        private int _latestVehiclesRefreshing;
        private bool _autoRefresh = true;
        private bool _previewInitialized;
        private readonly Dictionary<string, ImageSource> _vehicleImageCache = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
        private TextBox _latestVehicleRefreshSecondsText;
        private int _cameraHistoryHours = 24;

        public ObservableCollection<DashboardCameraHistoryItem> CameraHistory { get; } = new ObservableCollection<DashboardCameraHistoryItem>();
        public ObservableCollection<DashboardVehicleItem> LatestVehicles { get; } = new ObservableCollection<DashboardVehicleItem>();
        public ObservableCollection<DashboardCameraPickerItem> CameraPickerItems { get; } = new ObservableCollection<DashboardCameraPickerItem>();
        public event EventHandler OpenFullMapRequested;
        public event EventHandler OpenFullLiveRequested;

        public DashboardPage_v3()
        {
            InitializeComponent();
            DataContext = this;
            ConfigureLatestVehicleRefreshInput();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            _timer.Tick += async (s, e) => await RefreshAsync();
            _latestVehicleTimer.Tick += async (s, e) => await RefreshLatestVehiclesAsync();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
            if (!_previewInitialized) await InitializeLivePreviewAsync();
            if (_autoRefresh) _timer.Start();
            _latestVehicleTimer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _latestVehicleTimer.Stop();
            foreach (var tile in _previewTiles) tile.Disconnect();
        }

        private List<string> GetProfileCameraIds()
        {
            return (GlobalSystem.Instance.CameraList ?? new List<Camera>())
                .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID))
                .Select(camera => camera.camID.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task RefreshAsync()
        {
            if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;
            try
            {
                var cameraIds = GetProfileCameraIds();
                var now = DateTime.Now;
                var statusTask = ApiManager.Instance.GetDeviceStatusBatchAsync(cameraIds, _lifetime.Token);
                var vehicleTask = ApiManager.Instance.GetCameraVehicleCountsAsync(now.Date, now, string.Join(",", cameraIds), _lifetime.Token);
                var densityTask = ApiManager.Instance.GetLiveFrameDetectionCountsAsync(cameraIds, _lifetime.Token);
                var eventTask = ApiManager.Instance.GetLiveAiEventFeedAsync(GetLatestVehicleStart(now), now, cameraIds, _lifetime.Token);
                var trendTask = ApiManager.Instance.GetCameraHealthTimeseriesAsync(
                    now.AddHours(-_cameraHistoryHours), now, GetCameraHistoryBucket(), cameraIds, _lifetime.Token);
                await Task.WhenAll(statusTask, vehicleTask, densityTask, eventTask, trendTask);

                var statuses = statusTask.Result ?? new List<ApiManager.DeviceStatusResponse>();
                var total = cameraIds.Count;
                var online = statuses.Count(status => status != null && status.IsOnline == true);
                var offline = Math.Max(0, total - online);
                var onlinePercent = total == 0 ? 0 : online * 100d / total;
                TotalText.Text = total.ToString("N0");
                OnlineText.Text = onlinePercent.ToString("0.0") + "%";
                StatusText.Text = string.Format("{0} online · {1} offline", online, offline);
                VehicleText.Text = (vehicleTask.Result?.CustomTotal ?? 0).ToString("N0");
                DensityText.Text = (densityTask.Result?.TotalDetectionCount ?? 0).ToString("N0");
                UpdatedText.Text = "Cập nhật: " + now.ToString("HH:mm:ss dd-MM");
                VehicleCompareText.Text = "Theo profile hiện tại";
                ApplyCameraHealthHistory(trendTask.Result?.Data, now, online, offline, onlinePercent);
                await UpdateLatestVehiclesAsync(eventTask.Result);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggerManager.LogException(ex, "Dashboard refresh"); }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        }

        private string GetCameraHistoryBucket()
        {
            if (_cameraHistoryHours <= 1) return "5m";
            if (_cameraHistoryHours <= 6) return "30m";
            if (_cameraHistoryHours <= 12) return "1h";
            return "2h";
        }

        private void ApplyCameraHealthHistory(IEnumerable<ApiManager.CameraHealthTimeseriesPoint> points,
            DateTime now, int currentOnline, int currentOffline, double currentUptimePercent)
        {
            var data = (points ?? Enumerable.Empty<ApiManager.CameraHealthTimeseriesPoint>()).ToList();
            if (data.Count == 0)
            {
                AppendCameraHistory(now, currentOnline, currentOffline, currentUptimePercent);
                return;
            }

            CameraHistory.Clear();
            var maxCount = Math.Max(1, data.Max(point => Math.Max(point.Online, point.Unavailable > 0 ? point.Unavailable : point.Offline + point.Unknown)));
            foreach (var point in data.Skip(Math.Max(0, data.Count - 12)))
            {
                DateTime timestamp;
                DateTime.TryParse(point.BucketStart, out timestamp);
                var offline = point.Unavailable > 0 ? point.Unavailable : point.Offline + point.Unknown;
                var uptime = point.UptimePercent ?? (point.Online + offline == 0 ? 0 : point.Online * 100d / (point.Online + offline));
                AddCameraHistoryItem(timestamp == DateTime.MinValue ? now : timestamp, point.Online, offline, uptime, maxCount);
            }
        }

        private void AppendCameraHistory(DateTime time, int online, int offline, double uptimePercent)
        {
            var maxCount = Math.Max(1, Math.Max(online, offline));
            AddCameraHistoryItem(time, online, offline, uptimePercent, maxCount);
            while (CameraHistory.Count > 12) CameraHistory.RemoveAt(0);
        }

        private void AddCameraHistoryItem(DateTime time, int online, int offline, double uptimePercent, int maxCount)
        {
            CameraHistory.Add(new DashboardCameraHistoryItem
            {
                Label = time.ToString("HH:mm"),
                TimestampText = time.ToString("HH:mm:ss dd/MM/yyyy"),
                Online = online,
                Offline = offline,
                UptimePercent = Math.Max(0, Math.Min(100, uptimePercent)),
                OnlineHeight = Math.Max(3, Math.Min(140, online * 140d / Math.Max(1, maxCount))),
                OfflineHeight = Math.Max(3, Math.Min(140, offline * 140d / Math.Max(1, maxCount))),
                UptimeHeight = Math.Max(3, Math.Min(140, uptimePercent * 1.4))
            });
        }

        private async void CameraHistoryRange_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = CameraHistoryRangeComboBox?.SelectedItem as ComboBoxItem;
            int hours;
            if (selected == null || !int.TryParse(selected.Tag as string, out hours)) return;
            _cameraHistoryHours = hours;
            CameraHistory.Clear();
            await RefreshAsync();
        }

        private async Task UpdateLatestVehiclesAsync(ApiManager.LiveAiEventFeedResponse response)
        {
            var incoming = (response?.Items ?? new List<ApiManager.LiveAiEventFeedItem>())
                .Take(5)
                .ToList();
            var incomingKeys = new HashSet<string>(incoming.Select(GetLatestVehicleEventKey), StringComparer.OrdinalIgnoreCase);
            var displayedKeys = new HashSet<string>(LatestVehicles.Select(item => item.EventKey), StringComparer.OrdinalIgnoreCase);

            // The feed is polled every five seconds, but it must remain visually stable
            // until the server actually reports a newly recorded vehicle.
            if (LatestVehicles.Count > 0 && incomingKeys.SetEquals(displayedKeys)) return;

            LatestVehicles.Clear();
            foreach (var item in incoming)
            {
                DateTime eventTime;
                var parsed = DateTime.TryParse(item.EventTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out eventTime);
                LatestVehicles.Add(new DashboardVehicleItem
                {
                    Identifier = string.IsNullOrWhiteSpace(item.ObjectId) ? (item.DetectionId ?? "—") : item.ObjectId,
                    VehicleType = string.IsNullOrWhiteSpace(item.MetaType) ? "car" : item.MetaType,
                    AssetId = item.AssetId,
                    EventKey = GetLatestVehicleEventKey(item),
                    CameraId = item.CameraId ?? "—",
                    TimeText = parsed ? eventTime.ToString("HH:mm:ss\ndd/MM/yyyy") : (item.EventTime ?? "—")
                });
            }
            await LoadVehicleThumbnailsAsync();
        }

        private static string GetLatestVehicleEventKey(ApiManager.LiveAiEventFeedItem item)
        {
            if (item == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(item.DetectionId)) return "d:" + item.DetectionId;
            if (!string.IsNullOrWhiteSpace(item.MessageId)) return "m:" + item.MessageId;
            return string.Concat("f:", item.CameraId ?? string.Empty, "|", item.EventTime ?? string.Empty, "|", item.ObjectId ?? string.Empty, "|", item.AssetId ?? string.Empty);
        }

        private DateTime GetLatestVehicleStart(DateTime now) { return now.Date; }

        private void ConfigureLatestVehicleRefreshInput()
        {
            var oldSelector = LatestVehicleRangeComboBox;
            var host = oldSelector?.Parent as StackPanel;
            if (host == null) return;
            var index = host.Children.IndexOf(oldSelector);
            oldSelector.Visibility = Visibility.Collapsed;
            var label = new TextBlock { Text = "Tự động", Foreground = new SolidColorBrush(Color.FromRgb(142, 167, 188)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            _latestVehicleRefreshSecondsText = new TextBox { Text = "5", Width = 34, Height = 25, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(11, 32, 50)), Foreground = new SolidColorBrush(Color.FromRgb(199, 217, 233)), BorderBrush = new SolidColorBrush(Color.FromRgb(36, 68, 94)), FontSize = 10 };
            var suffix = new TextBlock { Text = "giây", Foreground = new SolidColorBrush(Color.FromRgb(142, 167, 188)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
            _latestVehicleRefreshSecondsText.TextChanged += LatestVehicleRefreshSeconds_TextChanged;
            _latestVehicleRefreshSecondsText.KeyDown += LatestVehicleRefreshSeconds_KeyDown;
            host.Children.Insert(index + 1, label);
            host.Children.Insert(index + 2, _latestVehicleRefreshSecondsText);
            host.Children.Insert(index + 3, suffix);
        }

        private async Task RefreshLatestVehiclesAsync()
        {
            if (_lifetime.IsCancellationRequested || Interlocked.Exchange(ref _latestVehiclesRefreshing, 1) != 0)
                return;
            try
            {
                var now = DateTime.Now;
                var response = await ApiManager.Instance.GetLiveAiEventFeedAsync(
                    GetLatestVehicleStart(now), now, GetProfileCameraIds(), _lifetime.Token);
                await UpdateLatestVehiclesAsync(response);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggerManager.LogException(ex, "Dashboard latest vehicles refresh"); }
            finally { Interlocked.Exchange(ref _latestVehiclesRefreshing, 0); }
        }

        private async Task LoadVehicleThumbnailsAsync()
        {
            foreach (var vehicle in LatestVehicles.Where(item => !string.IsNullOrWhiteSpace(item.AssetId)).ToList())
            {
                if (_vehicleImageCache.TryGetValue(vehicle.AssetId, out var cached))
                {
                    vehicle.Thumbnail = cached;
                    continue;
                }
                var accessUrl = await ApiManager.Instance.GetDashboardAssetAccessUrlAsync(vehicle.AssetId, _lifetime.Token);
                if (string.IsNullOrWhiteSpace(accessUrl)) continue;
                try
                {
                    // Do not let WPF resolve the network URI itself. The storage URL is
                    // short-lived and relative URLs are normalized by ApiManager; loading
                    // the JPEG bytes first makes thumbnail rendering deterministic.
                    using (var client = new WebClient())
                    {
                        var imageBytes = await client.DownloadDataTaskAsync(new Uri(accessUrl, UriKind.Absolute));
                        using (var imageStream = new MemoryStream(imageBytes, writable: false))
                        {
                            var image = new BitmapImage();
                            image.BeginInit();
                            image.CacheOption = BitmapCacheOption.OnLoad;
                            image.StreamSource = imageStream;
                            image.EndInit();
                            _vehicleImageCache[vehicle.AssetId] = image;
                            vehicle.Thumbnail = image;
                        }
                    }
                }
                catch (Exception ex) { LoggerManager.LogException(ex, "Dashboard vehicle thumbnail"); }
            }
        }

        private void LatestVehicleRefreshSeconds_TextChanged(object sender, TextChangedEventArgs e)
        {
            int seconds;
            if (!int.TryParse(_latestVehicleRefreshSecondsText?.Text, out seconds)) return;
            _latestVehicleTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Math.Min(3600, seconds)));
        }

        private async void LatestVehicleRefreshSeconds_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Keyboard.ClearFocus();
            await RefreshLatestVehiclesAsync();
        }

        // Kept for the hidden legacy selector declared in XAML.
        private void LatestVehicleRange_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private async Task InitializeLivePreviewAsync()
        {
            _previewInitialized = true;
            var cameras = (GlobalSystem.Instance.CameraList ?? new List<Camera>())
                .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID))
                .OrderByDescending(camera => camera.is_online == true)
                .Take(4)
                .ToList();
            _selectedPreviewCameras.Clear();
            _selectedPreviewCameras.AddRange(cameras);
            await RenderLivePreviewAsync();
        }

        private async Task RenderLivePreviewAsync()
        {
            // Keep existing native pipelines alive. Only the camera that was
            // added or removed is touched; rebuilding all tiles caused every
            // visible stream to reconnect after a simple picker change.
            var desired = _selectedPreviewCameras.Take(6).ToList();
            foreach (var tile in _previewTiles.Where(tile => tile.Slot?.Camera == null ||
                !desired.Any(camera => string.Equals(camera.camID, tile.Slot.Camera.camID, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                tile.Disconnect();
                tile.Dispose();
                if (_previewHosts.TryGetValue(tile, out var host)) LivePreviewHost.Children.Remove(host);
                _previewHosts.Remove(tile);
                _previewTiles.Remove(tile);
            }
            foreach (var camera in desired.Where(camera => !_previewTiles.Any(tile => tile.Slot?.Camera != null &&
                string.Equals(tile.Slot.Camera.camID, camera.camID, StringComparison.OrdinalIgnoreCase))))
                await AddPreviewTileAsync(camera);

            foreach (var button in LivePreviewHost.Children.OfType<Button>()
                .Where(button => Equals(button.Tag, "dashboard-add-camera")).ToList())
                LivePreviewHost.Children.Remove(button);
            AddCameraButton();
            _quickViewStartIndex = Math.Min(_quickViewStartIndex, GetMaxQuickViewStart());
            UpdateQuickViewPage();
            return;

            foreach (var tile in _previewTiles)
            {
                tile.Disconnect();
                tile.Dispose();
            }
            _previewTiles.Clear();
            LivePreviewHost.Children.Clear();
            foreach (var camera in _selectedPreviewCameras.Take(6).ToList())
            {
                var slot = new LiveSlotViewModel_v3 { SlotId = _previewTiles.Count + 1, Camera = camera, SelectedStream = camera.Streams?.FirstOrDefault() };
                var tile = new LiveTile_v3 { CompactDashboardMode = true, Width = QuickViewTileWidth, Height = 82, VerticalAlignment = VerticalAlignment.Top };
                tile.Bind(slot);
                _previewTiles.Add(tile);
                var host = new Grid { Width = QuickViewTileWidth, Height = 82, Margin = new Thickness(0, 0, QuickViewTileSpacing, 0), VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
                host.Children.Add(tile);
                tile.RemoveRequested += async (s, e) =>
                {
                    _selectedPreviewCameras.Remove(camera);
                    await RenderLivePreviewAsync();
                };
                LivePreviewHost.Children.Add(host);
            }
            foreach (var tile in _previewTiles)
            {
                await tile.ConnectAsync();
                tile.SynchronizeNativeVideoSurfaces();
            }
            var add = new Button
            {
                Style = (Style)FindResource("DashboardAddCameraButton"),
                Content = "+\nThêm camera", Width = 124, Height = 92,
                Background = System.Windows.Media.Brushes.Transparent, BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 68, 94)),
                Foreground = System.Windows.Media.Brushes.White, FontSize = 12,
                IsEnabled = _selectedPreviewCameras.Count < 6
            };
            add.Width = 132;
            add.Height = 82;
            add.Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "+", FontSize = 23, Foreground = System.Windows.Media.Brushes.DodgerBlue, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "Thêm camera", FontSize = 10, Foreground = System.Windows.Media.Brushes.DodgerBlue, HorizontalAlignment = HorizontalAlignment.Center }
                }
            };
            add.Click += AddCamera_Click;
            LivePreviewHost.Children.Add(add);
        }

        private async Task AddPreviewTileAsync(Camera camera)
        {
            var slot = new LiveSlotViewModel_v3 { SlotId = _previewTiles.Count + 1, Camera = camera, SelectedStream = camera.Streams?.FirstOrDefault() };
            var tile = new LiveTile_v3 { CompactDashboardMode = true, Width = QuickViewTileWidth, Height = 82, VerticalAlignment = VerticalAlignment.Top };
            tile.Bind(slot);
            var host = new Grid { Width = QuickViewTileWidth, Height = 82, Margin = new Thickness(0, 0, QuickViewTileSpacing, 0), VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
            host.Children.Add(tile);
            _previewTiles.Add(tile);
            _previewHosts[tile] = host;
            tile.RemoveRequested += async (s, e) =>
            {
                _selectedPreviewCameras.Remove(camera);
                await RenderLivePreviewAsync();
            };
            LivePreviewHost.Children.Add(host);
            await tile.ConnectAsync();
            tile.SynchronizeNativeVideoSurfaces();
        }

        private void AddCameraButton()
        {
            var add = new Button
            {
                Tag = "dashboard-add-camera",
                Width = QuickViewTileWidth,
                Height = 82,
                Margin = new Thickness(0, 0, QuickViewTileSpacing, 0),
                Style = (Style)FindResource("DashboardAddCameraButton"),
                IsEnabled = _previewTiles.Count < 6,
                Content = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "+", FontSize = 23, Foreground = System.Windows.Media.Brushes.DodgerBlue, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = "Thêm camera", FontSize = 10, Foreground = System.Windows.Media.Brushes.DodgerBlue, HorizontalAlignment = HorizontalAlignment.Center }
                    }
                }
            };
            add.Click += AddCamera_Click;
            LivePreviewHost.Children.Add(add);
        }


        private async void Refresh_Click(object sender, RoutedEventArgs e) { await RefreshAsync(); }

        private void OpenFullMap_Click(object sender, RoutedEventArgs e)
        {
            OpenFullMapRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OpenFullLive_Click(object sender, RoutedEventArgs e)
        {
            OpenFullLiveRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ScrollLiveLeft_Click(object sender, RoutedEventArgs e)
        {
            _quickViewStartIndex = Math.Max(0, _quickViewStartIndex - QuickViewPageSize);
            UpdateQuickViewPage();
        }

        private void ScrollLiveRight_Click(object sender, RoutedEventArgs e)
        {
            var maximumStart = GetMaxQuickViewStart();
            _quickViewStartIndex = Math.Min(maximumStart, _quickViewStartIndex + QuickViewPageSize);
            UpdateQuickViewPage();
        }

        private void UpdateQuickViewPage()
        {
            var showAdd = _previewTiles.Count < 6 &&
                (_previewTiles.Count < QuickViewPageSize || _quickViewStartIndex > 0);
            var visibleCount = showAdd ? QuickViewPageSize - 1 : QuickViewPageSize;
            var visibleTiles = _previewTiles.Skip(_quickViewStartIndex).Take(visibleCount).ToHashSet();
            foreach (var pair in _previewHosts)
            {
                var visible = visibleTiles.Contains(pair.Key);
                pair.Value.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (visible) pair.Key.SynchronizeNativeVideoSurfaces();
            }

            foreach (var button in LivePreviewHost.Children.OfType<Button>()
                .Where(button => Equals(button.Tag, "dashboard-add-camera")))
                button.Visibility = showAdd ? Visibility.Visible : Visibility.Collapsed;
        }

        private int GetMaxQuickViewStart()
        {
            // A five-camera first page uses the spare width. When a sixth can
            // still be selected, reserve one slot on the final page for Add.
            var visibleCameraSlots = _previewTiles.Count < 6
                ? QuickViewPageSize - 1
                : QuickViewPageSize;
            return Math.Max(0, _previewTiles.Count - visibleCameraSlots);
        }

        private void AddCamera_Click(object sender, RoutedEventArgs e)
        {
            CameraPickerSearch.Text = string.Empty;
            RebuildCameraPicker();
            CameraPickerPopup.IsOpen = true;
        }

        private void RebuildCameraPicker()
        {
            CameraPickerItems.Clear();
            var selectedIds = new HashSet<string>(_selectedPreviewCameras.Select(camera => camera.camID), StringComparer.OrdinalIgnoreCase);
            foreach (var camera in (GlobalSystem.Instance.CameraList ?? new List<Camera>())
                .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID))
                .OrderBy(camera => camera.camID))
                CameraPickerItems.Add(new DashboardCameraPickerItem { Camera = camera, IsSelected = selectedIds.Contains(camera.camID) });
            CameraPickerCountText.Text = _selectedPreviewCameras.Count + "/6";
            RefreshPickerAvailability();
        }

        private void CameraPickerSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = CameraPickerSearch.Text == null ? string.Empty : CameraPickerSearch.Text.Trim();
            CameraPickerItems.Clear();
            var selectedIds = new HashSet<string>(_selectedPreviewCameras.Select(camera => camera.camID), StringComparer.OrdinalIgnoreCase);
            foreach (var camera in (GlobalSystem.Instance.CameraList ?? new List<Camera>())
                .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID) &&
                    (string.IsNullOrEmpty(query) || camera.camID.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || (camera.name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(camera => camera.camID))
                CameraPickerItems.Add(new DashboardCameraPickerItem { Camera = camera, IsSelected = selectedIds.Contains(camera.camID) });
        }

        private async void ApplyCameraPicker_Click(object sender, RoutedEventArgs e)
        {
            var selected = CameraPickerItems.Where(item => item.IsSelected).Select(item => item.Camera).Take(6).ToList();
            _selectedPreviewCameras.Clear();
            _selectedPreviewCameras.AddRange(selected);
            CameraPickerPopup.IsOpen = false;
            await RenderLivePreviewAsync();
        }

        private void CancelCameraPicker_Click(object sender, RoutedEventArgs e) { CameraPickerPopup.IsOpen = false; }

        private void CameraPickerToggleChanged(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox?.DataContext is DashboardCameraPickerItem item && item.IsSelected && CameraPickerItems.Count(entry => entry.IsSelected) > 6)
                item.IsSelected = false;
            CameraPickerCountText.Text = CameraPickerItems.Count(entry => entry.IsSelected) + "/6";
            RefreshPickerAvailability();
        }

        private void RefreshPickerAvailability()
        {
            var full = CameraPickerItems.Count(item => item.IsSelected) >= 6;
            foreach (var item in CameraPickerItems)
                item.CanSelect = item.IsSelected || !full;
        }

        private void AutoRefresh_Click(object sender, RoutedEventArgs e)
        {
            _autoRefresh = !_autoRefresh;
            AutoRefreshButton.Content = _autoRefresh ? "Auto 30s" : "Auto off";
            AutoRefreshButton.Foreground = _autoRefresh
                ? System.Windows.Media.Brushes.DodgerBlue
                : System.Windows.Media.Brushes.LightSlateGray;
            if (_autoRefresh) _timer.Start(); else _timer.Stop();
        }
    }
}
