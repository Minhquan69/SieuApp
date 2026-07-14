using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using V3SClient.libs;

namespace V3SClient.Services
{
    public class FileWatcherService : IDisposable
    {
        private FileSystemWatcher _watcher;
        private readonly ProcessingQueue _queue;
        private readonly DatabaseHelper _dbHelper;
        private readonly string _watchPath;
        private readonly string _fileFilter;
        private readonly string _documentType;
        private readonly int _scanHoursBack;
        private readonly bool _enableFolderMode;
        private readonly double _confidenceThreshold;
        private readonly int _folderDebounceMs;
        private static readonly HashSet<string> SupportedImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif"
        };
        private static readonly HashSet<string> SupportedPdfExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf"
        };
        private static readonly PdfPageImageExtractionService PdfPageExtractor = new PdfPageImageExtractionService();

        // Debounce: theo dõi folder đang chờ xử lý, tránh enqueue trùng
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingFolders
            = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);

        public FileWatcherService(ProcessingQueue queue, DatabaseHelper dbHelper, string watchPath, string documentType, string fileFilter = "*.*", int scanHoursBack = 24, bool enableFolderMode = false, double confidenceThreshold = 0.80, int folderDebounceMs = 3000)
        {
            _queue = queue;
            _dbHelper = dbHelper;
            _watchPath = watchPath;
            _documentType = documentType;
            _fileFilter = fileFilter;
            _scanHoursBack = scanHoursBack;
            _enableFolderMode = enableFolderMode;
            _confidenceThreshold = confidenceThreshold;
            _folderDebounceMs = folderDebounceMs;
        }

        public void Start()
        {
            if (!Directory.Exists(_watchPath))
            {
                Directory.CreateDirectory(_watchPath);
            }

            _watcher = new FileSystemWatcher(_watchPath, _fileFilter)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.DirectoryName,
                IncludeSubdirectories = _enableFolderMode,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileOrDirCreated;
            
            LoggerManager.LogInfo($"Bắt đầu theo dõi thư mục: {_watchPath} (FolderMode={_enableFolderMode})");
            
            // Scan existing files/folders on startup
            ScanExistingFiles();
            if (_enableFolderMode)
            {
                ScanExistingSubdirectories();
            }
        }

        private void ScanExistingFiles()
        {
            try
            {
                var files = Directory.GetFiles(_watchPath, _fileFilter);
                
                foreach (var file in files)
                {
                    // Partitioning: Chỉ quét các file trong khoảng thời gian cấu hình
                        
                    // Bỏ qua file đã xử lý thành công

                    EnqueueFileSafely(file);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi quét thư mục ban đầu", ex);
            }
        }

        private void ExtractPdfAndEnqueuePages(string pdfPath)
        {
            Task.Run(async () =>
            {
                await Task.Delay(500);

                int retries = 5;
                while (retries > 0)
                {
                    if (IsFileReady(pdfPath))
                    {
                        try
                        {
                            var pageImages = await PdfPageExtractor.ExtractPagesAsync(pdfPath, CancellationToken.None).ConfigureAwait(false);
                            if (pageImages.Count == 0)
                            {
                                LoggerManager.LogWarn($"PDF khong co trang de trich xuat: {pdfPath}");
                                return;
                            }

                            LoggerManager.LogInfo($"Da trich xuat {pageImages.Count} trang PDF thanh anh: {pdfPath}");
                            foreach (var imagePath in pageImages)
                            {
                                EnqueueFileSafely(imagePath);
                            }
                            DocumentProcessingManager.Instance.RaiseProcessingUpdated();
                        }
                        catch (Exception ex)
                        {
                            LoggerManager.LogError($"Khong the trich xuat PDF thanh anh: {pdfPath}", ex);
                        }
                        return;
                    }

                    retries--;
                    await Task.Delay(1000);
                }

                LoggerManager.LogWarn($"PDF bi khoa khong the doc duoc sau nhieu lan thu: {pdfPath}");
            });
        }

        /// <summary>
        /// Scan các thư mục con hiện có (Mode B) khi khởi động
        /// </summary>
        private void ScanExistingSubdirectories()
        {
            try
            {
                // Hỗ trợ cả cấu trúc plate/file và yyyy-MM-dd/plate/file.
                foreach (var dir in Directory.EnumerateDirectories(_watchPath, "*", SearchOption.AllDirectories))
                {

                    // Kiểm tra folder có file JSON không (dấu hiệu Mode B)
                    if (!IsPlateFolder(dir))
                        continue;


                    EnqueueFolderSafely(dir);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi quét thư mục con ban đầu", ex);
            }
        }

        private void OnFileOrDirCreated(object sender, FileSystemEventArgs e)
        {
            if (!_enableFolderMode)
            {
                // Mode A only: chỉ xử lý file rời ở root
                if (Directory.Exists(e.FullPath))
                    return;

                if (!IsInSubdirectory(e.FullPath))
                {
                    EnqueueFileSafely(e.FullPath);
                }
                return;
            }

            // Folder mode: chỉ enqueue thư mục lá thực sự có JSON nhận dạng.
            // Không enqueue thư mục nhóm theo ngày do ImageSync tạo ra.
            if (Directory.Exists(e.FullPath))
            {
                LoggerManager.LogInfo($"Phát hiện thư mục mới: {e.FullPath}");
                if (IsPlateFolder(e.FullPath))
                    EnqueueFolderSafely(e.FullPath);
            }
            else if (File.Exists(e.FullPath))
            {
                if (IsInSubdirectory(e.FullPath))
                {
                    if (!IsSupportedImageFile(e.FullPath) &&
                        !Path.GetExtension(e.FullPath).Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(e.FullPath).Equals(ImageSyncService.SyncReadyFileName, StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(e.FullPath).Equals(ImageSyncService.SyncInProgressFileName, StringComparison.OrdinalIgnoreCase))
                        return;

                    // File mới trong thư mục nhiều cấp → xử lý đúng thư mục trực tiếp chứa file.
                    string subfolderPath = GetParentSubfolder(e.FullPath);
                    if (subfolderPath != null)
                    {
                        string fileName = Path.GetFileName(e.FullPath);
                        if (fileName.Equals(ImageSyncService.SyncInProgressFileName, StringComparison.OrdinalIgnoreCase))
                            return;

                        if (fileName.Equals(ImageSyncService.SyncReadyFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(e.FullPath); } catch { }
                        }

                        EnqueueFolderSafely(subfolderPath);
                    }
                }
                else
                {
                    // File rời ở root → Mode A
                    EnqueueFileSafely(e.FullPath);
                }
            }
        }

        /// <summary>
        /// Kiểm tra path có nằm trong subdirectory (so với _watchPath) không
        /// </summary>
        private bool IsInSubdirectory(string fullPath)
        {
            string relativePath = fullPath.Substring(_watchPath.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
            return relativePath.Contains(Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Lấy thư mục trực tiếp chứa file (thư mục biển số), không ép về cấp 1.
        /// </summary>
        private string GetParentSubfolder(string filePath)
        {
            try
            {
                string parent = Path.GetDirectoryName(filePath);
                if (string.IsNullOrWhiteSpace(parent)) return null;

                string fullRoot = Path.GetFullPath(_watchPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string fullParent = Path.GetFullPath(parent)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                return fullParent.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
                    ? parent
                    : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Kiểm tra folder có phải là plate folder (có JSON metadata) không
        /// </summary>
        private bool IsPlateFolder(string folderPath)
        {
            try
            {
                if (File.Exists(Path.Combine(folderPath, ImageSyncService.SyncInProgressFileName)))
                    return false;
                return Directory.GetFiles(folderPath, "*.json").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private void EnqueueFileSafely(string filePath)
        {
            // Chỉ ảnh được enqueue độc lập. JSON chỉ là metadata trong folder biển số.
            if (IsSupportedPdfFile(filePath))
            {
                ExtractPdfAndEnqueuePages(filePath);
                return;
            }

            if (!IsSupportedImageFile(filePath))
                return;

            Task.Run(async () =>
            {
                // Đợi một chút để file kịp được copy hoàn toàn vào hệ thống
                await Task.Delay(500); 
                
                // Kiểm tra file bị lock (nếu có process khác đang ghi)
                int retries = 5;
                while (retries > 0)
                {
                    if (IsFileReady(filePath))
                    {
                        long jobId = _dbHelper.RegisterJob(filePath, false, _documentType, 1);
                        _dbHelper.RegisterJobAsset(jobId, filePath);
                        if (!_dbHelper.TryMarkJobQueued(jobId)) return;
                        var task = new ProcessingTask
                        {
                            JobId = jobId,
                            FilePath = filePath,
                            FileType = _documentType,
                            RetryCount = 0
                        };
                        _queue.Enqueue(task);
                        DocumentProcessingManager.Instance.RaiseProcessingUpdated();
                        return;
                    }
                    retries--;
                    await Task.Delay(1000);
                }
                
                LoggerManager.LogWarn($"File bị khóa không thể đọc được sau nhiều lần thử: {filePath}");
            });
        }

        /// <summary>
        /// Enqueue cả thư mục biển số với debounce.
        /// Đợi _folderDebounceMs sau lần thay đổi cuối để đảm bảo tất cả file đã copy xong.
        /// </summary>
        private void EnqueueFolderSafely(string folderPath)
        {
            // Cancel pending debounce nếu có (reset timer)
            if (_pendingFolders.TryRemove(folderPath, out var existingCts))
            {
                CancelAndDispose(existingCts);
            }

            var cts = new CancellationTokenSource();
            _pendingFolders[folderPath] = cts;

            Task.Run(async () =>
            {
                try
                {
                    // Debounce: đợi để tất cả file trong folder được ghi xong
                    await Task.Delay(_folderDebounceMs, cts.Token);

                    if (File.Exists(Path.Combine(folderPath, ImageSyncService.SyncInProgressFileName)))
                    {
                        LoggerManager.LogDebug($"Folder đang được image sync ghi dữ liệu, tạm hoãn: {folderPath}");
                        return;
                    }

                    // Đợi thêm để đảm bảo file không bị lock
                    if (!await WaitForFolderReady(folderPath, cts.Token))
                    {
                        LoggerManager.LogWarn($"Folder chưa sẵn sàng sau nhiều lần thử: {folderPath}");
                        return;
                    }

                    // Parse JSON files trong folder
                    var recognitionDataList = ParseFolderJsonFiles(folderPath);
                    if (recognitionDataList.Count == 0)
                    {
                        LoggerManager.LogWarn($"Không tìm thấy dữ liệu nhận dạng trong folder: {folderPath}");
                        return;
                    }

                    // Lấy biển số và confidence cao nhất
                    var bestData = recognitionDataList.OrderByDescending(d => d.Confidence).First();
                    int requiredAssetCount = recognitionDataList.Count +
                        recognitionDataList.Count(data => !string.IsNullOrWhiteSpace(data.PlateImagePath));
                    long jobId = _dbHelper.RegisterJob(folderPath, true, _documentType, requiredAssetCount);
                    foreach (var data in recognitionDataList)
                    {
                        if (!string.IsNullOrWhiteSpace(data.ImagePath))
                            _dbHelper.RegisterJobAsset(jobId, data.ImagePath, data.CaptureRole);
                        if (!string.IsNullOrWhiteSpace(data.PlateImagePath))
                            _dbHelper.RegisterJobAsset(jobId, data.PlateImagePath, "LicensePlate");
                    }
                    if (!_dbHelper.TryMarkJobQueued(jobId)) return;

                    var task = new ProcessingTask
                    {
                        JobId = jobId,
                        IsFolderTask = true,
                        FolderPath = folderPath,
                        FilePath = folderPath, // Dùng folder path cho DB tracking
                        FileType = _documentType,
                        PreExtractedPlate = bestData.Plate,
                        PlateConfidence = bestData.Confidence,
                        RecognitionDataList = recognitionDataList,
                        RetryCount = 0
                    };

                    _queue.Enqueue(task);
                    DocumentProcessingManager.Instance.RaiseProcessingUpdated();
                    LoggerManager.LogInfo($"Đã enqueue folder: {folderPath} (plate={bestData.Plate}, conf={bestData.Confidence:P1})");
                }
                catch (OperationCanceledException)
                {
                    // Debounce bị reset, bỏ qua
                }
                catch (Exception ex)
                {
                    LoggerManager.LogError($"Lỗi khi enqueue folder: {folderPath}", ex);
                }
                finally
                {
                    if (_pendingFolders.TryGetValue(folderPath, out var currentCts) &&
                        ReferenceEquals(currentCts, cts))
                    {
                        _pendingFolders.TryRemove(folderPath, out _);
                    }

                    CancelAndDispose(cts);
                }
            }, cts.Token);
        }

        /// <summary>
        /// Parse tất cả file JSON trong folder để lấy PlateRecognitionData
        /// </summary>
        private List<PlateRecognitionData> ParseFolderJsonFiles(string folderPath)
        {
            var result = new List<PlateRecognitionData>();
            try
            {
                var jsonFiles = Directory.GetFiles(folderPath, "*.json");
                foreach (var jsonFile in jsonFiles)
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(jsonFile);
                        var jObj = JObject.Parse(jsonContent);

                        string baseName = Path.GetFileNameWithoutExtension(jsonFile);
                        string folderDir = Path.GetDirectoryName(jsonFile);

                        // Tìm ảnh tương ứng (FRONT_xxx.jpg hoặc BACK_xxx.jpg)
                        string imagePath = FindMatchingImage(folderDir, baseName);
                        if (imagePath == null) continue;

                        // Tìm ảnh crop biển số (_plate.jpg)
                        string plateImagePath = FindPlateImage(folderDir, baseName);

                        var data = new PlateRecognitionData
                        {
                            ImagePath = imagePath,
                            JsonPath = jsonFile,
                            PlateImagePath = plateImagePath,
                            Plate = jObj["plate"]?.ToString() ?? "",
                            Confidence = jObj["confidence"]?.Value<double>() ?? 0,
                            CaptureRole = jObj["capture_role"]?.ToString() ?? "",
                            CaptureTime = jObj["snapshot_at"]?.ToString() ?? "",
                            CamId = jObj["capture_cam_id"]?.ToString() ?? ""
                        };

                        result.Add(data);
                    }
                    catch (Exception ex)
                    {
                        LoggerManager.LogWarn($"Lỗi parse JSON: {jsonFile} - {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError($"Lỗi khi đọc folder JSON: {folderPath}", ex);
            }
            return result;
        }

        private string FindMatchingImage(string folderDir, string jsonBaseName)
        {
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            foreach (var ext in imageExtensions)
            {
                string candidate = Path.Combine(folderDir, jsonBaseName + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        private string FindPlateImage(string folderDir, string jsonBaseName)
        {
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            foreach (var ext in imageExtensions)
            {
                string candidate = Path.Combine(folderDir, jsonBaseName + "_plate" + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        /// <summary>
        /// Đợi tất cả file trong folder sẵn sàng (không bị lock)
        /// </summary>
        private async Task<bool> WaitForFolderReady(string folderPath, CancellationToken ct)
        {
            int retries = 5;
            while (retries > 0 && !ct.IsCancellationRequested)
            {
                bool allReady = true;
                var files = Directory.GetFiles(folderPath)
                    .Where(file => IsSupportedImageFile(file) ||
                                   Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                                   Path.GetFileName(file).Equals(ImageSyncService.SyncInProgressFileName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                foreach (var file in files)
                {
                    if (!IsFileReady(file))
                    {
                        allReady = false;
                        break;
                    }
                }
                if (allReady) return true;
                retries--;
                await Task.Delay(1000, ct);
            }
            return false;
        }

        private bool IsFileReady(string filename)
        {
            try
            {
                using (FileStream inputStream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return inputStream.Length > 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsSupportedImageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return SupportedImageExtensions.Contains(Path.GetExtension(path));
        }

        private static bool IsSupportedPdfFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return SupportedPdfExtensions.Contains(Path.GetExtension(path));
        }

        public void Stop()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileOrDirCreated;
                _watcher.Dispose();
            }

            // Cancel all pending folder debounces
            foreach (var kvp in _pendingFolders)
            {
                CancelAndDispose(kvp.Value);
            }
            _pendingFolders.Clear();
        }

        public void Dispose()
        {
            Stop();
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
