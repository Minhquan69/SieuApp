using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace V3SClient.ucs
{
    public partial class ucImageViewer : UserControl
    {
        private Point _origin;
        private Point _start;
        private bool _isHandMode = false;

        public ucImageViewer()
        {
            InitializeComponent();
        }

        public void LoadImage(string filePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                    bitmap.EndInit();
                    imgPreview.Source = bitmap;
                    ResetToActualSize();
                }
            }
            catch (Exception ex)
            {
                libs.LoggerManager.LogError($"Không thể tải ảnh: {filePath}", ex);
            }
        }

        private void imgPreview_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            double zoom = e.Delta > 0 ? .2 : -.2;
            if (!(e.Delta > 0) && (st.ScaleX < .4 || st.ScaleY < .4)) return;

            Point relative = e.GetPosition(imgPreview);
            double absoluteX = relative.X * st.ScaleX + tt.X;
            double absoluteY = relative.Y * st.ScaleY + tt.Y;

            st.ScaleX += zoom;
            st.ScaleY += zoom;

            tt.X = absoluteX - relative.X * st.ScaleX;
            tt.Y = absoluteY - relative.Y * st.ScaleY;
        }

        private void imgPreview_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isHandMode)
            {
                imgPreview.CaptureMouse();
                _start = e.GetPosition(this);
                _origin = new Point(tt.X, tt.Y);
                Cursor = System.Windows.Input.Cursors.Hand;
            }
        }

        private void imgPreview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (imgPreview.IsMouseCaptured && _isHandMode)
            {
                Vector v = _start - e.GetPosition(this);
                tt.X = _origin.X - v.X;
                tt.Y = _origin.Y - v.Y;
            }
        }

        private void imgPreview_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isHandMode)
            {
                imgPreview.ReleaseMouseCapture();
                Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        private void menuHand_Click(object sender, RoutedEventArgs e)
        {
            _isHandMode = menuHand.IsChecked;
            if (_isHandMode)
                imgPreview.Cursor = System.Windows.Input.Cursors.Hand;
            else
                imgPreview.Cursor = System.Windows.Input.Cursors.Arrow;
        }

        private void btnActualSize_Click(object sender, RoutedEventArgs e)
        {
            ResetToActualSize();
        }

        private void ResetToActualSize()
        {
            st.ScaleX = 1.0;
            st.ScaleY = 1.0;
            tt.X = 0.0;
            tt.Y = 0.0;
            rt.Angle = 0;
        }

        private void btnFit_Click(object sender, RoutedEventArgs e)
        {
            if (!(imgPreview.Source is BitmapSource bitmap)) return;

            double availableWidth = Math.Max(1, PreviewViewport.ActualWidth - 12);
            double availableHeight = Math.Max(1, PreviewViewport.ActualHeight - 12);
            double imageWidth = Math.Max(1, bitmap.Width);
            double imageHeight = Math.Max(1, bitmap.Height);
            double fitScale = Math.Min(availableWidth / imageWidth, availableHeight / imageHeight);

            st.ScaleX = fitScale;
            st.ScaleY = fitScale;
            tt.X = 0;
            tt.Y = 0;
            rt.Angle = 0;
        }

        private void btnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            st.ScaleX += 0.2;
            st.ScaleY += 0.2;
        }

        private void btnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (st.ScaleX > 0.4 && st.ScaleY > 0.4)
            {
                st.ScaleX -= 0.2;
                st.ScaleY -= 0.2;
            }
        }

        private void btnRotateLeft_Click(object sender, RoutedEventArgs e)
        {
            rt.Angle -= 90;
        }

        private void btnRotateRight_Click(object sender, RoutedEventArgs e)
        {
            rt.Angle += 90;
        }
    }
}
