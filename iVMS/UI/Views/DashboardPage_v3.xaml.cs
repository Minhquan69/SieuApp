using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using LiveCharts;
using LiveCharts.Wpf;
using Newtonsoft.Json.Linq;
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
        private readonly HttpClient _metricsHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
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
        private readonly Queue<string> _vehicleImageCacheOrder = new Queue<string>();
        private const int MaxVehicleImageCacheEntries = 100;
        private TextBox _latestVehicleRefreshSecondsText;
        private int _cameraHistoryHours = 24;
        private const string MetricEndpointKey = "_systemMetric";
        private readonly DispatcherTimer _storageGaugeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        private DateTime _storageGaugeStartedAt;
        private double _storageGaugeStartValue;
        private double _storageGaugeTargetValue;
        private double _storageGaugeDisplayedValue;

        public ObservableCollection<DashboardCameraHistoryItem> CameraHistory { get; } = new ObservableCollection<DashboardCameraHistoryItem>();
        public ObservableCollection<string> CameraHistoryLabels { get; } = new ObservableCollection<string>();
        public ChartValues<double> CameraOnlineValues { get; } = new ChartValues<double>();
        public ChartValues<double> CameraOfflineValues { get; } = new ChartValues<double>();
        public ChartValues<double> CameraUptimeValues { get; } = new ChartValues<double>();
        public ObservableCollection<string> MetricHistoryLabels { get; } = new ObservableCollection<string>();
        public ChartValues<double> StorageReadValues { get; } = new ChartValues<double>();
        public ChartValues<double> StorageWriteValues { get; } = new ChartValues<double>();
        public ChartValues<double> NetworkReceiveValues { get; } = new ChartValues<double>();
        public ChartValues<double> NetworkTransmitValues { get; } = new ChartValues<double>();
        public SeriesCollection CameraHealthSeries { get; }
        public SeriesCollection StorageSeries { get; }
        public SeriesCollection NetworkSeries { get; }
        public Func<double, string> UptimeLabelFormatter { get; } = value => value.ToString("0") + "%";
        public Func<double, string> MegabytesLabelFormatter { get; } = value => (value / 1024d / 1024d).ToString("0.0") + " MB/s";
        public Func<double, string> NetworkRateLabelFormatter { get; } = value => value >= 1000d ? (value / 1000d).ToString("0.0") + " Gbps" : value.ToString(value >= 100d ? "0" : "0.0") + " Mbps";
        public ObservableCollection<DashboardVehicleItem> LatestVehicles { get; } = new ObservableCollection<DashboardVehicleItem>();
        public ObservableCollection<DashboardCameraPickerItem> CameraPickerItems { get; } = new ObservableCollection<DashboardCameraPickerItem>();
        public event EventHandler OpenFullMapRequested;
        public event EventHandler OpenFullLiveRequested;

        public DashboardPage_v3()
        {
            CameraHealthSeries = new SeriesCollection
            {
                new ColumnSeries
                {
                    Title = "Trực tuyến", Values = CameraOnlineValues,
                    Fill = new SolidColorBrush(Color.FromRgb(39, 201, 109)),
                    Stroke = new SolidColorBrush(Color.FromRgb(17, 125, 66)), StrokeThickness = 1.5,
                    MaxColumnWidth = 24, ColumnPadding = 2, ScalesYAt = 0
                },
                new ColumnSeries
                {
                    Title = "Ngoại tuyến", Values = CameraOfflineValues,
                    Fill = new SolidColorBrush(Color.FromRgb(255, 112, 133)),
                    Stroke = new SolidColorBrush(Color.FromRgb(187, 57, 82)), StrokeThickness = 1.5,
                    MaxColumnWidth = 24, ColumnPadding = 2, ScalesYAt = 0
                },
                new LineSeries
                {
                    Title = "Hoạt động", Values = CameraUptimeValues,
                    Stroke = new SolidColorBrush(Color.FromRgb(54, 123, 255)),
                    Fill = Brushes.Transparent, StrokeThickness = 2,
                    PointGeometry = DefaultGeometries.Circle, PointGeometrySize = 7,
                    ScalesYAt = 1, LineSmoothness = 0.55
                }
            };
            StorageSeries = CreateMetricSeries(StorageReadValues, StorageWriteValues, "Đọc", "Ghi");
            NetworkSeries = CreateMetricSeries(NetworkReceiveValues, NetworkTransmitValues, "RX", "TX");
            DataContext = this;
            // Series and DataContext must exist before XAML creates the chart axes.
            // This avoids LiveCharts receiving an incomplete axis collection.
            InitializeComponent();
            ConfigureCameraHistoryAxes();
            ConfigureLatestVehicleRefreshInput();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            _timer.Tick += async (s, e) => await RefreshAsync();
            _latestVehicleTimer.Tick += async (s, e) => await RefreshLatestVehiclesAsync();
            _storageGaugeTimer.Tick += StorageGaugeTimer_Tick;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Each visit starts the storage gauge from zero; subsequent refreshes
            // still animate from the displayed value to the new value.
            _storageGaugeTimer.Stop();
            _storageGaugeDisplayedValue = 0;
            _storageGaugeTargetValue = double.NaN;
            SetStorageUsageArc(0);
            StorageUsageCenterText.Text = "0,0%";
            await RefreshAsync();
            if (_autoRefresh) _timer.Start();
        }

        private void ConfigureCameraHistoryAxes()
        {
            foreach (var chart in FindVisualChildren<CartesianChart>(this)
                .Where(candidate => ReferenceEquals(candidate.Series, CameraHealthSeries)))
            {
                var chartContainer = VisualTreeHelper.GetParent(chart) as Grid;
                var customLegend = chartContainer?.Children.OfType<Border>().FirstOrDefault();
                if (customLegend != null)
                    customLegend.Margin = new Thickness(0, -30, 0, 0);

                chart.AxisY = new AxesCollection
                {
                    new Axis
                    {
                        Title = "Camera",
                        MinValue = 0,
                        MaxValue = 60,
                        Separator = new LiveCharts.Wpf.Separator { Step = 15, Stroke = new SolidColorBrush(Color.FromRgb(36, 69, 93)) }
                    },
                    new Axis
                    {
                        Title = "Hoạt động",
                        MinValue = 0,
                        MaxValue = 100,
                        Position = AxisPosition.RightTop,
                        LabelFormatter = UptimeLabelFormatter,
                        Separator = new LiveCharts.Wpf.Separator { Step = 25, StrokeThickness = 0 }
                    }
                };
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T result) yield return result;
                foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _latestVehicleTimer.Stop();
            _storageGaugeTimer.Stop();
            ReleaseLivePreviewTiles();
        }

        // LiveTile owns a native video pipeline. A disconnected tile cannot be
        // safely reused after its visual host was unloaded, so recreate the
        // tiles when the dashboard is displayed again. The selected cameras are
        // intentionally retained.
        private void ReleaseLivePreviewTiles()
        {
            foreach (var tile in _previewTiles.ToList())
            {
                tile.Disconnect();
                tile.Dispose();
            }

            _previewTiles.Clear();
            _previewHosts.Clear();
            LivePreviewHost.Children.Clear();
            _previewInitialized = false;
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
                var profileCameraIdSet = new HashSet<string>(cameraIds, StringComparer.OrdinalIgnoreCase);
                var profileCameras = (GlobalSystem.Instance.CameraList ?? new List<Camera>())
                    .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID) && profileCameraIdSet.Contains(camera.camID.Trim()))
                    .ToList();
                var aiCameraIds = profileCameras
                    .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID) &&
                        (camera.HasAIStream ||
                         string.Equals(camera.type, "ai_processed", StringComparison.OrdinalIgnoreCase) ||
                         (camera.Streams != null && camera.Streams.Any(stream => stream != null && stream.IsAiMode == true))))
                    .Select(camera => camera.camID.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var aiCameraCount = aiCameraIds.Count;
                var recordingCameraCount = profileCameras.Count(camera => camera.is_recording);
                // Some profiles expose AI capability only after stream
                // metadata loads; keep the report usable during that window.
                if (aiCameraIds.Count == 0)
                    aiCameraIds = cameraIds;
                var now = DateTime.Now;
                // Online/offline is a current device state, supplied by the
                // Portal gateway rather than the historical _deviceReport API.
                var statusTask = ApiManager.Instance.GetPortalDeviceStatusBatchAsync(cameraIds, _lifetime.Token);
                var vehicleTask = ApiManager.Instance.GetCameraVehicleCountsAsync(now.Date, now, string.Join(",", aiCameraIds), _lifetime.Token);
                var densityTask = ApiManager.Instance.GetLiveFrameDetectionCountsAsync(cameraIds, _lifetime.Token);
                var eventTask = ApiManager.Instance.GetLiveAiEventFeedAsync(GetLatestVehicleStart(now), now, cameraIds, cancellationToken: _lifetime.Token);
                var trendTask = ApiManager.Instance.GetCameraHealthTimeseriesAsync(
                    now.AddHours(-_cameraHistoryHours), now, GetCameraHistoryBucket(), cameraIds, _lifetime.Token);
                // Request beyond five records first.  The API sorts globally; filtering a
                // global top-five could otherwise omit every camera in the active profile.
                var attentionTask = ApiManager.Instance.GetCameraHealthAttentionCamerasAsync(
                    Math.Max(100, cameraIds.Count * 2), _lifetime.Token);
                var infrastructureMetricsTask = RefreshInfrastructureMetricsAsync(now);

                // Do not make independent cards wait for the slowest report.
                // Continuations return to the UI dispatcher and publish each
                // result as soon as its request completes.
                _ = vehicleTask.ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (task.Status == TaskStatus.RanToCompletion) ApplyVehicleStats(task.Result);
                })));
                _ = densityTask.ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (task.Status == TaskStatus.RanToCompletion) ApplyDensityStats(task.Result);
                })));
                _ = eventTask.ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (task.Status == TaskStatus.RanToCompletion) _ = UpdateLatestVehiclesAsync(task.Result);
                })));

                // Online state is published before slower reporting requests.
                var statuses = await statusTask ?? new List<ApiManager.DeviceStatusResponse>();
                ApplyDeviceStatuses(profileCameras, statuses);

                await Task.WhenAll(vehicleTask, densityTask, eventTask, trendTask, attentionTask, infrastructureMetricsTask);
                var total = cameraIds.Count;
                var hasDeviceReportStates = statuses.Any(status => status != null && !string.IsNullOrWhiteSpace(status.DeviceId));
                // Keep the last shared state if the Portal request is
                // temporarily unavailable instead of showing every camera as
                // offline.
                var online = hasDeviceReportStates
                    ? statuses.Count(status => status != null && status.IsOnline == true)
                    : profileCameras.Count(camera => camera.is_online == true);
                var offline = Math.Max(0, total - online);
                var onlinePercent = total == 0 ? 0 : online * 100d / total;
                TotalText.Text = total.ToString("N0");
                OnlineText.Text = onlinePercent.ToString("0.0") + "%";
                StatusText.Text = string.Format("{0} online · {1} offline", online, offline);
                VehicleText.Text = (vehicleTask.Result?.CustomTotal ?? 0).ToString("N0");
                DensityText.Text = (densityTask.Result?.TotalDetectionCount ?? 0).ToString("N0");
                OverviewTotalText.Text = total.ToString("N0");
                OverviewRecordingText.Text = recordingCameraCount.ToString("N0");
                OverviewAiText.Text = aiCameraCount.ToString("N0");
                OverviewOnlineText.Text = onlinePercent.ToString("0.0") + "%";
                var vehicleToday = vehicleTask.Result?.TodayTotal ?? vehicleTask.Result?.CustomTotal ?? 0;
                var vehicleYesterday = vehicleTask.Result?.YesterdayTotal ?? 0;
                var vehicleChange = vehicleYesterday == 0 ? 0d : (vehicleToday - vehicleYesterday) * 100d / vehicleYesterday;
                OverviewVehicleText.Text = vehicleToday.ToString("N0");
                OverviewVehicleDetailText.Text = vehicleYesterday == 0
                    ? "Chưa có số liệu kỳ trước"
                    : string.Format("{0} {1:0.0}% so với kỳ trước", vehicleChange >= 0 ? "↑" : "↓", Math.Abs(vehicleChange));
                OverviewVehicleDetailText.Foreground = vehicleYesterday == 0
                    ? new SolidColorBrush(Color.FromRgb(211, 196, 255))
                    : vehicleChange >= 0 ? new SolidColorBrush(Color.FromRgb(39, 201, 109)) : new SolidColorBrush(Color.FromRgb(255, 112, 133));
                OverviewDensityText.Text = (densityTask.Result?.TotalDetectionCount ?? 0).ToString("N0");
                OverviewRecordingDetailText.Text = total == 0 ? "Chưa có camera" : "Độ phủ " + (recordingCameraCount * 100d / total).ToString("0.0") + "%";
                OverviewAiDetailText.Text = total == 0 ? "Chưa có camera" : "Độ phủ " + (aiCameraCount * 100d / total).ToString("0.0") + "%";
                OverviewOnlineCountText.Text = online + " trực tuyến";
                OverviewOfflineCountText.Text = offline + " ngoại tuyến";
                CameraStatusTotalText.Text = total.ToString("N0");
                CameraStatusOnlineText.Text = online.ToString("N0");
                CameraStatusOfflineText.Text = offline.ToString("N0");
                UpdateAttentionPanel(attentionTask.Result, cameraIds);
                UpdatedText.Text = "Cập nhật: " + now.ToString("HH:mm:ss dd-MM");
                VehicleCompareText.Text = "Theo profile hiện tại";
                ApplyCameraHealthHistory(trendTask.Result?.Data, now, online, offline, onlinePercent);
                await UpdateLatestVehiclesAsync(eventTask.Result);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggerManager.LogException(ex, "Dashboard refresh"); }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        }

        private static void ApplyDeviceStatuses(IEnumerable<Camera> cameras, IEnumerable<ApiManager.DeviceStatusResponse> statuses)
        {
            var stateById = (statuses ?? Enumerable.Empty<ApiManager.DeviceStatusResponse>())
                .Where(status => status != null && !string.IsNullOrWhiteSpace(status.DeviceId))
                .GroupBy(status => status.DeviceId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().IsOnline == true, StringComparer.OrdinalIgnoreCase);

            foreach (var camera in cameras ?? Enumerable.Empty<Camera>())
            {
                if (camera == null || string.IsNullOrWhiteSpace(camera.camID)) continue;
                bool online;
                if (!stateById.TryGetValue(camera.camID.Trim(), out online)) continue;
                camera.is_online = online;
                camera.Status = online ? "online" : "offline";
            }
        }

        private void UpdateAttentionPanel(
            IEnumerable<ApiManager.CameraHealthAttentionCamera> cameras,
            IEnumerable<string> profileCameraIds)
        {
            var allowedCameraIds = new HashSet<string>(
                (profileCameraIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var rowsFromApi = (cameras ?? Enumerable.Empty<ApiManager.CameraHealthAttentionCamera>())
                .Where(camera => camera != null)
                .Where(camera => allowedCameraIds.Contains((camera.CameraId ?? string.Empty).Trim())
                    || allowedCameraIds.Contains((camera.CameraCode ?? string.Empty).Trim()))
                .GroupBy(camera => (string.IsNullOrWhiteSpace(camera.CameraId) ? camera.CameraCode : camera.CameraId)?.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(camera => camera.UptimePercent ?? double.MaxValue).First())
                .OrderBy(camera => camera.UptimePercent ?? double.MaxValue)
                .Take(5)
                .ToList();
            var rows = new[] { AttentionCamera1Text, AttentionCamera2Text, AttentionCamera3Text, AttentionCamera4Text, AttentionCamera5Text };
            for (var index = 0; index < rows.Length; index++)
            {
                rows[index].Text = index < rowsFromApi.Count ? FormatAttentionCameraRow(rowsFromApi[index]) : index == 0 && rowsFromApi.Count == 0
                    ? "Chưa có dữ liệu uptime từ API" : string.Empty;
                rows[index].Foreground = index < rowsFromApi.Count
                    ? new SolidColorBrush(Color.FromRgb(255, 160, 175))
                    : new SolidColorBrush(Color.FromRgb(213, 227, 238));
            }

            var cellRows = new[]
            {
                new[] { AttentionCamera1IdCell, AttentionCamera1UptimeCell, AttentionCamera1DowntimeCell, AttentionCamera1StatusCell },
                new[] { AttentionCamera2IdCell, AttentionCamera2UptimeCell, AttentionCamera2DowntimeCell, AttentionCamera2StatusCell },
                new[] { AttentionCamera3IdCell, AttentionCamera3UptimeCell, AttentionCamera3DowntimeCell, AttentionCamera3StatusCell },
                new[] { AttentionCamera4IdCell, AttentionCamera4UptimeCell, AttentionCamera4DowntimeCell, AttentionCamera4StatusCell },
                new[] { AttentionCamera5IdCell, AttentionCamera5UptimeCell, AttentionCamera5DowntimeCell, AttentionCamera5StatusCell }
            };
            var statusBadges = new[]
            {
                AttentionCamera1StatusBadge, AttentionCamera2StatusBadge, AttentionCamera3StatusBadge,
                AttentionCamera4StatusBadge, AttentionCamera5StatusBadge
            };
            for (var index = 0; index < cellRows.Length; index++)
            {
                var camera = index < rowsFromApi.Count ? rowsFromApi[index] : null;
                cellRows[index][0].Text = camera == null ? string.Empty : (string.IsNullOrWhiteSpace(camera.CameraId) ? camera.CameraCode : camera.CameraId);
                cellRows[index][1].Text = camera != null && camera.UptimePercent.HasValue ? Math.Max(0, Math.Min(100, camera.UptimePercent.Value)).ToString("0.0") + "%" : string.Empty;
                cellRows[index][2].Text = camera != null && camera.UnavailableSeconds.HasValue ? FormatDuration(camera.UnavailableSeconds.Value) : string.Empty;
                cellRows[index][3].Text = camera == null ? string.Empty : GetAttentionCameraStatus(camera);
                var statusBrush = camera == null ? new SolidColorBrush(Color.FromRgb(173, 190, 203)) : GetAttentionCameraStatusBrush(camera);
                statusBadges[index].BorderBrush = statusBrush;
                statusBadges[index].Background = camera == null ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(35, statusBrush.Color.R, statusBrush.Color.G, statusBrush.Color.B));
                foreach (var cell in cellRows[index])
                    cell.Foreground = camera == null ? new SolidColorBrush(Color.FromRgb(213, 227, 238)) : new SolidColorBrush(Color.FromRgb(255, 160, 175));
                cellRows[index][3].Foreground = statusBrush;
            }
        }

        private static string FormatAttentionCameraRow(ApiManager.CameraHealthAttentionCamera camera)
        {
            var id = string.IsNullOrWhiteSpace(camera.CameraId) ? camera.CameraCode : camera.CameraId;
            var uptime = camera.UptimePercent.HasValue ? Math.Max(0, Math.Min(100, camera.UptimePercent.Value)).ToString("0.0") + "%" : "—";
            var downtime = camera.UnavailableSeconds.HasValue ? FormatDuration(camera.UnavailableSeconds.Value) : "—";
            var status = !camera.UptimePercent.HasValue ? "CẢNH BÁO" : camera.UptimePercent.Value >= 99 ? "TỐT" : camera.UptimePercent.Value >= 95 ? "CẢNH BÁO" : "NGHIÊM TRỌNG";
            // Consolas character columns match the fixed XAML header columns
            // (128 / 80 / 60 pixels) so headings and values share the same left edge.
            return string.Format("{0,-21}{1,-14}{2,-10}{3}", id ?? "—", uptime, downtime, status);
        }
        private static string GetAttentionCameraStatus(ApiManager.CameraHealthAttentionCamera camera)
        {
            if (!camera.UptimePercent.HasValue) return "C\u1ea2NH B\u00c1O";
            if (camera.UptimePercent.Value >= 99) return "T\u1ed0T";
            return camera.UptimePercent.Value >= 95 ? "C\u1ea2NH B\u00c1O" : "NGHI\u00caM TR\u1eccNG";
        }

        private static SolidColorBrush GetAttentionCameraStatusBrush(ApiManager.CameraHealthAttentionCamera camera)
        {
            if (!camera.UptimePercent.HasValue) return new SolidColorBrush(Color.FromRgb(255, 154, 61));
            if (camera.UptimePercent.Value >= 99) return new SolidColorBrush(Color.FromRgb(39, 201, 109));
            return camera.UptimePercent.Value >= 95
                ? new SolidColorBrush(Color.FromRgb(255, 154, 61))
                : new SolidColorBrush(Color.FromRgb(255, 112, 133));
        }

        private static string FormatDuration(long totalSeconds)
        {
            var duration = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            return duration.TotalDays >= 1 ? string.Format("{0}d {1}h", (int)duration.TotalDays, duration.Hours) : string.Format("{0}h {1}m", (int)duration.TotalHours, duration.Minutes);
        }

        private static SeriesCollection CreateMetricSeries(ChartValues<double> first, ChartValues<double> second, string firstTitle, string secondTitle)
        {
            return new SeriesCollection
            {
                new LineSeries { Title = firstTitle, Values = first, Stroke = new SolidColorBrush(Color.FromRgb(54, 123, 255)), Fill = Brushes.Transparent, StrokeThickness = 2, PointGeometry = null, LineSmoothness = 0.2 },
                new LineSeries { Title = secondTitle, Values = second, Stroke = new SolidColorBrush(Color.FromRgb(255, 121, 37)), Fill = Brushes.Transparent, StrokeThickness = 2, PointGeometry = null, LineSmoothness = 0.2 }
            };
        }

        private async Task RefreshInfrastructureMetricsAsync(DateTime now)
        {
            try
            {
                await EnsureMetricsEndpointAsync();
                var start = now.ToUniversalTime().AddHours(-24);
                var end = now.ToUniversalTime();
                var overviewTask = GetSystemMetricAsync("/overview?instance=*");
                var nodesTask = GetSystemMetricAsync("/nodes?instance=*");
                var diskReadTask = GetSystemMetricSeriesAsync("disk_read", start, end);
                var diskWriteTask = GetSystemMetricSeriesAsync("disk_write", start, end);
                await Task.WhenAll(overviewTask, nodesTask, diskReadTask, diskWriteTask);
                var overview = await overviewTask;
                var network = overview["network"] as JObject;
                var networkDevice = network?["device"]?.ToString();
                var networkReceiveTask = string.IsNullOrWhiteSpace(networkDevice) ? Task.FromResult(new JObject { ["series"] = new JArray() }) : GetSystemMetricSeriesAsync("network_receive", start, end, networkDevice);
                var networkTransmitTask = string.IsNullOrWhiteSpace(networkDevice) ? Task.FromResult(new JObject { ["series"] = new JArray() }) : GetSystemMetricSeriesAsync("network_transmit", start, end, networkDevice);
                await Task.WhenAll(networkReceiveTask, networkTransmitTask);
                ApplyInfrastructureMetrics(overview, await nodesTask, await diskReadTask, await diskWriteTask, await networkReceiveTask, await networkTransmitTask);
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Dashboard system metrics refresh");
                InfrastructureHostText.Text = "Không tải được Metrics API";
                InfrastructureStatusText.Text = "Cần kiểm tra kết nối";
                StorageUsageText.Text = "—"; StorageReadText.Text = "—"; StorageWriteText.Text = "—";
                NetworkStatusText.Text = "Không tải được Metrics API"; NetworkRxText.Text = "—"; NetworkTxText.Text = "—";
                InfrastructureCpuBar.Value = InfrastructureMemoryBar.Value = InfrastructureGpuBar.Value = 0;
                InfrastructureCpuText.Text = InfrastructureMemoryText.Text = InfrastructureGpuText.Text = "—";
                UpdateMetricCharts(new List<MetricPoint>(), new List<MetricPoint>(), new List<MetricPoint>(), new List<MetricPoint>());
            }
        }

        private async Task EnsureMetricsEndpointAsync()
        {
            if (string.IsNullOrWhiteSpace(ApiManager.Instance.GetEndpointUrl(MetricEndpointKey))) await ApiManager.Instance.DiscoverEndpointsAsync();
            if (string.IsNullOrWhiteSpace(ApiManager.Instance.GetEndpointUrl(MetricEndpointKey))) throw new InvalidOperationException("Portal chưa cấu hình endpoint _systemMetric.");
        }

        private async Task<JObject> GetSystemMetricSeriesAsync(string metric, DateTime start, DateTime end, string device = null)
        {
            var path = string.Format("/timeseries?metric={0}&instance=*&start={1:O}&end={2:O}&step=3600", Uri.EscapeDataString(metric), start, end);
            if (!string.IsNullOrWhiteSpace(device)) path += "&device=" + Uri.EscapeDataString(device);
            return await GetSystemMetricAsync(path);
        }

        private async Task<JObject> GetSystemMetricAsync(string path)
        {
            var root = ApiManager.Instance.GetEndpointUrl(MetricEndpointKey)?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Portal chưa cấu hình endpoint _systemMetric.");
            if (!root.EndsWith("/api/metrics", StringComparison.OrdinalIgnoreCase)) root += "/api/metrics";
            var request = new HttpRequestMessage(HttpMethod.Get, root + path);
            var token = ApiManager.Instance.GetEndpointToken(MetricEndpointKey);
            if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Replace("Bearer ", "").Trim());
            var response = await _metricsHttp.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + body);
            return JObject.Parse(body);
        }

        private void ApplyInfrastructureMetrics(JObject overview, JObject nodesResponse, JObject diskReadResponse, JObject diskWriteResponse, JObject networkReceiveResponse, JObject networkTransmitResponse)
        {
            var host = overview["host"] as JObject ?? overview["resources"] as JObject ?? new JObject();
            var gpu = overview["gpu"] as JObject ?? new JObject();
            var nodes = nodesResponse["nodes"] as JArray ?? new JArray();
            var firstNode = nodes.OfType<JObject>().FirstOrDefault();
            var hasNode = firstNode != null;
            var isOnline = firstNode?.Value<bool?>("up") == true;
            InfrastructureHostText.Text = MetricText(firstNode?["display_name"] ?? firstNode?["instance"]);
            InfrastructureStatusText.Text = !hasNode ? "—" : isOnline ? "Healthy" : "Ngoại tuyến";
            InfrastructureStatusText.Foreground = !hasNode ? new SolidColorBrush(Color.FromRgb(183, 205, 224)) : isOnline ? new SolidColorBrush(Color.FromRgb(39, 201, 109)) : new SolidColorBrush(Color.FromRgb(255, 112, 133));
            SetMetricBar(InfrastructureCpuBar, InfrastructureCpuText, host["cpu_percent"]);
            SetMetricBar(InfrastructureMemoryBar, InfrastructureMemoryText, host["memory_percent"]);
            SetMetricBar(InfrastructureGpuBar, InfrastructureGpuText, gpu["utilization_percent"]);
            var diskPercent = MetricNumber(host["disk_percent"]);
            StorageUsageText.Text = diskPercent.HasValue ? diskPercent.Value.ToString("0.0", CultureInfo.GetCultureInfo("vi-VN")) + "%" : "—";
            AnimateStorageUsage(diskPercent);
            var diskRead = MetricPoints(diskReadResponse); var diskWrite = MetricPoints(diskWriteResponse); var networkReceive = MetricPoints(networkReceiveResponse); var networkTransmit = MetricPoints(networkTransmitResponse);
            StorageReadText.Text = MetricRate(diskRead.LastOrDefault()?.Value); StorageWriteText.Text = MetricRate(diskWrite.LastOrDefault()?.Value);
            var networkUnit = networkReceiveResponse["unit"]?.ToString() ?? networkTransmitResponse["unit"]?.ToString() ?? "megabits_per_second";
            NetworkRxText.Text = NetworkRate(networkReceive.LastOrDefault()?.Value, networkUnit); NetworkTxText.Text = NetworkRate(networkTransmit.LastOrDefault()?.Value, networkUnit);
            NetworkStatusText.Text = (overview["network"] as JObject)?["device"]?.ToString() + " · Lưu lượng 24 giờ";
            UpdateMetricCharts(diskRead, diskWrite, networkReceive, networkTransmit);
        }

        private void UpdateMetricCharts(List<MetricPoint> diskRead, List<MetricPoint> diskWrite, List<MetricPoint> networkReceive, List<MetricPoint> networkTransmit)
        {
            var labels = diskRead.Count > 0 ? diskRead : diskWrite.Count > 0 ? diskWrite : networkReceive.Count > 0 ? networkReceive : networkTransmit;
            MetricHistoryLabels.Clear();
            for (var i = 0; i < labels.Count; i++) MetricHistoryLabels.Add(labels[i].Timestamp.HasValue ? labels[i].Timestamp.Value.ToLocalTime().ToString("HH:mm") : string.Empty);
            ReplaceChartValues(StorageReadValues, diskRead); ReplaceChartValues(StorageWriteValues, diskWrite); ReplaceChartValues(NetworkReceiveValues, networkReceive); ReplaceChartValues(NetworkTransmitValues, networkTransmit);
        }

        private static void ReplaceChartValues(ChartValues<double> target, List<MetricPoint> points) { target.Clear(); foreach (var point in points) target.Add(point.Value); }
        private static List<MetricPoint> MetricPoints(JObject response)
        {
            var raw = ((response["series"] as JArray)?.FirstOrDefault()?["points"] as JArray ?? new JArray()).OfType<JObject>()
                .Select(point => new MetricPoint { Timestamp = MetricTimestamp(point["timestamp"]), Value = MetricNumber(point["value"]) ?? 0 }).ToList();
            if (raw.Count <= 24) return raw;
            return Enumerable.Range(0, 24).Select(index => raw[(int)Math.Round(index * (raw.Count - 1d) / 23d)]).ToList();
        }
        private static DateTime? MetricTimestamp(JToken token)
        {
            long epoch; if (token != null && long.TryParse(token.ToString(), out epoch)) return epoch > 100000000000L ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime : DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
            DateTime parsed; return token != null && DateTime.TryParse(token.ToString(), out parsed) ? parsed : (DateTime?)null;
        }
        private static double? MetricNumber(JToken token) { double value; return double.TryParse(token?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value) ? value : (double?)null; }
        private static string MetricText(JToken token) => string.IsNullOrWhiteSpace(token?.ToString()) ? "—" : token.ToString();
        private static string MetricRate(double? value) => value.HasValue ? (value.Value / 1024d / 1024d).ToString("0.0", CultureInfo.GetCultureInfo("vi-VN")) + " MiB/s" : "—";
        private static string NetworkRate(double? value, string unit)
        {
            if (!value.HasValue) return "—";
            var culture = CultureInfo.GetCultureInfo("vi-VN");
            if (string.Equals(unit, "megabytes_per_second", StringComparison.OrdinalIgnoreCase)) return value.Value.ToString(value.Value >= 100 ? "0" : "0.0", culture) + " MB/s";
            return value.Value >= 1000 ? (value.Value / 1000d).ToString("0.0", culture) + " Gbps" : value.Value.ToString(value.Value >= 100 ? "0" : "0.0", culture) + " Mbps";
        }
        private void AnimateStorageUsage(double? percent)
        {
            if (!percent.HasValue)
            {
                _storageGaugeTimer.Stop();
                StorageUsageCenterText.Text = "—";
                _storageGaugeDisplayedValue = 0;
                SetStorageUsageArc(0);
                return;
            }
            var targetValue = Math.Max(0, Math.Min(100, percent.Value));
            if (Math.Abs(targetValue - _storageGaugeTargetValue) < 0.01d && !_storageGaugeTimer.IsEnabled)
                return;

            _storageGaugeStartValue = _storageGaugeDisplayedValue;
            _storageGaugeTargetValue = targetValue;
            _storageGaugeStartedAt = DateTime.UtcNow;
            _storageGaugeTimer.Stop();
            _storageGaugeTimer.Start();
        }
        private void StorageGaugeTimer_Tick(object sender, EventArgs e)
        {
            var progress = Math.Min(1d, (DateTime.UtcNow - _storageGaugeStartedAt).TotalMilliseconds / 1300d);
            var eased = 1d - Math.Pow(1d - progress, 3d);
            var value = _storageGaugeStartValue + (_storageGaugeTargetValue - _storageGaugeStartValue) * eased;
            _storageGaugeDisplayedValue = value;
            SetStorageUsageArc(value);
            StorageUsageCenterText.Text = value.ToString("0.0", CultureInfo.GetCultureInfo("vi-VN")) + "%";
            if (progress >= 1d) _storageGaugeTimer.Stop();
        }
        private void SetStorageUsageArc(double percent)
        {
            if (StorageUsageArc == null) return;
            const double circumferenceInStrokeUnits = 21.36d;
            var visibleLength = Math.Max(0d, Math.Min(100d, percent)) / 100d * circumferenceInStrokeUnits;
            var dashArray = StorageUsageArc.StrokeDashArray;
            if (dashArray == null || dashArray.Count < 2)
            {
                StorageUsageArc.StrokeDashArray = new DoubleCollection { visibleLength, 100d };
                return;
            }
            dashArray[0] = visibleLength;
            dashArray[1] = 100d;
        }
        private static void SetMetricBar(ProgressBar bar, TextBlock label, JToken value) { var number = Math.Max(0, Math.Min(100, MetricNumber(value) ?? 0)); bar.Value = number; label.Text = MetricNumber(value).HasValue ? number.ToString("0.0", CultureInfo.GetCultureInfo("vi-VN")) + "%" : "—"; }
        private sealed class MetricPoint { public DateTime? Timestamp { get; set; } public double Value { get; set; } }

        private void ApplyVehicleStats(ApiManager.CameraVehicleCountsResponse vehicle)
        {
            VehicleText.Text = (vehicle?.CustomTotal ?? 0).ToString("N0");
            var vehicleToday = vehicle?.TodayTotal ?? vehicle?.CustomTotal ?? 0;
            var vehicleYesterday = vehicle?.YesterdayTotal ?? 0;
            var vehicleChange = vehicleYesterday == 0 ? 0d : (vehicleToday - vehicleYesterday) * 100d / vehicleYesterday;
            OverviewVehicleText.Text = vehicleToday.ToString("N0");
            OverviewVehicleDetailText.Text = vehicleYesterday == 0
                ? "Chưa có số liệu kỳ trước"
                : string.Format("{0} {1:0.0}% so với kỳ trước", vehicleChange >= 0 ? "↑" : "↓", Math.Abs(vehicleChange));
            OverviewVehicleDetailText.Foreground = vehicleYesterday == 0
                ? new SolidColorBrush(Color.FromRgb(211, 196, 255))
                : vehicleChange >= 0 ? new SolidColorBrush(Color.FromRgb(39, 201, 109)) : new SolidColorBrush(Color.FromRgb(255, 112, 133));
        }

        private void ApplyDensityStats(ApiManager.LiveFrameDetectionCountsResponse density)
        {
            var count = density?.TotalDetectionCount ?? 0;
            DensityText.Text = count.ToString("N0");
            OverviewDensityText.Text = count.ToString("N0");
        }

        private string GetCameraHistoryBucket()
        {
            if (_cameraHistoryHours <= 1) return "5m";
            // Uptime service accepts only auto, 5m, 1h, 1d and 7d.
            // Do not send legacy 30m/2h values because it rejects the
            // complete request with HTTP 400.
            if (_cameraHistoryHours <= 24) return "1h";
            return "1d";
        }

        private void ApplyCameraHealthHistory(IEnumerable<ApiManager.CameraHealthTimeseriesPoint> points,
            DateTime now, int currentOnline, int currentOffline, double currentUptimePercent)
        {
            var data = (points ?? Enumerable.Empty<ApiManager.CameraHealthTimeseriesPoint>()).ToList();
            if (data.Count == 0)
            {
                CameraHistory.Clear();
                RefreshCameraHealthChart();
                return;
            }

            CameraHistory.Clear();
            var maxCount = Math.Max(1, data.Max(point => Math.Max(point.Online, point.Unavailable > 0 ? point.Unavailable : point.Offline + point.Unknown)));
            var visiblePointCount = _cameraHistoryHours <= 24
                ? 24
                : _cameraHistoryHours <= 7 * 24 ? 7 : 30;
            foreach (var point in data.Skip(Math.Max(0, data.Count - visiblePointCount)))
            {
                DateTime timestamp;
                if (!DateTime.TryParse(point.BucketStart, out timestamp)) continue;
                var offline = point.Unavailable > 0 ? point.Unavailable : point.Offline + point.Unknown;
                var uptime = point.UptimePercent ?? (point.Online + offline == 0 ? 0 : point.Online * 100d / (point.Online + offline));
                AddCameraHistoryItem(timestamp, point.Online, offline, uptime, maxCount);
            }
            RefreshCameraHealthChart();
        }

        private void RefreshCameraHealthChart()
        {
            CameraHistoryLabels.Clear();
            CameraOnlineValues.Clear();
            CameraOfflineValues.Clear();
            CameraUptimeValues.Clear();
            for (var index = 0; index < CameraHistory.Count; index++)
            {
                var item = CameraHistory[index];
                CameraHistoryLabels.Add(item.Label);
                CameraOnlineValues.Add(item.Online);
                CameraOfflineValues.Add(item.Offline);
                CameraUptimeValues.Add(item.UptimePercent);
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
                Label = _cameraHistoryHours <= 24 ? time.ToString("HH:mm") : time.ToString("dd-MM"),
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

        private async void CameraHistoryRangeButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || !int.TryParse(button.Tag as string, out var hours)) return;
            _cameraHistoryHours = hours;
            CameraHistory.Clear();
            UpdateCameraHistoryRangeButtons();
            await RefreshCameraHistoryAsync();
        }

        private async Task RefreshCameraHistoryAsync()
        {
            try
            {
                var cameraIds = GetProfileCameraIds();
                var now = DateTime.Now;
                var trend = await ApiManager.Instance.GetCameraHealthTimeseriesAsync(
                    now.AddHours(-_cameraHistoryHours), now, GetCameraHistoryBucket(), cameraIds, _lifetime.Token);
                var profileCameraIds = new HashSet<string>(cameraIds, StringComparer.OrdinalIgnoreCase);
                var cameras = (GlobalSystem.Instance.CameraList ?? new List<Camera>())
                    .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID)
                        && profileCameraIds.Contains(camera.camID.Trim()))
                    .ToList();
                var online = cameras.Count(camera => camera != null && camera.is_online == true);
                var offline = Math.Max(0, cameraIds.Count - online);
                var uptime = cameraIds.Count == 0 ? 0 : online * 100d / cameraIds.Count;
                ApplyCameraHealthHistory(trend?.Data, now, online, offline, uptime);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggerManager.LogException(ex, "Dashboard camera history refresh"); }
        }

        private void UpdateCameraHistoryRangeButtons()
        {
            foreach (var button in new[]
            {
                CameraHistory24hButton, CameraHistory7dButton, CameraHistory30dButton,
                CameraHistory24hButtonNew, CameraHistory7dButtonNew, CameraHistory30dButtonNew,
                CameraHistory24hButtonVisible, CameraHistory7dButtonVisible, CameraHistory30dButtonVisible
            })
            {
                if (button == null) continue;
                var selected = int.TryParse(button.Tag as string, out var hours) && hours == _cameraHistoryHours;
                button.Background = selected ? new SolidColorBrush(Color.FromRgb(45, 106, 255)) : Brushes.Transparent;
                button.Foreground = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(203, 219, 237));
            }
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
                    GetLatestVehicleStart(now), now, GetProfileCameraIds(), cancellationToken: _lifetime.Token);
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
                            image.Freeze();
                            _vehicleImageCache[vehicle.AssetId] = image;
                            _vehicleImageCacheOrder.Enqueue(vehicle.AssetId);
                            while (_vehicleImageCacheOrder.Count > MaxVehicleImageCacheEntries)
                            {
                                var expiredAssetId = _vehicleImageCacheOrder.Dequeue();
                                _vehicleImageCache.Remove(expiredAssetId);
                            }
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
            if (_selectedPreviewCameras.Count == 0)
            {
                var cameras = (GlobalSystem.Instance.CameraList ?? new List<Camera>())
                    .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID))
                    .OrderByDescending(camera => camera.is_online == true)
                    .Take(4)
                    .ToList();
                _selectedPreviewCameras.AddRange(cameras);
            }
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
