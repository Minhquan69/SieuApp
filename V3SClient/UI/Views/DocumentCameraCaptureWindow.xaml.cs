using System.Windows;
using System.Windows.Input;

namespace V3SClient.UI.Views
{
    public partial class DocumentCameraCaptureWindow : Window
    {
        public DocumentCameraCaptureWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
