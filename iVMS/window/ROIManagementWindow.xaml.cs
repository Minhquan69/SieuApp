using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using V3SClient.libs;
using static V3SClient.libs.ApiManager;

namespace V3SClient.window
{
    public partial class ROIManagementWindow : Window
    {
        private string _camId;

        public ROIManagementWindow(string camId)
        {
            InitializeComponent();
            _camId = camId;
            Loaded += ROIManagementWindow_Loaded;
        }

        private async void ROIManagementWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private async System.Threading.Tasks.Task LoadData()
        {
            try
            {
                dgRois.ItemsSource = null;
                var rois = await ApiManager.Instance.GetRoisAsync(_camId, CancellationToken.None);
                dgRois.ItemsSource = rois;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi tải danh sách ROI: ", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var view = new ROIConfigWindow(_camId, null);
            view.Owner = this;
            if (view.ShowDialog() == true)
            {
                _ = LoadData();
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            var roi = btn?.DataContext as ApiManager.RoiInfo;
            if (roi != null)
            {
                var view = new ROIConfigWindow(_camId, roi);
                view.Owner = this;
                if (view.ShowDialog() == true)
                {
                    _ = LoadData();
                }
            }
        }

        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            var roi = btn?.DataContext as RoiInfo;
            if (roi != null)
            {
                if (MessageBox.Show($"Bạn có chắc chắn muốn xóa ROI '{roi.Name}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    bool deleted = await ApiManager.Instance.DeleteRoiAsync(roi.Id, CancellationToken.None);
                    if (deleted)
                    {
                        MessageBox.Show("Đã xóa ROI thành công.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                        await LoadData();
                    }
                    else
                    {
                        MessageBox.Show("Xóa ROI thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

















