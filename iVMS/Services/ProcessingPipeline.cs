using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json;
using V3SClient.libs;
using VehicleDocumentProcessing.WPF.Services;

namespace V3SClient.Services
{
    public class ProcessingPipeline : IDisposable
    {
        private readonly ProcessingQueue _queue;
        private readonly LLMClient _llmClient;
        private readonly DatabaseHelper _dbHelper;
        private readonly FileNamingService _fileNamingService;
        private readonly string _outputDirectoryPlate;
        private readonly string _outputDirectoryDocument;
        private readonly ProcessingMode _processingMode;
        private readonly int _maxParallelProcessingJobs;
        private static readonly HashSet<string> SupportedImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif"
        };
        private CancellationTokenSource _cts;
        private Task[] _workerTasks = new Task[0];
        private int _maxRetries = 3;
        private int _retryWaitTime = 5;
        
        private bool _isCopyMode = false;
        private double _confidenceThreshold = 0.80;
        private int _processingCount = 0;
        public int ProcessingCount => _processingCount;

        private void EmitStatusEvent(string currentItemPath = "", string currentFolderPath = "")
        {
            var stats = _dbHelper.GetOverviewStatistics();
            var args = new ProcessingStatusChangedEventArgs
            {
                PendingCount = _queue.PendingCount,
                ProcessingCount = _processingCount,
                SuccessCount = stats.success,
                ErrorCount = stats.error,
                UnknownPlateCount = stats.unknownPlate,
                CurrentItemPath = currentItemPath,
                CurrentFolderPath = currentFolderPath,
                LastUpdatedAt = DateTime.Now
            };
            DocumentProcessingManager.Instance.RaiseProcessingStatusChanged(args);
            DocumentProcessingManager.Instance.RaiseProcessingUpdated();
        }

        public ProcessingPipeline(ProcessingQueue queue, LLMClient llmClient, DatabaseHelper dbHelper, FileNamingService fileNamingService, 
            string outputDirectoryPlate, string outputDirectoryDocument, int maxRetries = 3, int retryWaitTime = 5,
            bool isCopyMode = false, double confidenceThreshold = 0.80,
            ProcessingMode processingMode = ProcessingMode.Combined,
            int maxParallelProcessingJobs = 2)
        {
            _queue = queue;
            _llmClient = llmClient;
            _dbHelper = dbHelper;
            _fileNamingService = fileNamingService;
            _outputDirectoryPlate = outputDirectoryPlate;
            _outputDirectoryDocument = outputDirectoryDocument;
            _maxRetries = maxRetries;
            _retryWaitTime = retryWaitTime;
            _isCopyMode = isCopyMode;
            _confidenceThreshold = confidenceThreshold;
            _processingMode = processingMode;
            _maxParallelProcessingJobs = Math.Max(1, Math.Min(8, maxParallelProcessingJobs));
        }

        public void Start()
        {
            // Dispose CTS cũ nếu có để tránh leak
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _workerTasks = new Task[_maxParallelProcessingJobs];
            for (int i = 0; i < _workerTasks.Length; i++)
            {
                _workerTasks[i] = Task.Run(() => ProcessLoop(_cts.Token), _cts.Token);
            }
            LoggerManager.LogInfo($"ProcessingPipeline da khoi dong voi {_maxParallelProcessingJobs} luong xu ly.");
        }

        private async Task ProcessLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var task = _queue.Dequeue(cancellationToken);
                if (task == null) continue;

                await ProcessSingleTaskAsync(task);
            }
        }

        private async Task ProcessSingleTaskAsync(ProcessingTask task)
        {
            if (!task.IsFolderTask && !SupportedImageExtensions.Contains(Path.GetExtension(task.FilePath)))
            {
                if (task.JobId > 0)
                    _dbHelper.MarkJobIgnored(task.JobId, "Định dạng không phải ảnh được hỗ trợ");
                LoggerManager.LogDebug($"Bỏ qua định dạng không hỗ trợ: {task.FilePath}");
                EmitStatusEvent(task.FilePath);
                return;
            }

            if (task.IsFolderTask)
            {
                await ProcessFolderTaskAsync(task);
                return;
            }

            if (task.JobId > 0)
            {
                _dbHelper.MarkJobProcessing(task.JobId);
                if (_dbHelper.IsAssetCompleted(task.JobId, task.FilePath))
                {
                    _dbHelper.CompleteJobIfReady(task.JobId, string.Empty, string.Empty);
                    if (!_isCopyMode) DeleteSourceAfterCommit(task.FilePath);
                    return;
                }
            }

            LoggerManager.LogInfo($"Đang xử lý file: {task.FilePath}");
            Interlocked.Increment(ref _processingCount);
            EmitStatusEvent(task.FilePath);

            try
            {
                if (!File.Exists(task.FilePath))
                {
                    LoggerManager.LogWarn($"File không tồn tại: {task.FilePath}");
                    return;
                }

                // 1. Phân loại và OCR trong 1 bước (Unified Call)
                var result = await _llmClient.ProcessDocumentAsync(task.FilePath);
                string docType = result?.LoaiGiayTo ?? "Không xác định";
                string licensePlate = result?.BienSo ?? "";
                string manualPlate = task.JobId > 0 ? _dbHelper.GetManualPlate(task.JobId) : string.Empty;
                if (!string.IsNullOrWhiteSpace(manualPlate))
                {
                    licensePlate = manualPlate.Trim().ToUpperInvariant();
                    docType = "Ảnh xe ô tô";
                    LoggerManager.LogInfo($"Sử dụng biển số xác nhận thủ công cho job {task.JobId}: {licensePlate}");
                }
                
                var extraFields = new System.Collections.Generic.Dictionary<string, string>();
                extraFields["mau_bien_so"] = result?.MauBienSo ?? "";
                extraFields["loai_xe"] = result?.LoaiXe ?? "";
                extraFields["mau_xe"] = result?.MauXe ?? "";
                extraFields["goc_chup"] = result?.GocChup ?? "";
                extraFields["manual_plate"] = manualPlate ?? string.Empty;
                if (result?.AdditionalData != null)
                {
                    foreach (var kvp in result.AdditionalData)
                    {
                        extraFields[kvp.Key] = kvp.Value?.ToString() ?? "";
                    }
                }

                // Truyền thêm thuộc tính phân biệt nguồn
                extraFields["SourceType"] = task.FileType;
                extraFields["source_timestamp"] = File.GetLastWriteTime(task.FilePath).ToString("HHmmss");

                // 2. Quyết định thư mục Output (Luôn 2 thư mục Output theo cấu trúc)
                // - Ảnh xe -> OutputDirectoryPlate
                // - Giấy tờ, Đơn, CCCD... -> OutputDirectoryDocument
                bool isVehiclePhoto = (docType == "Ảnh xe ô tô");
                string rejectedReason;
                if (!IsAllowedByMode(isVehiclePhoto, task.FileType, out rejectedReason))
                {
                    RejectTask(task, docType, rejectedReason);
                    EmitStatusEvent(task.FilePath);
                    return;
                }
                string targetOutputDirectory = isVehiclePhoto ? _outputDirectoryPlate : _outputDirectoryDocument;

                // 3. Đổi tên và di chuyển file
                string newFilePath = _fileNamingService.ProcessAndMoveFile(task.FilePath, docType, licensePlate, targetOutputDirectory,
                    isVehiclePhoto, task.JobId > 0 ? true : _isCopyMode, extraFields);
                
                if (newFilePath != null)
                {
                    string processedPlate = _fileNamingService.GetProcessedLicensePlate(licensePlate, isVehiclePhoto);
                    // 4. Lưu thông tin vào Database với cả path cũ và mới
                    _dbHelper.InsertRecord(task.FilePath, newFilePath, "Thành công", docType, processedPlate);
                    if (task.JobId > 0)
                    {
                        _dbHelper.MarkAssetCompleted(task.JobId, task.FilePath, newFilePath, extraFields["goc_chup"]);
                        _dbHelper.CompleteJobIfReady(task.JobId, processedPlate, Path.GetDirectoryName(newFilePath));
                        if (!_isCopyMode) DeleteSourceAfterCommit(task.FilePath);
                    }
                    LoggerManager.LogInfo($"Xử lý thành công file: {task.FilePath} -> {newFilePath}");
                    EmitStatusEvent(task.FilePath);
                }
                else
                {
                    if (task.JobId > 0) _dbHelper.MarkJobFailed(task.JobId, "Không thể di chuyển hoặc đổi tên file");
                    _dbHelper.InsertRecord(task.FilePath, task.FilePath, "Lỗi khi di chuyển/đổi tên file");
                    LoggerManager.LogWarn($"Không thể di chuyển/đổi tên file: {task.FilePath}");
                    EmitStatusEvent(task.FilePath);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError($"Lỗi khi xử lý file: {task.FilePath}", ex);
                string friendlyError = GetFriendlyError(ex);
                
                if (task.RetryCount < _maxRetries)
                {
                    task.RetryCount++;
                    if (task.JobId > 0) _dbHelper.MarkJobRetryPending(task.JobId, friendlyError);
                    LoggerManager.LogInfo($"Thử lại file {task.FilePath} (Lần {task.RetryCount}) sau {_retryWaitTime} giây");
                    
                    // Chạy ngầm chờ và đẩy lại vào queue để không block luồng xử lý chính
                    // Truyền CancellationToken để huỷ khi pipeline Stop
                    var token = _cts.Token;
                    Task.Run(async () => 
                    {
                        try
                        {
                            await Task.Delay(_retryWaitTime * 1000, token);
                            _queue.Enqueue(task);
                        }
                        catch (OperationCanceledException) { /* Pipeline đã Stop, bỏ qua retry */ }
                    }, token);
                }
                else
                {
                    if (task.JobId > 0) _dbHelper.MarkJobFailed(task.JobId, friendlyError);
                    LoggerManager.LogError($"File {task.FilePath} đã lỗi {_maxRetries} lần. Dừng xử lý.");
                    _dbHelper.InsertRecord(task.FilePath, Path.GetFileName(task.FilePath), "Lỗi xử lý");
                    EmitStatusEvent(task.FilePath);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _processingCount);
                EmitStatusEvent();
            }
        }

        private async Task ProcessFolderTaskAsync(ProcessingTask task)
        {
            if (task.JobId > 0) _dbHelper.MarkJobProcessing(task.JobId);
            LoggerManager.LogInfo($"Đang xử lý folder: {task.FolderPath}");
            Interlocked.Increment(ref _processingCount);
            EmitStatusEvent("", task.FolderPath);

            try
            {
                if (_processingMode == ProcessingMode.DocumentOnly)
                {
                    RejectTask(task, "Ảnh xe ô tô", "Chế độ hiện tại chỉ xử lý giấy tờ xe");
                    EmitStatusEvent("", task.FolderPath);
                    return;
                }

                // 1. Xác định biển số chính thức
                string finalPlate;
                if (task.PlateConfidence >= _confidenceThreshold)
                {
                    // Confidence cao → dùng trực tiếp
                    finalPlate = task.PreExtractedPlate;
                    LoggerManager.LogInfo($"Biển số từ JSON (conf={task.PlateConfidence:P1}): {finalPlate}");
                }
                else
                {
                    // Confidence thấp → gọi LLM confirm
                    // Tìm ảnh crop biển số tốt nhất (confidence cao nhất)
                    var bestData = System.Linq.Enumerable.First(System.Linq.Enumerable.OrderByDescending(task.RecognitionDataList, d => d.Confidence));
                    if (!string.IsNullOrEmpty(bestData.PlateImagePath) && File.Exists(bestData.PlateImagePath))
                    {
                        finalPlate = await _llmClient.ConfirmPlateAsync(bestData.PlateImagePath, task.PreExtractedPlate);
                        LoggerManager.LogInfo($"Biển số confirm từ LLM: {finalPlate}");
                    }
                    else
                    {
                        finalPlate = task.PreExtractedPlate;
                        LoggerManager.LogWarn($"Không tìm thấy ảnh crop biển số, dùng biển số gợi ý: {finalPlate}");
                    }
                }

                // 2. Xử lý từng ảnh trong folder
                foreach (var data in task.RecognitionDataList)
                {
                    if (task.JobId > 0 && _dbHelper.IsAssetCompleted(task.JobId, data.ImagePath))
                    {
                        if (!string.IsNullOrWhiteSpace(data.PlateImagePath) &&
                            File.Exists(data.PlateImagePath) &&
                            !_dbHelper.IsAssetCompleted(task.JobId, data.PlateImagePath))
                        {
                            var recoveryFields = new System.Collections.Generic.Dictionary<string, string>
                            {
                                ["goc_chup"] = "bsx",
                                ["SourceType"] = task.FileType,
                                ["source_timestamp"] = File.GetLastWriteTime(data.PlateImagePath).ToString("HHmmss")
                            };
                            string recoveredPlatePath = _fileNamingService.ProcessAndMoveFile(
                                data.PlateImagePath, "Ảnh xe ô tô", finalPlate, _outputDirectoryPlate,
                                true, true, recoveryFields);
                            if (recoveredPlatePath != null)
                            {
                                string recoveredPlate = _fileNamingService.GetProcessedLicensePlate(finalPlate, true);
                                _dbHelper.InsertRecord(data.PlateImagePath, recoveredPlatePath, "Thành công", "Ảnh crop biển số", recoveredPlate);
                                _dbHelper.MarkAssetCompleted(task.JobId, data.PlateImagePath, recoveredPlatePath, "LicensePlate");
                                if (!_isCopyMode) DeleteSourceAfterCommit(data.PlateImagePath);
                            }
                        }
                        if (!_isCopyMode) DeleteSourceAfterCommit(data.ImagePath);
                        continue;
                    }
                    if (!File.Exists(data.ImagePath))
                        throw new FileNotFoundException("Thiếu ảnh nguồn chưa được ghi nhận hoàn thành", data.ImagePath);

                    LoggerManager.LogInfo($"Đang xử lý file trong folder: {data.ImagePath}");
                    
                    // Gọi LLM với prompt tăng cường (có hints)
                    var result = await _llmClient.ProcessDocumentWithHintsAsync(data.ImagePath, data);
                    
                    // Override biển số bằng finalPlate (đã xác nhận)
                    result.BienSo = finalPlate;
                    
                    // Map capture_role → goc_chup từ JSON (đáng tin cậy hơn LLM)
                    if (!string.IsNullOrEmpty(data.CaptureRole))
                    {
                        result.GocChup = data.CaptureRole.ToLower() == "front" ? "truoc" : "sau";
                    }
                    
                    // Di chuyển file + lưu DB (giống Mode A)
                    string docType = result.LoaiGiayTo ?? "Ảnh xe ô tô";
                    bool isVehiclePhoto = (docType == "Ảnh xe ô tô");
                    string targetOutputDirectory = isVehiclePhoto ? _outputDirectoryPlate : _outputDirectoryDocument;

                    var extraFields = new System.Collections.Generic.Dictionary<string, string>();
                    extraFields["mau_bien_so"] = result?.MauBienSo ?? "";
                    extraFields["loai_xe"] = result?.LoaiXe ?? "";
                    extraFields["mau_xe"] = result?.MauXe ?? "";
                    extraFields["goc_chup"] = result?.GocChup ?? "";
                    if (result?.AdditionalData != null)
                    {
                        foreach (var kvp in result.AdditionalData)
                        {
                            extraFields[kvp.Key] = kvp.Value?.ToString() ?? "";
                        }
                    }
                    extraFields["SourceType"] = task.FileType;
                    extraFields["source_timestamp"] = File.GetLastWriteTime(data.ImagePath).ToString("HHmmss");
                    
                    string newFilePath = _fileNamingService.ProcessAndMoveFile(
                        data.ImagePath, docType, finalPlate, targetOutputDirectory, 
                        isVehiclePhoto, task.JobId > 0 ? true : _isCopyMode, extraFields);
                    
                    if (newFilePath != null)
                    {
                        string processedPlate = _fileNamingService.GetProcessedLicensePlate(finalPlate, isVehiclePhoto);
                        _dbHelper.InsertRecord(data.ImagePath, newFilePath, "Thành công", docType, processedPlate);
                        if (task.JobId > 0)
                        {
                            _dbHelper.MarkAssetCompleted(task.JobId, data.ImagePath, newFilePath, data.CaptureRole);
                            if (!_isCopyMode) DeleteSourceAfterCommit(data.ImagePath);
                        }
                        LoggerManager.LogInfo($"Xử lý thành công file: {data.ImagePath} -> {newFilePath}");
                        
                        // Xử lý ảnh crop biển số (_plate.jpg): dùng chung quy tắc đổi tên nhưng góc chụp = "bsx"
                        if (!string.IsNullOrEmpty(data.PlateImagePath) && File.Exists(data.PlateImagePath))
                        {
                            var plateExtraFields = new System.Collections.Generic.Dictionary<string, string>(extraFields);
                            plateExtraFields["goc_chup"] = "bsx";
                            plateExtraFields["source_timestamp"] = File.GetLastWriteTime(data.PlateImagePath).ToString("HHmmss");
                            
                            string newPlateFilePath = _fileNamingService.ProcessAndMoveFile(
                                data.PlateImagePath, docType, finalPlate, targetOutputDirectory, 
                                isVehiclePhoto, task.JobId > 0 ? true : _isCopyMode, plateExtraFields);

                            if (newPlateFilePath != null)
                            {
                                string processedPlate_ = _fileNamingService.GetProcessedLicensePlate(finalPlate, isVehiclePhoto);
                                _dbHelper.InsertRecord(data.PlateImagePath, newPlateFilePath, "Thành công", "Ảnh crop biển số", processedPlate_);
                                if (task.JobId > 0)
                                {
                                    _dbHelper.MarkAssetCompleted(task.JobId, data.PlateImagePath, newPlateFilePath, "LicensePlate");
                                    if (!_isCopyMode) DeleteSourceAfterCommit(data.PlateImagePath);
                                }
                            }
                            else
                            {
                                LoggerManager.LogWarn($"Không thể di chuyển/đổi tên ảnh crop biển số: {data.PlateImagePath}");
                            }
                        }
                    }
                    else
                    {
                        _dbHelper.InsertRecord(data.ImagePath, data.ImagePath, "Lỗi khi di chuyển/đổi tên file");
                        LoggerManager.LogWarn($"Không thể di chuyển/đổi tên file: {data.ImagePath}");
                    }
                }

                if (task.JobId > 0)
                {
                    string processedPlate = _fileNamingService.GetProcessedLicensePlate(finalPlate, true);
                    string outputFolder = Path.Combine(_outputDirectoryPlate, processedPlate);
                    if (!_dbHelper.CompleteJobIfReady(task.JobId, processedPlate, outputFolder))
                        throw new InvalidOperationException("Job chưa xử lý đủ ảnh yêu cầu");
                }
                
                EmitStatusEvent("", task.FolderPath);
            }
            catch (Exception ex)
            {
                LoggerManager.LogError($"Lỗi khi xử lý folder: {task.FolderPath}", ex);
                string friendlyError = GetFriendlyError(ex);
                // Giữ nguyên logic Retry cho Folder Task
                if (task.RetryCount < _maxRetries)
                {
                    task.RetryCount++;
                    if (task.JobId > 0) _dbHelper.MarkJobRetryPending(task.JobId, friendlyError);
                    LoggerManager.LogInfo($"Thử lại folder {task.FolderPath} (Lần {task.RetryCount}) sau {_retryWaitTime} giây");
                    var token = _cts.Token;
                    Task.Run(async () => 
                    {
                        try
                        {
                            await Task.Delay(_retryWaitTime * 1000, token);
                            _queue.Enqueue(task);
                        }
                        catch (OperationCanceledException) { }
                    }, token);
                }
                else
                {
                    if (task.JobId > 0) _dbHelper.MarkJobFailed(task.JobId, friendlyError);
                    LoggerManager.LogError($"Folder {task.FolderPath} đã lỗi {_maxRetries} lần. Dừng xử lý.");
                    _dbHelper.InsertRecord(task.FolderPath, task.FolderPath, "Lỗi xử lý");
                    EmitStatusEvent("", task.FolderPath);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _processingCount);
                EmitStatusEvent();
            }
        }

        private bool IsAllowedByMode(bool isVehiclePhoto, string sourceType, out string reason)
        {
            reason = null;
            if (_processingMode == ProcessingMode.PlateOnly && !isVehiclePhoto)
                reason = "Chế độ hiện tại chỉ xử lý biển số xe";
            else if (_processingMode == ProcessingMode.DocumentOnly && isVehiclePhoto)
                reason = "Chế độ hiện tại chỉ xử lý giấy tờ xe";
            else if (string.Equals(sourceType, "Biển số xe", StringComparison.OrdinalIgnoreCase) && !isVehiclePhoto)
                reason = "Dữ liệu giấy tờ được đặt nhầm trong thư mục biển số";
            else if (string.Equals(sourceType, "Giấy tờ xe", StringComparison.OrdinalIgnoreCase) && isVehiclePhoto)
                reason = "Ảnh biển số được đặt nhầm trong thư mục giấy tờ";

            return reason == null;
        }

        private void RejectTask(ProcessingTask task, string detectedType, string reason)
        {
            if (task.JobId > 0)
                _dbHelper.MarkJobRejected(task.JobId, reason);

            string sourcePath = task.IsFolderTask ? task.FolderPath : task.FilePath;
            _dbHelper.InsertRecord(sourcePath, sourcePath, "Không phù hợp chế độ", detectedType, string.Empty);
            LoggerManager.LogWarn($"Bỏ qua dữ liệu không phù hợp mode: {sourcePath}. {reason}");
        }

        private static string GetFriendlyError(Exception exception)
        {
            if (exception is FileNotFoundException)
                return "Không tìm thấy file ảnh nguồn.";
            if (exception is UnauthorizedAccessException)
                return "Không có quyền truy cập file hoặc thư mục.";
            if (exception is TaskCanceledException || exception is TimeoutException)
                return "Dịch vụ AI phản hồi quá thời gian.";
            if (exception is HttpRequestException)
                return "Không thể kết nối đến dịch vụ AI.";
            if (exception is JsonException)
                return "Kết quả AI trả về không đúng định dạng.";
            if (exception is IOException)
                return "Không thể đọc hoặc ghi file ảnh.";
            if (exception is InvalidOperationException &&
                exception.Message.IndexOf("chưa xử lý đủ", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Bộ ảnh chưa được xử lý đầy đủ.";

            return "Xử lý ảnh không thành công.";
        }

        public void Stop()
        {
            _cts?.Cancel();
            // Dùng timeout thay vì Wait() vô hạn để tránh deadlock trên UI thread
            if (_workerTasks != null && _workerTasks.Length > 0 && !Task.WaitAll(_workerTasks, TimeSpan.FromSeconds(5)))
            {
                LoggerManager.LogWarn("ProcessingPipeline worker không kết thúc trong 5 giây.");
            }
        }

        private static void DeleteSourceAfterCommit(string sourcePath)
        {
            try
            {
                if (File.Exists(sourcePath)) File.Delete(sourcePath);
            }
            catch (Exception ex)
            {
                LoggerManager.LogWarn($"Đã commit DB nhưng chưa thể xóa file nguồn {sourcePath}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
