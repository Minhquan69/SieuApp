using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using V3SClient.models;

namespace V3SClient.window
{
    public partial class ManualPlateProcessingWindow : Window
    {
        public string PlateNumber { get; private set; }

        public ManualPlateProcessingWindow(ProcessingJobStatusItem job)
        {
            InitializeComponent();
            if (job == null) return;

            FileNameText.Text = job.FileName;
            ErrorText.Text = string.IsNullOrWhiteSpace(job.FriendlyErrorMessage)
                ? "Không có chi tiết lỗi."
                : job.FriendlyErrorMessage;
            LoadPreview(job.SourcePath);
            PlateTextBox.Focus();
        }

        private void LoadPreview(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                PreviewImage.Source = image;
            }
            catch
            {
                PreviewImage.Source = null;
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            string plate = Regex.Replace((PlateTextBox.Text ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", string.Empty);
            if (!Regex.IsMatch(plate, @"^[0-9A-Z.-]{4,15}$"))
            {
                MessageBox.Show("Biển số không hợp lệ. Chỉ dùng chữ, số, dấu chấm hoặc dấu gạch ngang.",
                    "Kiểm tra biển số", MessageBoxButton.OK, MessageBoxImage.Warning);
                PlateTextBox.Focus();
                return;
            }
            if (ConfirmImageCheckBox.IsChecked != true)
            {
                MessageBox.Show("Bạn cần xác nhận chất lượng ảnh và biển số trước khi xử lý lại.",
                    "Xác nhận ảnh", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PlateNumber = plate;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
