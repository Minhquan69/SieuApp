using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;
using V3SClient.libs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace V3SClient.window
{
    public partial class CameraGroupEditWindow : Window
    {
        private Guid? _groupId;
        private List<ApiManager.CameraGroupInfo> _allGroups;

        public CameraGroupEditWindow(List<ApiManager.CameraGroupInfo> allGroups, ApiManager.CameraGroupInfo existingGroup = null)
        {
            InitializeComponent();
            _allGroups = allGroups;
            
            // Populate parents (exclude self and potential circular references - simplified here as just exclude self)
            var parentOptions = allGroups
                .Where(g => existingGroup == null || g.Id != existingGroup.Id)
                .Select(g => new { Id = (Guid?)g.Id, Name = g.Name })
                .ToList();
            parentOptions.Insert(0, new { Id = (Guid?)null, Name = "(không có - Cấp cao nhất)" });
            cboParent.ItemsSource = parentOptions;
            cboParent.SelectedIndex = 0;

            if (existingGroup != null)
            {
                _groupId = existingGroup.Id;
                txtTitle.Text = "Chỉnh sửa Nhóm camera";
                txtName.Text = existingGroup.Name;
                txtCode.Text = existingGroup.Code;
                txtDescription.Text = existingGroup.Description;
                
                // Set Type
                foreach (ComboBoxItem item in cboType.Items)
                {
                    if (item.Tag?.ToString() == existingGroup.Type)
                    {
                        cboType.SelectedItem = item;
                        break;
                    }
                }

                // Set Parent
                if (existingGroup.ParentId != null)
                {
                    cboParent.SelectedValue = existingGroup.ParentId;
                }

                // Set TalkId if applicable
                if (existingGroup.Type == "talk_group" && existingGroup.ExtraMetadata != null)
                {
                    try {
                        var extra = Newtonsoft.Json.Linq.JObject.FromObject(existingGroup.ExtraMetadata);
                        txtTalkId.Text = extra["talk_id"]?.ToString();
                    } catch {}
                }
            }
        }

        private void CboType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (panelTalkId == null) return;
            var selectedItem = cboType.SelectedItem as ComboBoxItem;
            panelTalkId.Visibility = (selectedItem?.Tag?.ToString() == "talk_group") ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Vui lòng nhập tên nhóm.");
                return;
            }

            var type = (cboType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            int? talkId = null;
            if (type == "talk_group")
            {
                if (int.TryParse(txtTalkId.Text, out int tid)) talkId = tid;
                else
                {
                    MessageBox.Show("Vui lòng nhập Kênh đàm thoại (số nguyên).");
                    return;
                }
            }

            Guid? parentId = (Guid?)cboParent.SelectedValue;

            var data = new
            {
                name = txtName.Text,
                code = txtCode.Text,
                type = type,
                description = txtDescription.Text,
                parent_id = parentId,
                extra_metadata = talkId.HasValue ? new { talk_id = talkId.Value } : null
            };

            bool success;
            if (_groupId == null)
            {
                success = await ApiManager.Instance.CreateCameraGroupAsync(data, System.Threading.CancellationToken.None);
            }
            else
            {
                success = await ApiManager.Instance.UpdateCameraGroupAsync(_groupId.Value, data, System.Threading.CancellationToken.None);
            }

            if (success)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Lưu thất bại. Vui lòng kiểm tra lại kết nối hoặc dữ liệu.");
            }
        }
    }
}

















