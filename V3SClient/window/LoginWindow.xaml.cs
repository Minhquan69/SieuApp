
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition.Primitives;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using V3SClient.libs;
using V3SClient.ucs;

namespace V3SClient.window
{
    /// <summary>
    /// Interaction logic for loginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {

        private string configFile = "config.txt";

        private bool _passwordVisible = false;

        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            LoggerManager.LogDebug("Bắt đầu quá trình đăng nhập");
            try
            {
                string username = UsernameBox.Text.Trim();
                string password = PasswordBox.Password;
                if (RememberMeCheckBox.IsChecked == true)
                {
                    SaveLoginInfo(username, password);
                }
                else
                {
                    DeleteLoginInfo();
                }
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    var result = await ApiManager.Instance.LoginAsync(username, password);

                    if (result.Success)
                    {
                        LoggerManager.LogInfo($"Đăng nhập thành công: {username}");
                        GlobalUserInfo.Instance.UserId = result.UserId;
                        GlobalUserInfo.Instance.UserName = username;
                        GlobalUserInfo.Instance.SetLoginTime();

                        // Fetch detailed user info (roles, permissions, tenant)
                        var userMe = await ApiManager.Instance.GetMeAsync(cts.Token);
                        if (userMe != null)
                        {
                            GlobalUserInfo.Instance.UserPermissions = userMe.Permissions;
                            GlobalUserInfo.Instance.UserRoles = userMe.Roles;
                            GlobalUserInfo.Instance.TenantId = userMe.TenantId;
                            GlobalUserInfo.Instance.IsSuperAdmin = userMe.IsSuperAdmin;
                            LoggerManager.LogInfo($"Đã tải phân quyền cho user: {username} (SuperAdmin: {userMe.IsSuperAdmin}, Permissions: {userMe.Permissions.Count})");
                        }
                        else
                        {
                            LoggerManager.LogWarn($"Không thể tải thông tin phân quyền cho user: {username}");
                        }

                        await LoadClientInfo(GlobalUserInfo.Instance.UserId);
                    }
                    else
                    {
                        LoggerManager.LogWarn($"Đăng nhập thất bại: {username} - Lý do: {result.Message}");
                        MessageBox.Show($"Đăng nhập không thành công: {result.Message}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                        LoginButton.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi xảy ra trong LoginButton_Click");
                MessageBox.Show("Không thể kết nối tới máy chủ. Vui lòng kiểm tra lại đường truyền internet hoặc địa chỉ máy chủ.", "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
                LoginButton.IsEnabled = true;
            }
        }

        private async Task LoadClientInfo(string userId)
        {
            LoggerManager.LogDebug($"Đang tải thông tin profile cho User: {userId}");
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    var profiles = await ApiManager.Instance.GetWebProfilesAsync(cts.Token);
                    if (profiles == null || profiles.Count == 0)
                        profiles = await ApiManager.Instance.GetMyAuthorizedProfilesAsync(cts.Token);
                    GlobalUserInfo.Instance.AuthorizedProfiles = profiles;

                    if (profiles == null || profiles.Count == 0)
                    {
                        LoggerManager.LogWarn($"Không tìm thấy profile cho User: {userId}");
                        MessageBox.Show("Không tìm thấy cấu hình Client cho người dùng này.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoginButton.IsEnabled = true;
                        return;
                    }

                    this.Width = 360;
                    this.Height = 290; // Tăng chiều cao để hiển thị ComboBox và nút Xác nhận
             
                    var clientDisplayList = profiles.Select(p => new ClientDisplayItem
                    {
                        ClientId = p.Id,
                        ClientName = p.Name,
                        DisplayText = p.Name
                    })
                    .OrderBy(x => x.ClientName)
                    .ToList();

                    ClientComboBox.ItemsSource = clientDisplayList;
                    ClientComboBox.SelectedIndex = 0;

                    LoginButton.Visibility = Visibility.Collapsed;
                    ConfirmButton.Visibility = Visibility.Visible;
                    ClientPanel.Visibility = Visibility.Visible;
                    RememberMeCheckBox.Visibility = Visibility.Collapsed;
                    LoggerManager.LogInfo($"Đã nạp {profiles.Count} profiles.");
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi nạp danh sách profile");
                MessageBox.Show("Có lỗi xảy ra khi tải thông tin cấu hình. Vui lòng thử lại sau.", "Lỗi ứng dụng", MessageBoxButton.OK, MessageBoxImage.Error);
                LoginButton.IsEnabled = true;
            }
        }

        private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (ClientComboBox.SelectedItem is ClientDisplayItem selectedItem)
            {
                ConfirmButton.IsEnabled = false;
                LoggerManager.LogDebug($"Xác nhận chọn profile: {selectedItem.ClientName}");
                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                    {
                        var camList = await ApiManager.Instance.GetCamInfoAsync(cts.Token, selectedItem.ClientId.ToString());
                        
                        if (camList == null || camList.Count == 0)
                        {
                            LoggerManager.LogWarn($"Profile {selectedItem.ClientName} không có camera.");
                            MessageBox.Show("Tài khoản đăng nhập không có cấu hình thiết bị cho profile này.\nVui lòng kiểm tra lại hoặc liên hệ người quản trị!", "Cảnh báo");
                            return;
                        }

                        // Update global state
                        GlobalUserInfo.Instance.ActiveClientId = selectedItem.ClientId;
                        GlobalUserInfo.Instance.SelectedClientName = selectedItem.ClientName;
                        GlobalUserInfo.Instance.GroupClients.Clear();
                        GlobalUserInfo.Instance.GroupClients[selectedItem.ClientId] = camList;

                        var lsCamInfoActive = camList;

                        var commanderIDs = lsCamInfoActive
                            .Where(c => c.Device_Role !=null && c.Device_Role != "client_device" && c.CamInfo_Type=="body_cam").ToList();
                        
                        if (commanderIDs.Count > 0)
                        {
                            GlobalUserInfo.Instance.Commanders = new ObservableCollection<CamInfo>(commanderIDs);
                            GlobalUserInfo.Instance.ActiveCommanderID = commanderIDs.First().CamInfo_CamId;
                        }

             
                        // Load intermediate server (Redis) info from discovery
                        //try
                        //{
                        //    string redisUrl = ApiManager.Instance.RedisUrl;
                        //    if (!string.IsNullOrEmpty(redisUrl))
                        //    {
                        //        // Parse IP and Port from endpoint (e.g., localhost:6379 or redis://1.2.3.4:6379)
                        //        string cleanEndpoint = redisUrl.Replace("redis://", "");
                        //        if (cleanEndpoint.Contains(":"))
                        //        {
                        //            var parts = cleanEndpoint.Split(':');
                        //            GlobalUserInfo.Instance.Redis_Server_IP = parts[0];
                        //            if (int.TryParse(parts[1], out int port))
                        //                GlobalUserInfo.Instance.Redis_Server_Port = port;
                        //        }
                        //        else
                        //        {
                        //            GlobalUserInfo.Instance.Redis_Server_IP = cleanEndpoint;
                        //        }
                                
                        //        // Token-based auth if available from endpoint
                        //        string redisToken = ApiManager.Instance.GetEndpointToken("Redis");
                        //        if (!string.IsNullOrEmpty(redisToken))
                        //        {
                        //            GlobalUserInfo.Instance.Redis_Password = redisToken;
                        //        }
                        //    }
                        //}
                        //catch (Exception ex)
                        //{
                        //    LoggerManager.LogError("Lỗi cấu hình Redis từ discovery", ex);
                        //}

                        GlobalUserInfo.Instance.BuildTreeViewWithOrganization();
                        LoggerManager.LogInfo($"Khởi động thành công Profile: {selectedItem.ClientName} ({camList.Count} thiết bị)");
                        ToastManager.ShowToast("Thông báo", $"Khởi động client: {selectedItem.ClientName}\nSố cam view: ({camList.Count - commanderIDs.Count})", ToastType.Info);

                        this.DialogResult = true;
                        this.Close();
                    }
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, $"Lỗi khi nạp dữ liệu camera cho profile {selectedItem.ClientName}");
                    MessageBox.Show("Có lỗi xảy ra khi nạp danh sách thiết bị. Vui lòng kiểm tra lại kết nối.", "Lỗi tải dữ liệu", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ConfirmButton.IsEnabled = true;
                }
            }
            else
            {
                MessageBox.Show("Vui lòng chọn một profile!", "Cảnh báo");
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            LoggerManager.LogDebug("Mở cửa sổ cấu hình máy chủ");
            try
            {
                var configWindow = new ServerConfigWindow();
                configWindow.Owner = this;
                if (configWindow.ShowDialog() == true)
                {
                    LoggerManager.LogInfo("Cấu hình máy chủ đã được cập nhật.");
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi mở cửa sổ ServerConfigWindow");
                MessageBox.Show("Không thể mở cửa sổ cấu hình. Vui lòng kiểm tra lại hệ thống.", "Lỗi ứng dụng", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        private void UsernameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UsernamePlaceholder.Visibility = string.IsNullOrEmpty(UsernameBox.Text)
                ? Visibility.Visible : Visibility.Hidden;
        }
        private void PasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_passwordVisible)
            {
                PasswordBox.Password = PasswordTextBox.Text;
            }

            PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordTextBox.Text)
                ? Visibility.Visible : Visibility.Hidden;
        }
        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_passwordVisible)
            {
                PasswordTextBox.Text = PasswordBox.Password;
            }
            PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordBox.Password)
                ? Visibility.Visible : Visibility.Hidden;
        }
        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            _passwordVisible = !_passwordVisible;

            if (_passwordVisible)
            {
                PasswordTextBox.Text = PasswordBox.Password;
                PasswordTextBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
                TogglePasswordButton.Content = "🔒";
            }
            else
            {
                PasswordBox.Password = PasswordTextBox.Text;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordTextBox.Visibility = Visibility.Collapsed;
                TogglePasswordButton.Content = "👁";
            }
        }
        private void LoadLoginInfo()
        {
            try
            {
                if (!File.Exists("login.tmp")) return;

                string content = File.ReadAllText("login.tmp");
                var parts = content.Split('|');
                if (parts.Length == 2)
                {
                    string username = DecodeBase64(parts[0]);
                    string password = DecodeBase64(parts[1]);

                    UsernameBox.Text = username;
                    PasswordBox.Password = password;
                    RememberMeCheckBox.IsChecked = true;
                }
            }
            catch { }
        }
        private void SaveLoginInfo(string username, string password)
        {
            try
            {
                string content = $"{EncodeBase64(username)}|{EncodeBase64(password)}";
                File.WriteAllText("login.tmp", content);
            }
            catch { }
        }
        private void DeleteLoginInfo()
        {
            try
            {
                if (File.Exists("login.tmp"))
                    File.Delete("login.tmp");
            }
            catch { }
        }
        private string EncodeBase64(string plainText)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(bytes);
        }

        private string DecodeBase64(string base64Text)
        {
            byte[] bytes = Convert.FromBase64String(base64Text);
            return Encoding.UTF8.GetString(bytes);
        }

        private void BackgroundVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            ((MediaElement)sender).Position = TimeSpan.Zero;
            ((MediaElement)sender).Play();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLoginInfo();
           // LoadConfig();
        }
    }
    public class ClientDisplayItem
    {
        public string DisplayText { get; set; }  
        public string ClientName { get; set; }   
        public Guid ClientId { get; set; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(DisplayText) ? ClientName ?? string.Empty : DisplayText;
        }
    }
}



















