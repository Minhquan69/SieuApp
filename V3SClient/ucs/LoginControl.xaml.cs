using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for LoginControl.xaml
    /// </summary>
    public partial class LoginControl : UserControl
    {
        public Action<string> OnLoginSuccess = null;

        public LoginControl()
        {
            InitializeComponent();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameBox.Text;
            string password = PasswordBox.Password;

            // Fake check login
            if (username == "admin" && password == "1234")
            {
                var configs = LoadUserSettings(username); // gi? s? tr? v? danh sách c?u hình
                OnLoginSuccess?.Invoke("OK");
                //if (configs.Any())
                //{
                //    UserSettingsComboBox.ItemsSource = configs;
                //    UserSettingsComboBox.SelectedIndex = 0;
                //    UserSettingsComboBox.Visibility = Visibility.Visible;
                //    OnLoginSuccess?.Invoke(configs[0]); // t? ch?n config d?u tiên
                //}
                //else
                //{
                //    OnLoginSuccess?.Invoke(""); // không có c?u hình
                //}
            }
            else
            {
                MessageBox.Show("Tên đăng nhập hoặc mật khẩu không chính xác.", "Lỗi đăng nhập", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private List<string> LoadUserSettings(string user)
        {
            // Replace with real logic
            if (user == "admin")
                return new List<string> { "Config 1", "Config 2" };
            return new List<string>();
        }

        private void UsernameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UsernamePlaceholder.Visibility = string.IsNullOrEmpty(UsernameBox.Text)
                ? Visibility.Visible : Visibility.Hidden;
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordBox.Password)
                ? Visibility.Visible : Visibility.Hidden;
        }
    }
}

















