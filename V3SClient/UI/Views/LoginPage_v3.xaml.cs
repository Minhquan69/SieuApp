using System.Windows;
using System.Windows.Controls;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class LoginPage_v3 : Page
    {
        public LoginPage_v3()
        {
            InitializeComponent();
        }

        private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as LoginViewModel_v3;
            if (viewModel != null)
            {
                viewModel.Password = PasswordInput.Password;
            }
        }

        public void ClearPassword()
        {
            PasswordInput.Clear();
        }
    }
}
