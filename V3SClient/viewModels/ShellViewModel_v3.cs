using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using V3SClient.libs;

namespace V3SClient.viewModels
{
    public sealed class ShellViewModel_v3 : VMBase, IDisposable
    {
        private readonly DispatcherTimer _clock;
        private bool _isSidebarCollapsed;
        private ShellNavigationItem_v3 _selectedNavigationItem;
        public ShellViewModel_v3()
        {
            NavigationItems = new ObservableCollection<ShellNavigationItem_v3>
            {
                new ShellNavigationItem_v3("▣", "Live View", "Live monitoring"),
                new ShellNavigationItem_v3("▶", "Playback", "Recorded video"),
                new ShellNavigationItem_v3("⌖", "E-Map", "Camera map")
            };
            SelectedNavigationItem = NavigationItems[0];
            ToggleSidebarCommand = new RelayCommand(_ => IsSidebarCollapsed = !IsSidebarCollapsed);
            SelectNavigationCommand = new RelayCommand(item => SelectedNavigationItem = item as ShellNavigationItem_v3 ?? SelectedNavigationItem);
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (s, e) => ServerTime = DateTime.Now;
            ServerTime = DateTime.Now;
            _clock.Start();
        }
        public ObservableCollection<ShellNavigationItem_v3> NavigationItems { get; private set; }
        public RelayCommand ToggleSidebarCommand { get; private set; }
        public RelayCommand SelectNavigationCommand { get; private set; }
        public string Username { get { return GlobalUserInfo.Instance.UserName ?? "User"; } }
        public string SelectedProfileName { get { return GlobalUserInfo.Instance.SelectedClientName ?? "No profile selected"; } }
        public DateTime ServerTime { get; private set; }
        public bool IsSidebarCollapsed { get { return _isSidebarCollapsed; } set { _isSidebarCollapsed = value; OnPropertyChanged(); } }
        public ShellNavigationItem_v3 SelectedNavigationItem { get { return _selectedNavigationItem; } set { _selectedNavigationItem = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageTitle)); } }
        public string PageTitle { get { return SelectedNavigationItem == null ? "VMS" : SelectedNavigationItem.Title; } }
        public void Dispose() { _clock.Stop(); }
    }
    public sealed class ShellNavigationItem_v3
    {
        public ShellNavigationItem_v3(string glyph, string title, string description) { Glyph = glyph; Title = title; Description = description; }
        public string Glyph { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
    }
}
