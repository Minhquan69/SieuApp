using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using V3SClient.viewModels;
using V3SClient.Services;
using V3SClient.ucs;
using V3SClient.libs;
using V3SClient.window;
using System.Threading;

namespace V3SClient.UI.Views
{
    public partial class ShellPage_v3 : UserControl
    {
        private ShellViewModel_v3 _viewModel;
        private readonly Grid _moduleHost = new Grid();
        private PlaybackPage_v3 _playbackPage;
        private DashboardPage_v3 _dashboardPage;
        private EventCenterPage_v3 _eventCenterPage;
        private AnalysisPage_v3 _analysisPage;
        private UIElement _activeModule;
        private bool _deferInitialNavigation;

        public ShellPage_v3()
        {
            InitializeComponent();
            GlobalDownloadProgressPanel_v3.DataContext = SmartDownloadManager.Instance;
            GlobalDownloadProgressPopup_v3.DataContext = SmartDownloadManager.Instance;
            ContentFrame.Content = _moduleHost;
            DataContextChanged += OnDataContextChanged;
            ShellHeader.SwitchClientRequested += OnSwitchClientRequested;
            ShellHeader.LogoutRequested += OnLogoutRequested;
            ShellSidebar.LayoutChangeStarting += OnShellSidebarLayoutChangeStarting;
        }

        public bool DeferInitialNavigation
        {
            get { return _deferInitialNavigation; }
            set { _deferInitialNavigation = value; }
        }

        public void CompleteInitialNavigation()
        {
            _deferInitialNavigation = false;
            NavigateToSelectedModule();
        }

        public void ShowInitialLoadFailure(string message)
        {
            ShowModule(CreateStartupStatus(message, "Unable to load selected client"));
        }

        private void CancelGlobalDownload_Click(object sender, RoutedEventArgs e)
        {
            SmartDownloadManager.Instance.Cancel(SmartDownloadManager.Instance.ActiveDownload);
        }

        private void OnShellSidebarLayoutChangeStarting(object sender, System.EventArgs e)
        {
            var livePage = _activeModule as LivePage_v3;
            if (livePage != null)
                livePage.BeginGeometryTransition();
        }

        private async void OnSwitchClientRequested(object sender, System.EventArgs e)
        {
            ShellHeader.IsEnabled = false;
            try
            {
                // The profile cache can be stale after a user's permissions
                // change. Always refresh it before presenting the selector.
                var session = new ClientSessionService();
                var profiles = await session.LoadAuthorizedClientsAsync(CancellationToken.None);
                if (profiles == null || profiles.Count == 0)
                {
                    MessageBox.Show("Tài khoản hiện tại chưa được gán client nào.", "Đổi client", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var picker = new ClientSwitchWindow_v3(profiles) { Owner = Window.GetWindow(this) };
                if (picker.ShowDialog() != true || picker.SelectedProfile == null) return;
                try
            {
                    await session.SwitchClientAsync(picker.SelectedProfile, CancellationToken.None);
                    ResetActiveModuleAfterClientSwitch();
                    _viewModel.RefreshSessionDisplay();
                    NavigateToSelectedModule();
                }
                catch (System.Exception ex) { MessageBox.Show(ex.Message, "Đổi client", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
            catch (System.Exception ex)
            {
                LoggerManager.LogException(ex, "Không thể tải danh sách client để chuyển phiên.");
                MessageBox.Show("Không thể tải danh sách client. Vui lòng thử lại.", "Đổi client", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                ShellHeader.IsEnabled = true;
            }
        }

        private void ResetActiveModuleAfterClientSwitch()
        {
            // Client-scoped views cache their own API results. Remove every cached
            // view so the selected module is constructed again against the new client.
            _moduleHost.Children.Clear();
            _activeModule = null;
            _playbackPage = null;
            _dashboardPage = null;
            _eventCenterPage = null;
            _analysisPage = null;
        }

        private void OnLogoutRequested(object sender, System.EventArgs e)
        {
            var shell = Window.GetWindow(this) as ShellWindow_v3;
            if (!VmsConfirmDialog_v3.ConfirmLogout(shell ?? Window.GetWindow(this))) return;
            ShellHeader.IsEnabled = false;
            new ClientSessionService().ClearSession();
            if (shell != null) shell.LogoutAndReturnToLogin();
            else ShellHeader.IsEnabled = true;
        }

        public void SetChromeVisible(bool visible)
        {
            ShellSidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ShellHeader.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ShellSidebarColumn.Width = visible ? new GridLength(1, GridUnitType.Auto) : new GridLength(0);
            ShellHeaderRow.Height = visible ? new GridLength(1, GridUnitType.Auto) : new GridLength(0);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _viewModel = e.NewValue as ShellViewModel_v3;
            if (_viewModel == null)
                return;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            if (_deferInitialNavigation)
            {
                ShowModule(CreateStartupStatus("Loading selected client data...", "Starting iVista VMS"));
                return;
            }
            NavigateToSelectedModule();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ShellViewModel_v3.SelectedNavigationItem))
                NavigateToSelectedModule();
        }

        private void NavigateToSelectedModule()
        {
            if (_deferInitialNavigation || _viewModel == null || _viewModel.SelectedNavigationItem == null)
                return;

            if (_viewModel.SelectedNavigationItem.Route == "/live")
            {
                ShellHeader.Visibility = Visibility.Visible;
                ShellHeaderRow.Height = GridLength.Auto;
                Grid.SetRow(ContentFrame, 0);
                Grid.SetRowSpan(ContentFrame, 2);
                Panel.SetZIndex(ShellHeader, 10);
                var livePage = new LivePage_v3();
                livePage.OpenEventCenterRequested += (s, e) => _viewModel.SelectNavigationCommand.Execute(
                    _viewModel.NavigationItems.FirstOrDefault(item => item.Title == "Sự kiện"));
                ShowModule(livePage);
                return;
            }
            ShellHeader.Visibility = Visibility.Visible;
            ShellHeaderRow.Height = GridLength.Auto;
            Grid.SetRow(ContentFrame, 1);
            Grid.SetRowSpan(ContentFrame, 1);
            Panel.SetZIndex(ShellHeader, 0);
            if (_viewModel.SelectedNavigationItem.Route == "/playback")
            {
                if (_playbackPage == null)
                    _playbackPage = new PlaybackPage_v3();
                ShowModule(_playbackPage);
                return;
            }
            if (_viewModel.ActiveRoute == "/dashboard")
            {
                if (_dashboardPage == null)
                {
                    _dashboardPage = new DashboardPage_v3();
                    _dashboardPage.OpenFullMapRequested += (s, e) => _viewModel.SetActiveRoute("/emap");
                    _dashboardPage.OpenFullLiveRequested += (s, e) => _viewModel.SetActiveRoute("/live");
                }
                ShowModule(_dashboardPage);
                return;
            }
            if (_viewModel.ActiveRoute == "/events")
            {
                if (_eventCenterPage == null) _eventCenterPage = new EventCenterPage_v3();
                ShowModule(_eventCenterPage);
                return;
            }
            if (_viewModel.ActiveRoute == "/analysis")
            {
                if (_analysisPage == null) _analysisPage = new AnalysisPage_v3();
                ShowModule(_analysisPage);
                return;
            }
            if (_viewModel.ActiveRoute == "/system")
            {
                ShowModule(new SystemMetricsPage_v3());
                return;
            }
            if (_viewModel.SelectedNavigationItem.Route == "/emap")
            {
                ShowModule(new MapPage_v3());
                return;
            }

            ShowModule(new TextBlock
            {
                Text = _viewModel.SelectedNavigationItem.Title + " is not yet available in the _v3 shell.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("VmsBodyText_v3")
            });
        }

        private void ShowModule(UIElement module)
        {
            if (module == null || ReferenceEquals(module, _activeModule))
                return;

            var isNewModule = !_moduleHost.Children.Contains(module);

            if (_activeModule != null)
            {
                // LiveCharts keeps an internal dispatcher timer. Keep chart pages in
                // the visual host while hidden so a delayed tick never sees a
                // detached Axis (LiveCharts.Wpf Axis.AsCoreElement null crash).
                if (ReferenceEquals(_activeModule, _playbackPage) || ReferenceEquals(_activeModule, _analysisPage))
                    _activeModule.Visibility = Visibility.Collapsed;
                else
                    _moduleHost.Children.Remove(_activeModule);
            }

            if (!_moduleHost.Children.Contains(module))
                _moduleHost.Children.Add(module);
            module.Visibility = Visibility.Visible;
            _activeModule = module;

            // Only surface a syncing state for a newly created view. Cached
            // views are already initialized and switching back to them should
            // remain instant and quiet.
            if (isNewModule)
                TrackInitialSynchronization(module);
        }

        private static UIElement CreateStartupStatus(string message, string title)
        {
            var content = new StackPanel
            {
                Width = 420,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });
            content.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return content;
        }

        private void TrackInitialSynchronization(UIElement module)
        {
            if (_viewModel == null || module == null)
                return;

            _viewModel.BeginSynchronization();
            var element = module as FrameworkElement;
            if (element == null)
            {
                Dispatcher.BeginInvoke(new System.Action(() => _viewModel?.EndSynchronization()),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
                return;
            }

            RoutedEventHandler loaded = null;
            loaded = (sender, args) =>
            {
                element.Loaded -= loaded;
                Dispatcher.BeginInvoke(new System.Action(() => _viewModel?.EndSynchronization()),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            };

            if (element.IsLoaded)
            {
                Dispatcher.BeginInvoke(new System.Action(() => _viewModel?.EndSynchronization()),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            else
            {
                element.Loaded += loaded;
            }
        }
    }
}
