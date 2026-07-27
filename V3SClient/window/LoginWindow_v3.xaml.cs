using System;
using System.Windows;
using V3SClient.viewModels;

namespace V3SClient.window
{
    public partial class LoginWindow_v3 : Window
    {
        private readonly LoginViewModel_v3 _viewModel;
        public LoginWindow_v3()
        {
            InitializeComponent();
            _viewModel = new LoginViewModel_v3();
            _viewModel.AuthenticationCompleted += ViewModel_AuthenticationCompleted;
            _viewModel.LoginResetRequested += ViewModel_LoginResetRequested;
            DataContext = _viewModel;
            Closed += LoginWindow_v3_Closed;
        }
        private void ViewModel_AuthenticationCompleted(object sender, EventArgs e) { LoginPage.ClearPassword(); DialogResult = true; Close(); }
        private void ViewModel_LoginResetRequested(object sender, EventArgs e) { LoginPage.ClearPassword(); }
        private void LoginWindow_v3_Closed(object sender, EventArgs e) { _viewModel.AuthenticationCompleted -= ViewModel_AuthenticationCompleted; _viewModel.LoginResetRequested -= ViewModel_LoginResetRequested; _viewModel.Dispose(); }
    }
}
