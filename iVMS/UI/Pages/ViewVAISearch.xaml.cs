using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace V3SClient.UI.Pages
{
    public enum VAISearchType
    {
        Face,
        Plate,
        Simulator
    }

    public class VAISearchArgs : EventArgs
    {
        public VAISearchType SearchType { get; set; }
        public string Query { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int Limit { get; set; }
    }

    public partial class ViewVAISearch : Page
    {
        public event EventHandler<VAISearchArgs> SearchRequested;

        public ViewVAISearch()
        {
            InitializeComponent();
            
            // Set default times: Today from 00:00:00 to 23:59:59
            DateTime now = DateTime.Now;
            dtpStart.Value = now.Date;
            dtpEnd.Value = now.Date.AddDays(1).AddSeconds(-1);
        }

        private int _selectedCameraCount = 0;

        public void UpdateSelectedCameraCount(int count)
        {
            _selectedCameraCount = count;
            if (txtCameraInfo == null) return;

            if (count > 0)
            {
                txtCameraInfo.Text = $"Đã chọn: {count} camera";
                txtCameraInfo.Foreground = new SolidColorBrush(Colors.LightGreen);
            }
            else
            {
                txtCameraInfo.Text = "Chưa chọn camera";
                txtCameraInfo.Foreground = new SolidColorBrush(Colors.OrangeRed);
            }
        }

        private void cmbSearchType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lblQuery == null) return;
            var item = cmbSearchType.SelectedItem as ComboBoxItem;
            if (item == null) return;

            string tag = item.Tag?.ToString();
            if (tag == "Face")
            {
                lblQuery.Text = "Nhập ID / tên";
            }
            else if (tag == "Simulator")
            {
                lblQuery.Text = "Bấm Tìm kiếm để chạy giả lập";
            }
            else
            {
                lblQuery.Text = "Nhập biển số xe";
            }
        }

        private void btnSearch_Click(object sender, RoutedEventArgs e)
        {
            var itemType = cmbSearchType.SelectedItem as ComboBoxItem;
            string tag = itemType?.Tag?.ToString();

            if (tag == "Simulator")
            {
                var argsSim = new VAISearchArgs
                {
                    SearchType = VAISearchType.Simulator,
                    Query = "SIM-9999",
                    StartTime = dtpStart.Value,
                    EndTime = dtpEnd.Value,
                    Limit = 15
                };
                SearchRequested?.Invoke(this, argsSim);
                return;
            }

            if (_selectedCameraCount == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất một camera trên cây thư mục trước khi tìm kiếm.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string query = txtSearchQuery.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                string typeName = itemType?.Content?.ToString() ?? "thông tin";
                MessageBox.Show($"Vui lòng nhập {typeName} cần tìm.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int limit = 100;
            if (cmbLimit.SelectedItem is ComboBoxItem itemLimit)
            {
                int.TryParse(itemLimit.Content.ToString(), out limit);
            }

            var selectedType = VAISearchType.Face;
            if (tag == "Plate")
            {
                selectedType = VAISearchType.Plate;
            }

            var args = new VAISearchArgs
            {
                SearchType = selectedType,
                Query = query,
                StartTime = dtpStart.Value,
                EndTime = dtpEnd.Value,
                Limit = limit
            };

            SearchRequested?.Invoke(this, args);
        }

        public void SetStatus(string message, bool isError = false)
        {
            txtStatus.Text = message;
            txtStatus.Foreground = isError ? new SolidColorBrush(Colors.OrangeRed) : new SolidColorBrush(Colors.LightGray);
        }

        public void AppendStatus(string message, Color? color = null)
        {
            var run = new Run(message + "\n");
            if (color.HasValue) run.Foreground = new SolidColorBrush(color.Value);
            
            // If txtStatus is empty, just set it, otherwise append inlines
            // Note: TextBlock.Inlines can be used if we want rich text
            // For simplicity, we just use Text for now, but let's try Inlines
            
            // Clear text if it's the first append
            if (txtStatus.Inlines.Count == 0 && !string.IsNullOrEmpty(txtStatus.Text))
            {
                txtStatus.Text = "";
            }

            txtStatus.Inlines.Add(run);
        }
        
        public void ClearStatus()
        {
            txtStatus.Inlines.Clear();
            txtStatus.Text = "";
        }
    }
}
