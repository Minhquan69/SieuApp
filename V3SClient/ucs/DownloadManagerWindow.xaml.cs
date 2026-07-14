using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using V3SClient.libs;

namespace V3SClient.ucs
{
    public partial class DownloadManagerWindow : Window
    {
        public DownloadManagerWindow()
        {
            InitializeComponent();
            lstTasks.ItemsSource = SmartDownloadManager.Instance.Tasks;
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

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            var task = button?.DataContext as SmartDownloadManager.DownloadTask;
            
            if (task != null && !string.IsNullOrEmpty(task.SavePath))
            {
                try
                {
                    if (System.IO.Directory.Exists(task.SavePath))
                    {
                        Process.Start("explorer.exe", task.SavePath);
                    }
                    else
                    {
                        MessageBox.Show("Directory does not exist: " + task.SavePath, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not open folder: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}

















