using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using V3SClient.libs;
using V3SClient.ucs.Settings.models;
using V3SClient.viewModels;

namespace V3SClient.window
{
    public partial class ClientProfileEditWindow : Window, INotifyPropertyChanged
    {
        private Guid? _profileId;
        private string _windowTitle;
        private string _profileName;
        private string _profileCode;
        private string _profileDescription;

        public event PropertyChangedEventHandler PropertyChanged;

        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(nameof(WindowTitle)); }
        }

        public string ProfileName
        {
            get => _profileName;
            set { _profileName = value; OnPropertyChanged(nameof(ProfileName)); }
        }

        public string ProfileCode
        {
            get => _profileCode;
            set { _profileCode = value; OnPropertyChanged(nameof(ProfileCode)); }
        }

        public string ProfileDescription
        {
            get => _profileDescription;
            set { _profileDescription = value; OnPropertyChanged(nameof(ProfileDescription)); }
        }

        private ObservableCollection<UserItem> _allUsers = new ObservableCollection<UserItem>();
        private ObservableCollection<UserItem> _userList = new ObservableCollection<UserItem>();
        public ObservableCollection<UserItem> UserList
        {
            get => _userList;
            set { _userList = value; OnPropertyChanged(nameof(UserList)); }
        }

        private ObservableCollection<CameraItem> _allCameras = new ObservableCollection<CameraItem>();
        private ObservableCollection<CameraItem> _cameraList = new ObservableCollection<CameraItem>();
        public ObservableCollection<CameraItem> CameraList
        {
            get => _cameraList;
            set { _cameraList = value; OnPropertyChanged(nameof(CameraList)); }
        }

        public ICommand SaveCommand { get; }

        public ClientProfileEditWindow(ClientInfoModel model = null)
        {
            InitializeComponent();
            DataContext = this;

            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteSave);

            if (model == null)
            {
                WindowTitle = "Thêm mới Client Profile";
                _profileId = null;
            }
            else
            {
                WindowTitle = "Chỉnh sửa Client Profile";
                _profileId = model.Id;
                ProfileName = model.ClientInfo_Name;
                ProfileCode = model.ClientInfo_Code;
                ProfileDescription = model.ClientInfo_Description;
            }

            LoadData(model);
        }

        private async void LoadData(ClientInfoModel model)
        {
            try
            {
                // Load Users
                var users = await ApiManager.Instance.GetAccountsAsync(CancellationToken.None);
                _allUsers.Clear();
                foreach (var u in users)
                {
                    var item = new UserItem
                    {
                        UserId = u.Id,
                        UserName = u.Username,
                        FullName = u.FullName,
                        IsChecked = model?.AccountIds?.Contains(u.Id) ?? false
                    };
                    _allUsers.Add(item);
                }
                FilterUsers();

                // Load Cameras
                var cams = await ApiManager.Instance.GetCamInfoAsync(CancellationToken.None);
                _allCameras.Clear();
                if (cams != null)
                {
                    foreach (var c in cams)
                    {
                        var item = new CameraItem
                        {
                            CameraId = c.Id,
                            CameraCode = c.CamInfo_CamId,
                            DisplayName = c.CamInfo_Name,
                            IsChecked = model?.CameraIds?.Contains(c.Id) ?? false
                        };
                        _allCameras.Add(item);
                    }
                }
                FilterCameras();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading data for ClientProfileEdit: " + ex.Message);
            }
        }

        private void FilterUsers()
        {
            string filter = TxtSearchUser?.Text?.ToLower() ?? "";
            UserList = new ObservableCollection<UserItem>(
                _allUsers.Where(u => string.IsNullOrEmpty(filter) || 
                                     u.UserName.ToLower().Contains(filter) || 
                                     (u.FullName != null && u.FullName.ToLower().Contains(filter)))
            );
        }

        private void FilterCameras()
        {
            string filter = TxtSearchCamera?.Text?.ToLower() ?? "";
            CameraList = new ObservableCollection<CameraItem>(
                _allCameras.Where(c => string.IsNullOrEmpty(filter) || 
                                       c.CameraCode.ToLower().Contains(filter) || 
                                       (c.DisplayName != null && c.DisplayName.ToLower().Contains(filter)))
            );
        }

        private void TxtSearchUser_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            FilterUsers();
        }

        private void TxtSearchCamera_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            FilterCameras();
        }

        private bool CanExecuteSave(object obj)
        {
            return !string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(ProfileCode);
        }

        private async void ExecuteSave(object obj)
        {
            try
            {
                var selectedUserIds = _allUsers.Where(u => u.IsChecked).Select(u => u.UserId).ToList();
                var selectedCameraIds = _allCameras.Where(c => c.IsChecked).Select(c => c.CameraId).ToList();

                var profileData = new
                {
                    name = this.ProfileName,
                    code = this.ProfileCode,
                    description = this.ProfileDescription ?? "",
                    layout_config = new { },
                    user_ids = selectedUserIds,
                    camera_ids = selectedCameraIds
                };

                bool isSuccess = false;

                if (_profileId == null) // Thêm mới
                {
                    var result = await ApiManager.Instance.CreateClientProfileAsync(profileData, CancellationToken.None);
                    isSuccess = result != null;
                }
                else // Cập nhật
                {
                    isSuccess = await ApiManager.Instance.UpdateClientProfileAsync(_profileId.Value, profileData, CancellationToken.None);
                }

                if (isSuccess)
                {
                    MessageBox.Show("Lưu thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.DialogResult = true;
                    this.Close();
                }
                else
                {
                    MessageBox.Show("Lưu thất bại! Mã client có thể đã tồn tại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Có lỗi xảy ra: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class UserItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(nameof(IsChecked)); }
        }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string FullName { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class CameraItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(nameof(IsChecked)); }
        }
        public string CameraId { get; set; }
        public string CameraCode { get; set; }
        public string DisplayName { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

















