using System.ComponentModel;
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
        private UIElement _activeModule;

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
            var picker = new ClientSwitchWindow_v3(GlobalUserInfo.Instance.AuthorizedProfiles) { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() != true || picker.SelectedProfile == null) return;
            try
            {
                  await new ClientSessionService().SwitchClientAsync(picker.SelectedProfile, CancellationToken.None);
                  ResetActiveModuleAfterClientSwitch();
                  _viewModel.RefreshSessionDisplay();
                  NavigateToSelectedModule();
            }
            catch (System.Exception ex) { MessageBox.Show(ex.Message, "Đổi client", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void ResetActiveModuleAfterClientSwitch()
        {
            if (_activeModule != null)
            {
                _moduleHost.Children.Remove(_activeModule);
                _activeModule.Visibility = Visibility.Collapsed;
            }
            _activeModule = null;
            _playbackPage = null;
        }

        private void OnLogoutRequested(object sender, System.EventArgs e)
        {
            if (MessageBox.Show("Bạn có muốn đăng xuất không?", "Đăng xuất", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var shell = Window.GetWindow(this) as ShellWindow_v3;
            new ClientSessionService().ClearSession();
            if (shell != null) shell.LogoutAndReturnToLogin();
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
            NavigateToSelectedModule();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ShellViewModel_v3.SelectedNavigationItem))
                NavigateToSelectedModule();
        }

        private void NavigateToSelectedModule()
        {
            if (_viewModel == null || _viewModel.SelectedNavigationItem == null)
                return;

            if (_viewModel.SelectedNavigationItem.Route == "/live")
            {
                ShowModule(new LivePage_v3());
                return;
            }
            if (_viewModel.SelectedNavigationItem.Route == "/playback")
            {
                if (_playbackPage == null)
                    _playbackPage = new PlaybackPage_v3();
                ShowModule(_playbackPage);
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

            if (_activeModule != null)
            {
                if (ReferenceEquals(_activeModule, _playbackPage))
                    _activeModule.Visibility = Visibility.Collapsed;
                else
                    _moduleHost.Children.Remove(_activeModule);
            }

            if (!_moduleHost.Children.Contains(module))
                _moduleHost.Children.Add(module);
            module.Visibility = Visibility.Visible;
            _activeModule = module;
        }
    }
}
