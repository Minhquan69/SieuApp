using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using V3SClient.libs;
using V3SClient.ucs.Settings.models;
using V3SClient.window;
using System.Windows.Input;

namespace V3SClient.ucs.Settings.viewmodels
{
    public class VMCamInfo : VMPageableBase<CamInfoModel>
    {
        // Commands
        public ICommand ROIConfigCommand { get; }
        public ICommand AIConfigCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand AddCameraCommand { get; }
        public ICommand ToggleStatusCommand { get; }

        // Search & Filter
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

        private string _filterType;
        public string FilterType
        {
            get => _filterType;
            set
            {
                if (_filterType != value)
                {
                    _filterType = value;
                    OnPropertyChanged(nameof(FilterType));
                    CurrentPage = 1;
                    UpdatePagedItems();
                }
            }
        }

        private string _filterGroupId;
        public string FilterGroupId
        {
            get => _filterGroupId;
            set
            {
                if (_filterGroupId != value)
                {
                    _filterGroupId = value;
                    OnPropertyChanged(nameof(FilterGroupId));
                    CurrentPage = 1;
                    UpdatePagedItems();
                }
            }
        }

        // Groups for filter dropdown
        private ObservableCollection<ApiManager.CameraGroupInfo> _groups = new ObservableCollection<ApiManager.CameraGroupInfo>();
        public ObservableCollection<ApiManager.CameraGroupInfo> Groups
        {
            get => _groups;
            set
            {
                _groups = value;
                OnPropertyChanged(nameof(Groups));
            }
        }

        // Total items for display
        public int TotalItems => AllItems.Count;

        public VMCamInfo() : base()
        {
            WindowTitle = "Quản lý Hệ thống Camera";
            ROIConfigCommand = new RelayCommand(param => OnROIConfig((CamInfoModel)param), param => param is CamInfoModel);
            AIConfigCommand = new RelayCommand(param => OnAIConfig((CamInfoModel)param), param => param is CamInfoModel);
            RefreshCommand = new RelayCommand(_ => LoadData());
            AddCameraCommand = new RelayCommand(_ => OnAddCamera());
            ToggleStatusCommand = new RelayCommand(param => OnToggleStatus((CamInfoModel)param), param => param is CamInfoModel);

            LoadData();
            LoadGroups();
        }

        private void OnROIConfig(CamInfoModel cam)
        {
            if (cam == null) return;
            if (!GlobalUserInfo.Instance.HasPermission("camera:edit"))
            {
                MessageBox.Show("Bạn không có quyền thiết lập ROI.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var view = new ROIManagementWindow(cam.Id);
            view.ShowDialog();
        }

        private void OnAIConfig(CamInfoModel cam)
        {
            if (cam == null) return;
            if (!GlobalUserInfo.Instance.HasPermission("ai_assignment:create"))
            {
                MessageBox.Show("Bạn không có quyền gán AI cho camera.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var view = new AIConfigWindow(cam.Id);
            view.ShowDialog();
        }

        private async void OnAddCamera()
        {
            if (!GlobalUserInfo.Instance.HasPermission("camera:create"))
            {
                MessageBox.Show("Bạn không có quyền thêm mới camera.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var window = new CameraEditWindow(null);
            bool? result = window.ShowDialog();
            if (result == true)
            {
                LoadData(); // Reload after adding
            }
        }

        protected override void OnEditItem(CamInfoModel item)
        {
            if (item == null) return;
            if (!GlobalUserInfo.Instance.HasPermission("camera:edit"))
            {
                MessageBox.Show("Bạn không có quyền chỉnh sửa camera.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var window = new CameraEditWindow(item);
            bool? result = window.ShowDialog();
            if (result == true)
            {
                LoadData(); // Reload after editing
            }
        }

        private async void OnToggleStatus(CamInfoModel cam)
        {
            if (cam == null) return;
            if (!GlobalUserInfo.Instance.HasPermission("camera:edit"))
            {
                MessageBox.Show("Bạn không có quyền thay đổi trạng thái camera.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string action = cam.IsActive ? "ngừng" : "kích hoạt";
            var result = MessageBox.Show($"Bạn có chắc muốn {action} camera \"{cam.DisplayName1}\"?",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                bool success = await ApiManager.Instance.UpdateCameraAsync(cam.Id,
                    new { is_active = !cam.IsActive }, CancellationToken.None);
                if (success)
                {
                    MessageBox.Show($"Đã {action} camera thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadData();
                }
                else
                {
                    MessageBox.Show("Cập nhật trạng thái thất bại!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        protected override async void OnDeleteItem(CamInfoModel item)
        {
            if (item == null) return;
            if (!GlobalUserInfo.Instance.HasPermission("camera:delete"))
            {
                MessageBox.Show("Bạn không có quyền xóa camera.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check dependencies first
            var deps = await ApiManager.Instance.GetCameraDependenciesAsync(item.Id, CancellationToken.None);
            string message;
            if (deps != null && (deps.GroupsCount > 0 || deps.AiAssignmentsCount > 0))
            {
                var parts = new List<string>();
                if (deps.GroupsCount > 0)
                    parts.Add($"• Đang thuộc {deps.GroupsCount} nhóm/khu vực");
                if (deps.AiAssignmentsCount > 0)
                    parts.Add($"• Đang gán vào {deps.AiAssignmentsCount} AI Server");
                message = $"Camera \"{item.DisplayName1}\" ({item.CameraCode}) có các dữ liệu liên quan:\n\n{string.Join("\n", parts)}\n\nXóa camera sẽ đồng thời xóa TẤT CẢ dữ liệu trên. Bạn có chắc chắn?";
            }
            else
            {
                message = $"Bạn có chắc chắn muốn xóa camera \"{item.DisplayName1}\" ({item.CameraCode})?";
            }

            if (MessageBox.Show(message, "Xác nhận xóa Camera", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                bool deleted = await ApiManager.Instance.DeleteCameraAsync(item.Id, CancellationToken.None);
                if (deleted)
                {
                    MessageBox.Show("Đã xóa camera thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadData();
                }
                else
                {
                    MessageBox.Show("Xóa camera thất bại!", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void LoadGroups()
        {
            try
            {
                var groups = await ApiManager.Instance.GetCameraGroupsAsync(CancellationToken.None);
                Groups.Clear();
                
                // Thêm mục "Tất cả"
                Groups.Add(new ApiManager.CameraGroupInfo { Id = Guid.Empty, Name = "Tất cả nhóm" });

                if (groups != null)
                {
                    foreach (var g in groups)
                        Groups.Add(g);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading groups: " + ex.Message);
            }
        }

        protected override async void LoadData()
        {
            try
            {
                AllItems.Clear();
                var cameras = await ApiManager.Instance.GetAllCamerasAsync(CancellationToken.None);

                if (cameras != null && cameras.Count != 0)
                {
                    foreach (var cam in cameras)
                    {
                        var camModel = new CamInfoModel
                        {
                            Id = cam.Id,
                            CameraCode = cam.CameraCode,
                            DisplayName1 = cam.DisplayName1,
                            DisplayName2 = cam.DisplayName2,
                            CameraType = cam.CameraType,
                            Codec = cam.Codec,
                            OperationMode = cam.OperationMode,
                            SourceIp = cam.SourceIp,
                            SourcePort = cam.SourcePort,
                            SourceStreamUrl = cam.SourceStreamUrl,
                            LocationName = cam.LocationName,
                            Latitude = cam.Latitude,
                            Longitude = cam.Longitude,
                            Description = cam.Description,
                            IsActive = cam.IsActive,
                            MediaServerId = cam.MediaServerId,
                            MediaServerName = cam.MediaServer?.Name ?? "-",
                            GroupIds = cam.GroupIds,
                            AiNodeName = cam.AiNodeName ?? "-"
                        };
                        AllItems.Add(camModel);
                    }
                }
                CurrentPage = 1;
                OnPropertyChanged(nameof(TotalItems));
                UpdatePagedItems();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading cameras: " + ex.Message);
            }
        }

        protected override IEnumerable<CamInfoModel> FilteredItems()
        {
            var items = AllItems.AsEnumerable();

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string lower = SearchText.ToLower();
                items = items.Where(c =>
                    (c.DisplayName1 != null && c.DisplayName1.ToLower().Contains(lower)) ||
                    (c.CameraCode != null && c.CameraCode.ToLower().Contains(lower)));
            }

            // Apply type filter
            if (!string.IsNullOrEmpty(FilterType))
            {
                items = items.Where(c => c.CameraType == FilterType);
            }

            // Apply group filter
            if (!string.IsNullOrEmpty(FilterGroupId) && FilterGroupId != Guid.Empty.ToString())
            {
                items = items.Where(c => c.GroupIds != null && c.GroupIds.Contains(FilterGroupId));
            }

            var result = items.ToList();
            for (int i = 0; i < result.Count; i++)
            {
                result[i].Index = i + 1;
            }
            return result;
        }
    }
}
