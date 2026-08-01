using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using V3SClient.enums;
using V3SClient.libs;
using V3SClient.models;
using V3SClient.viewModels;
using System.Diagnostics;
using Org.BouncyCastle.Asn1.Ocsp;
using SharpDX.Win32;

using V3SClient.ucs.Settings.views;
using V3SClient.UI.Converters;
using V3SClient.ucs.Settings.models;
using V3SClient.window;
using System.Windows.Input;

namespace V3SClient.ucs.Settings.viewmodels
{
    internal class VMCameraGroup : VMPageableBase<CameraGroupModel>
    {
        public Action RefreshCallback { get; set; }
        private List<ApiManager.CameraGroupInfo> _rawGroups = new List<ApiManager.CameraGroupInfo>();
        public ICommand RefreshCommand { get; }

        public VMCameraGroup() : base()
        {
            WindowTitle = "Thông Tin Đơn Vị Cơ Sở";
            RefreshCommand = new RelayCommand(_ => LoadData());
            LoadData();
        }

        protected override async void LoadData()
        {
            try
            {
                AllItems.Clear();
                _rawGroups = await ApiManager.Instance.GetCameraGroupsAsync(System.Threading.CancellationToken.None);

                if (_rawGroups != null && _rawGroups.Count != 0)
                {
                    foreach (var group in _rawGroups)
                    {
                        int? talkId = null;
                        if (group.Type == "talk_group" && group.ExtraMetadata != null)
                        {
                            try {
                                var extra = Newtonsoft.Json.Linq.JObject.FromObject(group.ExtraMetadata);
                                talkId = extra["talk_id"]?.ToObject<int>();
                            } catch {}
                        }

                        var bitem = new CameraGroupModel
                        {
                            CameraGroup_Id = group.Id,
                            CameraGroup_Name = group.Name,
                            CameraGroup_Code = group.Code ?? group.Type,
                            CameraGroup_Type = group.Type,
                            Description = group.Description,
                            CameraGroup_Parent_Id = group.ParentId,
                            TalkId = talkId,
                            Active = true
                        };
                        AllItems.Add(bitem);
                    }
                }
                CurrentPage = 1; 
                UpdatePagedItems();

            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading camera groups: " + ex.Message);
            }
        }
        
        public int TotalItems => AllItems.Count;

        protected override IEnumerable<CameraGroupModel> FilteredItems()
        {
            for (int i = 0; i < AllItems.Count; i++)
            {
                AllItems[i].Index = i + 1;
            }
            return AllItems;
        }

        protected override void OnEditItem(CameraGroupModel baseItem)
        {
            if (!GlobalUserInfo.Instance.HasPermission("camera_group:edit"))
            {
                MessageBox.Show("Bạn không có quyền chỉnh sửa đơn vị.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var existingInfo = _rawGroups.FirstOrDefault(g => g.Id == baseItem.CameraGroup_Id);
            var window = new CameraGroupEditWindow(_rawGroups, existingInfo);
            if (window.ShowDialog() == true)
            {
                LoadData();
            }
        }

        protected override void OnAddItem()
        {
            if (!GlobalUserInfo.Instance.HasPermission("camera_group:create"))
            {
                MessageBox.Show("Bạn không có quyền thêm mới đơn vị.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var window = new CameraGroupEditWindow(_rawGroups, null);
            if (window.ShowDialog() == true)
            {
                LoadData();
            }
        }

        protected override async void OnDeleteItem(CameraGroupModel item)
        {
            if (!GlobalUserInfo.Instance.HasPermission("camera_group:delete"))
            {
                MessageBox.Show("Bạn không có quyền xóa đơn vị.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (MessageBox.Show($"Bạn có chắc muốn xóa đơn vị '{item.CameraGroup_Name}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    bool ret = await ApiManager.Instance.DeleteCameraGroupAsync(item.CameraGroup_Id, System.Threading.CancellationToken.None);
                    if (ret)
                    {
                        AllItems.Remove(item);
                        UpdatePagedItems();
                    }
                    else
                    {
                        MessageBox.Show("Xóa thất bại!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi xóa: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}



















