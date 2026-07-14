using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.models
{
   public class MetaAIResult: INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        private string _cameraInfo { get; set; } // camera id


        public string CameraInfo
        {
            get => _cameraInfo;
            set
            {
                if (_cameraInfo != value)
                {
                    _cameraInfo = value;
                    OnPropertyChanged(nameof(CameraInfo));
                }
            }
        }

        public SharpDX.RectangleF BoundingBox { get; set; }
        private string _caption { get; set; }
        public string Caption
        {
            get => _caption;
            set
            {
                if (_caption != value)
                {
                    _caption = value;
                    OnPropertyChanged(nameof(Caption));
                }
            }
        }
        private bool _isBlackList { get; set; }
        public bool IsBlackList
        {
            get => _isBlackList;
            set
            {
                if (_isBlackList != value)
                {
                    _isBlackList = value;
                    OnPropertyChanged(nameof(IsBlackList));
                }
            }
        }

        private bool _isDisplay { get; set; }
        public bool IsDisplay
        {
            get => _isDisplay;
            set
            {
                if (_isDisplay != value)
                {
                    _isDisplay = value;
                    OnPropertyChanged(nameof(IsDisplay));
                }
            }
        }

        private string _objectID { get; set; } // CCCD, Plate number
        public string ObjectID
        {
            get => _objectID;
            set
            {
                if (_objectID != value)
                {
                    _objectID = value;
                    OnPropertyChanged(nameof(ObjectID));
                }
            }
        }

        private string _encodeObjectImage { get; set; }

        public string EncodeObjectImage
        {
            get => _encodeObjectImage;
            set
            {
                if (_encodeObjectImage != value)
                {
                    _encodeObjectImage = value;
                    OnPropertyChanged(nameof(EncodeObjectImage));
                }
            }
        }

        private string _roiDwellSecondsInfo { get; set; }
        public string RoiDwellSecondsInfo
        {
            get => _roiDwellSecondsInfo;
            set
            {
                if (_roiDwellSecondsInfo != value)
                {
                    _roiDwellSecondsInfo = value;
                    OnPropertyChanged(nameof(RoiDwellSecondsInfo));
                }
            }
        }




        public string EventType { get; set; }
        private string _timeStamp { get; set; }


        public string TimeStamp
        {
            get => _timeStamp;
            set
            {
                if (_timeStamp != value)
                {
                    _timeStamp = value;
                    OnPropertyChanged(nameof(TimeStamp));
                }
            }
        }
        
        private float _confidence { get; set; } // Ä‘á»™ tin cáº­y của káº¿t quáº£ nháº­n diá»‡n, tá»« 0 Ä‘áº¿n 1
        public float Confidence
        {
            get => _confidence;
            set
            {
                if (_confidence != value)
                {
                    _confidence = value;
                    OnPropertyChanged(nameof(Confidence));
                }
            }
        }

        public string MetaType { get; set; }
        public string TrackingObjectIndex { get; set; } // Ä‘á»‹nh danh của Ä‘á»‘i tÆ°á»£ng trong tracking, dÃ¹ng Ä‘á»ƒ phÃ¢n biá»‡t cÃ¡c Ä‘á»‘i tÆ°á»£ng khÃ¡c nhau trong cÃ¹ng má»™t camera, 


        public MetaAIResult(SharpDX.RectangleF boundingBox, string caption = "No Name",
            bool isBlackList = false, bool isDisplay = true, string objectID ="Car 011", 
            string eventType = "appear", string encodeObjectImage=null,
            string timeStamp = "2023-10-01T12:00:00Z", string metaType="face"  , 
            string trackingObjectIndex="001", string cameraInfo="No Info", float confidence = 0.0f)
        {
            MetaType = metaType;
            TrackingObjectIndex = trackingObjectIndex; // Ä‘á»‹nh danh bá»™ tracking for Face, Plate, Coco
            BoundingBox = boundingBox;
           
            IsBlackList = isBlackList;
            IsDisplay = isDisplay;
            ObjectID = objectID; // Äá»‹nh danh của Ä‘á»‘i tÆ°á»£ng Biá»ƒn sá»‘ xe, hoáº·c CCCD
            Caption = caption;

            EncodeObjectImage = encodeObjectImage;
            EventType = eventType;
            TimeStamp = timeStamp;
            CameraInfo = cameraInfo; // camera id
            Confidence = confidence;
        }
        public MetaAIResult()
        {
            TimeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}















