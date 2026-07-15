using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using V3SClient.libs;

namespace V3SClient.viewModels
{
    public sealed class ShellViewModel_v3 : VMBase, IDisposable
    {
        private readonly DispatcherTimer _clock;
        private ShellNavigationItem_v3 _selectedNavigationItem;
        private string _activeRoute;

        public ShellViewModel_v3()
        {
            NavigationItems = new ObservableCollection<ShellNavigationItem_v3>
            {
                new ShellNavigationItem_v3("Dashboard", PackIconMaterialKind.ViewDashboardOutline, null, null),
                new ShellNavigationItem_v3("Live View", PackIconMaterialKind.CameraOutline, "/live", null),
                new ShellNavigationItem_v3("Events", PackIconMaterialKind.RobotOutline, null, null),
                new ShellNavigationItem_v3("Playback", PackIconMaterialKind.PlayCircleOutline, "/playback", null),
                new ShellNavigationItem_v3("Map", PackIconMaterialKind.MapOutline, "/emap", null),
                new ShellNavigationItem_v3("Devices", PackIconMaterialKind.PackageVariantClosed, null, null),
                new ShellNavigationItem_v3("Analysis", PackIconMaterialKind.ChartBar, null, null),
                new ShellNavigationItem_v3("Reports", PackIconMaterialKind.FileDocumentOutline, null, null),
                new ShellNavigationItem_v3("Configuration", PackIconMaterialKind.CogOutline, null, null),
                new ShellNavigationItem_v3("System", PackIconMaterialKind.Server, null, null)
            };
            ActiveRoute = "/live";
            SelectedNavigationItem = NavigationItems[1];
            SelectNavigationCommand = new RelayCommand(item => SelectNavigation(item as ShellNavigationItem_v3));
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (s, e) => ServerTime = DateTime.Now;
            ServerTime = DateTime.Now;
            _clock.Start();
        }

        public ObservableCollection<ShellNavigationItem_v3> NavigationItems { get; private set; }
        public RelayCommand SelectNavigationCommand { get; private set; }
        public string Username { get { return GlobalUserInfo.Instance.UserName ?? "User"; } }
        public string SelectedProfileName { get { return GlobalUserInfo.Instance.SelectedClientName ?? "No profile selected"; } }
        public DateTime ServerTime { get; private set; }
        public string Theme { get; private set; } = "dark";
        public string Language { get; private set; } = "vi";
        public string ActiveRoute { get { return _activeRoute; } private set { _activeRoute = value; OnPropertyChanged(); } }
        public ShellNavigationItem_v3 SelectedNavigationItem { get { return _selectedNavigationItem; } private set { _selectedNavigationItem = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageTitle)); } }
        public string PageTitle { get { return SelectedNavigationItem == null ? "VMS" : SelectedNavigationItem.Title; } }

        private void SelectNavigation(ShellNavigationItem_v3 item)
        {
            if (item == null || !item.HasRoute) return;
            ActiveRoute = item.Route;
            SelectedNavigationItem = item;
        }

        public void SetActiveRoute(string route)
        {
            var item = NavigationItems.FirstOrDefaultSafe(n => string.Equals(n.Route, route, StringComparison.OrdinalIgnoreCase));
            if (item == null) return;
            ActiveRoute = route;
            SelectedNavigationItem = item;
        }

        public void Dispose() { _clock.Stop(); }
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
