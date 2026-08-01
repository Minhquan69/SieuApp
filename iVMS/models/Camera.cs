using System;
using System.Collections.Generic;
using V3SClient.libs;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace V3SClient.models
{
    public class Camera: INotifyPropertyChanged
    {
        public bool is_H264 { get; set; } = true; // False nghÄ©a lÃ  H265 mode
        public string camID { get; set; }
        public string groupID { get; set; }
        public string name { get; set; }
        public string long_Name { get; set; }
        public string description { get; set; }
        public string type {  get; set; }
        public bool is_Live { get; set; }
        // Returned directly by the camera-list API; retain its JSON name.
        public bool is_recording { get; set; }
        public string rtps { get; set; }
        
        // Multi-stream properties
        public string RtspUrlRaw { get; set; }      
        public string RtspUrlAI { get; set; }       
        public string RtspUrlMainRaw { get; set; }  
        public string RtspUrlMainAI { get; set; }   
        
        public bool IsH264Raw { get; set; } = true;
        public bool IsH264AI { get; set; } = true;
        public bool IsH264MainRaw { get; set; } = true;
        public bool IsH264MainAI { get; set; } = true;

        public bool HasAIStream { get; set; }       
        public List<CameraStreamInfo> Streams { get; set; }
        // Relay metadata is retained for the isolated WebRTC/WHEP live view.
        // Existing RTSP consumers continue to use the fields above.
        public string ServerRelayPublicEndpoint { get; set; }
        public string ServerRelayRtcInternalEndpoint { get; set; }
        public string ServerRelayRtcPublicEndpoint { get; set; }

        public bool is_Master {  get; set; } = false;

        private string _activeStreamMode = "";
        public string ActiveStreamMode
        {
            get => _activeStreamMode;
            set
            {
                if (_activeStreamMode != value)
                {
                    _activeStreamMode = value;
                    OnPropertyChanged(nameof(ActiveStreamMode));
                }
            }
        }

        private Visibility _allowSelecting = Visibility.Collapsed;
        public Visibility AllowSelecting
        {
            get => _allowSelecting;
            set
            {
                if (_allowSelecting != value)
                {
                    _allowSelecting = value;
                    OnPropertyChanged(nameof(AllowSelecting));
                }
            }
        }

        // Geolocation and Metadata for Map
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public object ExtraMetadata { get; set; }
        public bool? is_online { get; set; } // Há»— trá»£ simulation vÃ  sync

        private string _status = "offline";
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                }
            }
        }

        private Visibility _nodeVisibility = Visibility.Visible;
        public Visibility NodeVisibility
        {
            get => _nodeVisibility;
            set
            {
                if (_nodeVisibility != value)
                {
                    _nodeVisibility = value;
                    OnPropertyChanged(nameof(NodeVisibility));
                }
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}