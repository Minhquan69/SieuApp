using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.ucs.Settings.viewmodels
{
    public class VMSystemConfig : VMBase
    {
        private string _apiUrl;
        public string ApiUrl
        {
            get => _apiUrl;
            set { _apiUrl = value; OnPropertyChanged(nameof(ApiUrl)); }
        }

        private string _networkMode;
        public string NetworkMode
        {
            get => _networkMode;
            set { _networkMode = value; OnPropertyChanged(nameof(NetworkMode)); }
        }

        private ObservableCollection<EndpointProfile> _endpoints;
        public ObservableCollection<EndpointProfile> Endpoints
        {
            get => _endpoints;
            set { _endpoints = value; OnPropertyChanged(nameof(Endpoints)); }
        }

        private bool _isSyncing;
        public bool IsSyncing
        {
            get => _isSyncing;
            set { _isSyncing = value; OnPropertyChanged(nameof(IsSyncing)); }
        }

        private string _lastSyncTime;
        public string LastSyncTime
        {
            get => _lastSyncTime;
            set { _lastSyncTime = value; OnPropertyChanged(nameof(LastSyncTime)); }
        }

        private int _maxLoadMinutes;
        public int MaxLoadMinutes
        {
            get => _maxLoadMinutes;
            set
            {
                _maxLoadMinutes = value;
                OnPropertyChanged(nameof(MaxLoadMinutes));
            }
        }

        private int _maxItemsInMemory;
        public int MaxItemsInMemory
        {
            get => _maxItemsInMemory;
            set
            {
                _maxItemsInMemory = value;
                OnPropertyChanged(nameof(MaxItemsInMemory));
            }
        }

        private string _allowedClassesString;
        public string AllowedClassesString
        {
            get => _allowedClassesString;
            set
            {
                _allowedClassesString = value;
                OnPropertyChanged(nameof(AllowedClassesString));
            }
        }

        private string _allowedImageClassesString;
        public string AllowedImageClassesString
        {
            get => _allowedImageClassesString;
            set
            {
                _allowedImageClassesString = value;
                OnPropertyChanged(nameof(AllowedImageClassesString));
            }
        }

        private bool _isAppearSelected=true;
        public bool IsAppearSelected
        {
            get => _isAppearSelected;
            set { _isAppearSelected = value; OnPropertyChanged(nameof(IsAppearSelected)); }
        }

        private bool _isUpdateSelected;
        public bool IsUpdateSelected
        {
            get => _isUpdateSelected;
            set { _isUpdateSelected = value; OnPropertyChanged(nameof(IsUpdateSelected)); }
        }

        private bool _isDisappearSelected;
        public bool IsDisappearSelected
        {
            get => _isDisappearSelected;
            set { _isDisappearSelected = value; OnPropertyChanged(nameof(IsDisappearSelected)); }
        }
        private bool _isExistSelected;
        public bool IsExistSelected
        {
            get => _isExistSelected;
            set { _isExistSelected = value; OnPropertyChanged(nameof(IsExistSelected)); }
        }

        private float _minConfidence;
        public float MinConfidence
        {
            get => _minConfidence;
            set { _minConfidence = value; OnPropertyChanged(nameof(MinConfidence)); }
        }

        private bool _roiInfoShow;
        public bool RoiInfoShow
        {
            get => _roiInfoShow;
            set { _roiInfoShow = value; OnPropertyChanged(nameof(RoiInfoShow)); }
        }

        public ICommand SaveCommand { get; }
        public ICommand SyncCommand { get; }

        public VMSystemConfig()
        {
            // Load current settings from Storage
            MaxLoadMinutes = MetaAIResultStorage.Instance.MaxLoadMinutes;
            MaxItemsInMemory = MetaAIResultStorage.Instance.MaxItemsInMemory;

            AllowedClassesString = string.Join(", ", MetaAIResultStorage.Instance.AllowedClasses);
            AllowedImageClassesString = string.Join(", ", MetaAIResultStorage.Instance.AllowedImageClasses);
            IsExistSelected = MetaAIResultStorage.Instance.AllowedEvents.Contains("object_exist");
            IsAppearSelected = MetaAIResultStorage.Instance.AllowedEvents.Contains("object_appear");
            IsUpdateSelected = MetaAIResultStorage.Instance.AllowedEvents.Contains("object_update");
            IsDisappearSelected = MetaAIResultStorage.Instance.AllowedEvents.Contains("object_disappear");
            MinConfidence = MetaAIResultStorage.Instance.MinConfidence;
            RoiInfoShow = MetaAIResultStorage.Instance.RoiInfoShow;

            // Load connection settings
            ApiUrl = ApiManager.Instance.BaseUrl;
            NetworkMode = ApiManager.Instance.NetworkMode;
            var allEndpoints = ApiManager.Instance.GetDiscoveredEndpoints();
            Endpoints = new ObservableCollection<EndpointProfile>(allEndpoints.Where(e => e.Keyword != null && !e.Keyword.StartsWith("_")));
            LastSyncTime = "Chưa đồng bộ";

            SaveCommand = new RelayCommand(_ => OnSave());
            SyncCommand = new RelayCommand(async _ => await OnSync());
        }

        private async Task OnSync()
        {
            IsSyncing = true;
            try
            {
                bool success = await ApiManager.Instance.DiscoverEndpointsAsync();
                if (success)
                {
                    var allEndpoints = ApiManager.Instance.GetDiscoveredEndpoints();
                    Endpoints = new ObservableCollection<EndpointProfile>(allEndpoints.Where(e => e.Keyword != null && !e.Keyword.StartsWith("_")));
                    LastSyncTime = DateTime.Now.ToString("HH:mm:ss");
                }
                else
                {
                    MessageBox.Show("Không thể kết nối tới Server để đồng bộ Endpoint.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                IsSyncing = false;
            }
        }

        private void OnSave()
        {
            if (MaxLoadMinutes <= 0 || MaxItemsInMemory <= 0)
            {
                MessageBox.Show("Giá trị cấu hình phải lớn hơn 0.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Save Connection Settings
            ApiManager.Instance.SaveConfig(ApiUrl, NetworkMode);

            MetaAIResultStorage.Instance.MaxLoadMinutes = MaxLoadMinutes;
            MetaAIResultStorage.Instance.MaxItemsInMemory = MaxItemsInMemory;

            // Parse classes
            MetaAIResultStorage.Instance.AllowedClasses = AllowedClassesString
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLower())
                .ToList();

            MetaAIResultStorage.Instance.AllowedImageClasses = AllowedImageClassesString
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLower())
                .ToList();

            // Events
            var events = new List<string>();
            
            if (IsExistSelected) events.Add("object_exist");
            if (IsAppearSelected) events.Add("object_appear");
            if (IsUpdateSelected) events.Add("object_update");
            if (IsDisappearSelected) events.Add("object_disappear");
            MetaAIResultStorage.Instance.AllowedEvents = events;

            MetaAIResultStorage.Instance.MinConfidence = MinConfidence;
            MetaAIResultStorage.Instance.RoiInfoShow = RoiInfoShow;

            MetaAIResultStorage.Instance.SaveConfig();

            // Notify about config change
            GlobalSystem.Instance.ReloadConfig();

            MessageBox.Show("Đã lưu cấu hình hệ thống thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }



}
