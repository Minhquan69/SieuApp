using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;

namespace V3SClient.ucs
{
    public partial class VehicleCaptureResultControl : UserControl
    {
        public VehicleCaptureResultControl()
        {
            InitializeComponent();
            Loaded += (s, e) => UpdateNavigationButtons();
        }

        private double GetScrollStep()
        {
            return ImageScrollViewer.ViewportWidth > 0
                ? ImageScrollViewer.ViewportWidth * 0.85
                : 360;
        }

        private void PreviousImages_Click(object sender, RoutedEventArgs e)
        {
            ImageScrollViewer.ScrollToHorizontalOffset(
                System.Math.Max(0, ImageScrollViewer.HorizontalOffset - GetScrollStep()));
        }

        private void NextImages_Click(object sender, RoutedEventArgs e)
        {
            ImageScrollViewer.ScrollToHorizontalOffset(
                System.Math.Min(ImageScrollViewer.ScrollableWidth, ImageScrollViewer.HorizontalOffset + GetScrollStep()));
        }

        private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ImageScrollViewer.ScrollToHorizontalOffset(
                System.Math.Max(0, System.Math.Min(
                    ImageScrollViewer.ScrollableWidth,
                    ImageScrollViewer.HorizontalOffset - (e.Delta / 3.0))));
            e.Handled = true;
        }

        private void ImageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateNavigationButtons();
        }

        private void UpdateNavigationButtons()
        {
            PreviousButton.IsEnabled = ImageScrollViewer.HorizontalOffset > 0;
            NextButton.IsEnabled = ImageScrollViewer.HorizontalOffset < ImageScrollViewer.ScrollableWidth;
        }
    }
}
