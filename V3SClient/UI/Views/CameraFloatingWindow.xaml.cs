using System;
using System.Windows;
using System.Windows.Input;
using V3SClient.ucs;

namespace V3SClient.UI.Views
{
    public partial class CameraFloatingWindow : Window
    {
        private ViewCamera _cameraView;
        private bool _isMinimized = false;
        private double _expandedHeight;

        public CameraFloatingWindow()
        {
            InitializeComponent();
            _expandedHeight = this.Height;
        }

        /// <summary>
        /// Hiển thị camera live trong cửa sổ nổi này.
        /// </summary>
        public void ShowCamera(models.Camera camera, Window ownerWindow)
        {
            // Đóng stream cũ nếu có
            StopCamera();

            txtCamName.Text = camera.long_Name ?? camera.name;
            _cameraView = new ViewCamera(camera);
            gridCameraView.Children.Add(_cameraView);

            // Gắn Owner để Window di chuyển theo cửa sổ chính
            try
            {
                this.Owner = ownerWindow;
            }
            catch { }

            // Đặt ở góc phải dưới của Owner
            if (ownerWindow != null)
            {
                this.Left = ownerWindow.Left + ownerWindow.ActualWidth - this.Width - 20;
                this.Top = ownerWindow.Top + ownerWindow.ActualHeight - this.Height - 40;
            }

            // Nếu đang thu nhỏ thì mở lại
            if (_isMinimized)
            {
                _isMinimized = false;
                gridCameraView.Visibility = Visibility.Visible;
                this.Height = _expandedHeight;
            }

            this.Show();
        }

        public void StopCamera()
        {
            if (_cameraView != null)
            {
                _cameraView.Dispose();
                gridCameraView.Children.Clear();
                _cameraView = null;
            }
        }

        // Kéo thanh tiêu đề để di chuyển cửa sổ
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        // Slider độ trong suốt — điều chỉnh toàn bộ Window
        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            this.Opacity = e.NewValue;
        }

        // Thu nhỏ / Mở rộng
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (_isMinimized)
            {
                // Mở rộng
                gridCameraView.Visibility = Visibility.Visible;
                this.Height = _expandedHeight;
                _isMinimized = false;
            }
            else
            {
                // Thu gọn: chỉ giữ header
                _expandedHeight = this.Height;
                gridCameraView.Visibility = Visibility.Collapsed;
                this.Height = 36;
                _isMinimized = true;
            }
        }

        // Đóng (ẩn, không destroy — để tái sử dụng)
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
            this.Hide();
        }

        // Cho phép đóng hẳn khi Page Unloaded
        private bool _allowClose = false;
        public void ForceClose()
        {
            _allowClose = true;
            StopCamera();
            this.Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                StopCamera();
                this.Hide();
            }
        }
    }
}
