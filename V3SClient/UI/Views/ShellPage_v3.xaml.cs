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

        public ShellPage_v3()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            ShellHeader.SwitchClientRequested += OnSwitchClientRequested;
            ShellHeader.LogoutRequested += OnLogoutRequested;
        }

        private async void OnSwitchClientRequested(object sender, System.EventArgs e)
        {
            var picker = new ClientSwitchWindow(GlobalUserInfo.Instance.AuthorizedProfiles) { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() != true || picker.SelectedProfile == null) return;
            try
            {
                await new ClientSessionService().SwitchClientAsync(picker.SelectedProfile, CancellationToken.None);
                _viewModel.RefreshSessionDisplay();
                NavigateToSelectedModule();
            }
            catch (System.Exception ex) { MessageBox.Show(ex.Message, "Đổi client", MessageBoxButton.OK, MessageBoxImage.Warning); }
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
                ContentFrame.Content = new LivePage_v3();
                return;
            }
            if (_viewModel.SelectedNavigationItem.Route == "/playback")
            {
                ContentFrame.Content = new PlaybackPage_v3();
                return;
            }
            if (_viewModel.SelectedNavigationItem.Route == "/emap")
            {
                ContentFrame.Content = new MapPage_v3();
                return;
            }

            ContentFrame.Content = new TextBlock
            {
                Text = _viewModel.SelectedNavigationItem.Title + " is not yet available in the _v3 shell.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("VmsBodyText_v3")
            };
        }
    }
}
