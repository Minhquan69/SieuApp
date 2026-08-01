using System;
using System.IO;

namespace V3SClient.models
{
    public class ProcessingJobStatusItem
    {
        public long Id { get; set; }
        public string SourcePath { get; set; }
        public string SourceType { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
        public long FileSize { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsFolder { get; set; }
        public string LicensePlate { get; set; }
        public string OutputFolder { get; set; }
        public string DisplayFilePath { get; set; }
        public string DisplayOutputFilePath { get; set; }

        public string FileName
        {
            get
            {
                string path = string.IsNullOrWhiteSpace(DisplayFilePath) ? SourcePath : DisplayFilePath;
                if (string.IsNullOrWhiteSpace(path)) return string.Empty;
                return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }

        public string PlateDisplay => IsSuccess && !string.IsNullOrWhiteSpace(LicensePlate)
            ? LicensePlate
            : "-";

        public string SizeText
        {
            get
            {
                if (FileSize <= 0) return IsFolder ? "Thư mục" : "—";
                if (FileSize >= 1024L * 1024L * 1024L) return $"{FileSize / (1024d * 1024d * 1024d):0.##} GB";
                if (FileSize >= 1024L * 1024L) return $"{FileSize / (1024d * 1024d):0.##} MB";
                if (FileSize >= 1024L) return $"{FileSize / 1024d:0.##} KB";
                return $"{FileSize} B";
            }
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case "Discovered": return "Đang xử lý";
                    case "Queued": return "Đang xử lý";
                    case "Processing": return "Đang xử lý";
                    case "RetryPending": return "Đang xử lý";
                    case "Rejected": return "Lỗi";
                    case "Failed": return "Lỗi";
                    case "Completed": return "Thành công";
                    default: return Status ?? string.Empty;
                }
            }
        }

        public bool IsError => Status == "Failed" || Status == "Rejected";
        public bool IsSuccess => Status == "Completed";
        public string ResultText => IsSuccess
            ? (string.IsNullOrWhiteSpace(LicensePlate) ? "Đã xử lý" : LicensePlate)
            : (IsError ? "Thất bại" : "—");
        public string FolderToOpen => IsSuccess && !string.IsNullOrWhiteSpace(OutputFolder)
            ? OutputFolder
            : (IsFolder ? SourcePath : Path.GetDirectoryName(SourcePath));
        public bool CanManualProcess => Status == "Failed" && !IsFolder &&
                                        !string.Equals(SourceType, "Giấy tờ xe", StringComparison.OrdinalIgnoreCase);

        public string FriendlyErrorMessage
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ErrorMessage)) return string.Empty;
                string firstLine = ErrorMessage.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                if (firstLine.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Dịch vụ xử lý phản hồi quá thời gian.";
                if (firstLine.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 || firstLine.Contains("401"))
                    return "Thông tin xác thực dịch vụ không hợp lệ.";
                if (firstLine.Length > 120)
                    return firstLine.Substring(0, 117) + "...";
                return firstLine;
            }
        }
    }
}
