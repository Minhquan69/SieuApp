using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using V3SClient.libs;
using V3SClient.models;

namespace V3SClient.Services
{
    public class OutputFolderMonitorService : IDisposable
    {
        private readonly string _outputDirectory;
        private readonly DatabaseHelper _dbHelper;
        private FileSystemWatcher _watcher;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
        private readonly int _debounceDelayMs = 500;
        private bool _isDisposed = false;

        public event Action<string> OnFolderChanged;

        public OutputFolderMonitorService(string outputDirectory, DatabaseHelper dbHelper, int debounceDelayMs = 500)
        {
            _outputDirectory = outputDirectory;
            _dbHelper = dbHelper;
            _debounceDelayMs = debounceDelayMs;
        }

        public void Start()
        {
            if (_watcher != null) return;

            if (string.IsNullOrWhiteSpace(_outputDirectory))
            {
                LoggerManager.LogWarn($"Thư mục theo dõi không tồn tại: {_outputDirectory}");
                return;
            }

            if (!Directory.Exists(_outputDirectory))
                Directory.CreateDirectory(_outputDirectory);

            _watcher = new FileSystemWatcher(_outputDirectory)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemEvent;
            _watcher.Changed += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemEvent;
            _watcher.Renamed += OnFileSystemEvent;

            LoggerManager.LogInfo($"Bắt đầu theo dõi Output: {_outputDirectory}");
        }

        public void Stop()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileSystemEvent;
                _watcher.Changed -= OnFileSystemEvent;
                _watcher.Deleted -= OnFileSystemEvent;
                _watcher.Renamed -= OnFileSystemEvent;
                _watcher.Dispose();
                _watcher = null;
            }
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            if (_isDisposed) return;

            string folderPath = ResolveTopLevelOutputFolder(e.FullPath);
            if (string.IsNullOrEmpty(folderPath)) return;

            // Debounce folder changes
            if (_debounceTokens.TryRemove(folderPath, out var existingCts))
            {
                CancelAndDispose(existingCts);
            }

            var cts = new CancellationTokenSource();
            _debounceTokens[folderPath] = cts;
            var token = cts.Token;

            Task.Delay(_debounceDelayMs, token).ContinueWith(t =>
            {
                if (!_isDisposed && !t.IsCanceled)
                {
                    if (_debounceTokens.TryGetValue(folderPath, out var currentCts) &&
                        ReferenceEquals(currentCts, cts))
                    {
                        _debounceTokens.TryRemove(folderPath, out _);
                    }

                    OnFolderChanged?.Invoke(folderPath);
                }
                CancelAndDispose(cts);
            }, TaskScheduler.Default);
        }

        private string ResolveTopLevelOutputFolder(string changedPath)
        {
            if (string.IsNullOrWhiteSpace(changedPath) || string.IsNullOrWhiteSpace(_outputDirectory))
                return null;

            try
            {
                string root = Path.GetFullPath(_outputDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullPath = Path.GetFullPath(changedPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase))
                    return null;

                string relative = fullPath.Length > root.Length
                    ? fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(relative))
                    return null;

                string topLevelName = relative
                    .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

                return string.IsNullOrWhiteSpace(topLevelName) ? null : Path.Combine(root, topLevelName);
            }
            catch (Exception ex)
            {
                LoggerManager.LogError($"Khong the xac dinh thu muc output thay doi: {changedPath}", ex);
                return Directory.Exists(changedPath) ? changedPath : Path.GetDirectoryName(changedPath);
            }
        }

        public List<PlateFolderItem> GetRecentFolders(int limit = 5, string folderSearch = null)
        {
            if (!Directory.Exists(_outputDirectory)) return new List<PlateFolderItem>();

            var result = new List<PlateFolderItem>();
            try
            {
                var folders = Directory.GetDirectories(_outputDirectory)
                                       .Select(d => new DirectoryInfo(d))
                                       .Where(d => string.IsNullOrWhiteSpace(folderSearch) ||
                                                   d.Name.IndexOf(folderSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                                       .OrderByDescending(d => d.LastWriteTime)
                                       .Take(limit)
                                       .ToList();

                foreach (var dir in folders)
                {
                    result.Add(GetFolderDetails(dir.FullName, includeImages: true));
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi quét GetRecentFolders", ex);
            }
            return result;
        }

        public int GetFolderCount(string folderSearch = null)
        {
            if (!Directory.Exists(_outputDirectory)) return 0;
            try
            {
                return Directory.EnumerateDirectories(_outputDirectory)
                    .Select(path => Path.GetFileName(path))
                    .Count(name => string.IsNullOrWhiteSpace(folderSearch) ||
                                   name.IndexOf(folderSearch, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi đếm thư mục output", ex);
                return 0;
            }
        }

        public List<PlateFolderItem> GetFolderPage(int limit, int offset, string folderSearch, string sortOption)
        {
            if (!Directory.Exists(_outputDirectory)) return new List<PlateFolderItem>();
            try
            {
                var query = Directory.EnumerateDirectories(_outputDirectory)
                    .Select(path => new DirectoryInfo(path))
                    .Where(dir => string.IsNullOrWhiteSpace(folderSearch) ||
                                  dir.Name.IndexOf(folderSearch, StringComparison.OrdinalIgnoreCase) >= 0);

                switch (sortOption)
                {
                    case "Cũ nhất": query = query.OrderBy(dir => dir.LastWriteTime); break;
                    case "Tên A → Z": query = query.OrderBy(dir => dir.Name, StringComparer.CurrentCultureIgnoreCase); break;
                    case "Tên Z → A": query = query.OrderByDescending(dir => dir.Name, StringComparer.CurrentCultureIgnoreCase); break;
                    default: query = query.OrderByDescending(dir => dir.LastWriteTime); break;
                }

                return query.Skip(Math.Max(0, offset))
                    .Take(Math.Max(1, limit))
                    .Select(dir => GetFolderDetails(dir.FullName, includeImages: true))
                    .ToList();
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi tải trang thư mục output", ex);
                return new List<PlateFolderItem>();
            }
        }

        public PlateFolderItem GetFolderDetails(string folderPath, bool includeImages = true)
        {
            if (!Directory.Exists(folderPath)) return null;

            var dirInfo = new DirectoryInfo(folderPath);
            string folderName = dirInfo.Name;
            bool isUnknown = folderName.Equals("UnknownPlate", StringComparison.OrdinalIgnoreCase) || 
                             folderName.Equals("Chưa nhận diện", StringComparison.OrdinalIgnoreCase);

            var item = new PlateFolderItem
            {
                FolderPath = folderPath,
                FolderName = folderName,
                PlateNumber = isUnknown ? "" : folderName,
                LastUpdatedAt = dirInfo.LastWriteTime,
                HasPlate = !isUnknown
            };

            try
            {
                var files = dirInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly);
                item.FileCount = files.Length;
                item.TotalSize = files.Sum(f => f.Length);
                item.Status = _dbHelper.GetFolderStatus(folderPath) ?? "Processing";
                item.PlateColorCode = ResolvePlateColor(files.Select(f => f.Name));
                item.VehicleColor = ResolveVehicleColor(files.Select(f => f.Name));

                if (includeImages)
                {
                    var imageItems = new List<PlateImageItem>();
                    foreach (var file in files.OrderByDescending(f => f.LastWriteTime))
                    {
                        var ext = file.Extension.ToLower();
                        if (ext == ".jpg" || ext == ".png" || ext == ".jpeg" || ext == ".bmp")
                        {
                            imageItems.Add(CreateImageItem(file.FullName));
                        }
                    }

                    foreach (var imageItem in imageItems)
                    {
                        item.OutputFiles.Add(imageItem);
                    }

                    string[] orderedRoles = { "LicensePlate", "Front", "Rear", "CargoBox", "Cabin", "Undercarriage" };
                    foreach (var role in orderedRoles)
                    {
                        var image = imageItems.FirstOrDefault(i => string.Equals(i.CaptureRole, role, StringComparison.OrdinalIgnoreCase));
                        item.Images.Add(image ?? new PlateImageItem { CaptureRole = role });
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError($"Lỗi khi lấy chi tiết folder: {folderPath}", ex);
            }

            return item;
        }

        private PlateImageItem CreateImageItem(string filePath)
        {
            var item = new PlateImageItem
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                CaptureTime = File.GetLastWriteTime(filePath),
                FileSize = new FileInfo(filePath).Length
            };

            // Resolve role from filename
            item.CaptureRole = ResolveRoleFromFilename(item.FileName);

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.DecodePixelWidth = 300; // cache giới hạn width để không tràn RAM
                bitmap.EndInit();
                bitmap.Freeze();
                item.Thumbnail = bitmap;
            }
            catch (Exception ex)
            {
                item.ErrorMessage = "Lỗi đọc ảnh";
                LoggerManager.LogError($"Lỗi tạo thumbnail: {filePath}", ex);
            }

            return item;
        }

        private string ResolveRoleFromFilename(string filename)
        {
            var lower = filename.ToLower();
            if (lower.Contains("_plate") || lower.StartsWith("bsx_") || lower.StartsWith("plate_"))
                return "LicensePlate";
            if (lower.Contains("front") || lower.Contains("truoc")) return "Front";
            if (lower.Contains("rear") || lower.Contains("back") || lower.Contains("sau")) return "Rear";
            if (lower.Contains("cargo") || lower.Contains("thung_xe")) return "CargoBox";
            if (lower.Contains("cabin") || lower.Contains("khoang_lai")) return "Cabin";
            if (lower.Contains("under") || lower.Contains("gam_xe")) return "Undercarriage";

            var roleTokens = Path.GetFileNameWithoutExtension(lower)
                .Replace('-', '_')
                .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (roleTokens.Skip(1).Contains("bsx")) return "LicensePlate";
            
            return "Khác";
        }

        private string ResolvePlateColor(IEnumerable<string> fileNames)
        {
            foreach (var fileName in fileNames)
            {
                var normalized = Path.GetFileNameWithoutExtension(fileName)
                    .Replace('-', '_')
                    .Replace(' ', '_')
                    .ToUpperInvariant();
                var tokens = normalized.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Contains("X") || normalized.Contains("XANH")) return "X";
                if (tokens.Contains("V") || normalized.Contains("VANG")) return "V";
                if (tokens.Contains("T") || normalized.Contains("TRANG")) return "T";
                if (tokens.Contains("D") || normalized.Contains("DO")) return "D";
            }

            return string.Empty;
        }

        private string ResolveVehicleColor(IEnumerable<string> fileNames)
        {
            string combined = string.Join("_", fileNames).ToLowerInvariant();
            if (combined.Contains("mau_xe_den") || combined.Contains("_den_")) return "Đen";
            if (combined.Contains("mau_xe_trang") || combined.Contains("_trang_")) return "Trắng";
            if (combined.Contains("mau_xe_do") || combined.Contains("_do_")) return "Đỏ";
            if (combined.Contains("mau_xe_xam") || combined.Contains("_xam_")) return "Xám";
            if (combined.Contains("mau_xe_bac") || combined.Contains("_bac_")) return "Bạc";
            if (combined.Contains("mau_xe_xanh") || combined.Contains("_xanh_")) return "Xanh";
            return "Chưa có dữ liệu";
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                Stop();
                foreach (var cts in _debounceTokens.Values)
                {
                    CancelAndDispose(cts);
                }
                _debounceTokens.Clear();
            }
        }

        private static void CancelAndDispose(CancellationTokenSource cts)
        {
            if (cts == null) return;

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }
}
