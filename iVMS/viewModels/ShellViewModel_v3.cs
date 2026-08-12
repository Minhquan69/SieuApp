using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using V3SClient.libs;
using V3SClient.Services;

namespace V3SClient.viewModels
{
    public sealed class ShellViewModel_v3 : VMBase, IDisposable
    {
        private readonly DispatcherTimer _clock;
        private readonly HttpClient _connectivityClient;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private ShellNavigationItem_v3 _selectedNavigationItem;
        private string _activeRoute;
        private DateTime _lastConnectivityCheck = DateTime.MinValue;
        private int _checkingConnectivity;
        private DateTime _serverTime;
        private string _connectionStatusText = "Đang kiểm tra";
        private Brush _connectionStatusBrush = CreateStatusBrush(115, 138, 163);
        private string _networkStatusText = "Đang kiểm tra";
        private Brush _networkStatusBrush = CreateStatusBrush(115, 138, 163);
        private int _synchronizationDepth;
        private bool _apiSynchronizationActive;

        public ShellViewModel_v3()
        {
            NavigationItems = new ObservableCollection<ShellNavigationItem_v3>
            {
                new ShellNavigationItem_v3("Tổng quan", PackIconMaterialKind.MonitorDashboard, null, null),
                new ShellNavigationItem_v3("Trực tiếp", PackIconMaterialKind.CameraOutline, "/live", null),
                new ShellNavigationItem_v3("Sự kiện", PackIconMaterialKind.RobotOutline, null, null),
                new ShellNavigationItem_v3("Phát lại", PackIconMaterialKind.PlayCircleOutline, "/playback", null),
                new ShellNavigationItem_v3("Bản đồ", PackIconMaterialKind.MapOutline, "/emap", null),
                new ShellNavigationItem_v3("Thiết bị", PackIconMaterialKind.PackageVariantClosed, null, null),
                new ShellNavigationItem_v3("Phân tích", PackIconMaterialKind.ChartBar, null, null),
                new ShellNavigationItem_v3("Báo cáo", PackIconMaterialKind.FileDocumentOutline, null, null),
                new ShellNavigationItem_v3("Cấu hình", PackIconMaterialKind.CogOutline, null, null),
                new ShellNavigationItem_v3("Hệ thống", PackIconMaterialKind.Server, "/system", null)
            };
            // Phân tích đứng trước Thiết bị như bố cục web.
            NavigationItems.Move(6, 5);
            ActiveRoute = "/live";
            SelectedNavigationItem = NavigationItems[1];
            SelectNavigationCommand = new RelayCommand(item => SelectNavigation(item as ShellNavigationItem_v3));
            _connectivityClient = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(4)
            };
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += async (s, e) =>
            {
                ServerTime = DateTime.Now;
                if (DateTime.UtcNow - _lastConnectivityCheck >= TimeSpan.FromSeconds(15))
                    await RefreshConnectivityAsync();
            };
            ServerTime = DateTime.Now;
            _clock.Start();
            AppSynchronizationStatus_v3.SynchronizationChanged += AppSynchronizationStatusChanged;
            _ = RefreshConnectivityAsync();
        }

        public ObservableCollection<ShellNavigationItem_v3> NavigationItems { get; private set; }
        public RelayCommand SelectNavigationCommand { get; private set; }
        public string Username { get { return GlobalUserInfo.Instance.UserName ?? "User"; } }
        public string UserInitials
        {
            get
            {
                var value = Username.Trim();
                if (string.IsNullOrEmpty(value)) return "US";
                return value.Length == 1 ? value.ToUpperInvariant() : value.Substring(0, 2).ToUpperInvariant();
            }
        }
        public string SelectedProfileName { get { return GlobalUserInfo.Instance.SelectedClientName ?? "No profile selected"; } }
        public DateTime ServerTime { get { return _serverTime; } private set { _serverTime = value; OnPropertyChanged(); } }
        public string ConnectionStatusText { get { return _connectionStatusText; } private set { _connectionStatusText = value; OnPropertyChanged(); } }
        public Brush ConnectionStatusBrush { get { return _connectionStatusBrush; } private set { _connectionStatusBrush = value; OnPropertyChanged(); } }
        public string Theme { get; private set; } = "dark";
        public string Language { get; private set; } = "vi";
        public string ActiveRoute { get { return _activeRoute; } private set { _activeRoute = value; OnPropertyChanged(); } }
        public ShellNavigationItem_v3 SelectedNavigationItem { get { return _selectedNavigationItem; } private set { _selectedNavigationItem = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageTitle)); } }
        public string PageTitle { get { return SelectedNavigationItem == null ? "VMS" : SelectedNavigationItem.Title; } }
        public void RefreshSessionDisplay() { OnPropertyChanged(nameof(Username)); OnPropertyChanged(nameof(UserInitials)); OnPropertyChanged(nameof(SelectedProfileName)); }

        public void BeginSynchronization()
        {
            if (Interlocked.Increment(ref _synchronizationDepth) != 1)
                return;

            ConnectionStatusText = "Đang đồng bộ";
            ConnectionStatusBrush = CreateStatusBrush(56, 189, 248);
        }

        public void EndSynchronization()
        {
            var depth = Interlocked.Decrement(ref _synchronizationDepth);
            if (depth > 0)
                return;

            if (depth < 0)
                Interlocked.Exchange(ref _synchronizationDepth, 0);
            PublishNetworkStatus();
        }

        private void AppSynchronizationStatusChanged(object sender, EventArgs e)
        {
            var dispatcher = System.Windows.Application.Current == null ? null : System.Windows.Application.Current.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                ApplyApiSynchronizationState();
            else
                dispatcher.BeginInvoke(new Action(ApplyApiSynchronizationState));
        }

        private void ApplyApiSynchronizationState()
        {
            _apiSynchronizationActive = AppSynchronizationStatus_v3.IsSynchronizing;
            if (_apiSynchronizationActive)
            {
                ConnectionStatusText = "Đang đồng bộ";
                ConnectionStatusBrush = CreateStatusBrush(56, 189, 248);
            }
            else
            {
                PublishNetworkStatus();
            }
        }

        private async Task RefreshConnectivityAsync()
        {
            if (_lifetime.IsCancellationRequested || Interlocked.Exchange(ref _checkingConnectivity, 1) != 0)
                return;

            _lastConnectivityCheck = DateTime.UtcNow;
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable())
                {
                    SetConnectionStatus("Mất mạng", CreateStatusBrush(239, 68, 68));
                    return;
                }

                Uri endpoint;
                if (!Uri.TryCreate(ApiManager.Instance.BaseUrl, UriKind.Absolute, out endpoint))
                {
                    SetConnectionStatus("Chưa cấu hình", CreateStatusBrush(245, 158, 11));
                    return;
                }

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    using (var request = new HttpRequestMessage(HttpMethod.Head, endpoint))
                    using (var response = await _connectivityClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                    {
                        // Any HTTP response (including 401/404) confirms that
                        // the configured server is reachable from this client.
                        SetConnectionStatus("Trực tuyến", CreateStatusBrush(34, 197, 94));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!_lifetime.IsCancellationRequested)
                    SetConnectionStatus("Phản hồi chậm", CreateStatusBrush(245, 158, 11));
            }
            catch (HttpRequestException)
            {
                SetConnectionStatus("Máy chủ không phản hồi", CreateStatusBrush(239, 68, 68));
            }
            catch
            {
                SetConnectionStatus("Không xác định", CreateStatusBrush(245, 158, 11));
            }
            finally
            {
                Interlocked.Exchange(ref _checkingConnectivity, 0);
            }
        }

        private void SetConnectionStatus(string text, Brush brush)
        {
            _networkStatusText = text;
            _networkStatusBrush = brush;
            PublishNetworkStatus();
        }

        private void PublishNetworkStatus()
        {
            if (Volatile.Read(ref _synchronizationDepth) > 0 || _apiSynchronizationActive)
                return;

            ConnectionStatusText = _networkStatusText;
            ConnectionStatusBrush = _networkStatusBrush;
        }

        private static Brush CreateStatusBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private void SelectNavigation(ShellNavigationItem_v3 item)
        {
            if (item == null) return;
            var route = item.Route;
            if (string.IsNullOrWhiteSpace(route) && item.IconKind == PackIconMaterialKind.MonitorDashboard) route = "/dashboard";
            if (string.IsNullOrWhiteSpace(route) && item.IconKind == PackIconMaterialKind.RobotOutline) route = "/events";
            if (string.IsNullOrWhiteSpace(route) && item.IconKind == PackIconMaterialKind.ChartBar) route = "/analysis";
            if (string.IsNullOrWhiteSpace(route)) return;
            ActiveRoute = route;
            SelectedNavigationItem = item;
        }

        public void SetActiveRoute(string route)
        {
            var item = NavigationItems.FirstOrDefaultSafe(n => string.Equals(n.Route, route, StringComparison.OrdinalIgnoreCase));
            if (item == null) return;
            ActiveRoute = route;
            SelectedNavigationItem = item;
        }

        public void Dispose()
        {
            _clock.Stop();
            AppSynchronizationStatus_v3.SynchronizationChanged -= AppSynchronizationStatusChanged;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _connectivityClient.Dispose();
        }
    }

    public sealed class ShellNavigationItem_v3
    {
        public ShellNavigationItem_v3(string title, PackIconMaterialKind iconKind, string route, string badgeText)
        {
            Title = title; IconKind = iconKind; Route = route; BadgeText = badgeText;
        }
        public PackIconMaterialKind IconKind { get; private set; }
        public string Title { get; private set; }
        public string Route { get; private set; }
        public string BadgeText { get; private set; }
        public bool HasBadge { get { return !string.IsNullOrWhiteSpace(BadgeText); } }
        public bool HasRoute { get { return !string.IsNullOrWhiteSpace(Route); } }
    }

    internal static class ShellNavigationExtensions
    {
        public static ShellNavigationItem_v3 FirstOrDefaultSafe(this ObservableCollection<ShellNavigationItem_v3> items, Func<ShellNavigationItem_v3, bool> predicate)
        {
            foreach (var item in items) if (predicate(item)) return item;
            return null;
        }
    }
}
