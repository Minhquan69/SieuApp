using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using V3SClient.enums;
using V3SClient.libs;
using V3SClient.models;
using V3SClient.viewModels;
using System.Diagnostics;
using Org.BouncyCastle.Asn1.Ocsp;
using SharpDX.Win32;
using System.Windows.Shapes;
using V3SClient.ucs.Settings.views;
using V3SClient.UI.Converters;
using V3SClient.ucs.Settings.models;

namespace V3SClient.ucs.Settings.viewmodels
{
    internal class VMGroupTalkInfo : VMPageableBase<CameraGroupModel>
    {
        public ICommand RefreshCommand { get; }
        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged(nameof(SearchText));
                    CurrentPage = 1;
                    UpdatePagedItems();
                }
            }
        }

        public int TotalItems => AllItems.Count;

        public VMGroupTalkInfo() : base()
        {
            WindowTitle = "Nhóm bộ đàm (Body Cam)";
            RefreshCommand = new RelayCommand(_ => LoadData());
            LoadData();
        }

        protected override async void LoadData()
        {
            try
            {
                AllItems.Clear();
                var groups = await ApiManager.Instance.GetCameraGroupsAsync(System.Threading.CancellationToken.None);

                if (groups != null)
                {
                    foreach (var group in groups)
                    {
                        // Chỉ lấy các nhóm loại đàm thoại
                        if (group.Type != "talk_group") continue;

                        int? talkId = null;
                        if (group.ExtraMetadata != null)
                        {
                            try {
                                var extra = Newtonsoft.Json.Linq.JObject.FromObject(group.ExtraMetadata);
                                talkId = extra["talk_id"]?.ToObject<int>();
                            } catch {}
                        }

                        AllItems.Add(new CameraGroupModel
                        {
                            CameraGroup_Id = group.Id,
                            CameraGroup_Name = group.Name,
                            CameraGroup_Code = group.Code,
                            CameraGroup_Type = group.Type,
                            Description = group.Description,
                            CameraGroup_Parent_Id = group.ParentId,
                            TalkId = talkId,
                            Active = true
                        });
                    }
                }
                CurrentPage = 1;
                OnPropertyChanged(nameof(TotalItems));
                UpdatePagedItems();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading group talks: " + ex.Message);
            }
        }

        protected override IEnumerable<CameraGroupModel> FilteredItems()
        {
            var items = AllItems.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string lower = SearchText.ToLower();
                items = items.Where(c =>
                    (c.CameraGroup_Name != null && c.CameraGroup_Name.ToLower().Contains(lower)) ||
                    (c.Description != null && c.Description.ToLower().Contains(lower)));
            }

            var result = items.ToList();
            for (int i = 0; i < result.Count; i++)
            {
                result[i].Index = i + 1;
            }
            return result;
        }

        protected override void OnEditItem(CameraGroupModel item)
        {
            if (item == null) return;
            if (!GlobalUserInfo.Instance.HasPermission("camera_group:edit"))
            {
                MessageBox.Show("Bạn không có quyền chỉnh sửa nhóm bộ đàm.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
             // Logic edit tương tự VMCameraGroup
        }

        protected override void OnAddItem()
        {
            if (!GlobalUserInfo.Instance.HasPermission("camera_group:create"))
            {
                MessageBox.Show("Bạn không có quyền thêm mới nhóm bộ đàm.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
             // Logic add tương tự VMCameraGroup
        }
    }
}
