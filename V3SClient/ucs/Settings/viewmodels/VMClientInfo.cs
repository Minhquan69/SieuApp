using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using V3SClient.enums;
using V3SClient.libs;
using V3SClient.ucs.Settings.models;
using V3SClient.ucs.Settings.views;
using V3SClient.viewModels;
using V3SClient.window;
using Xceed.Wpf.Toolkit.Primitives;

namespace V3SClient.ucs.Settings.viewmodels
{
    public class VMClientInfo : VMPageableBase<ClientInfoModel>
    {
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

        public VMClientInfo() : base()
        {
            WindowTitle = "Quản lý Client Profile";
            RefreshCommand = new RelayCommand(_ => LoadData());
            AddCommand = new RelayCommand(ExecuteAdd, null);
            EditCommand = new RelayCommand(ExecuteEdit, null);
            LoadData();
        }

        protected override async void LoadData()
        {
            try
            {
                AllItems.Clear();
                var profiles = await ApiManager.Instance.GetClientProfilesAsync(CancellationToken.None);
                
                if (profiles != null && profiles.Count > 0)
                {
                    foreach (var p in profiles)
                    {
                        AllItems.Add(new ClientInfoModel
                        {
                            Id = p.Id,
                            ClientInfo_Name = p.Name,
                            ClientInfo_Code = p.Code,
                            ClientInfo_Description = p.Description,
                            CameraIds = p.CameraIds ?? new List<string>(),
                            AccountIds = p.AccountIds ?? new List<int>()
                        });
                    }
                }
                CurrentPage = 1;
                UpdatePagedItems();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading client profiles: " + ex.Message);
            }
        }

        protected override IEnumerable<ClientInfoModel> FilteredItems()
        {
            var items = AllItems.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string lower = SearchText.ToLower();
                items = items.Where(c =>
                    (c.ClientInfo_Name != null && c.ClientInfo_Name.ToLower().Contains(lower)) ||
                    (c.ClientInfo_Code != null && c.ClientInfo_Code.ToLower().Contains(lower)));
            }

            var result = items.ToList();
            for (int i = 0; i < result.Count; i++)
            {
                result[i].Index = i + 1;
            }
            return result;
        }

        protected override async void OnDeleteItem(ClientInfoModel item)
        {
            if (item == null) return;
            if (!GlobalUserInfo.Instance.HasPermission("client_profile:delete"))
            {
                MessageBox.Show("Bạn không có quyền xóa Client Profile.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (MessageBox.Show($"Bạn có chắc chắn muốn xóa Client Profile \"{item.ClientInfo_Name}\" ({item.ClientInfo_Code})?", 
                "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                bool deleted = await ApiManager.Instance.DeleteClientProfileAsync(item.Id, CancellationToken.None);
                if (deleted)
                {
                    MessageBox.Show("Thao tác thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadData();
                }
                else
                {
                    MessageBox.Show("Thao tác thất bại!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public ICommand RefreshCommand { get; }
        public new ICommand AddCommand { get; }
        public new ICommand EditCommand { get; }

        private void ExecuteAdd(object obj)
        {
            if (!GlobalUserInfo.Instance.HasPermission("client_profile:create"))
            {
                MessageBox.Show("Bạn không có quyền thêm mới Client Profile.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var win = new ClientProfileEditWindow();
            if (win.ShowDialog() == true)
            {
                LoadData();
            }
        }

        private void ExecuteEdit(object obj)
        {
            if (obj is ClientInfoModel item)
            {
                if (!GlobalUserInfo.Instance.HasPermission("client_profile:edit"))
                {
                    MessageBox.Show("Bạn không có quyền chỉnh sửa Client Profile.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var win = new ClientProfileEditWindow(item);
                if (win.ShowDialog() == true)
                {
                    LoadData();
                }
            }
        }
    }
}

















