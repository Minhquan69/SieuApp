using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Linq;

namespace V3SClient.models
{
    public class PlateFolderItem : INotifyPropertyChanged
    {
        private string _folderPath;
        private string _folderName;
        private string _plateNumber;
        private int _fileCount;
        private DateTime _lastUpdatedAt;
        private string _status;
        private long _totalSize;
        private bool _hasPlate;
        private ObservableCollection<PlateImageItem> _images;
        private ObservableCollection<PlateImageItem> _outputFiles;
        private string _plateColorCode;
        private string _vehicleColor;

        public PlateFolderItem()
        {
            _images = new ObservableCollection<PlateImageItem>();
            _outputFiles = new ObservableCollection<PlateImageItem>();
        }

        public string FolderPath
        {
            get => _folderPath;
            set { _folderPath = value; OnPropertyChanged(); }
        }

        public string FolderName
        {
            get => _folderName;
            set { _folderName = value; OnPropertyChanged(); }
        }

        public string PlateNumber
        {
            get => _plateNumber;
            set { _plateNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayPlateNumber)); }
        }

        public string DisplayPlateNumber => string.IsNullOrWhiteSpace(_plateNumber)
            ? "CHƯA CÓ BIỂN SỐ"
            : _plateNumber;

        public int FileCount
        {
            get => _fileCount;
            set { _fileCount = value; OnPropertyChanged(); }
        }

        public DateTime LastUpdatedAt
        {
            get => _lastUpdatedAt;
            set { _lastUpdatedAt = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public long TotalSize
        {
            get => _totalSize;
            set { _totalSize = value; OnPropertyChanged(); }
        }

        public bool HasPlate
        {
            get => _hasPlate;
            set { _hasPlate = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PlateImageItem> Images
        {
            get => _images;
            set { _images = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PlateImageItem> OutputFiles
        {
            get => _outputFiles;
            set { _outputFiles = value; OnPropertyChanged(); }
        }

        public int RequiredImageCount => 3;

        public int ReceivedRequiredImageCount => Images.Count(i => i.HasImage &&
            (i.CaptureRole == "LicensePlate" || i.CaptureRole == "Front" || i.CaptureRole == "Rear"));

        public string CompletenessText => ReceivedRequiredImageCount >= RequiredImageCount
            ? "Đủ ảnh "
            : $"Thiếu {RequiredImageCount - ReceivedRequiredImageCount} ảnh bắt buộc";

        public string PlateColorCode
        {
            get => _plateColorCode;
            set
            {
                _plateColorCode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlateBackground));
                OnPropertyChanged(nameof(PlateForeground));
                OnPropertyChanged(nameof(PlateColorName));
            }
        }

        public string VehicleColor
        {
            get => string.IsNullOrWhiteSpace(_vehicleColor) ? "Chưa có dữ liệu" : _vehicleColor;
            set { _vehicleColor = value; OnPropertyChanged(); }
        }

        public Brush PlateBackground
        {
            get
            {
                switch ((_plateColorCode ?? string.Empty).ToUpperInvariant())
                {
                    case "X": return new SolidColorBrush(Color.FromRgb(29, 78, 216));
                    case "V": return new SolidColorBrush(Color.FromRgb(245, 197, 66));
                    case "T": return new SolidColorBrush(Color.FromRgb(244, 244, 244));
                    case "D": return new SolidColorBrush(Color.FromRgb(185, 28, 28));
                    default: return new SolidColorBrush(Color.FromRgb(48, 42, 36));
                }
            }
        }

        public Brush PlateForeground =>
            string.Equals(_plateColorCode, "V", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_plateColorCode, "T", StringComparison.OrdinalIgnoreCase)
                ? Brushes.Black
                : Brushes.White;

        public string PlateColorName
        {
            get
            {
                switch ((_plateColorCode ?? string.Empty).ToUpperInvariant())
                {
                    case "X": return "Biển xanh";
                    case "V": return "Biển vàng";
                    case "T": return "Biển trắng";
                    case "D": return "Biển đỏ";
                    default: return "Chưa xác định màu biển";
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
