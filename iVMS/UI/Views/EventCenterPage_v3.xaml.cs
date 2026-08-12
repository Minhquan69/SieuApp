using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using V3SClient.libs;

namespace V3SClient.UI.Views
{
    // ─────────────────────────────────────────────────────────
    //  View-model for one event / vehicle row
    // ─────────────────────────────────────────────────────────
    public sealed class EventRow_v3 : INotifyPropertyChanged
    {
        // Shared fields
        public string DetectionId   { get; set; }
        public string AssetId       { get; set; }
        public string Camera        { get; set; }
        public string Title         { get; set; }   // biển số / object-id
        public string Type          { get; set; }   // friendly event type
        public string Confidence    { get; set; }
        public double ConfidenceRaw { get; set; }   // 0–100

        // Vehicle-specific
        public string VehicleType   { get; set; }
        public string RoiName       { get; set; }
        public string TimeIn        { get; set; }
        public string TimeOnly => string.IsNullOrWhiteSpace(TimeIn) ? "—" : TimeIn.Split(' ')[0];
        public string DateOnly => string.IsNullOrWhiteSpace(TimeIn) || TimeIn.Split(' ').Length < 2 ? string.Empty : TimeIn.Split(' ')[1];
        public string TimeOut       { get; set; }
        public string Duration      { get; set; }
        public string PlaybackUrl   { get; set; }

        // Thumbnail (lazy)
        private BitmapImage _thumb;
        public BitmapImage ThumbnailSource
        {
            get => _thumb;
            set { _thumb = value; OnPropertyChanged(nameof(ThumbnailSource)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string n) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed class CameraCheckItem : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Name { get; set; }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string n) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ─────────────────────────────────────────────────────────
    //  Page
    // ─────────────────────────────────────────────────────────
    public partial class EventCenterPage_v3 : UserControl
    {
        // Collections
        private readonly ObservableCollection<EventRow_v3> _allRows   = new ObservableCollection<EventRow_v3>();
        private readonly ObservableCollection<EventRow_v3> _pageRows  = new ObservableCollection<EventRow_v3>();
        private readonly ObservableCollection<CameraCheckItem> _cameraFilters = new ObservableCollection<CameraCheckItem>();
        private readonly Dictionary<string, string> _roiNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // State
        private CancellationTokenSource _cts        = new CancellationTokenSource();
        private CancellationTokenSource _tableCts    = new CancellationTokenSource();
        private readonly DispatcherTimer _fullRefreshTimer;
        private bool _isFullRefreshRunning;
        private bool   _isPopulating  = false;
        private int    _currentPage   = 1;
        private int    _pageSize      = 10;
        private int    _totalPages    = 1;
        private int    _totalItems    = 0;
        private string _searchQuery   = string.Empty;
        private string _selCamera     = "all";
        private string _activeTab     = "completed";
        private string _selectedRoiId = "all";
        private double _minConfidence = 0.0;
        
        private DateTime _displayedCalendarMonth = DateTime.Today;
        private DateTime _selectedDate = DateTime.Today;
        private VPlaybackHLS _detailPlayback;
        private EventRow_v3 _detailRow;
        private bool _isAdjustingDetailPlaybackHeight;

        private void DetailPlaybackPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isAdjustingDetailPlaybackHeight || DetailPlaybackPanel == null || DetailPlaybackPanel.ActualWidth <= 0)
                return;

            var targetHeight = Math.Max(465d,
                Math.Ceiling(Math.Max(0d, DetailPlaybackPanel.ActualWidth - 18d) * 9d / 16d) + 126d);
            if (Math.Abs(DetailPlaybackPanel.Height - targetHeight) < 1d)
                return;

            _isAdjustingDetailPlaybackHeight = true;
            try { DetailPlaybackPanel.Height = targetHeight; }
            finally { _isAdjustingDetailPlaybackHeight = false; }
        }

        // ─────────────────────────────────────────────────────
        public EventCenterPage_v3()
        {
            InitializeComponent();

            _fullRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _fullRefreshTimer.Tick += async (sender, args) => await RefreshAllAsync();

            // Keep labels readable even when legacy XAML has been saved using a
            // different code page on another workstation.
            TabCompleted.Content = "Phương tiện hoàn thành công đoạn";
            TabRecorded.Content = "Phương tiện ghi nhận";
            SearchPlaceholder.Text = "Tìm kiếm biển số, loại xe, camera...";

            CompletedGrid.ItemsSource = _pageRows;
            RecordedGrid.ItemsSource  = _pageRows;
            DetailList.ItemsSource = _pageRows;

            // Set up initial state for calendar
            _selectedDate = DateTime.Today;
            _displayedCalendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var initialStr = _selectedDate.ToString("dd/MM/yyyy");
            DateFilterText.Text = initialStr;
            PopupDateInputText.Text = initialStr;
            PageSizeCombo.SelectedIndex      = 0;

            ActivateTab("completed");

            // hide search placeholder on type
            SearchBox.TextChanged += (_, __) =>
                SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;

            Loaded += EventCenterPage_Loaded;
            // A pending async refresh can resume after Unloaded (for example while
            // navigating quickly).  Cancelling is sufficient; disposing here races
            // with RefreshAllAsync and causes ObjectDisposedException on Cancel().
            Unloaded += EventCenterPage_Unloaded;
        }

        private async void EventCenterPage_Loaded(object sender, RoutedEventArgs e)
        {
            await InitAsync();
            _fullRefreshTimer.Start();

            // VPlaybackHLS releases its native video surfaces while this page is
            // unloaded. Rebuild the embedded player when the Event page is shown
            // again so the Frame never keeps a detached gray video surface.
            if (_detailRow != null && DetailPanel.Visibility == Visibility.Visible)
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                if (IsLoaded && _detailRow != null && DetailPanel.Visibility == Visibility.Visible)
                    await OpenDetailPlaybackAsync(_detailRow);
            }
        }

        private void EventCenterPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _fullRefreshTimer.Stop();
            _cts.Cancel();
            _tableCts.Cancel();

            // Detaching forces VPlaybackHLS.Unloaded to dispose its native decoder
            // handles before another page occupies the same HWND host.
            if (_detailPlayback != null)
            {
                DetailPlaybackHost.Content = null;
                _detailPlayback = null;
            }
        }

        // ─────────────────────────────────────────────────────
        //  INIT
        // ─────────────────────────────────────────────────────
        private async Task InitAsync()
        {
            if (_cts.IsCancellationRequested)
            {
                _cts = new CancellationTokenSource();
            }
            PopulateCameraFilters();
            await LoadRoiBatchAsync(_cts.Token);
            await RefreshAllAsync();
        }

        private void PopulateCameraFilters()
        {
            _isPopulating = true;
            try
            {
                var ids = AllCameraIds();
                
                // Populate normal combobox for table filter
                ListCameraCombo.Items.Clear();
                ListCameraCombo.Items.Add(new ComboBoxItem { Content = "Tất cả camera", Tag = "all" });
                DetailCameraCombo.Items.Clear();
                DetailCameraCombo.Items.Add(new ComboBoxItem { Content = "Tất cả camera", Tag = "all" });
                foreach (var id in ids)
                {
                    ListCameraCombo.Items.Add(new ComboBoxItem { Content = id, Tag = id });
                    DetailCameraCombo.Items.Add(new ComboBoxItem { Content = id, Tag = id });
                }
                ListCameraCombo.SelectedIndex = 0;
                DetailCameraCombo.SelectedIndex = 0;

            // Populate multi-select for top filter
            _cameraFilters.Clear();
            var allItem = new CameraCheckItem { Id = "all", Name = "Tất cả camera", IsSelected = true };
            _cameraFilters.Add(allItem);
            foreach (var id in ids)
                _cameraFilters.Add(new CameraCheckItem { Id = id, Name = id, IsSelected = false });
            
            CameraCheckList.ItemsSource = _cameraFilters;
            UpdateCameraFilterUI();
            }
            finally
            {
                _isPopulating = false;
            }
        }

        private void UpdateCameraFilterUI()
        {
            var selected = _cameraFilters.Where(x => x.Id != "all" && x.IsSelected).ToList();
            if (selected.Count == 0 || _cameraFilters.First(x => x.Id == "all").IsSelected)
            {
                CameraToggleText.Text = "Tất cả camera";
                CameraSelectedCountText.Text = $"0/{_cameraFilters.Count - 1}";
            }
            else
            {
                CameraToggleText.Text = selected.Count == 1 ? selected[0].Name : $"{selected.Count} camera";
                CameraSelectedCountText.Text = $"{selected.Count}/{_cameraFilters.Count - 1}";
            }
        }

        private List<string> AllCameraIds()
        {
            return (GlobalSystem.Instance.CameraList ?? new List<models.Camera>())
                .Where(c => !string.IsNullOrWhiteSpace(c.camID))
                .Select(c => c.camID.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        // ─────────────────────────────────────────────────────
        //  FULL REFRESH
        // ─────────────────────────────────────────────────────
        private async Task RefreshAllAsync()
        {
            if (_isFullRefreshRunning) return;
            _isFullRefreshRunning = true;
            var previousCts = _cts;
            previousCts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            try
            {
                await Task.WhenAll(LoadKpiAsync(token), LoadEventsAsync(token));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggerManager.LogException(ex, "EventCenter.RefreshAll"); }
            finally { _isFullRefreshRunning = false; }
        }

        // ─────────────────────────────────────────────────────
        //  KPI
        // ─────────────────────────────────────────────────────
        private async Task LoadKpiAsync(CancellationToken token)
        {
            try
            {
                var date   = await Dispatcher.InvokeAsync(() => _selectedDate);
                var camIds = FilteredCameraIds();

                var summary = await ApiManager.Instance.GetAiEventSummaryAsync(date.Date, camIds, cancellationToken: token);
                if (token.IsCancellationRequested) return;

                if (summary == null)
                {        

                    await Dispatcher.InvokeAsync(() => {
                        KpiTodayDetail.Text = "API returned null";
                    });
                    return;
                }

                int today = summary.Today?.Value ?? 0;
                int week = summary.Last7Days?.Value ?? 0;
                int month = summary?.Month?.Value ?? 0;
                int density = summary?.InStation?.Value ?? 0;

                string TrendValue(ApiManager.AiEventCenterSummaryKpi item)
                {
                    if (item == null) return "N/A";
                    if (string.Equals(item.Trend, "new", StringComparison.OrdinalIgnoreCase)) return "new";
                    if (item.Percentage.HasValue) return item.Percentage.Value.ToString(CultureInfo.InvariantCulture);
                    if (!item.PreviousValue.HasValue)
                        return string.Equals(item.Trend, "up", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(item.Trend, "down", StringComparison.OrdinalIgnoreCase)
                            ? item.Trend
                            : "N/A";
                    if (item.PreviousValue.Value == 0) return item.Value > 0 ? "new" : "0";
                    return Math.Round(((item.Value - item.PreviousValue.Value) / (double)item.PreviousValue.Value) * 100.0, 1)
                        .ToString(CultureInfo.InvariantCulture);
                }

                string tTrend = TrendValue(summary?.Today);
                string wTrend = TrendValue(summary?.Last7Days);
                string mTrend = TrendValue(summary?.Month);
                string dTrend = TrendValue(summary?.InStation);

                await Dispatcher.InvokeAsync(() =>
                {
                    var vi = new CultureInfo("vi-VN");
                    KpiTodayValue.Text    = today.ToString("N0", vi);
                    KpiTodayDetail.Text   = string.Empty;
                    SetTrendUI(KpiTodayTrend, tTrend);

                    KpiDensityValue.Text  = density.ToString("N0", vi);
                    KpiDensityDetail.Text = "so với kỳ trước";
                    SetTrendUI(KpiDensityTrend, dTrend);

                    KpiWeekValue.Text     = week.ToString("N0", vi);
                    KpiWeekDetail.Text    = string.Empty;
                    SetTrendUI(KpiWeekTrend, wTrend);

                    KpiMonthValue.Text    = month.ToString("N0", vi);
                    KpiMonthDetail.Text   = string.Empty;
                    SetTrendUI(KpiMonthTrend, mTrend);

                    LastSyncText.Text     = DateTime.Now.ToString("HH:mm:ss dd-MM-yyyy");
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) 
            { 
                LoggerManager.LogException(ex, "EventCenter.KPI"); 
                await Dispatcher.InvokeAsync(() => {
                    KpiTodayDetail.Text = "Error: " + ex.Message;
                });
            }
        }

        private void SetTrendUI(TextBlock block, string trendStr)
        {
            if (block == null) return;
            if (string.Equals(trendStr, "new", StringComparison.OrdinalIgnoreCase))
            {
                block.Text = "↑ Mới phát sinh so với kỳ trước";
                block.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                return;
            }
            if (string.Equals(trendStr, "up", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trendStr, "down", StringComparison.OrdinalIgnoreCase))
            {
                var isUp = string.Equals(trendStr, "up", StringComparison.OrdinalIgnoreCase);
                block.Text = (isUp ? "↑" : "↓") + " So với kỳ trước";
                block.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isUp ? "#10B981" : "#EF4444"));
                return;
            }
            if (string.IsNullOrWhiteSpace(trendStr) || trendStr == "N/A" || !double.TryParse(trendStr.Replace("%", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double tVal))
            {
                block.Text = string.Empty;
                block.Foreground = FindResource("VmsTextTertiaryBrush_v3") as Brush;
                return;
            }

            if (tVal > 0)
            {
                block.Text = $"↑ {tVal}% so với kỳ trước";
                block.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")); // Green
            }
            else if (tVal < 0)
            {
                block.Text = $"↓ {Math.Abs(tVal)}% so với kỳ trước";
                block.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Red
            }
            else
            {
                block.Text = $"~ {tVal}% so với kỳ trước";
                block.Foreground = FindResource("VmsTextTertiaryBrush_v3") as Brush;
            }
        }

        // ─────────────────────────────────────────────────────
        //  ROI BATCH
        // ─────────────────────────────────────────────────────
        private async Task LoadRoiBatchAsync(CancellationToken token)
        {
            try
            {
                var camIds = FilteredCameraIds();
                var rois = await ApiManager.Instance.GetRoiBatchByCamerasAsync(camIds, cancellationToken: token);
                if (token.IsCancellationRequested) return;    
                var previousRoiId = _selectedRoiId;


                await Dispatcher.InvokeAsync(() =>
                {
                    _isPopulating = true;
                    
                    RoiHeaderCombo.Items.Clear();
                    ProcessCombo.Items.Clear();
                    _roiNames.Clear();
                    
                    var allItem1 = new ComboBoxItem { Content = "Tất cả công đoạn", Tag = "all" };
                    var allItem2 = new ComboBoxItem { Content = "Tất cả công đoạn", Tag = "all" };
                    RoiHeaderCombo.Items.Add(allItem1);
                    ProcessCombo.Items.Add(allItem2);
                    
                    foreach (var item in rois?.Data ?? Enumerable.Empty<ApiManager.RoiBatchItem>())
                    {
                        var roi = item.Roi;
                        var roiId = roi?.Id ?? roi?.RoiId;
                        var roiName = roi?.Name ?? roi?.RoiRule?.Name ?? roiId;
                        if (string.IsNullOrWhiteSpace(roiId) || string.IsNullOrWhiteSpace(roiName)) continue;

                        foreach (var alias in new[] { roi?.Id, roi?.RoiId }.Where(value => !string.IsNullOrWhiteSpace(value)))
                            _roiNames[$"{item.CameraId}:{alias}"] = roiName;

                        RoiHeaderCombo.Items.Add(new ComboBoxItem { Content = roiName, Tag = roiId });
                        ProcessCombo.Items.Add(new ComboBoxItem { Content = roiName, Tag = roiId });
                    }

                    var selectedHeader = RoiHeaderCombo.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(x => x.Tag?.ToString() == previousRoiId);
                    var selectedProcess = ProcessCombo.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(x => x.Tag?.ToString() == previousRoiId);
                    RoiHeaderCombo.SelectedItem = selectedHeader ?? allItem1;
                    ProcessCombo.SelectedItem = selectedProcess ?? allItem2;
                    _selectedRoiId = previousRoiId == "all" || selectedProcess == null ? "all" : previousRoiId;
                    
                    _isPopulating = false;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "EventCenter.LoadRoiBatch");
            }
        }

        // ─────────────────────────────────────────────────────
        //  EVENT / VEHICLE LIST
        // ─────────────────────────────────────────────────────
        private async Task LoadEventsAsync(CancellationToken token)
        {
            try
            {
                var date   = await Dispatcher.InvokeAsync(() => _selectedDate);
                var start  = date.Date;
                var end    = date.Date == DateTime.Today.Date ? DateTime.Now : date.Date.AddDays(1).AddTicks(-1);
                var camIds = FilteredCameraIds();
                var q      = _searchQuery.Trim();

                var rows = new List<EventRow_v3>();

                if (_activeTab == "completed")
                {
                    string rId = _selectedRoiId == "all" ? null : _selectedRoiId;
                    var response = await ApiManager.Instance.GetRoiObjectsAsync(start, camIds, _currentPage, _pageSize, q, roiId: rId, cancellationToken: token);
                    if (token.IsCancellationRequested) return;
                    _totalItems = response?.TotalItems ?? 0;
                    _totalPages = response?.TotalPages ?? 1;
                    var items = response?.OverThreshold ?? new List<ApiManager.RoiObjectItem>();
                    rows = items.Select(MapRow).ToList();
                }
                else
                {
                    var response = await ApiManager.Instance.GetAiEventObjectCropHistoryAsync(start, end, camIds, _currentPage, _pageSize, q, minConfidence: _minConfidence, cancellationToken: token);
                    if (token.IsCancellationRequested) return;
                    _totalItems = response?.TotalItems ?? 0;
                    _totalPages = response?.TotalPages ?? 1;
                    var items = response?.Items ?? new List<ApiManager.AiEventObjectCropItem>();
                    rows = items.Select(MapRow).ToList();
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    _allRows.Clear();
                    _pageRows.Clear();
                    foreach (var r in rows)
                    {
                        _allRows.Add(r);
                        _pageRows.Add(r);
                    }
                    ApplyPagination();
                    UpdateEmptyState();
                    LastSyncText.Text = DateTime.Now.ToString("HH:mm:ss dd-MM-yyyy");
                });

                _ = LoadThumbnailsAsync(token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "EventCenter.LoadEvents");
                await Dispatcher.InvokeAsync(() => UpdateEmptyState());
            }
        }

        private EventRow_v3 MapRow(ApiManager.RoiObjectItem x)
        {
            var title = new[] { x.Plate, x.Label, x.DetectedObjectIds, x.Name, x.ObjectId }
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Không nhận diện";
            var rawType = new[] { x.ObjectType, x.MetaType, x.EventType }
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
            var timeIn = FormatEventDateTime(x.EnteredAt ?? x.EventTime);
            var timeOut = FormatEventDateTime(x.ExitedAt);
            var roiKey = $"{x.CameraId}:{x.RoiId}";
            var roiName = _roiNames.TryGetValue(roiKey, out var configuredRoiName)
                ? configuredRoiName
                : (x.RoiId ?? "—");
            var confidence = (x.PlateConfidence ?? x.Confidence ?? 0) * 100.0;

            return new EventRow_v3
            {
                DetectionId  = x.ObjectKey ?? x.RoiId ?? "—",
                AssetId      = x.CropAssetId ?? x.AssetId ?? "—",
                Camera       = x.CameraId ?? "—",
                Title        = title,
                Type         = FriendlyType(rawType),
                Confidence   = confidence > 0 ? confidence.ToString("0.#") + "%" : "—",
                ConfidenceRaw = confidence,
                VehicleType  = FriendlyType(rawType),
                RoiName      = roiName,
                TimeIn       = timeIn,
                TimeOut      = timeOut,
                Duration     = !string.IsNullOrWhiteSpace(x.DwellHuman)
                    ? x.DwellHuman
                    : (x.DwellSeconds.HasValue ? FormatDuration(x.DwellSeconds.Value) : (x.Duration ?? "—")),
                PlaybackUrl  = x.PlaybackUrl,
            };
        }

        private static string FormatEventDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "—";
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
                return parsed.ToString("HH:mm:ss dd/MM/yyyy");
            return value;
        }

        private static string FormatDuration(double totalSeconds)
        {
            var duration = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}p {duration.Seconds + duration.Milliseconds / 1000.0:0.#}s";
            return $"{duration.TotalSeconds:0.#}s";
        }

        private EventRow_v3 MapRow(ApiManager.AiEventObjectCropItem x)
        {
            DateTime t;
            DateTime.TryParse(x.EventTime, null, DateTimeStyles.RoundtripKind, out t);
            var timeStr = t == DateTime.MinValue ? x.EventTime : t.ToString("HH:mm:ss dd/MM/yyyy");
            var conf    = (x.Confidence ?? 0) * 100.0;
            var objId   = string.IsNullOrWhiteSpace(x.ObjectId) ? (x.MetaType ?? "—") : x.ObjectId;

            return new EventRow_v3
            {
                DetectionId  = x.DetectionId ?? x.MessageId ?? "—",
                AssetId      = !string.IsNullOrWhiteSpace(x.AssetId) ? x.AssetId : (x.CropAssetId ?? "—"),
                Camera       = x.CameraId ?? "—",
                Title        = objId,
                Type         = FriendlyType(x.EventType ?? x.MetaType ?? ""),
                Confidence   = conf.ToString("0.#") + "%",
                ConfidenceRaw = conf,
                VehicleType  = FriendlyType(x.MetaType ?? x.EventType ?? ""),
                RoiName      = "—",
                TimeIn       = timeStr,
                TimeOut      = "—",
                Duration     = "—",
                PlaybackUrl  = null,
            };
        }

        // ─────────────────────────────────────────────────────
        //  THUMBNAILS
        // ─────────────────────────────────────────────────────
        private async Task LoadThumbnailsAsync(CancellationToken token)
        {
            var rows = _pageRows.ToList();
            var assetIds = rows.Where(row => row != null && !string.IsNullOrWhiteSpace(row.AssetId) && row.AssetId != "—")
                .Select(row => row.AssetId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var accessUrls = await ApiManager.Instance.GetDashboardAssetAccessUrlsAsync(assetIds, token);
            await Task.WhenAll(rows.Select(async row =>
            {
                if (token.IsCancellationRequested) return;
                if (string.IsNullOrWhiteSpace(row.AssetId) || row.AssetId == "—")
                {
                    LoggerManager.LogDebug("EventCenter thumbnail skipped: no asset id for " + row.Title);
                    return;
                }
                try
                {
                    string url;
                    if (accessUrls == null || !accessUrls.TryGetValue(row.AssetId, out url)) return;
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        LoggerManager.LogWarn("EventCenter thumbnail URL unavailable for asset " + row.AssetId);
                        return;
                    }
                    using (var client = new WebClient())
                    using (var imageStream = new MemoryStream(await client.DownloadDataTaskAsync(new Uri(url)), writable: false))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 144;
                        bmp.StreamSource = imageStream;
                        bmp.EndInit();
                        bmp.Freeze();
                        await Dispatcher.InvokeAsync(() =>
                        {
                            row.ThumbnailSource = bmp;
                        });
                    }
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, "EventCenter thumbnail " + row.AssetId);
                }
            }));
        }


        // ─────────────────────────────────────────────────────
        //  PAGINATION
        // ─────────────────────────────────────────────────────
        private void ApplyPagination()
        {
            // Guard: controls may not be ready during InitializeComponent
            if (PageInfoText == null || PageLabel == null || PrevBtn == null || NextBtn == null)
                return;

            int total = Math.Max(0, _totalItems);
            _totalPages = Math.Max(1, _totalPages);
            _currentPage = Math.Max(1, Math.Min(_currentPage, _totalPages));

            int start = total == 0 ? 0 : ((_currentPage - 1) * _pageSize) + 1;
            int end = total == 0 ? 0 : Math.Min(start + _pageRows.Count - 1, total);
            PageInfoText.Text = $"Hiển thị {start}\u2013{end} của {total} sự kiện";
            PageLabel.Text    = $"{_currentPage} / {_totalPages}";
            if (DetailPageLabel != null) DetailPageLabel.Text = $"{start}–{end} / {total} · {_currentPage}/{_totalPages}";
            PrevBtn.IsEnabled = _currentPage > 1;
            NextBtn.IsEnabled = _currentPage < _totalPages;
        }

        private void UpdateEmptyState()
        {
            if (EmptyState == null) return;
            EmptyState.Visibility = _allRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────
        private List<string> FilteredCameraIds()
        {
            if (_cameraFilters.Count == 0) return AllCameraIds();
            if (_cameraFilters.First(x => x.Id == "all").IsSelected) return AllCameraIds();

            var selected = _cameraFilters.Where(x => x.Id != "all" && x.IsSelected).Select(x => x.Id).ToList();
            if (selected.Count == 0) return AllCameraIds();
            
            return selected;
        }

        private static bool Matches(string s, string q) =>
            !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string FriendlyType(string raw)
        {
            switch ((raw ?? "").ToLowerInvariant())
            {
                case "car":         return "Xe con";
                case "vehicle_lp":  return "Xe";
                case "motorcycle":  return "Xe máy";
                case "truck":       return "Xe tải";
                case "bus":         return "Xe khách";
                case "person":      return "Người";
                case "fire":        return "Lửa / Khói";
                case "motion":      return "Chuyển động";
                case "intrusion":   return "Xâm nhập";
                default:            return string.IsNullOrWhiteSpace(raw) ? "Khác" : raw;
            }
        }

        private void ActivateTab(string tab)
        {
            _activeTab = tab;

            var primary   = FindResource("VmsPrimaryBrush_v3")       as Brush;
            var secondary = FindResource("VmsTextSecondaryBrush_v3") as Brush;

            TabCompleted.Tag        = tab == "completed" ? "active" : null;
            TabRecorded.Tag         = tab == "recorded"  ? "active" : null;
            TabCompleted.Foreground = tab == "completed" ? primary : secondary;
            TabRecorded.Foreground  = tab == "recorded"  ? primary : secondary;

            CompletedGrid.Visibility = tab == "completed" ? Visibility.Visible : Visibility.Collapsed;
            RecordedGrid.Visibility  = tab == "recorded"  ? Visibility.Visible : Visibility.Collapsed;
            
            if (ProcessCombo != null) ProcessCombo.Visibility = tab == "completed" ? Visibility.Visible : Visibility.Collapsed;
            if (ConfidencePanel != null) ConfidencePanel.Visibility = tab == "recorded" ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─────────────────────────────────────────────────────
        //  DETAIL PANEL
        // ─────────────────────────────────────────────────────
        private async Task ShowDetailAsync(EventRow_v3 row)
        {
            if (row == null) return;
            if (ReferenceEquals(_detailRow, row) && DetailPanel.Visibility == Visibility.Visible) return;
            _detailRow = row;

            // The detail panel is now a full-width overlay, matching the web
            // detail view. Keep the underlying list at its original width.
            DetailCol.Width       = new GridLength(0);
            DetailPanel.Visibility = Visibility.Visible;
            DetailList.SelectedItem = row;

            DetailTitle.Text = "Biển số: " + row.Title;
            DTitle.Text      = row.Title;
            DCamera.Text     = row.Camera;
            DRoi.Text        = row.RoiName;
            DTimeIn.Text     = row.TimeIn;
            DTimeOut.Text    = row.TimeOut;
            DDuration.Text   = row.Duration;
            DDetId.Text      = row.DetectionId;
            DAssetId.Text    = row.AssetId;

            PreviewImage.Source         = null;
            DetailImage.Source          = row.ThumbnailSource;
            PreviewImage.Visibility     = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            await OpenDetailPlaybackAsync(row);

            if (row.AssetId != "—" && !string.IsNullOrWhiteSpace(row.AssetId))
            {
                try
                {
                    var urls = await ApiManager.Instance.GetDashboardAssetAccessUrlsAsync(new[] { row.AssetId }, _cts.Token);
                    string url;
                    if (urls == null || !urls.TryGetValue(row.AssetId, out url)) return;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        BitmapImage bmp;
                        using (var client = new WebClient())
                        using (var stream = new MemoryStream(await client.DownloadDataTaskAsync(new Uri(url)), writable: false))
                        {
                            bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = stream;
                            bmp.EndInit();
                            bmp.Freeze();
                        }
                        PreviewImage.Source          = bmp;
                        DetailImage.Source           = bmp;
                        PreviewImage.Visibility      = Visibility.Visible;
                        PreviewPlaceholder.Visibility = Visibility.Collapsed;
                        if (row.ThumbnailSource == null)
                        {
                            try
                            {
                                var t = new BitmapImage();
                                t.BeginInit();
                                t.UriSource       = new Uri(url);
                                t.CacheOption     = BitmapCacheOption.OnLoad;
                                t.DecodePixelWidth = 80;
                                t.EndInit(); t.Freeze();
                                row.ThumbnailSource = t;
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex) { LoggerManager.LogException(ex, "EventCenter.Detail.Image"); }
            }
        }

        private void CloseDetail()
        {
            DetailPlaybackHost.Content = null;
            _detailPlayback = null;
            _detailRow = null;
            DetailCol.Width       = new GridLength(0);
            DetailPanel.Visibility = Visibility.Collapsed;
            CompletedGrid.UnselectAll();
            RecordedGrid.UnselectAll();
        }

        private async Task OpenDetailPlaybackAsync(EventRow_v3 row)
        {
            DateTime start;
            if (!TryParseEventTime(row.TimeIn, out start))
            {
                DetailVideoPlaceholder.Text = "Không xác định được thời điểm sự kiện";
                DetailVideoPlaceholder.Visibility = Visibility.Visible;
                return;
            }
            DateTime end;
            if (!TryParseEventTime(row.TimeOut, out end) || end <= start) end = start.AddMinutes(1);
            start = start.AddSeconds(-15);
            end = end.AddSeconds(15);

            try
            {
                DetailVideoPlaceholder.Text = "Đang tải video từ playback...";
                DetailVideoPlaceholder.Visibility = Visibility.Visible;
                // A GStreamer playback page owns native decoder handles. Release the
                // previous page before opening another event; otherwise only the first
                // selected record can decode/seek reliably.
                if (_detailPlayback != null)
                {
                    DetailPlaybackHost.Content = null;
                    _detailPlayback = null;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                }
                _detailPlayback = new VPlaybackHLS(GlobalSystem.Instance.CameraGroups.CamGroupList);
                _detailPlayback.SetEmbeddedMode();
                DetailPlaybackHost.Navigate(_detailPlayback);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                _detailPlayback.OpenEventPlayback(row.Camera, start, end);
                DetailVideoPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                DetailVideoPlaceholder.Text = "Không thể tải video playback";
                DetailVideoPlaceholder.Visibility = Visibility.Visible;
                LoggerManager.LogException(ex, "EventCenter.Detail.Playback");
            }
        }

        private static bool TryParseEventTime(string value, out DateTime time)
        {
            return DateTime.TryParseExact(value, "HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture,
                       DateTimeStyles.AllowWhiteSpaces, out time)
                   || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time);
        }

        private async void DetailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = DetailList.SelectedItem as EventRow_v3;
            if (row != null) await ShowDetailAsync(row);
        }

        private void DetailSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            DetailSearchPlaceholder.Visibility = string.IsNullOrEmpty(DetailSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (SearchBox != null && SearchBox.Text != DetailSearchBox.Text) SearchBox.Text = DetailSearchBox.Text;
        }

        private void DetailCamera_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tag = (DetailCameraCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            var match = ListCameraCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
            if (match != null) ListCameraCombo.SelectedItem = match;
        }

        private void DetailPrevPage_Click(object sender, RoutedEventArgs e) => PrevPage_Click(sender, e);
        private void DetailNextPage_Click(object sender, RoutedEventArgs e) => NextPage_Click(sender, e);

        // ─────────────────────────────────────────────────────
        //  EVENT HANDLERS
        // ─────────────────────────────────────────────────────
        private async void Refresh_Click(object s, RoutedEventArgs e)
            => await RefreshAllAsync();

        private async void TableRefresh_Click(object s, RoutedEventArgs e)
        {
            var previousCts = _tableCts;
            previousCts.Cancel();
            _tableCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var token = _tableCts.Token;
            await LoadEventsAsync(token);
        }

        private void DateFilterToggle_Click(object s, RoutedEventArgs e)
        {
            if (DateFilterToggle.IsChecked == true)
            {
                var date = _selectedDate;
                PopupDateInputText.Text = date.ToString("dd/MM/yyyy");
                _displayedCalendarMonth = new DateTime(date.Year, date.Month, 1);
                RenderCalendar();
                // Popup opens via binding IsOpen="{Binding IsChecked, ...}"
            }
        }

        private void PreviousMonth_Click(object sender, RoutedEventArgs e)
        {
            _displayedCalendarMonth = _displayedCalendarMonth.AddMonths(-1);
            RenderCalendar();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _displayedCalendarMonth = _displayedCalendarMonth.AddMonths(1);
            RenderCalendar();
        }

        private void CalendarDay_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || !(button.Tag is DateTime dt)) return;
            _selectedDate = dt.Date;
            DateFilterToggle.IsChecked = false;
            
            var dStr = _selectedDate.ToString("dd/MM/yyyy");
            DateFilterText.Text = dStr;
            PopupDateInputText.Text = dStr;

            if (!IsLoaded) return;
            _currentPage = 1;
            _ = RefreshAllAsync();
        }

        private void RenderCalendar()
        {
            if (calendarDaysPanel == null) return;
            calendarMonthText.Text = _displayedCalendarMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            calendarDaysPanel.Children.Clear();

            DateTime first = new DateTime(_displayedCalendarMonth.Year, _displayedCalendarMonth.Month, 1);
            DateTime visibleStart = first.AddDays(-(int)first.DayOfWeek);

            for (var i = 0; i < 42; i++)
            {
                DateTime day = visibleStart.AddDays(i);
                var button = new Button
                {
                    Content = day.Day.ToString(CultureInfo.InvariantCulture),
                    Tag = day,
                    Style = FindResource("PlaybackCalendarDayButton_v3") as Style,
                    Foreground = day.Month == _displayedCalendarMonth.Month
                        ? FindResource("VmsTextPrimaryBrush_v3") as Brush
                        : FindResource("VmsTextTertiaryBrush_v3") as Brush,
                    Opacity = day.Month == _displayedCalendarMonth.Month ? 1.0 : 0.45
                };
                if (day.Date == _selectedDate.Date)
                {
                    button.Background = FindResource("VmsPrimarySoftBrush_v3") as Brush;
                    button.BorderBrush = FindResource("VmsPrimaryBrush_v3") as Brush;
                }
                button.Click += CalendarDay_Click;
                calendarDaysPanel.Children.Add(button);
            }
        }

        private void ListCameraFilter_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_isPopulating) return;

            if (ListCameraCombo.SelectedItem is ComboBoxItem item)
            {
                var sel = item.Tag?.ToString() ?? "all";
                _selCamera = sel;
                if (ProcessCombo != null)
                    ProcessCombo.IsEnabled = !string.Equals(sel, "all", StringComparison.OrdinalIgnoreCase);
                if (ProcessCombo != null && string.Equals(sel, "all", StringComparison.OrdinalIgnoreCase))
                {
                    _selectedRoiId = "all";
                    _isPopulating = true;
                    try { ProcessCombo.SelectedIndex = 0; }
                    finally { _isPopulating = false; }
                }
                // sync to _cameraFilters
                foreach (var cam in _cameraFilters)
                {
                    cam.IsSelected = (sel == "all" && cam.Id == "all") || (sel != "all" && cam.Id == sel);
                }
                UpdateCameraFilterUI();

                if (!IsLoaded) return;
                _currentPage = 1;
                _ = LoadEventsAsync(_cts.Token);
            }
        }

        private void CameraDropdownSearch_TextChanged(object s, TextChangedEventArgs e)
        {
            if (!(s is TextBox search)) return;
            var combo = search.TemplatedParent as ComboBox;
            if (combo == null || combo != ListCameraCombo) return;

            var query = search.Text?.Trim() ?? string.Empty;
            if (search.Parent is Panel host)
            {
                var hint = host.Children.OfType<TextBlock>().FirstOrDefault();
                if (hint != null) hint.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            }
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(combo.Items);
            if (view != null)
            {
                view.Filter = item =>
                {
                    var camera = item as ComboBoxItem;
                    if (camera == null) return false;
                    if (string.IsNullOrEmpty(query)) return true;
                    return (camera.Content?.ToString() ?? string.Empty)
                        .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                };
            }
        }

        private void Search_TextChanged(object s, TextChangedEventArgs e)
        {
            _searchQuery = SearchBox.Text ?? "";
            if (!IsLoaded) return;
            _currentPage = 1;
            _ = LoadEventsAsync(_cts.Token);
        }

        private string _completedSortMember;
        private bool _completedSortDescending;

        private static DateTime SortEventTime(string value)
        {
            DateTime time;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out time) ? time : DateTime.MinValue;
        }

        private static double SortDuration(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "—") return 0;
            var seconds = 0d;
            foreach (var part in value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var digits = new string(part.TakeWhile(char.IsDigit).ToArray());
                double number;
                if (!double.TryParse(digits, NumberStyles.Any, CultureInfo.InvariantCulture, out number)) continue;
                if (part.Contains("p")) seconds += number * 60;
                else if (part.Contains("s")) seconds += number;
            }
            return seconds;
        }

        private void CompletedGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            var member = e.Column.SortMemberPath;
            if (string.IsNullOrWhiteSpace(member)) return;
            _completedSortDescending = string.Equals(_completedSortMember, member, StringComparison.Ordinal) && !_completedSortDescending;
            _completedSortMember = member;
            foreach (var column in CompletedGrid.Columns) column.SortDirection = null;
            e.Column.SortDirection = _completedSortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;

            IEnumerable<EventRow_v3> sorted;
            switch (member)
            {
                case "TimeIn": sorted = _pageRows.OrderBy(row => SortEventTime(row.TimeIn)); break;
                case "TimeOut": sorted = _pageRows.OrderBy(row => SortEventTime(row.TimeOut)); break;
                case "Duration": sorted = _pageRows.OrderBy(row => SortDuration(row.Duration)); break;
                case "ConfidenceRaw": sorted = _pageRows.OrderBy(row => row.ConfidenceRaw); break;
                default: sorted = _pageRows.OrderBy(row => row.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase); break;
            }
            if (_completedSortDescending) sorted = sorted.Reverse();
            var rows = sorted.ToList();
            _pageRows.Clear();
            foreach (var row in rows) _pageRows.Add(row);
        }

        private async void Event_Selected(object s, SelectionChangedEventArgs e)
        {
            var grid = s as DataGrid;
            var row  = grid?.SelectedItem as EventRow_v3;
            await ShowDetailAsync(row);
        }

        private void CloseDetail_Click(object s, RoutedEventArgs e) => CloseDetail();

        private void TabCompleted_Click(object s, RoutedEventArgs e)
        {
            ActivateTab("completed");
            _currentPage = 1;
            _ = LoadEventsAsync(_cts.Token);
        }

        private void TabRecorded_Click(object s, RoutedEventArgs e)
        {
            ActivateTab("recorded");
            _currentPage = 1;
            _ = LoadEventsAsync(_cts.Token);
        }

        private void PrevPage_Click(object s, RoutedEventArgs e)
        {
            if (_currentPage <= 1) return;
            _currentPage--;
            _ = LoadEventsAsync(_cts.Token);
        }

        private void NextPage_Click(object s, RoutedEventArgs e)
        {
            if (_currentPage >= _totalPages) return;
            _currentPage++;
            _ = LoadEventsAsync(_cts.Token);
        }

        private void PageSize_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;   // ignore events during InitializeComponent
            if (PageSizeCombo.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out var sz))
            {
                _pageSize    = sz;
                _currentPage = 1;
                _ = LoadEventsAsync(_cts.Token);
            }
        }

        // ─────────────────────────────────────────────────────
        //  CAMERA MULTI-SELECT HANDLERS
        // ─────────────────────────────────────────────────────
        private void CameraSearchBox_TextChanged(object s, TextChangedEventArgs e)
        {
            var q = CameraSearchBox.Text?.Trim() ?? "";
            CameraSearchPlaceholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(CameraCheckList.ItemsSource);
            if (view != null)
            {
                view.Filter = item =>
                {
                    if (string.IsNullOrEmpty(q)) return true;
                    var cam = item as CameraCheckItem;
                    if (cam == null) return false;
                    if (cam.Id == "all") return true; // always show 'all'
                    return cam.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                };
            }
        }

        private void CameraCheckBox_Click(object s, RoutedEventArgs e)
        {
            if (s is CheckBox chk && chk.DataContext is CameraCheckItem clickedItem)
            {
                if (clickedItem.Id == "all")
                {
                    // if 'all' is toggled, toggle all others to match or false
                    bool state = clickedItem.IsSelected;
                    foreach (var cam in _cameraFilters)
                    {
                        if (cam.Id != "all") cam.IsSelected = false;
                    }
                    clickedItem.IsSelected = state; // 'all' must be checked manually
                }
                else
                {
                    // if a specific camera is toggled, uncheck 'all'
                    var allItem = _cameraFilters.FirstOrDefault(x => x.Id == "all");
                    if (allItem != null && clickedItem.IsSelected)
                    {
                        allItem.IsSelected = false;
                    }
                }
                UpdateCameraFilterUI();
            }
        }

        private void CameraResetBtn_Click(object s, RoutedEventArgs e)
        {
            CameraSearchBox.Text = "";
            var allItem = _cameraFilters.FirstOrDefault(x => x.Id == "all");
            if (allItem != null) allItem.IsSelected = true;
            
            foreach (var cam in _cameraFilters)
            {
                if (cam.Id != "all") cam.IsSelected = false;
            }
            UpdateCameraFilterUI();
        }

        private async void CameraApplyBtn_Click(object s, RoutedEventArgs e)
        {
            CameraFilterToggle.IsChecked = false;
            if (!IsLoaded) return;
            _currentPage = 1;
            await LoadRoiBatchAsync(_cts.Token);
            await RefreshAllAsync();
        }

        // ─────────────────────────────────────────────────────
        //  NEW FILTERS (ROI & CONFIDENCE)
        // ─────────────────────────────────────────────────────
        private void RoiHeaderCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_isPopulating) return;
            if (RoiHeaderCombo.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString() ?? "all";
                _selectedRoiId = tag;
                // The header ComboBox is created before ProcessCombo. Its initial
                // SelectionChanged event can therefore fire during InitializeComponent.
                if (ProcessCombo == null) return;

                _isPopulating = true;
                try
                {
                    foreach (ComboBoxItem pi in ProcessCombo.Items)
                    {
                        if (pi.Tag?.ToString() == tag) ProcessCombo.SelectedItem = pi;
                    }
                }
                finally { _isPopulating = false; }
                if (!IsLoaded) return;
                _currentPage = 1;
                _ = LoadEventsAsync(_cts.Token);
            }
        }

        private void ProcessCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_isPopulating) return;
            if (ProcessCombo.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString() ?? "all";
                _selectedRoiId = tag;
                if (RoiHeaderCombo != null)
                {
                    _isPopulating = true;
                    try
                    {
                        foreach (ComboBoxItem hi in RoiHeaderCombo.Items)
                        {
                            if (hi.Tag?.ToString() == tag) { RoiHeaderCombo.SelectedItem = hi; break; }
                        }
                    }
                    finally { _isPopulating = false; }
                }
                if (!IsLoaded) return;
                _currentPage = 1;
                _ = LoadEventsAsync(_cts.Token);
            }
        }

        private void ConfidenceBox_LostFocus(object s, RoutedEventArgs e) => ApplyConfidence();
        private void ConfidenceBox_KeyDown(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyConfidence();
        }
        
        private void ApplyConfidence()
        {
            if (double.TryParse(ConfidenceBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
            {
                if (val < 0) val = 0;
                if (val > 1) val = 1;
                _minConfidence = val;
                ConfidenceBox.Text = val.ToString("0.##", CultureInfo.InvariantCulture);
            }
            else
            {
                _minConfidence = 0;
                ConfidenceBox.Text = "0";
            }
            if (!IsLoaded) return;
            _currentPage = 1;
            _ = LoadEventsAsync(_cts.Token);
        }
    }
}

