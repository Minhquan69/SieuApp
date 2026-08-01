using System;
using System.Windows;
using V3SClient.libs;

namespace V3SClient.window
{
    public partial class ServerConfigWindow : Window
    {
        public ServerConfigWindow()
        {
            InitializeComponent();
            LoadCurrentConfig();
        }

        private void LoadCurrentConfig()
        {
            ApiUrlBox.Text = ApiManager.Instance.BaseUrl;
            if (ApiManager.Instance.NetworkMode == "Internal")
            {
                InternalModeRadio.IsChecked = true;
            }
            else
            {
                PublicModeRadio.IsChecked = true;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string apiUrl = ApiUrlBox.Text.Trim();
            if (string.IsNullOrEmpty(apiUrl))
            {
                MessageBox.Show("Vui lòng nhập địa chỉ API!", "Cảnh báo");
                return;
            }

            if (!apiUrl.StartsWith("http"))
            {
                apiUrl = "http://" + apiUrl;
            }

            string mode = InternalModeRadio.IsChecked == true ? "Internal" : "Public";
            ApiManager.Instance.SaveConfig(apiUrl, mode);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. Kiểm tra xem AgentUpdater có đang chạy hay không để tránh mở nhiều tiến trình
            if (System.Diagnostics.Process.GetProcessesByName("AgentUpdater").Length > 0)
            {
                MessageBox.Show("Chương trình cập nhật đang chạy!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string updaterExe = System.IO.Path.Combine(baseDir, "AgentUpdater.exe");

            if (!System.IO.File.Exists(updaterExe))
            {
                MessageBox.Show("Phần mềm đang là mới nhất!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 2. Vô hiệu hóa nút để tránh double-click nhanh tạo nhiều tiến trình trước khi process kịp start
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null)
            {
                btn.IsEnabled = false;
            }

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = updaterExe,
                    Arguments = $"--process-name V3SClient --app-dir \"{baseDir.TrimEnd('\\')}\" --app-name \"V3SClient\" --restart true",
                    WorkingDirectory = baseDir,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể khởi chạy bộ cập nhật: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                if (btn != null)
                {
                    btn.IsEnabled = true;
                }
            }
        }
    }
}

















