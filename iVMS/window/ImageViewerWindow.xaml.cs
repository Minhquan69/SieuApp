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
using System.Windows.Shapes;

namespace V3SClient.window
{
    /// <summary>
    /// Interaction logic for ImageViewerWindow.xaml
    /// </summary>
    public partial class ImageViewerWindow : Window
    {
        private double currentScale = 1.0;
        private bool _isPanning = false;
        private Point _panStart;
        public ImageViewerWindow()
        {
            InitializeComponent();
        }

        public void SetImage(ImageSource source)
        {
            LargeImage.Source = source;
            FitImage(); 
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            currentScale *= 1.2;
            ApplyZoom();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            currentScale /= 1.2;
            ApplyZoom();
        }

        private void FitImage_Click(object sender, RoutedEventArgs e)
        {
            FitImage();
        }

        private void ApplyZoom()
        {
            ImageScale.ScaleX = currentScale;
            ImageScale.ScaleY = currentScale;
        }

        private void FitImage()
        {
            if (LargeImage.Source is BitmapSource bitmap)
            {
                double scaleX = ScrollViewer.ViewportWidth / bitmap.PixelWidth;
                double scaleY = ScrollViewer.ViewportHeight / bitmap.PixelHeight;

                currentScale = Math.Min(scaleX, scaleY);
                if (double.IsInfinity(currentScale) || currentScale <= 0)
                    currentScale = 1.0;

                ApplyZoom();
            }
        }
        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                e.Handled = true;

                Point mousePos = e.GetPosition(LargeImage);
                double oldScale = currentScale;

                if (e.Delta > 0)
                    currentScale *= 1.1;
                else
                    currentScale /= 1.1;

                currentScale = Math.Max(0.1, Math.Min(currentScale, 10.0));
                ApplyZoom();

                // Adjust scroll offsets to keep zoom centered under mouse
                Point relative = e.GetPosition(ScrollViewer);
                double offsetX = relative.X + ScrollViewer.HorizontalOffset;
                double offsetY = relative.Y + ScrollViewer.VerticalOffset;

                double scaleRatio = currentScale / oldScale;
                ScrollViewer.ScrollToHorizontalOffset(offsetX * scaleRatio - relative.X);
                ScrollViewer.ScrollToVerticalOffset(offsetY * scaleRatio - relative.Y);
            }
        }
        private void PanToggle_Click(object sender, RoutedEventArgs e)
        {
            _isPanning = !_isPanning;

            if (_isPanning)
            {
                LargeImage.Cursor = Cursors.Hand;
                LargeImage.MouseLeftButtonDown += LargeImage_MouseLeftButtonDown;
                LargeImage.MouseMove += LargeImage_MouseMove;
                LargeImage.MouseLeftButtonUp += LargeImage_MouseLeftButtonUp;
            }
            else
            {
                LargeImage.Cursor = Cursors.Arrow;
                LargeImage.MouseLeftButtonDown -= LargeImage_MouseLeftButtonDown;
                LargeImage.MouseMove -= LargeImage_MouseMove;
                LargeImage.MouseLeftButtonUp -= LargeImage_MouseLeftButtonUp;
            }
        }
        private void LargeImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _panStart = e.GetPosition(ScrollViewer);
                LargeImage.CaptureMouse();
            }
        }

        private void LargeImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning && LargeImage.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            {
                var current = e.GetPosition(ScrollViewer);
                var delta = _panStart - current;

                ScrollViewer.ScrollToHorizontalOffset(ScrollViewer.HorizontalOffset + delta.X);
                ScrollViewer.ScrollToVerticalOffset(ScrollViewer.VerticalOffset + delta.Y);

                _panStart = current;
            }
        }

        private void LargeImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                LargeImage.ReleaseMouseCapture();
            }
        }
    }
}

















