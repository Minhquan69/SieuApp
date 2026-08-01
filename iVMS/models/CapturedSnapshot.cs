using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace V3SClient.models
{
    public class CapturedSnapshot : INotifyPropertyChanged
    {
        private bool _isSaving;
        private string _saveError;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FilePath { get; set; }
        public string FileName { get; set; }
        public ImageSource PreviewImage { get; set; }
        public DateTime CapturedAt { get; set; }

        public bool IsSaving
        {
            get { return _isSaving; }
            set
            {
                if (_isSaving == value)
                    return;

                _isSaving = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string SaveError
        {
            get { return _saveError; }
            set
            {
                if (_saveError == value)
                    return;

                _saveError = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string StatusText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(SaveError))
                    return "Lưu lỗi";
                return IsSaving ? "Đang lưu..." : "Đã lưu";
            }
        }

        public static CapturedSnapshot FromFrame(string filePath, ImageSource previewImage, DateTime capturedAt)
        {
            return new CapturedSnapshot
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                PreviewImage = previewImage,
                CapturedAt = capturedAt
            };
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
