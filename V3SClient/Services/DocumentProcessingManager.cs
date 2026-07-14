using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using V3SClient.libs;
using V3SClient.viewModels;
using V3SClient.models;
using VehicleDocumentProcessing.WPF.Services;

namespace V3SClient.Services
{
    public class ProcessingStatusChangedEventArgs : EventArgs
    {
        public int PendingCount { get; set; }
        public int ProcessingCount { get; set; }
        public int SuccessCount { get; set; }
        public int UnknownPlateCount { get; set; }
        public int ErrorCount { get; set; }
        public string CurrentItemPath { get; set; }
        public string CurrentFolderPath { get; set; }
        public DateTime LastUpdatedAt { get; set; }
    }

    public class DocumentProcessingManager
    {
        private static readonly Lazy<DocumentProcessingManager> _instance = new Lazy<DocumentProcessingManager>(() => new DocumentProcessingManager());
        public static DocumentProcessingManager Instance => _instance.Value;

        private ProcessingPipeline _pipeline;
        private ProcessingQueue _queue;
        private DatabaseHelper _dbHelper;
        private FileNamingService _fileNamingService;
        private LLMClient _llmClient;
        private FileWatcherService _plateWatcher;
        private FileWatcherService _documentWatcher;
        private ImageSyncService _imageSyncService;
        private CancellationTokenSource _imageSyncCancellation;
        private Task _imageSyncTask;
        private VMDocumentConfig _config;
        private readonly object _imageSyncLifecycleLock = new object();

        private bool _isRunning;
        public bool IsRunning => _isRunning;

        public ProcessingPipeline Pipeline => _pipeline;
        public ProcessingQueue Queue => _queue;

        public event Action OnProcessingUpdated;
        public void RaiseProcessingUpdated() => OnProcessingUpdated?.Invoke();

        public event Action<bool> RunningStateChanged;

        public event Action<ProcessingStatusChangedEventArgs> OnProcessingStatusChanged;
        public void RaiseProcessingStatusChanged(ProcessingStatusChangedEventArgs args) => OnProcessingStatusChanged?.Invoke(args);

        private DocumentProcessingManager() { }

        public void Initialize(VMDocumentConfig config)
        {
            try
            {
                StopImageSync();
                _config = config;
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V3SClient", "Data");
                string dbPath = Path.Combine(appDataPath, "DocumentProcessing.db");

                _dbHelper = new DatabaseHelper();
                _dbHelper.RecoverInterruptedJobs();
                _dbHelper.BackfillCompletedOutputFolders(config.OutputDirectoryPlate, config.InputDirectoryPlate);
                _queue = new ProcessingQueue();
                
                var llmConfig = new LLMConfig
                {
                    ApiUrl = config.ApiUrl,
                    ApiKey = config.ApiKey,
                    Model = string.IsNullOrWhiteSpace(config.Model) ? LLMConfig.DefaultModel : config.Model
                };
                
                _llmClient = new LLMClient(llmConfig, null);
                _fileNamingService = new FileNamingService(config.PlateNamingRule, config.DocumentNamingRule);
                
                _pipeline = new ProcessingPipeline(_queue, _llmClient, _dbHelper, _fileNamingService, 
                    config.OutputDirectoryPlate, config.OutputDirectoryDocument, 
                    config.MaxRetries, config.RetryWaitTime, config.IsCopyMode, config.ConfidenceThreshold,
                    config.ProcessingMode, config.MaxParallelProcessingJobs);

                // Initialize Watchers
                _plateWatcher = null;
                _documentWatcher = null;

                if (config.ProcessingMode == ProcessingMode.PlateOnly)
                {
                    _plateWatcher = new FileWatcherService(_queue, _dbHelper, config.InputDirectoryPlate, "Biển số xe", 
                        enableFolderMode: true, confidenceThreshold: config.ConfidenceThreshold, folderDebounceMs: config.FolderDebounceMs);
                }
                else if (config.ProcessingMode == ProcessingMode.DocumentOnly)
                {
                    _documentWatcher = new FileWatcherService(_queue, _dbHelper, config.InputDirectoryDocument, "Giấy tờ xe");
                }
                else if (config.InputLayout == InputLayout.Separate &&
                         !string.Equals(config.InputDirectoryPlate, config.InputDirectoryDocument, StringComparison.OrdinalIgnoreCase))
                {
                    _plateWatcher = new FileWatcherService(_queue, _dbHelper, config.InputDirectoryPlate, "Biển số xe",
                        enableFolderMode: true, confidenceThreshold: config.ConfidenceThreshold, folderDebounceMs: config.FolderDebounceMs);
                    _documentWatcher = new FileWatcherService(_queue, _dbHelper, config.InputDirectoryDocument, "Giấy tờ xe");
                }
                else
                {
                    _plateWatcher = new FileWatcherService(_queue, _dbHelper, config.InputDirectoryPlate, "Tất cả", 
                        enableFolderMode: true, confidenceThreshold: config.ConfidenceThreshold, folderDebounceMs: config.FolderDebounceMs);
                }

                if (config.AutoStart)
                {
                    Start();
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi khởi tạo DocumentProcessingManager", ex);
            }
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _pipeline?.Start();
                _plateWatcher?.Start();
                _documentWatcher?.Start();
                _isRunning = true;
                RunningStateChanged?.Invoke(true);
                try
                {
                    StartImageSync();
                }
                catch (Exception syncException)
                {
                    LoggerManager.LogError("Không thể khởi động image sync; pipeline chính vẫn tiếp tục chạy.", syncException);
                }
                LoggerManager.LogInfo("Đã khởi động Document Processing Manager.");
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi Start DocumentProcessingManager", ex);
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _isRunning = false;
                RunningStateChanged?.Invoke(false);
                _plateWatcher?.Stop();
                _documentWatcher?.Stop();
                StopImageSync();
                _pipeline?.Stop();
                LoggerManager.LogInfo("Đã dừng Document Processing Manager.");
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi Stop DocumentProcessingManager", ex);
            }
        }

        private void StartImageSync()
        {
            lock (_imageSyncLifecycleLock)
            {
                if (_config == null ||
                    _config.ProcessingMode == ProcessingMode.DocumentOnly ||
                    !_config.EnableImageSync ||
                    string.IsNullOrWhiteSpace(_config.ImageSyncBaseUrl))
                {
                    StopImageSync();
                    return;
                }

                StopImageSync();
                Directory.CreateDirectory(_config.InputDirectoryPlate);

            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "V3SClient",
                "Data");
            var stateFile = Path.Combine(appDataPath, "ImageSyncState.json");
            var cancellation = new CancellationTokenSource();
            var service = new ImageSyncService();
            _imageSyncCancellation = cancellation;
            _imageSyncService = service;

            string baseUrl = _config.ImageSyncBaseUrl;
            string token = _config.ImageSyncToken;
            string inputDirectory = _config.InputDirectoryPlate;
            int pageSize = Math.Max(1, Math.Min(500, _config.ImageSyncPageSize));
            int intervalSeconds = Math.Max(1, _config.ImageSyncIntervalSeconds);
            int downloadParallelism = Math.Max(1, Math.Min(16, _config.ImageSyncDownloadParallelism));

                _imageSyncTask = Task.Run(async () =>
                {
                LoggerManager.LogInfo($"Image sync đã khởi động: {baseUrl} -> {inputDirectory}");
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        var result = await service.SyncOnceAsync(
                            baseUrl,
                            token,
                            inputDirectory,
                            stateFile,
                            limit: pageSize,
                            downloadParallelism: downloadParallelism,
                            cancellationToken: cancellation.Token).ConfigureAwait(false);

                        if (result.Downloads > 0)
                        {
                            LoggerManager.LogInfo($"Image sync: tải {result.Downloads} file từ {result.Items} bản ghi.");
                        }

                        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (cancellation.IsCancellationRequested)
                            break;

                        string reason = ex.GetBaseException().Message;
                        LoggerManager.LogError($"Image sync gặp lỗi: {reason}. Sẽ thử lại ở chu kỳ tiếp theo.");
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellation.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
                LoggerManager.LogInfo("Image sync đã dừng.");
                }, cancellation.Token);
            }
        }

        private void StopImageSync()
        {
            lock (_imageSyncLifecycleLock)
            {
                var cancellation = _imageSyncCancellation;
                var service = _imageSyncService;
                _imageSyncCancellation = null;
                _imageSyncService = null;
                _imageSyncTask = null;

                if (cancellation == null && service == null)
                    return;

                try
                {
                    cancellation?.Cancel();
                    service?.Dispose();
                }
                catch (Exception ex)
                {
                    LoggerManager.LogWarn($"Không thể dừng image sync sạch sẽ: {ex.Message}");
                }
                finally
                {
                    cancellation?.Dispose();
                }
            }
        }

        public void ApplyImageSyncConfig(VMDocumentConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            try
            {
                _config = config;
                if (_isRunning)
                    StartImageSync();
                else
                    StopImageSync();
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Không thể áp dụng cấu hình image sync; pipeline chính không bị thay đổi.", ex);
            }
        }

        public void ReloadConfig(VMDocumentConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            bool shouldRestart = IsRunning;
            bool configuredAutoStart = config.AutoStart;
            try
            {
                Stop();
                config.AutoStart = false;
                Initialize(config);
                if (shouldRestart)
                    Start();
            }
            finally
            {
                config.AutoStart = configuredAutoStart;
            }
        }

        public bool TryQueueManualRetry(ProcessingJobStatusItem job, string manualPlate, out string error)
        {
            error = string.Empty;
            if (job == null || job.Id <= 0)
            {
                error = "Job không hợp lệ.";
                return false;
            }
            if (!_isRunning || _queue == null || _dbHelper == null)
            {
                error = "Dịch vụ xử lý đang dừng. Hãy khởi động dịch vụ trước.";
                return false;
            }
            if (job.IsFolder || !File.Exists(job.SourcePath))
            {
                error = "Chỉ hỗ trợ xử lý thủ công cho một file ảnh còn tồn tại.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(manualPlate))
            {
                error = "Biển số xe không được để trống.";
                return false;
            }
            if (!_dbHelper.PrepareManualRetry(job.Id, manualPlate.Trim().ToUpperInvariant()))
            {
                error = "Job không còn ở trạng thái lỗi hoặc đã được xử lý lại.";
                return false;
            }

            _queue.Enqueue(new ProcessingTask
            {
                JobId = job.Id,
                FilePath = job.SourcePath,
                FileType = job.SourceType,
                RetryCount = 0
            });
            RaiseProcessingUpdated();
            return true;
        }
    }
}
