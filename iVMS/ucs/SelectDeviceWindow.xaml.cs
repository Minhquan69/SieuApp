using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using V3SClient.libs;


namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for SelectDeviceWindow.xaml
    /// </summary>
    public partial class SelectDeviceWindow : Window
    {
        public ObservableCollection<CamInfo> AvailableDevices { get; set; } = new ObservableCollection<CamInfo>();
        public ObservableCollection<CamInfo> SelectedDevices { get; private set; } = new ObservableCollection<CamInfo>();
        public SelectDeviceWindow(ObservableCollection<CamInfo> availableDevices)
        {
            InitializeComponent();
            DeviceListBox.ItemsSource = availableDevices;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in DeviceListBox.SelectedItems)
            {
                SelectedDevices.Add((CamInfo)item);
            }
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

















