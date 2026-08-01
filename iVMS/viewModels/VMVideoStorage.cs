using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using V3SClient.libs;
using V3SClient.models;

namespace V3SClient.viewModels
{
     class VMVideoStorage: VMBase
    {
        public ObservableCollection<VideoStorage> VideoStorageList { get; set; }  
            = new ObservableCollection<VideoStorage>();
        private VideoStorage _selectedVideoStorage;
        public VideoStorage SelectedVideoStorage
        {
            get => _selectedVideoStorage;
            set
            {
                _selectedVideoStorage = value;
                OnPropertyChanged(nameof(SelectedVideoStorage));
            }
        }
        public VMVideoStorage()
        {
            var cloudStorage = new VideoStorage(
                name: "Cloud",
                location: GlobalUserInfo.Instance.Share_Data_Server_IP,
                port: GlobalUserInfo.Instance.Share_Data_Server_Port);

            VideoStorageList.Add(cloudStorage);
            SelectedVideoStorage = cloudStorage;
        }

        
    }
}















