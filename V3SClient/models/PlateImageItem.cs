using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace V3SClient.models
{
    public class PlateImageItem : INotifyPropertyChanged
    {
        private string _filePath;
        private string _fileName;
        private string _captureRole;
        private double _confidence;
        private DateTime _captureTime;
        private string _camId;
        private string _plate;
        private BitmapImage _thumbnail;
        private string _errorMessage;
        private long _fileSize;

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public string CaptureRole
        {
            get => _captureRole;
            set { _captureRole = value; OnPropertyChanged(); }
        }

        public double Confidence
        {
            get => _confidence;
            set { _confidence = value; OnPropertyChanged(); }
        }

        public DateTime CaptureTime
        {
            get => _captureTime;
            set { _captureTime = value; OnPropertyChanged(); }
        }

        public string CamId
        {
            get => _camId;
            set { _camId = value; OnPropertyChanged(); }
        }

        public string Plate
        {
            get => _plate;
            set { _plate = value; OnPropertyChanged(); }
        }

        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public long FileSize
        {
            get => _fileSize;
            set { _fileSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeText)); }
        }

        public string SizeText
        {
            get
            {
                if (_fileSize <= 0) return string.Empty;
                if (_fileSize >= 1024L * 1024L) return $"{_fileSize / (1024d * 1024d):0.##} MB";
                if (_fileSize >= 1024L) return $"{_fileSize / 1024d:0.##} KB";
                return $"{_fileSize} B";
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        public bool HasImage => _thumbnail != null;

        public string DisplayRole
        {
            get
            {
                switch ((_captureRole ?? string.Empty).ToLowerInvariant())
                {
                    case "licenseplate": return "Biển số xe";
                    case "front": return "Trước xe";
                    case "rear": return "Sau xe";
                    case "cargobox": return "Thùng xe";
                    case "cabin": return "Khoang lái";
                    case "undercarriage": return "Gầm xe";
                    default: return string.IsNullOrWhiteSpace(_captureRole) ? "Chưa phân loại" : _captureRole;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
