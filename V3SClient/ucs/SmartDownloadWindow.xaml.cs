using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using V3SClient.libs;

namespace V3SClient.ucs
{
    public partial class SmartDownloadWindow : Window
    {
        private List<string> _cameraIds;
        private DateTime _startTime;
        private DateTime _endTime;

        public SmartDownloadWindow(List<string> cameraIds, DateTime start, DateTime end)
        {
            InitializeComponent();
            _cameraIds = cameraIds;
            _startTime = start;
            _endTime = end;
            
            txtCameraCount.Text = $"{_cameraIds.Count} thiết bị được chọn";
            txtStartTime.Text = _startTime.ToString("yyyy-MM-dd HH:mm:ss");
            txtEndTime.Text = _endTime.ToString("yyyy-MM-dd HH:mm:ss");
            txtSavePath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iVista_Downloads");
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            // Use WinForms FolderBrowserDialog for better folder selection experience in WPF if available,
            // but the decompiled code used SaveFileDialog for path selection.
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                FileName = "Chọn Thư Mục",
                CheckPathExists = true,
                Title = "Chọn thư mục lưu trữ"
            };
            
            if (saveFileDialog.ShowDialog() == true)
            {
                txtSavePath.Text = Path.GetDirectoryName(saveFileDialog.FileName);
            }
        }

        private async void StartDownload_Click(object sender, RoutedEventArgs e)
        {
            string savePath = txtSavePath.Text;
            if (string.IsNullOrEmpty(savePath))
            {
                MessageBox.Show("Vui lòng chọn đường dẫn lưu trữ hợp lệ.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            if (!DateTime.TryParse(txtStartTime.Text, out var newStart) || !DateTime.TryParse(txtEndTime.Text, out var newEnd))
            {
                MessageBox.Show("Định dạng ngày/giờ không hợp lệ. Vui lòng sử dụng YYYY-MM-DD HH:MM:SS", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
                return;
            }

            if (newEnd <= newStart)
            {
                MessageBox.Show("Thời gian kết thúc phải sau thời gian bắt đầu.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
                return;
            }

            if (!Directory.Exists(savePath))
            {
                try
                {
                    Directory.CreateDirectory(savePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Không thể tạo thư mục lưu trữ: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
                    return;
                }
            }

            _startTime = newStart;
            _endTime = newEnd;
            
            btnStartDownload.IsEnabled = false;
            pnlProgress.Visibility = Visibility.Visible;
            txtStatus.Text = "Đang khởi tạo các tác vụ nền...";

            // Run on background thread to avoid blocking UI during initial discovery
            _ = Task.Run(async () =>
            {
                try
                {
                    await SmartDownloadManager.Instance.StartSmartDownloadAsync(_cameraIds, _startTime, _endTime, savePath);
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Quá trình tải xuống nền thất bại: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Hand);
                    });
                }
            });

            MessageBox.Show("Quá trình tải xuống đã bắt đầu ở chế độ nền. Bạn có thể theo dõi tiến độ trong Trình quản lý tải xuống.", "Tải thông minh", MessageBoxButton.OK, MessageBoxImage.Asterisk);
            this.DialogResult = true;
            Close();
        }
    }
}
