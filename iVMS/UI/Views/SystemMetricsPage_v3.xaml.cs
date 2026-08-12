using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using V3SClient.libs;

namespace V3SClient.UI.Views
{
    public partial class SystemMetricsPage_v3 : UserControl
    {
        private const string MetricEndpointKey = "_systemMetric";
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private readonly DispatcherTimer _refreshTimer = new DispatcherTimer();
        private readonly Dictionary<string, List<double>> _history = new Dictionary<string, List<double>>();
        private readonly Dictionary<string, List<DateTime?>> _historyTimes = new Dictionary<string, List<DateTime?>>();
        private readonly Dictionary<Canvas, int> _chartHoverIndexes = new Dictionary<Canvas, int>();
        private bool _loading;
        private bool _updatingHostList;
        private string _selectedHostCpuTemperature = "—";

        public SystemMetricsPage_v3()
        {
            InitializeComponent();
            KeepOnlyRequestedSummaryKpis();
            ApplyOverviewKpiPalette();
            Loaded += async (s, e) => { ConfigureRefreshTimer(); await RefreshAsync(); };
            Unloaded += (s, e) => { _refreshTimer.Stop(); _http.Dispose(); };
            _refreshTimer.Tick += async (s, e) => await RefreshAsync();
            HostBox.SelectionChanged += async (s, e) => { if (IsLoaded && !_updatingHostList) await RefreshAsync(); };
        }

        private void KeepOnlyRequestedSummaryKpis()
        {
            RemoveSummaryKpi(OfflineText);
            RemoveSummaryKpi(CpuTemperatureText);
            RemoveSummaryKpi(GpuCountText);
            RemoveSummaryKpi(GpuMemoryText);
            RemoveSummaryKpi(GpuTemperatureText);
            MoveSummaryKpi(DiskText, SystemKpiPrimaryGrid);
            MoveSummaryKpi(GpuText, SystemKpiPrimaryGrid);
        }

        private static void MoveSummaryKpi(FrameworkElement metric, UniformGrid destination)
        {
            DependencyObject current = metric;
            while (current != null)
            {
                var card = current as Border;
                var parent = card == null ? null : VisualTreeHelper.GetParent(card) as UniformGrid;
                if (parent != null)
                {
                    parent.Children.Remove(card);
                    destination.Children.Add(card);
                    return;
                }

                current = VisualTreeHelper.GetParent(current);
            }
        }

        private void ApplyOverviewKpiPalette()
        {
            ApplySummaryKpiPalette(TotalText, "#287BFF", "#103967", "#091D35");
            ApplySummaryKpiPalette(OnlineText, "#20BB77", "#0C4A3A", "#092A2C");
            ApplySummaryKpiPalette(CpuText, "#35B5FF", "#0B3A5B", "#091D35");
            ApplySummaryKpiPalette(MemoryText, "#9B5CFF", "#2B1E58", "#151630");
            ApplySummaryKpiPalette(DiskText, "#FF9F43", "#492919", "#211A1B");
            ApplySummaryKpiPalette(GpuText, "#E06CFF", "#43205B", "#1B1635");
        }

        private static void ApplySummaryKpiPalette(FrameworkElement metric, string accent, string start, string end)
        {
            DependencyObject current = metric;
            while (current != null)
            {
                var card = current as Border;
                if (card != null && VisualTreeHelper.GetParent(card) is UniformGrid)
                {
                    card.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
                    card.Background = new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString(start),
                        (Color)ColorConverter.ConvertFromString(end),
                        new Point(0, 0), new Point(1, 1));

                    if (card.Child is Grid content)
                    {
                        var accentBar = content.Children.OfType<Border>().FirstOrDefault(item => item.Height == 3);
                        if (accentBar != null)
                            accentBar.Visibility = Visibility.Collapsed;
                    }
                    return;
                }

                current = VisualTreeHelper.GetParent(current);
            }
        }

        private static void RemoveSummaryKpi(FrameworkElement metric)
        {
            DependencyObject current = metric;
            while (current != null)
            {
                var card = current as Border;
                var parent = card == null ? null : VisualTreeHelper.GetParent(card) as UniformGrid;
                if (parent != null)
                {
                    parent.Children.Remove(card);
                    return;
                }

                current = VisualTreeHelper.GetParent(current);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
        private async void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) await RefreshAsync(); }
        private void RefreshSecondsBox_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);
        private void RefreshSecondsBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ConfigureRefreshTimer();

        private async void ResetFilters_Click(object sender, RoutedEventArgs e)
        {
            HostBox.SelectedIndex = 0;
            RangeBox.SelectedIndex = 0;
            await RefreshAsync();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_refreshTimer.IsEnabled)
            {
                _refreshTimer.Stop();
                StopButton.Content = "Bắt đầu";
                return;
            }

            ConfigureRefreshTimer();
            StopButton.Content = "Dừng";
        }

        private void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawCharts();

        private void ConfigureRefreshTimer()
        {
            _refreshTimer.Stop();
            if (!int.TryParse(RefreshSecondsBox.Text, out var seconds) || seconds < 1) seconds = 60;
            seconds = Math.Min(seconds, 3600);
            RefreshSecondsBox.Text = seconds.ToString();
            _refreshTimer.Interval = TimeSpan.FromSeconds(seconds);
            _refreshTimer.Start();
            StopButton.Content = "Dừng";
        }

        private async Task RefreshAsync()
        {
            if (_loading) return;
            _loading = true;
            try
            {
                StatusText.Text = "Đang tải dữ liệu hạ tầng…";
                await EnsureEndpointAsync();
                var instance = SelectedInstance();
                var encodedInstance = Uri.EscapeDataString(instance);
                var overviewTask = GetAsync("/overview?instance=" + encodedInstance);
                var nodesTask = GetAsync("/nodes?instance=" + encodedInstance);
                var allNodesTask = instance == "*" ? nodesTask : GetAsync("/nodes?instance=*");
                var gpusTask = GetAsync("/gpus");
                var seconds = SelectedTag(RangeBox, 1800);
                var historyStep = HistoryStep(seconds);
                var historyPointLimit = HistoryPointLimit(seconds);
                var end = DateTime.UtcNow;
                var start = end.AddSeconds(-seconds);
                var historyTasks = new Dictionary<string, Task<JObject>>
                {
                    ["cpu"] = GetSeriesAsync("cpu", instance, start, end, historyStep), ["memory"] = GetSeriesAsync("memory", instance, start, end, historyStep),
                    ["cpu_temperature"] = GetSeriesAsync("cpu_temperature", instance, start, end, historyStep), ["gpu_utilization"] = GetSeriesAsync("gpu_utilization", instance, start, end, historyStep),
                    ["gpu_memory"] = GetSeriesAsync("gpu_memory", instance, start, end, historyStep), ["disk_read"] = GetSeriesAsync("disk_read", instance, start, end, historyStep), ["disk_write"] = GetSeriesAsync("disk_write", instance, start, end, historyStep)
                };
                await Task.WhenAll(historyTasks.Values.Append(overviewTask).Append(nodesTask).Append(allNodesTask).Append(gpusTask));
                var overview = await overviewTask;
                RenderOverview(overview);
                RenderNodes(await nodesTask);
                PopulateHosts(await allNodesTask);
                RenderGpus(await gpusTask);
                foreach (var item in historyTasks)
                {
                    var series = await item.Value;
                    _history[item.Key] = LimitPoints(Points(series), historyPointLimit);
                    _historyTimes[item.Key] = LimitPoints(PointTimes(series), historyPointLimit);
                }
                var network = overview["network"] as JObject;
                var networkDevice = Text(network?["device"], string.Empty);
                NetworkDetailText.Text = string.IsNullOrWhiteSpace(networkDevice) ? "Chưa xác định NIC" : networkDevice + " · NIC vật lý";
                if (!string.IsNullOrWhiteSpace(networkDevice))
                {
                    var receiveTask = GetSeriesAsync("network_receive", instance, start, end, historyStep, networkDevice);
                    var transmitTask = GetSeriesAsync("network_transmit", instance, start, end, historyStep, networkDevice);
                    await Task.WhenAll(receiveTask, transmitTask);
                    _history["network_receive"] = LimitPoints(Points(await receiveTask), historyPointLimit);
                    _historyTimes["network_receive"] = LimitPoints(PointTimes(await receiveTask), historyPointLimit);
                    _history["network_transmit"] = LimitPoints(Points(await transmitTask), historyPointLimit);
                    _historyTimes["network_transmit"] = LimitPoints(PointTimes(await transmitTask), historyPointLimit);
                }
                else
                {
                    _history["network_receive"] = new List<double>();
                    _historyTimes["network_receive"] = new List<DateTime?>();
                    _history["network_transmit"] = new List<double>();
                    _historyTimes["network_transmit"] = new List<DateTime?>();
                }
                DrawCharts();
                UpdatedText.Text = "Cập nhật: " + DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy");
                StatusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Tải System Metrics thất bại");
                StatusText.Text = "Không thể tải dữ liệu: " + ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
            finally { _loading = false; }
        }

        private async Task EnsureEndpointAsync()
        {
            if (string.IsNullOrWhiteSpace(ApiManager.Instance.GetEndpointUrl(MetricEndpointKey))) await ApiManager.Instance.DiscoverEndpointsAsync();
            if (string.IsNullOrWhiteSpace(ApiManager.Instance.GetEndpointUrl(MetricEndpointKey))) throw new InvalidOperationException("Portal chưa cấu hình endpoint _systemMetric.");
        }

        private async Task<JObject> GetSeriesAsync(string metric, string instance, DateTime start, DateTime end, int step, string device = null)
        {
            var query = string.Format("/timeseries?metric={0}&instance={1}&start={2:O}&end={3:O}&step={4}", Uri.EscapeDataString(metric), Uri.EscapeDataString(instance), start, end, step);
            if (!string.IsNullOrWhiteSpace(device)) query += "&device=" + Uri.EscapeDataString(device);
            return await GetAsync(query);
        }

        private async Task<JObject> GetAsync(string path)
        {
            var root = ApiManager.Instance.GetEndpointUrl(MetricEndpointKey)?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Portal chưa cấu hình endpoint _systemMetric.");
            if (!root.EndsWith("/api/metrics", StringComparison.OrdinalIgnoreCase)) root += "/api/metrics";
            var token = ApiManager.Instance.GetEndpointToken(MetricEndpointKey);
            var response = await _http.SendAsync(CreateMetricsRequest(root + path, token));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + body);
            return JObject.Parse(body);
        }

        private static HttpRequestMessage CreateMetricsRequest(string url, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Replace("Bearer ", "").Trim());
            return request;
        }

        private void RenderOverview(JObject data)
        {
            var nodes = data["nodes"] as JObject ?? new JObject(); var host = data["host"] as JObject ?? data["resources"] as JObject ?? new JObject(); var gpu = data["gpu"] as JObject ?? new JObject();
            TotalText.Text = Text(nodes["total"]);
            OnlineText.Text = Text(nodes["online"]);
            OfflineText.Text = Text(nodes["offline"]);
            CpuText.Text = Percent(host["cpu_percent"]);
            CpuDetail.Text = Text(host["cpu_cores"]) + " lõi";
            CpuTemperatureText.Text = Temperature(host["cpu_temperature_c"]);
            _selectedHostCpuTemperature = CpuTemperatureText.Text;
            MemoryText.Text = Percent(host["memory_percent"]);
            MemoryDetail.Text = Bytes(host["memory_bytes"]);
            DiskText.Text = Percent(host["disk_percent"]);
            GpuCountText.Text = Text(gpu["count"]);
            GpuCountDetail.Text = gpu.Value<bool?>("exporter_up") == true ? "Telemetry sẵn sàng" : "Telemetry chưa sẵn sàng";
            GpuText.Text = Percent(gpu["utilization_percent"]);
            GpuUtilDetail.Text = Mib(gpu["memory_used_mib"]);
            GpuMemoryText.Text = Percent(gpu["memory_percent"]);
            GpuMemoryDetail.Text = Mib(gpu["memory_used_mib"]) + " đã dùng";
            GpuTemperatureText.Text = Temperature(gpu["temperature_c"]);
            GpuPowerText.Text = Number(gpu["power_watts"]).HasValue ? Number(gpu["power_watts"]).Value.ToString("0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " W" : "—";
            SetBar(CpuBar, CpuBarText, host["cpu_percent"]); SetBar(MemoryBar, MemoryBarText, host["memory_percent"]); SetBar(DiskBar, DiskBarText, host["disk_percent"]); SetBar(GpuBar, GpuBarText, gpu["utilization_percent"]); SetBar(GpuMemoryBar, GpuMemoryBarText, gpu["memory_percent"]);
        }

        private void RenderNodes(JObject data)
        {
            var nodes = (data["nodes"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            NodesGrid.ItemsSource = nodes
                .Select(x =>
                {
                    var isOnline = x.Value<bool?>("up") == true;
                    var nodeTemperature = Temperature(x["cpu_temperature_c"]);
                    return new NodeRow
                    {
                        Host = Text(x["display_name"] ?? x["instance"]),
                        Instance = Text(x["instance"], string.Empty),
                        Status = isOnline ? "Trực tuyến" : "Ngoại tuyến",
                        StatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isOnline ? "#20D37B" : "#FF4D5E")),
                        CpuTemperature = nodeTemperature == "—" && nodes.Count == 1 ? _selectedHostCpuTemperature : nodeTemperature
                    };
                }).ToList();
        }

        private void RenderGpus(JObject data)
        {
            var gpus = (data["gpus"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            GpusList.ItemsSource = gpus.Select((x, i) => new GpuRow { Device = Text(x["model"] ?? x["name"] ?? x["gpu"], "GPU " + i), Metrics = "Sử dụng " + Percent(x["utilization_percent"]) + " · VRAM " + Percent(x["memory_percent"]) + " · " + Temperature(x["temperature_c"]) }).ToList();

            var gpu = gpus.FirstOrDefault();
            GpuPanelCountText.Text = gpus.Count.ToString();
            GpuPanelUtilizationText.Text = gpu == null ? "—" : Percent(gpu["utilization_percent"]);
            GpuPanelMemoryText.Text = gpu == null ? "—" : Percent(gpu["memory_percent"]);
            GpuPanelTemperatureText.Text = gpu == null ? "—" : Temperature(gpu["temperature_c"]);
        }
        private void PopulateHosts(JObject data)
        {
            var selectedInstance = SelectedInstance();
            var nodes = (data["nodes"] as JArray ?? new JArray()).OfType<JObject>()
                .Select(x => new { Instance = Text(x["instance"], string.Empty), Name = Text(x["display_name"] ?? x["instance"], string.Empty) })
                .Where(x => !string.IsNullOrWhiteSpace(x.Instance))
                .GroupBy(x => x.Instance, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First()).ToList();

            _updatingHostList = true;
            try
            {
                HostBox.Items.Clear();
                HostBox.Items.Add(new ComboBoxItem { Tag = "*", Content = "Tất cả máy chủ" });
                foreach (var node in nodes) HostBox.Items.Add(new ComboBoxItem { Tag = node.Instance, Content = node.Name });
                var index = HostBox.Items.OfType<ComboBoxItem>().ToList().FindIndex(x => string.Equals(x.Tag?.ToString(), selectedInstance, StringComparison.OrdinalIgnoreCase));
                HostBox.SelectedIndex = Math.Max(0, index);
            }
            finally { _updatingHostList = false; }
        }

        private string SelectedInstance() => (HostBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "*";
        private static List<double> Points(JObject data) => ((data["series"] as JArray)?.FirstOrDefault()?["points"] as JArray ?? new JArray()).OfType<JObject>().Select(x => Number(x["value"]) ?? 0).ToList();
        private static List<DateTime?> PointTimes(JObject data) => ((data["series"] as JArray)?.FirstOrDefault()?["points"] as JArray ?? new JArray()).OfType<JObject>().Select(x => PointTime(x["timestamp"] ?? x["time"] ?? x["ts"])).ToList();
        private static List<T> LimitPoints<T>(List<T> points, int maximum)
        {
            if (points.Count <= maximum) return points;
            return Enumerable.Range(0, maximum).Select(i => points[(int)Math.Round(i * (points.Count - 1d) / (maximum - 1))]).ToList();
        }
        private static int HistoryPointLimit(int seconds) => seconds >= 86400 ? 24 : seconds >= 21600 ? 12 : seconds >= 3600 ? 12 : 15;
        private static int HistoryStep(int seconds) => seconds >= 86400 ? 3600 : seconds >= 21600 ? 1800 : seconds >= 3600 ? 300 : 120;
        private static DateTime? PointTime(JToken token)
        {
            if (token == null) return null;
            long epoch;
            if (long.TryParse(token.ToString(), out epoch)) return (epoch > 100000000000L ? DateTimeOffset.FromUnixTimeMilliseconds(epoch) : DateTimeOffset.FromUnixTimeSeconds(epoch)).LocalDateTime;
            DateTime value;
            return DateTime.TryParse(token.ToString(), null, System.Globalization.DateTimeStyles.AssumeLocal, out value) ? value : (DateTime?)null;
        }
        private static void SetBar(ProgressBar bar, TextBlock label, JToken value) { var number = Math.Max(0, Math.Min(100, Number(value) ?? 0)); bar.Value = number; label.Text = Percent(value); }
        private static double? Number(JToken token) { double value; return double.TryParse(token?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value) ? value : (double?)null; }
        private static string Text(JToken token, string fallback = "—") => string.IsNullOrWhiteSpace(token?.ToString()) ? fallback : token.ToString();
        private static string Percent(JToken token) { var value = Number(token); return value.HasValue ? value.Value.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + "%" : "—"; }
        private static string Temperature(JToken token) { var value = Number(token); return value.HasValue ? value.Value.ToString("0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " °C" : "—"; }
        private static string Bytes(JToken token) { var value = Number(token); return value.HasValue ? (value.Value / 1024d / 1024d / 1024d).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " GB" : "—"; }
        private static string Mib(JToken token) { var value = Number(token); return value.HasValue ? (value.Value / 1024d).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " GiB" : "—"; }
        private static int SelectedTag(ComboBox box, int fallback) { int value; return int.TryParse((box.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out value) ? value : fallback; }

        private void Chart_MouseMove(object sender, MouseEventArgs e)
        {
            var canvas = sender as Canvas;
            if (canvas == null || canvas.ActualWidth <= 0) return;
            var keys = ChartKeys(canvas);
            var count = keys.Select(k => _history.ContainsKey(k) ? _history[k].Count : 0).DefaultIfEmpty(0).Max();
            if (count == 0) return;
            var left = 42d; var right = 8d; var plotWidth = Math.Max(1, canvas.ActualWidth - left - right);
            var index = Math.Max(0, Math.Min(count - 1, (int)Math.Round((e.GetPosition(canvas).X - left) / plotWidth * Math.Max(0, count - 1))));
            int prior;
            if (_chartHoverIndexes.TryGetValue(canvas, out prior) && prior == index) return;
            _chartHoverIndexes[canvas] = index;
            DrawChartForCanvas(canvas, index);
        }
        private void Chart_MouseLeave(object sender, MouseEventArgs e) { var canvas = sender as Canvas; if (canvas == null) return; _chartHoverIndexes.Remove(canvas); DrawChartForCanvas(canvas, null); }
        private string[] ChartKeys(Canvas canvas)
        {
            if (canvas == CpuMemoryChart) return new[] { "cpu", "memory" };
            if (canvas == CpuTemperatureChart) return new[] { "cpu_temperature" };
            if (canvas == GpuChart) return new[] { "gpu_utilization", "gpu_memory" };
            if (canvas == NetworkChart) return new[] { "network_receive", "network_transmit" };
            return new[] { "disk_read", "disk_write" };
        }
        private static string ChartLabel(string key) => key == "cpu" ? "CPU" : key == "memory" ? "Bộ nhớ" : key == "cpu_temperature" ? "Nhiệt CPU" : key == "gpu_utilization" ? "GPU sử dụng" : key == "gpu_memory" ? "Bộ nhớ GPU" : key == "disk_read" ? "Đọc" : key == "disk_write" ? "Ghi" : key == "network_receive" ? "RX" : "TX";
        private static string ChartValue(string key, double value)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
            if (key == "disk_read" || key == "disk_write") return (value / 1024d / 1024d).ToString("0.00", culture) + " MB/s";
            if (key == "network_receive" || key == "network_transmit") return NetworkRate(value, culture);
            if (key == "cpu_temperature") return value.ToString("0.0", culture) + " °C";
            return value.ToString("0.0", culture) + "%";
        }
        private void DrawCharts() { _chartHoverIndexes.Clear(); DrawChartForCanvas(CpuMemoryChart, null); DrawChartForCanvas(CpuTemperatureChart, null); DrawChartForCanvas(GpuChart, null); DrawChartForCanvas(DiskChart, null); DrawChartForCanvas(NetworkChart, null); }
        private void DrawChartForCanvas(Canvas canvas, int? hoverIndex)
        {
            if (canvas == CpuMemoryChart) DrawChart(canvas, new[] { "cpu", "memory" }, new[] { "#5B9CFF", "#31D07F" }, 100, hoverIndex);
            else if (canvas == CpuTemperatureChart) DrawChart(canvas, new[] { "cpu_temperature" }, new[] { "#F6BD54" }, null, hoverIndex);
            else if (canvas == GpuChart) DrawChart(canvas, new[] { "gpu_utilization", "gpu_memory" }, new[] { "#9B7BFF", "#39C6D8" }, 100, hoverIndex);
            else if (canvas == DiskChart) DrawChart(canvas, new[] { "disk_read", "disk_write" }, new[] { "#5B9CFF", "#F6BD54" }, null, hoverIndex);
            else if (canvas == NetworkChart) DrawChart(canvas, new[] { "network_receive", "network_transmit" }, new[] { "#20D37B", "#FF7085" }, null, hoverIndex);
        }
        private void DrawChart(Canvas canvas, string[] keys, string[] colors, double? fixedMax, int? hoverIndex)
        {
            canvas.Children.Clear(); var width = canvas.ActualWidth; var height = canvas.ActualHeight; if (width < 80 || height < 50) return;
            var values = keys.SelectMany(k => _history.ContainsKey(k) ? _history[k] : new List<double>()).ToList(); var max = fixedMax ?? Math.Max(1, values.DefaultIfEmpty(0).Max() * 1.08);
            const double left = 42, right = 8, top = 8, bottom = 24; var plotWidth = width - left - right; var plotHeight = height - top - bottom;
            for (var i = 0; i < 5; i++)
            {
                var y = top + i * plotHeight / 4;
                canvas.Children.Add(new Line { X1 = left, X2 = width - right, Y1 = y, Y2 = y, Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B3146")), StrokeThickness = .7, StrokeDashArray = new DoubleCollection { 2, 2 } });
                var axis = new TextBlock { Text = ChartAxisValue(keys[0], max * (1 - i / 4d)), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9DB8D0")), FontSize = 9, Width = left - 4, TextAlignment = TextAlignment.Right };
                Canvas.SetTop(axis, y - 6); Canvas.SetLeft(axis, 0); canvas.Children.Add(axis);
            }
            var pointCount = keys.Select(k => _history.ContainsKey(k) ? _history[k].Count : 0).DefaultIfEmpty(0).Max();
            var ticks = Math.Min(7, pointCount);
            for (var i = 0; i < ticks; i++)
            {
                var index = ticks == 1 ? 0 : (int)Math.Round(i * (pointCount - 1d) / (ticks - 1));
                var label = ChartTime(keys, index);
                if (string.IsNullOrWhiteSpace(label)) continue;
                var x = left + index * plotWidth / Math.Max(1, pointCount - 1);
                var time = new TextBlock { Text = label, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9DB8D0")), FontSize = 9, Width = 42, TextAlignment = TextAlignment.Center };
                Canvas.SetLeft(time, x - 21); Canvas.SetTop(time, height - bottom + 5); canvas.Children.Add(time);
            }
            for (var seriesIndex = 0; seriesIndex < keys.Length; seriesIndex++)
            {
                List<double> points; if (!_history.TryGetValue(keys[seriesIndex], out points) || points.Count == 0) continue;
                var line = new Polyline { Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[seriesIndex])), StrokeThickness = 2 };
                for (var i = 0; i < points.Count; i++) line.Points.Add(new Point(left + i * plotWidth / Math.Max(1, points.Count - 1), top + plotHeight * (1 - Math.Max(0, Math.Min(max, points[i])) / max)));
                canvas.Children.Add(line);
            }
            if (hoverIndex.HasValue) DrawChartHover(canvas, keys, colors, max, hoverIndex.Value, left, top, plotWidth, plotHeight);
        }
        private void DrawChartHover(Canvas canvas, string[] keys, string[] colors, double max, int index, double left, double top, double plotWidth, double plotHeight)
        {
            var count = keys.Select(k => _history.ContainsKey(k) ? _history[k].Count : 0).DefaultIfEmpty(0).Max();
            if (count == 0) return;
            var x = left + index * plotWidth / Math.Max(1, count - 1);
            canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + plotHeight, Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7B97B2")), StrokeThickness = 1 });
            var content = new StackPanel(); var time = ChartTime(keys, index); if (!string.IsNullOrWhiteSpace(time)) content.Children.Add(new TextBlock { Text = time, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 10, Margin = new Thickness(0, 0, 0, 5) });
            for (var i = 0; i < keys.Length; i++)
            {
                List<double> values; if (!_history.TryGetValue(keys[i], out values) || index >= values.Count) continue;
                var y = top + plotHeight * (1 - Math.Max(0, Math.Min(max, values[index])) / max);
                var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])), Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B1D2D")), StrokeThickness = 2 };
                Canvas.SetLeft(dot, x - 4); Canvas.SetTop(dot, y - 4); canvas.Children.Add(dot);
                var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])), VerticalAlignment = VerticalAlignment.Center });
                var name = new TextBlock { Text = ChartLabel(keys[i]), Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D7E6F4")), FontSize = 10, Margin = new Thickness(4, 0, 14, 0) }; Grid.SetColumn(name, 1); row.Children.Add(name);
                var value = new TextBlock { Text = ChartValue(keys[i], values[index]), Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold }; Grid.SetColumn(value, 2); row.Children.Add(value); content.Children.Add(row);
            }
            var tooltip = new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B1D2D")), BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C3850")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 7, 9, 7), Child = content };
            Canvas.SetLeft(tooltip, Math.Max(left, Math.Min(canvas.ActualWidth - 150, x + 10))); Canvas.SetTop(tooltip, Math.Max(top, Math.Min(canvas.ActualHeight - 70, top + 18))); canvas.Children.Add(tooltip);
        }
        private string ChartTime(string[] keys, int index)
        {
            foreach (var key in keys) { List<DateTime?> times; if (_historyTimes.TryGetValue(key, out times) && index < times.Count && times[index].HasValue) return times[index].Value.ToString("HH:mm"); }
            return string.Empty;
        }
        private static string ChartAxisValue(string key, double value)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
            if (key == "disk_read" || key == "disk_write") return (value / 1024d / 1024d).ToString("0.00", culture) + " MB/s";
            if (key == "network_receive" || key == "network_transmit") return NetworkRate(value, culture);
            if (key == "cpu_temperature") return value.ToString("0", culture) + " °C";
            return value.ToString("0", culture) + "%";
        }
        private static string NetworkRate(double value, System.Globalization.CultureInfo culture)
        {
            return Math.Abs(value) >= 1000d
                ? (value / 1000d).ToString("0.0", culture) + " Gbps"
                : value.ToString(Math.Abs(value) >= 100d ? "0" : "0.0", culture) + " Mbps";
        }
        private sealed class NodeRow { public string Host { get; set; } public string Instance { get; set; } public string Status { get; set; } public Brush StatusBrush { get; set; } public string CpuTemperature { get; set; } }
        private sealed class GpuRow { public string Device { get; set; } public string Metrics { get; set; } }
    }
}
