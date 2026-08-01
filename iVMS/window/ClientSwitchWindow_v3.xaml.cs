using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.window
{
    public partial class ClientSwitchWindow_v3 : Window
    {
        private readonly ClientSwitchViewModel_v3 _viewModel;
        public ApiManager.ClientProfile SelectedProfile { get { return _viewModel.SelectedProfile; } }

        public ClientSwitchWindow_v3(IList<ApiManager.ClientProfile> profiles)
        {
            InitializeComponent();
            _viewModel = new ClientSwitchViewModel_v3(profiles);
            _viewModel.Confirmed += ViewModel_Confirmed;
            DataContext = _viewModel;
        }

        private void ViewModel_Confirmed(object sender, EventArgs e) { DialogResult = true; }
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
        private void Minimize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
        protected override void OnClosed(EventArgs e) { _viewModel.Confirmed -= ViewModel_Confirmed; _viewModel.Dispose(); base.OnClosed(e); }
    }
}
