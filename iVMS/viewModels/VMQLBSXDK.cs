using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using V3SClient.libs;
using V3SClient.models;
using V3SClient.Services;
using V3SClient.ucs;
using VehicleDocumentProcessing.WPF.Models;

namespace V3SClient.viewModels
{
    public class PaginationButtonItem
    {
        public int PageNumber { get; set; }
        public string Text { get; set; }
        public bool IsCurrent { get; set; }
        public bool CanNavigate => PageNumber > 0 && !IsCurrent;
    }

    public class VMQLBSXDK : INotifyPropertyChanged
    {
        private readonly DatabaseHelper _dbHelper;
        private OutputFolderMonitorService _folderMonitor;
        private InputCaptureMonitorService _inputCaptureMonitor;
        private RtspStreamService _rtspStreamService;
        private readonly NamingRule _plateNamingRule;
        private readonly string _sourceTypeFilter;

        private int _pendingCount;
        private int _processingCount;
        private int _successCount;
        private int _unknownPlateCount;
        private int _errorCount;
        private int _overviewSuccessCount;
        private int _overviewUnknownPlateCount;
        private int _overviewErrorCount;

        private ObservableCollection<PlateFolderItem> _folders;
        private ObservableCollection<PlateFolderItem> _outputFolders;
        private ObservableCollection<PlateImageItem> _recentCaptures;
        private ObservableCollection<ProcessingJobStatusItem> _processingJobs;
        private PlateFolderItem _selectedFolder;
        private PlateImageItem _selectedOutputFile;
        private string _folderSearchText;
        private string _editedPlateNumber;
        private string _editStatusMessage;
        private bool _isEditingPlate;
        private bool _isServiceRunning;
        private bool _isServiceChanging;
        private readonly DispatcherTimer _searchDebounceTimer;
        private readonly DispatcherTimer _newFolderBadgeTimer;
        private readonly DispatcherTimer _storageUsageTimer;
        private readonly Counter _storageCounter;
        private double _inputStorageUsagePercent;
        private string _inputStorageUsageText = "Đang cập nhật...";
        private double _storageUsagePercent;
        private string _storageUsageText = "Đang cập nhật...";
        private int _recentResultLimit = 5;
        private int _isRefreshingProcessingJobs;
        private int _processingJobsRefreshRequested;
        private string _selectedOutputFolderSort = "Mới nhất";
        private string _newestOutputFolderPath;
        private DateTime _newFolderBadgeExpiresAt;
        private bool _isDisposed;
        private int _inputPageSize = 10;
        private int _outputPageSize = 8;
        private int _inputCurrentPage = 1;
        private int _inputTotalPages = 1;
        private int _outputCurrentPage = 1;
        private int _outputTotalPages = 1;
        private string _inputSearchText;
        private ImageSource _latestDocumentCameraFrame;
        private string _documentRtspUrl = "rtsp://127.0.0.1:8554/mobilestream";
        private string _documentCameraStatus = "Luồng camera chưa chạy.";
        private string _documentCameraNotification;
        private bool _isDocumentCameraNotificationVisible;
        private bool _isDocumentCameraSettingsVisible;
        private bool _isDocumentCameraRunning;
        private bool _isTakingDocumentPicture;
        private string _selectedInputStatus = "Tất cả trạng thái";
        public ObservableCollection<PaginationButtonItem> InputPageButtons { get; } = new ObservableCollection<PaginationButtonItem>();
        public ObservableCollection<PaginationButtonItem> OutputPageButtons { get; } = new ObservableCollection<PaginationButtonItem>();
        public ObservableCollection<int> InputPageSizeOptions { get; } = new ObservableCollection<int> { 10, 20, 50 };
        public ObservableCollection<int> OutputPageSizeOptions { get; } = new ObservableCollection<int> { 8, 12, 20 };
        public ObservableCollection<CapturedSnapshot> DocumentSnapshots { get; } = new ObservableCollection<CapturedSnapshot>();

        public double StorageUsagePercent
        {
            get => _storageUsagePercent;
            private set { _storageUsagePercent = value; OnPropertyChanged(); }
        }

        public double InputStorageUsagePercent
        {
            get => _inputStorageUsagePercent;
            private set { _inputStorageUsagePercent = value; OnPropertyChanged(); }
        }

        public string InputStorageUsageText
        {
            get => _inputStorageUsageText;
            private set { _inputStorageUsageText = value; OnPropertyChanged(); }
        }

        public string StorageUsageText
        {
            get => _storageUsageText;
            private set { _storageUsageText = value; OnPropertyChanged(); }
        }

        public int InputCurrentPage
        {
            get => _inputCurrentPage;
            private set { _inputCurrentPage = value; OnPropertyChanged(); OnPropertyChanged(nameof(InputPageText)); RebuildPageButtons(InputPageButtons, value, InputTotalPages); CommandManager.InvalidateRequerySuggested(); }
        }
        public int InputTotalPages
        {
            get => _inputTotalPages;
            private set { _inputTotalPages = value; OnPropertyChanged(); OnPropertyChanged(nameof(InputPageText)); RebuildPageButtons(InputPageButtons, InputCurrentPage, value); CommandManager.InvalidateRequerySuggested(); }
        }
        public string InputPageText => $"Trang {InputCurrentPage}/{InputTotalPages}";

        public int OutputCurrentPage
        {
            get => _outputCurrentPage;
            private set { _outputCurrentPage = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutputPageText)); RebuildPageButtons(OutputPageButtons, value, OutputTotalPages); CommandManager.InvalidateRequerySuggested(); }
        }
        public int OutputTotalPages
        {
            get => _outputTotalPages;
            private set { _outputTotalPages = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutputPageText)); RebuildPageButtons(OutputPageButtons, OutputCurrentPage, value); CommandManager.InvalidateRequerySuggested(); }
        }
        public string OutputPageText => $"Trang {OutputCurrentPage}/{OutputTotalPages}";

        public int InputPageSize
        {
            get => _inputPageSize;
            set { if (_inputPageSize == value) return; _inputPageSize = value; OnPropertyChanged(); InputCurrentPage = 1; RefreshProcessingJobs(); }
        }

        public int OutputPageSize
        {
            get => _outputPageSize;
            set { if (_outputPageSize == value) return; _outputPageSize = value; OnPropertyChanged(); OutputCurrentPage = 1; RefreshOutputFolders(); }
        }

        public string InputSearchText
        {
            get => _inputSearchText;
            set
            {
                if (_inputSearchText == value) return;
                _inputSearchText = value;
                OnPropertyChanged();
                InputCurrentPage = 1;
                RefreshProcessingJobs();
            }
        }

        public ObservableCollection<string> InputStatusOptions { get; } = new ObservableCollection<string>
        {
            "Tất cả trạng thái", "Thành công", "Đang xử lý", "Lỗi"
        };

        public string SelectedInputStatus
        {
            get => _selectedInputStatus;
            set
            {
                if (_selectedInputStatus == value) return;
                _selectedInputStatus = value ?? "Tất cả trạng thái";
                OnPropertyChanged();
                InputCurrentPage = 1;
                RefreshProcessingJobs();
            }
        }

        public ImageSource LatestDocumentCameraFrame
        {
            get => _latestDocumentCameraFrame;
            private set { _latestDocumentCameraFrame = value; OnPropertyChanged(); }
        }

        public string DocumentRtspUrl
        {
            get => _documentRtspUrl;
            set { _documentRtspUrl = string.IsNullOrWhiteSpace(value) ? "rtsp://127.0.0.1:8554/mobilestream" : value.Trim(); OnPropertyChanged(); }
        }

        public string DocumentCameraStatus
        {
            get => _documentCameraStatus;
            private set { _documentCameraStatus = value; OnPropertyChanged(); }
        }

        public string DocumentCameraNotification
        {
            get => _documentCameraNotification;
            private set
            {
                _documentCameraNotification = value;
                OnPropertyChanged();
                IsDocumentCameraNotificationVisible = !string.IsNullOrWhiteSpace(value);
            }
        }

        public bool IsDocumentCameraNotificationVisible
        {
            get => _isDocumentCameraNotificationVisible;
            private set { _isDocumentCameraNotificationVisible = value; OnPropertyChanged(); }
        }

        public bool IsDocumentCameraSettingsVisible
        {
            get => _isDocumentCameraSettingsVisible;
            set { _isDocumentCameraSettingsVisible = value; OnPropertyChanged(); }
        }

        public bool IsDocumentCameraRunning
        {
            get => _isDocumentCameraRunning;
            private set { _isDocumentCameraRunning = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        public bool IsTakingDocumentPicture
        {
            get => _isTakingDocumentPicture;
            private set { _isTakingDocumentPicture = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        public bool HasDocumentSnapshots => DocumentSnapshots.Count > 0;

        public int PendingCount
        {
            get => _pendingCount;
            set { _pendingCount = value; OnPropertyChanged(); }
        }

        public int TotalProcessed
        {
            get => SuccessCount + ErrorCount + UnknownPlateCount;
        }

        public int OverviewTotalProcessed
        {
            get => OverviewSuccessCount + OverviewErrorCount + OverviewUnknownPlateCount;
        }

        public int OverviewIssueCount
        {
            get => OverviewErrorCount + OverviewUnknownPlateCount;
        }

        public double SuccessRate
        {
            get
            {
                int total = TotalProcessed;
                return total == 0 ? 0 : (double)SuccessCount / total * 100;
            }
        }

        public int ProcessingCount
        {
            get => _processingCount;
            set { _processingCount = value; OnPropertyChanged(); }
        }

        public int SuccessCount
        {
            get => _successCount;
            set { _successCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalProcessed)); OnPropertyChanged(nameof(SuccessRate)); }
        }

        public int UnknownPlateCount
        {
            get => _unknownPlateCount;
            set { _unknownPlateCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalProcessed)); OnPropertyChanged(nameof(SuccessRate)); }
        }

        public int ErrorCount
        {
            get => _errorCount;
            set { _errorCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalProcessed)); OnPropertyChanged(nameof(SuccessRate)); }
        }

        public int OverviewSuccessCount
        {
            get => _overviewSuccessCount;
            set { _overviewSuccessCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(OverviewTotalProcessed)); OnPropertyChanged(nameof(OverviewIssueCount)); }
        }

        public int OverviewUnknownPlateCount
        {
            get => _overviewUnknownPlateCount;
            set { _overviewUnknownPlateCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(OverviewTotalProcessed)); OnPropertyChanged(nameof(OverviewIssueCount)); }
        }

        public int OverviewErrorCount
        {
            get => _overviewErrorCount;
            set { _overviewErrorCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(OverviewTotalProcessed)); OnPropertyChanged(nameof(OverviewIssueCount)); }
        }

        public ObservableCollection<PlateFolderItem> Folders
        {
            get => _folders;
            set { _folders = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PlateFolderItem> OutputFolders
        {
            get => _outputFolders;
            set { _outputFolders = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PlateImageItem> RecentCaptures
        {
            get => _recentCaptures;
            set { _recentCaptures = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ProcessingJobStatusItem> ProcessingJobs
        {
            get => _processingJobs;
            set { _processingJobs = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> OutputFolderSortOptions { get; } = new ObservableCollection<string>
        {
            "Mới nhất", "Cũ nhất", "Tên A → Z", "Tên Z → A"
        };

        public string SelectedOutputFolderSort
        {
            get => _selectedOutputFolderSort;
            set
            {
                if (_selectedOutputFolderSort == value) return;
                _selectedOutputFolderSort = value ?? "Mới nhất";
                OnPropertyChanged();
                OutputCurrentPage = 1;
                RefreshOutputFolders();
            }
        }

        public string NewestOutputFolderPath
        {
            get => _newestOutputFolderPath;
            private set { _newestOutputFolderPath = value; OnPropertyChanged(); }
        }

        public DateTime NewFolderBadgeExpiresAt
        {
            get => _newFolderBadgeExpiresAt;
            private set { _newFolderBadgeExpiresAt = value; OnPropertyChanged(); }
        }

        public string FolderSearchText
        {
            get => _folderSearchText;
            set
            {
                if (_folderSearchText == value) return;
                _folderSearchText = value;
                OnPropertyChanged();
                OutputCurrentPage = 1;
                ScheduleLiveSearch();
            }
        }

        public PlateImageItem SelectedOutputFile
        {
            get => _selectedOutputFile;
            set { _selectedOutputFile = value; OnPropertyChanged(); }
        }

        public PlateFolderItem SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                _selectedFolder = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedFolder));
                EditedPlateNumber = value?.PlateNumber;
                IsEditingPlate = false;
                EditStatusMessage = string.Empty;
            }
        }

        public bool IsEditingPlate
        {
            get => _isEditingPlate;
            set { _isEditingPlate = value; OnPropertyChanged(); }
        }

        public string EditedPlateNumber
        {
            get => _editedPlateNumber;
            set { _editedPlateNumber = value; OnPropertyChanged(); }
        }

        public string EditStatusMessage
        {
            get => _editStatusMessage;
            set { _editStatusMessage = value; OnPropertyChanged(); }
        }

        public bool HasSelectedFolder => _selectedFolder != null;

        public bool IsServiceRunning
        {
            get => _isServiceRunning;
            private set
            {
                if (_isServiceRunning == value) return;
                _isServiceRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ServiceStatusText));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsServiceChanging
        {
            get => _isServiceChanging;
            private set
            {
                if (_isServiceChanging == value) return;
                _isServiceChanging = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ServiceStatusText));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string ServiceStatusText => IsServiceChanging
            ? "AI ĐANG CHUYỂN TRẠNG THÁI"
            : IsServiceRunning ? "AI ĐANG HOẠT ĐỘNG" : "AI ĐÃ DỪNG";

        public ObservableCollection<int> RecentResultLimits { get; } = new ObservableCollection<int> { 3, 5 };

        public int RecentResultLimit
        {
            get => _recentResultLimit;
            set
            {
                if (_recentResultLimit == value) return;
                _recentResultLimit = value;
                OnPropertyChanged();
                RefreshFolders();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand OpenConfigCommand { get; }
        public ICommand ToggleServiceCommand { get; }
        public ICommand BrowseInputDirectoryCommand { get; }
        public ICommand BrowseOutputDirectoryCommand { get; }
        public ICommand SearchFoldersCommand { get; }
        public ICommand RefreshOutputFoldersCommand { get; }
        public ICommand OpenSelectedFileCommand { get; }
        public ICommand SavePlateCorrectionCommand { get; }
        public ICommand TogglePlateEditCommand { get; }
        public ICommand CancelPlateEditCommand { get; }
        public ICommand ViewProcessingJobCommand { get; }
        public ICommand ManualProcessJobCommand { get; }
        public ICommand OpenProcessingJobFolderCommand { get; }
        public ICommand ShowProcessingErrorCommand { get; }
        public ICommand PreviousInputPageCommand { get; }
        public ICommand NextInputPageCommand { get; }
        public ICommand PreviousOutputPageCommand { get; }
        public ICommand NextOutputPageCommand { get; }
        public ICommand GoToInputPageCommand { get; }
        public ICommand GoToOutputPageCommand { get; }
        public ICommand TakeDocumentPictureCommand { get; }
        public ICommand ToggleDocumentCameraSettingsCommand { get; }
        public ICommand SaveDocumentRtspCommand { get; }
        public ICommand RestartDocumentCameraCommand { get; }
        public ICommand OpenDocumentCameraWindowCommand { get; }
        public ICommand DeleteDocumentSnapshotCommand { get; }
        public bool IsDocumentMode => string.Equals(_sourceTypeFilter, "Giấy tờ xe", StringComparison.OrdinalIgnoreCase);
        public string JobDisplayName => IsDocumentMode ? "giấy tờ xe" : "biển số xe";
        public string DashboardTitle => IsDocumentMode ? "iDK - DOC" : "iDK - VMS";
        public string DashboardSubtitle => IsDocumentMode
            ? "AI tự động phân loại hồ sơ theo giấy tờ xe"
            : "AI tự động tạo hồ sơ ảnh theo biển số";
        public string OutputGroupUnitText => IsDocumentMode ? "nhóm tài liệu" : "biển số";
        public string InputBrowseDescription => IsDocumentMode
            ? "Chọn thư mục ảnh giấy tờ xe đầu vào"
            : "Chọn thư mục lắng nghe ảnh biển số";
        private string _inputDirectory;
        public string InputDirectory
        {
            get => _inputDirectory;
            private set { _inputDirectory = value; OnPropertyChanged(); }
        }

        private string _outputDirectory;
        public string OutputDirectory
        {
            get => _outputDirectory;
            private set { _outputDirectory = value; OnPropertyChanged(); }
        }

        public VMQLBSXDK(DatabaseHelper dbHelper, string outputDirectory, string inputDirectory = null, NamingRule plateNamingRule = null, string sourceTypeFilter = null)
        {
            _dbHelper = dbHelper;
            _plateNamingRule = plateNamingRule;
            _sourceTypeFilter = sourceTypeFilter;
            OutputDirectory = outputDirectory;
            InputDirectory = inputDirectory;
            _folders = new ObservableCollection<PlateFolderItem>();
            _outputFolders = new ObservableCollection<PlateFolderItem>();
            _recentCaptures = new ObservableCollection<PlateImageItem>();
            _processingJobs = new ObservableCollection<ProcessingJobStatusItem>();
            DocumentSnapshots.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasDocumentSnapshots));
            if (IsDocumentMode)
            {
                var documentConfig = new VMDocumentConfig();
                DocumentRtspUrl = documentConfig.DocumentRtspUrl;
                InitializeDocumentCamera();
            }
            _storageCounter = new Counter();
            _storageUsageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _storageUsageTimer.Tick += (s, e) => UpdateStorageUsage();
            _storageUsageTimer.Start();
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                RefreshOutputFolders();
            };
            _newFolderBadgeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(20)
            };
            _newFolderBadgeTimer.Tick += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(NewestOutputFolderPath) && DateTime.Now >= NewFolderBadgeExpiresAt)
                {
                    NewestOutputFolderPath = null;
                    NewFolderBadgeExpiresAt = DateTime.MinValue;
                }
                else
                {
                    OnPropertyChanged(nameof(NewFolderBadgeExpiresAt));
                }
            };
            _newFolderBadgeTimer.Start();

            // Setup Folder Monitor
            _folderMonitor = new OutputFolderMonitorService(outputDirectory, _dbHelper);
            _folderMonitor.OnFolderChanged += FolderMonitor_OnFolderChanged;
            _folderMonitor.Start();
            _inputCaptureMonitor = new InputCaptureMonitorService(inputDirectory);
            _inputCaptureMonitor.CapturesChanged += InputCaptureMonitor_CapturesChanged;
            _inputCaptureMonitor.Start();

            // Setup Pipeline Events
            DocumentProcessingManager.Instance.OnProcessingStatusChanged += Instance_OnProcessingStatusChanged;
            DocumentProcessingManager.Instance.OnProcessingUpdated += Instance_OnProcessingUpdated;
            DocumentProcessingManager.Instance.RunningStateChanged += Instance_RunningStateChanged;

            // Commands
            RefreshCommand = new RelayCommand(obj => RefreshData());
            OpenConfigCommand = new RelayCommand(obj => OpenConfig());
            ToggleServiceCommand = new RelayCommand(
                async obj => await SetServiceRunningAsync(!IsServiceRunning),
                obj => !IsServiceChanging);
            BrowseInputDirectoryCommand = new RelayCommand(async obj => await BrowseAndReloadDirectoryAsync(true));
            BrowseOutputDirectoryCommand = new RelayCommand(async obj => await BrowseAndReloadDirectoryAsync(false));
            SearchFoldersCommand = new RelayCommand(obj =>
            {
                _searchDebounceTimer.Stop();
                RefreshOutputFolders();
            });
            RefreshOutputFoldersCommand = new RelayCommand(obj => RefreshOutputFolders());
            OpenSelectedFileCommand = new RelayCommand(obj => OpenSelectedFile());
            SavePlateCorrectionCommand = new RelayCommand(obj => SavePlateCorrection());
            TogglePlateEditCommand = new RelayCommand(obj =>
            {
                if (SelectedFolder == null) return;
                EditedPlateNumber = SelectedFolder.PlateNumber;
                EditStatusMessage = string.Empty;
                IsEditingPlate = !IsEditingPlate;
            });
            CancelPlateEditCommand = new RelayCommand(obj =>
            {
                EditedPlateNumber = SelectedFolder?.PlateNumber;
                EditStatusMessage = string.Empty;
                IsEditingPlate = false;
            });
            ViewProcessingJobCommand = new RelayCommand(obj => ViewProcessingJob(obj as ProcessingJobStatusItem));
            OpenProcessingJobFolderCommand = new RelayCommand(obj => OpenProcessingJobFolder(obj as ProcessingJobStatusItem));
            ShowProcessingErrorCommand = new RelayCommand(obj => ShowProcessingError(obj as ProcessingJobStatusItem));
            ManualProcessJobCommand = new RelayCommand(
                obj => ManualProcessJob(obj as ProcessingJobStatusItem),
                obj => (obj as ProcessingJobStatusItem)?.CanManualProcess == true);
            PreviousInputPageCommand = new RelayCommand(obj => { InputCurrentPage--; RefreshProcessingJobs(); }, obj => InputCurrentPage > 1);
            NextInputPageCommand = new RelayCommand(obj => { InputCurrentPage++; RefreshProcessingJobs(); }, obj => InputCurrentPage < InputTotalPages);
            PreviousOutputPageCommand = new RelayCommand(obj => { OutputCurrentPage--; RefreshOutputFolders(); }, obj => OutputCurrentPage > 1);
            NextOutputPageCommand = new RelayCommand(obj => { OutputCurrentPage++; RefreshOutputFolders(); }, obj => OutputCurrentPage < OutputTotalPages);
            GoToInputPageCommand = new RelayCommand(obj => { if (obj is int page) { InputCurrentPage = page; RefreshProcessingJobs(); } });
            GoToOutputPageCommand = new RelayCommand(obj => { if (obj is int page) { OutputCurrentPage = page; RefreshOutputFolders(); } });
            TakeDocumentPictureCommand = new RelayCommand(async obj => await TakeDocumentPictureAsync(), obj => IsDocumentMode && !IsTakingDocumentPicture);
            ToggleDocumentCameraSettingsCommand = new RelayCommand(obj => IsDocumentCameraSettingsVisible = !IsDocumentCameraSettingsVisible, obj => IsDocumentMode);
            SaveDocumentRtspCommand = new RelayCommand(obj => SaveDocumentRtsp(), obj => IsDocumentMode);
            RestartDocumentCameraCommand = new RelayCommand(obj => RestartDocumentCamera(), obj => IsDocumentMode);
            OpenDocumentCameraWindowCommand = new RelayCommand(obj => OpenDocumentCameraWindow(), obj => IsDocumentMode);
            DeleteDocumentSnapshotCommand = new RelayCommand(obj => DeleteDocumentSnapshot(obj as CapturedSnapshot), obj => IsDocumentMode);
            RebuildPageButtons(InputPageButtons, InputCurrentPage, InputTotalPages);
            RebuildPageButtons(OutputPageButtons, OutputCurrentPage, OutputTotalPages);

            // Initial load
            IsServiceRunning = DocumentProcessingManager.Instance.IsRunning;
            LoadInitialStatus();
            RefreshFolders();
            RefreshOutputFolders();
            SelectLatestResult();
            RefreshRecentCaptures();
            RefreshProcessingJobs();
            UpdateStorageUsage();
            if (IsDocumentMode)
                StartDocumentCamera();
        }

        private void UpdateStorageUsage()
        {
            UpdateStorageUsageForPath(InputDirectory, true);
            UpdateStorageUsageForPath(OutputDirectory, false);
        }

        private void UpdateStorageUsageForPath(string directory, bool isInputDirectory)
        {
            try
            {
                string root = Path.GetPathRoot(directory ?? string.Empty);
                string driveLetter = root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).TrimEnd(':');
                if (string.IsNullOrWhiteSpace(driveLetter) ||
                    !_storageCounter.DiskSpaceTotal.TryGetValue(driveLetter, out double totalGb) || totalGb <= 0)
                {
                    SetStorageUsage(isInputDirectory, 0, "Đang cập nhật...");
                    return;
                }

                double usedGb = Math.Max(0, _storageCounter.GetUsedSpaceLabel(driveLetter));
                double percent = Math.Max(0, Math.Min(100, _storageCounter.GetFreeSpaceGaugePercent(driveLetter)));
                SetStorageUsage(isInputDirectory, percent, $"{usedGb:0.#} / {totalGb:0.#} GB");
            }
            catch (Exception ex)
            {
                SetStorageUsage(isInputDirectory, 0, "Không xác định");
                LoggerManager.LogError("Không thể đọc dung lượng ổ lưu trữ", ex);
            }
        }

        private void SetStorageUsage(bool isInputDirectory, double percent, string text)
        {
            if (isInputDirectory)
            {
                InputStorageUsagePercent = percent;
                InputStorageUsageText = text;
                return;
            }

            StorageUsagePercent = percent;
            StorageUsageText = text;
        }

        private void InputCaptureMonitor_CapturesChanged()
        {
            Application.Current.Dispatcher.InvokeAsync(RefreshRecentCaptures);
        }

        private void FolderMonitor_OnFolderChanged(string folderPath)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var details = _folderMonitor.GetFolderDetails(folderPath);
                if (details == null)
                {
                    RefreshFolders();
                    RefreshOutputFolders();
                    RefreshPersistentStatistics();
                    return;
                }

                var existing = _folders.FirstOrDefault(f => f.FolderPath == folderPath);
                if (existing != null)
                {
                    int index = _folders.IndexOf(existing);
                    _folders[index] = details; // Replace to trigger UI update

                    if (IsSamePath(_selectedFolder?.FolderPath, folderPath))
                    {
                        SelectedFolder = details;
                        SelectedOutputFile = details.OutputFiles.FirstOrDefault();
                    }
                }
                else
                {
                    _folders.Insert(0, details); // Thêm mới lên đầu
                }

                while (_folders.Count > RecentResultLimit)
                {
                    _folders.RemoveAt(_folders.Count - 1);
                }

                MarkOutputFolderAsNew(folderPath);
                UpsertOutputFolder(details);
                RefreshPersistentStatistics();
            });
        }

        private void UpsertOutputFolder(PlateFolderItem details)
        {
            if (details == null) return;

            var existing = OutputFolders.FirstOrDefault(f => IsSamePath(f.FolderPath, details.FolderPath));
            if (existing != null)
            {
                int index = OutputFolders.IndexOf(existing);
                OutputFolders[index] = details;

                if (IsSamePath(_selectedFolder?.FolderPath, details.FolderPath))
                {
                    SelectedFolder = details;
                    SelectedOutputFile = details.OutputFiles.FirstOrDefault();
                }
                return;
            }

            RefreshOutputFolders();
        }

        private static bool IsSamePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private void Instance_OnProcessingStatusChanged(ProcessingStatusChangedEventArgs e)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PendingCount = e.PendingCount;
                ProcessingCount = e.ProcessingCount;
                ErrorCount = e.ErrorCount;
                RefreshOutputStatistics();
                RefreshProcessingJobs();
                RefreshRecentCaptures();
                RefreshOutputFolders();
            });
        }

        private void Instance_RunningStateChanged(bool isRunning)
        {
            Application.Current.Dispatcher.InvokeAsync(() => IsServiceRunning = isRunning);
        }

        private void Instance_OnProcessingUpdated()
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RefreshProcessingJobs();
                RefreshRecentCaptures();
                RefreshOutputFolders();
            });
        }

        private void LoadInitialStatus()
        {
            RefreshPersistentStatistics();

            // Pending vẫn lấy từ queue; ProcessingCount đã lấy từ toàn bộ job active trong DB
            // để số trên dashboard khớp với danh sách đang hiển thị.
            PendingCount = DocumentProcessingManager.Instance.Queue?.PendingCount ?? 0;
        }

        private void RefreshPersistentStatistics()
        {
            var overviewStats = _dbHelper.GetOverviewStatistics(_sourceTypeFilter);
            OverviewSuccessCount = overviewStats.success;
            OverviewErrorCount = overviewStats.error;
            OverviewUnknownPlateCount = overviewStats.unknownPlate;

            var todayStats = _dbHelper.GetTodayStatistics(_sourceTypeFilter);
            SuccessCount = todayStats.success;
            ErrorCount = todayStats.error;
            UnknownPlateCount = todayStats.unknownPlate;
            ProcessingCount = _dbHelper.GetActiveProcessingJobCount(_sourceTypeFilter);
            OnPropertyChanged(nameof(TotalProcessed));
            OnPropertyChanged(nameof(SuccessRate));
            OnPropertyChanged(nameof(OverviewTotalProcessed));
            OnPropertyChanged(nameof(OverviewIssueCount));
        }

        private void RefreshOutputStatistics()
        {
            RefreshPersistentStatistics();
        }

        private void RefreshData()
        {
            LoadInitialStatus();
            RefreshFolders();
            RefreshOutputFolders();
            RefreshRecentCaptures();
            RefreshProcessingJobs();
        }

        private static void RebuildPageButtons(ObservableCollection<PaginationButtonItem> target, int currentPage, int totalPages)
        {
            if (target == null) return;
            target.Clear();
            totalPages = Math.Max(1, totalPages);
            currentPage = Math.Max(1, Math.Min(currentPage, totalPages));

            var pages = new List<int>();
            if (totalPages <= 2)
            {
                for (int page = 1; page <= totalPages; page++) pages.Add(page);
            }
            else if (currentPage <= 2)
            {
                pages.AddRange(new[] { 1, 2, -1, totalPages });
            }
            else if (currentPage >= totalPages - 1)
            {
                pages.Add(1);
                pages.Add(-1);
                pages.Add(totalPages - 1);
                pages.Add(totalPages);
            }
            else
            {
                pages.AddRange(new[] { 1, -1, currentPage, -1, totalPages });
            }

            foreach (int page in pages)
            {
                target.Add(new PaginationButtonItem
                {
                    PageNumber = page,
                    Text = page < 0 ? "…" : page.ToString(),
                    IsCurrent = page == currentPage
                });
            }
        }

        private async void RefreshProcessingJobs()
        {
            if (_isDisposed)
                return;
            if (Interlocked.CompareExchange(ref _isRefreshingProcessingJobs, 1, 0) != 0)
            {
                Interlocked.Exchange(ref _processingJobsRefreshRequested, 1);
                return;
            }

            try
            {
                int requestedPage = InputCurrentPage;
                string searchText = InputSearchText;
                string statusFilter = SelectedInputStatus == "Thành công"
                    ? "SUCCESS"
                    : SelectedInputStatus == "Đang xử lý"
                        ? "ACTIVE"
                        : SelectedInputStatus == "Lỗi" ? "ERROR" : string.Empty;
                var result = await Task.Run(() =>
                {
                    int count = _dbHelper.GetVisibleProcessingJobCount(searchText, statusFilter, _sourceTypeFilter);
                    int totalPages = Math.Max(1, (int)Math.Ceiling(count / (double)InputPageSize));
                    int page = Math.Max(1, Math.Min(requestedPage, totalPages));
                    var pageJobs = _dbHelper.GetVisibleProcessingJobs(InputPageSize, (page - 1) * InputPageSize, searchText, statusFilter, _sourceTypeFilter);
                    return new { TotalPages = totalPages, Page = page, Jobs = pageJobs };
                });
                if (_isDisposed) return;

                InputTotalPages = result.TotalPages;
                InputCurrentPage = result.Page;
                ProcessingJobs.Clear();
                foreach (var job in result.Jobs)
                    ProcessingJobs.Add(job);

            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Không thể tải danh sách trạng thái file xử lý", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _isRefreshingProcessingJobs, 0);
                if (!_isDisposed && Interlocked.Exchange(ref _processingJobsRefreshRequested, 0) == 1)
                    RefreshProcessingJobs();
            }
        }

        private void ViewProcessingJob(ProcessingJobStatusItem job)
        {
            if (job == null) return;
            string imagePath = job.IsSuccess && !string.IsNullOrWhiteSpace(job.DisplayOutputFilePath)
                ? job.DisplayOutputFilePath
                : job.IsSuccess && !string.IsNullOrWhiteSpace(job.OutputFolder)
                    ? job.OutputFolder
                : (string.IsNullOrWhiteSpace(job.DisplayFilePath) ? job.SourcePath : job.DisplayFilePath);
            if (Directory.Exists(imagePath))
            {
                imagePath = Directory.GetFiles(imagePath, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(path => Regex.IsMatch(Path.GetExtension(path), @"^\.(jpg|jpeg|png|webp|gif)$", RegexOptions.IgnoreCase));
            }
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                ToastManager.ShowToast("Không thể xem ảnh", "File ảnh nguồn không còn tồn tại.", ToastType.Error);
                return;
            }

            try
            {
                var viewer = new ucs.ucImageViewer();
                viewer.LoadImage(imagePath);
                var window = new Window
                {
                    Title = "Xem ảnh: " + Path.GetFileName(imagePath),
                    Content = viewer,
                    Width = 1000,
                    Height = 720,
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Không thể mở ảnh xử lý lỗi", ex);
                ToastManager.ShowToast("Không thể xem ảnh", ex.Message, ToastType.Error);
            }
        }

        private void ManualProcessJob(ProcessingJobStatusItem job)
        {
            if (job?.CanManualProcess != true) return;
            var dialog = new window.ManualPlateProcessingWindow(job)
            {
                Owner = Application.Current?.MainWindow
            };
            if (dialog.ShowDialog() != true) return;

            string error;
            if (DocumentProcessingManager.Instance.TryQueueManualRetry(job, dialog.PlateNumber, out error))
            {
                ToastManager.ShowToast("Đã đưa vào xử lý", $"Biển số xác nhận: {dialog.PlateNumber}", ToastType.Info);
                RefreshProcessingJobs();
            }
            else
            {
                ToastManager.ShowToast("Không thể xử lý lại", error, ToastType.Error);
            }
        }

        private void OpenProcessingJobFolder(ProcessingJobStatusItem job)
        {
            string folder = job?.FolderToOpen;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                ToastManager.ShowToast("Không thể mở thư mục", "Đường dẫn không còn tồn tại.", ToastType.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Không thể mở thư mục xử lý", ex);
                ToastManager.ShowToast("Không thể mở thư mục", ex.Message, ToastType.Error);
            }
        }

        private void ShowProcessingError(ProcessingJobStatusItem job)
        {
            if (job?.IsError != true) return;
            string error = (job.ErrorMessage ?? string.Empty).ToLowerInvariant();
            string title;
            string message;

            if (error.Contains("connect") || error.Contains("server") || error.Contains("timeout") ||
                error.Contains("http") || error.Contains("401") || error.Contains("unauthorized") ||
                error.Contains("máy chủ") || error.Contains("kết nối"))
            {
                title = "Máy chủ AI không kết nối";
                message = "Không thể kết nối đến máy chủ AI. Vui lòng kiểm tra kết nối và thử lại.";
            }
            else if (error.Contains("nhận dạng") || error.Contains("biển số") || error.Contains("plate") ||
                     error.Contains("unknown") || error.Contains("confidence"))
            {
                title = "Lỗi không nhận dạng";
                message = "Không nhận dạng được biển số hoặc thông tin cần thiết trong ảnh.";
                ToastManager.ShowToast(title, message, ToastType.Warning);
                return;
            }
            else if (error.Contains("không thể di chuyển")|| error.Contains("đổi tên file"))
            {
                title = "File lỗi";
                message = "Lỗi khi di chuyển file.";
            }
           
            else
            {
                title = "File lỗi";
                message = "File ảnh không hợp lệ, không còn tồn tại hoặc không thể đọc được.";
            }

            ToastManager.ShowToast(title, message, ToastType.Error);
        }

        private void RefreshFolders()
        {
            var recent = _folderMonitor.GetRecentFolders(RecentResultLimit);
            Folders.Clear();
            foreach (var f in recent)
            {
                Folders.Add(f);
            }
        }

        private void RefreshOutputFolders()
        {
            var searchText = (FolderSearchText ?? string.Empty).Trim();
            int count = _folderMonitor.GetFolderCount(searchText);
            OutputTotalPages = Math.Max(1, (int)Math.Ceiling(count / (double)OutputPageSize));
            OutputCurrentPage = Math.Max(1, Math.Min(OutputCurrentPage, OutputTotalPages));
            var folders = _folderMonitor.GetFolderPage(
                OutputPageSize,
                (OutputCurrentPage - 1) * OutputPageSize,
                searchText,
                SelectedOutputFolderSort);

            if (string.IsNullOrWhiteSpace(NewestOutputFolderPath))
            {
                var newest = _folderMonitor.GetRecentFolders(1).FirstOrDefault();
                if (newest != null && DateTime.Now - newest.LastUpdatedAt <= TimeSpan.FromMinutes(5))
                {
                    NewestOutputFolderPath = newest.FolderPath;
                    NewFolderBadgeExpiresAt = newest.LastUpdatedAt.AddMinutes(5);
                }
            }

            OutputFolders.Clear();
            foreach (var folder in folders)
            {
                OutputFolders.Add(folder);
            }
        }

        private void MarkOutputFolderAsNew(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;
            NewestOutputFolderPath = folderPath;
            NewFolderBadgeExpiresAt = DateTime.Now.AddMinutes(5);
        }

        private void SelectLatestResult()
        {
            var latestFolder = OutputFolders.FirstOrDefault();
            if (latestFolder == null)
            {
                SelectedFolder = null;
                return;
            }

            SelectedFolder = latestFolder;
            SelectedOutputFile = latestFolder.OutputFiles.FirstOrDefault();
        }

        private void RefreshRecentCaptures()
        {
            var captures = _inputCaptureMonitor.GetRecentCaptures(10);
            RecentCaptures.Clear();
            foreach (var capture in captures)
                RecentCaptures.Add(capture);
        }

        private void ScheduleLiveSearch()
        {
            if (_searchDebounceTimer == null) return;
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void OpenSelectedFile()
        {
            var filePath = SelectedOutputFile?.FilePath;
            var folderPath = SelectedFolder?.FolderPath;

            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
                    return;
                }

                if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folderPath}\"") { UseShellExecute = true });
                    return;
                }

                ToastManager.ShowToast("Không thể mở thư mục", "Hãy chọn một thư mục hoặc file còn tồn tại.", ToastType.Error);
            }
            catch (Exception ex)
            {
                LoggerManager.LogError($"Không thể mở thư mục hoặc file: {filePath ?? folderPath}", ex);
                ToastManager.ShowToast("Không thể mở Explorer", ex.Message, ToastType.Error);
            }
        }

        private void SavePlateCorrection()
        {
            if (SelectedFolder == null) return;

            string newPlateRaw = NormalizePlate(EditedPlateNumber);
            if (string.IsNullOrWhiteSpace(newPlateRaw))
            {
                EditStatusMessage = "Biển số không hợp lệ.";
                return;
            }

            string newPlate = ApplyPlateReplacementRules(newPlateRaw);
            string oldFolder = SelectedFolder.FolderPath;
            string oldPlate = SelectedFolder.PlateNumber;
            string parent = Path.GetDirectoryName(oldFolder);
            string newFolder = Path.Combine(parent, newPlate);
            if (!string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase) && Directory.Exists(newFolder))
            {
                EditStatusMessage = "Đã tồn tại thư mục của biển số này.";
                return;
            }

            _folderMonitor.Stop();
            try
            {
                var renamedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.GetFiles(oldFolder))
                {
                    string renamed = ReplacePlateInFileName(Path.GetFileName(file), oldPlate, newPlateRaw);
                    string target = Path.Combine(oldFolder, renamed);
                    if (!string.Equals(file, target, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(target))
                            throw new IOException("Trùng tên file sau khi áp dụng naming rule: " + renamed);
                        File.Move(file, target);
                    }
                    renamedFiles[file] = Path.Combine(newFolder, renamed);
                }

                if (!string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
                    Directory.Move(oldFolder, newFolder);

                foreach (var renamedFile in renamedFiles)
                    _dbHelper.UpdateProcessedFilePath(renamedFile.Key, renamedFile.Value);
                _dbHelper.UpdatePlateFolder(oldFolder, newFolder, oldPlate, newPlate);
                EditStatusMessage = "Đã cập nhật biển số, tên file và cơ sở dữ liệu.";
                IsEditingPlate = false;
                RefreshFolders();
                RefreshOutputFolders();
                SelectedFolder = OutputFolders.FirstOrDefault(f =>
                    string.Equals(f.FolderPath, newFolder, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                EditStatusMessage = "Cập nhật thất bại: " + ex.Message;
                LoggerManager.LogError("Lỗi cập nhật biển số thủ công", ex);
            }
            finally
            {
                _folderMonitor.Start();
            }
        }

        private static string NormalizePlate(string plate)
        {
            if (string.IsNullOrWhiteSpace(plate)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            return new string(plate.Trim().ToUpperInvariant()
                .Where(c => !invalid.Contains(c) && !char.IsWhiteSpace(c)).ToArray());
        }

        private string ApplyPlateReplacementRules(string plate)
        {
            string result = plate ?? string.Empty;
            var plateSegment = _plateNamingRule?.Segments?.FirstOrDefault(s =>
                s.SegmentType == "Field" && s.Value == "bien_so");
            if (plateSegment?.ReplacementRules == null) return result;

            foreach (var replacement in plateSegment.ReplacementRules)
            {
                if (string.IsNullOrEmpty(replacement.SourceChars)) continue;
                foreach (char sourceChar in replacement.SourceChars)
                    result = result.Replace(sourceChar.ToString(), replacement.TargetChar ?? string.Empty);
            }
            return result;
        }

        private string ReplacePlateInFileName(string fileName, string oldPlate, string newPlate)
        {
            if (string.IsNullOrWhiteSpace(oldPlate)) return fileName;
            string oldRuleValue = ApplyPlateReplacementRules(oldPlate);
            string newRuleValue = ApplyPlateReplacementRules(newPlate);
            string oldCompact = new string(oldPlate.Where(char.IsLetterOrDigit).ToArray());
            string newCompact = new string(newPlate.Where(char.IsLetterOrDigit).ToArray());

            var replacements = new[]
            {
                new { Old = oldRuleValue, New = newRuleValue },
                new { Old = oldCompact, New = newCompact },
                new { Old = oldPlate, New = newPlate }
            }.Where(x => !string.IsNullOrWhiteSpace(x.Old))
             .OrderByDescending(x => x.Old.Length);

            string result = fileName;
            foreach (var replacement in replacements)
                result = Regex.Replace(result, Regex.Escape(replacement.Old), replacement.New, RegexOptions.IgnoreCase);
            return result;
        }

        private void InitializeDocumentCamera()
        {
            _rtspStreamService = new RtspStreamService();
            _rtspStreamService.FrameReceived += OnDocumentCameraFrameReceived;
            _rtspStreamService.StatusChanged += OnDocumentCameraStatusChanged;
        }

        private void StartDocumentCamera()
        {
            if (!IsDocumentMode || _rtspStreamService == null || _isDisposed)
                return;

            if (_rtspStreamService.IsRunning)
            {
                IsDocumentCameraRunning = true;
                return;
            }

            try
            {
                DocumentCameraNotification = "";
                DocumentCameraStatus = "Đang mở camera...";
                _rtspStreamService.StartPreview(RtspStreamService.ResolveBundledFfmpegPath(), DocumentRtspUrl);
                IsDocumentCameraRunning = true;
            }
            catch (Exception ex)
            {
                IsDocumentCameraRunning = false;
                DocumentCameraStatus = "Không mở được camera.";
                DocumentCameraNotification = "Không mở được camera: " + ex.Message;
                LoggerManager.LogError("Không mở được luồng camera chụp ảnh hồ sơ", ex);
            }
        }

        private void RestartDocumentCamera()
        {
            if (!IsDocumentMode || _rtspStreamService == null)
                return;

            LatestDocumentCameraFrame = null;
            _rtspStreamService.StopPreview();
            StartDocumentCamera();
        }

        private void OpenDocumentCameraWindow()
        {
            if (!IsDocumentMode)
                return;

            var window = new UI.Views.DocumentCameraCaptureWindow
            {
                Owner = Application.Current?.MainWindow,
                DataContext = this
            };
            window.Show();
        }

        private async System.Threading.Tasks.Task TakeDocumentPictureAsync()
        {
            if (!IsDocumentMode || _rtspStreamService == null)
                return;

            try
            {
                IsTakingDocumentPicture = true;
                DocumentCameraNotification = "";

                LatestFrameSnapshot frameSnapshot;
                if (!_rtspStreamService.TryGetLatestFrameSnapshot(out frameSnapshot))
                {
                    DocumentCameraNotification = "Chưa có khung hình từ camera.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(InputDirectory))
                {
                    DocumentCameraNotification = "Chưa cấu hình thư mục input hồ sơ.";
                    return;
                }

                Directory.CreateDirectory(InputDirectory);
                var targetPath = GetUniqueDocumentSnapshotPath(frameSnapshot.Timestamp);
                var snapshot = CapturedSnapshot.FromFrame(targetPath, frameSnapshot.PreviewImage, frameSnapshot.Timestamp);
                snapshot.IsSaving = true;
                DocumentSnapshots.Insert(0, snapshot);

                await System.Threading.Tasks.Task.Run(() => File.WriteAllBytes(targetPath, frameSnapshot.JpegBytes));

                snapshot.IsSaving = false;
                DocumentCameraNotification = "Đã chụp ảnh vào input: " + snapshot.FileName;
                RefreshRecentCaptures();
                RefreshProcessingJobs();
            }
            catch (Exception ex)
            {
                DocumentCameraNotification = "Chụp ảnh thất bại: " + ex.Message;
                LoggerManager.LogError("Không thể chụp ảnh hồ sơ từ camera", ex);
            }
            finally
            {
                IsTakingDocumentPicture = false;
            }
        }

        private string GetUniqueDocumentSnapshotPath(DateTime timestamp)
        {
            var baseName = "docscan_" + timestamp.ToString("yyyyMMdd_HHmmssfff");
            var candidate = Path.Combine(InputDirectory, baseName + ".jpg");
            var index = 1;

            while (File.Exists(candidate))
            {
                candidate = Path.Combine(InputDirectory, baseName + "_" + index + ".jpg");
                index++;
            }

            return candidate;
        }

        private void DeleteDocumentSnapshot(CapturedSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            try
            {
                DocumentSnapshots.Remove(snapshot);
                if (!string.IsNullOrWhiteSpace(snapshot.FilePath) && File.Exists(snapshot.FilePath))
                    File.Delete(snapshot.FilePath);
                DocumentCameraNotification = "Đã xóa ảnh chụp.";
                RefreshRecentCaptures();
                RefreshProcessingJobs();
            }
            catch (Exception ex)
            {
                DocumentCameraNotification = "Không xóa được ảnh: " + ex.Message;
                LoggerManager.LogError("Không thể xóa ảnh chụp hồ sơ", ex);
            }
        }

        private void SaveDocumentRtsp()
        {
            try
            {
                var config = new VMDocumentConfig
                {
                    ReloadServicesOnSave = false,
                    DocumentRtspUrl = DocumentRtspUrl
                };
                config.SaveCommand.Execute(null);
                IsDocumentCameraSettingsVisible = false;
                DocumentCameraNotification = "Đã lưu link stream.";
                RestartDocumentCamera();
            }
            catch (Exception ex)
            {
                DocumentCameraNotification = "Không lưu được link stream: " + ex.Message;
                LoggerManager.LogError("Không thể lưu link stream chụp hồ sơ", ex);
            }
        }

        private void OnDocumentCameraFrameReceived(ImageSource frame)
        {
            Application.Current.Dispatcher.Invoke(() => LatestDocumentCameraFrame = frame);
        }

        private void OnDocumentCameraStatusChanged(string status)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DocumentCameraStatus = status;
                IsDocumentCameraRunning = _rtspStreamService != null && _rtspStreamService.IsRunning;
            });
        }

        private void OpenConfig()
        {
            var configWin = new Window
            {
                Title = string.Empty,
                Width = 1280,
                Height = 840,
                MinWidth = 1100,
                MinHeight = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 15, 18))
            };

            var layout = new System.Windows.Controls.Grid();
            layout.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(36) });
            layout.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var darkHeader = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 19, 24)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 40, 48)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            darkHeader.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    configWin.DragMove();
            };

            var closeButton = new System.Windows.Controls.Button
            {
                Content = "✕",
                Width = 42,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 170, 184)),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            closeButton.Click += (s, e) => configWin.Close();
            darkHeader.Child = closeButton;

            var content = new ucs.ucDocumentConfig();
            System.Windows.Controls.Grid.SetRow(darkHeader, 0);
            System.Windows.Controls.Grid.SetRow(content, 1);
            layout.Children.Add(darkHeader);
            layout.Children.Add(content);
            configWin.Content = layout;
            configWin.ShowDialog();
        }

        private async System.Threading.Tasks.Task SetServiceRunningAsync(bool shouldRun)
        {
            if (IsServiceChanging || IsServiceRunning == shouldRun) return;

            IsServiceChanging = true;
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    if (shouldRun)
                        DocumentProcessingManager.Instance.Start();
                    else
                        DocumentProcessingManager.Instance.Stop();
                });
            }
            catch (Exception ex)
            {
                LoggerManager.LogError(shouldRun
                    ? "Không thể khởi động dịch vụ AI phân loại và nhận diện"
                    : "Không thể dừng dịch vụ AI phân loại và nhận diện", ex);
            }
            finally
            {
                IsServiceRunning = DocumentProcessingManager.Instance.IsRunning;
                IsServiceChanging = false;
            }
        }

        private async System.Threading.Tasks.Task BrowseAndReloadDirectoryAsync(bool isInputDirectory)
        {
            string currentPath = isInputDirectory ? InputDirectory : OutputDirectory;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = isInputDirectory
                    ? "Chọn thư mục lắng nghe ảnh biển số"
                    : "Chọn thư mục output sau xử lý",
                SelectedPath = Directory.Exists(currentPath) ? currentPath : string.Empty,
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK ||
                    string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;

                string selectedPath = Path.GetFullPath(dialog.SelectedPath);
                if (string.Equals(currentPath, selectedPath, StringComparison.OrdinalIgnoreCase)) return;

                var config = new VMDocumentConfig();
                if (IsDocumentMode)
                {
                    if (isInputDirectory)
                        config.InputDirectoryDocument = selectedPath;
                    else
                        config.OutputDirectoryDocument = selectedPath;
                }
                else
                {
                    if (isInputDirectory)
                        config.InputDirectoryPlate = selectedPath;
                    else
                        config.OutputDirectoryPlate = selectedPath;
                }

                config.ReloadServicesOnSave = false;
                config.SaveCommand.Execute(null);
                bool wasRunning = DocumentProcessingManager.Instance.IsRunning;
                IsServiceChanging = true;
                try
                {
                    await System.Threading.Tasks.Task.Run(() => ReloadProcessingServices(config, wasRunning));

                    InputDirectory = IsDocumentMode ? config.InputDirectoryDocument : config.InputDirectoryPlate;
                    OutputDirectory = IsDocumentMode ? config.OutputDirectoryDocument : config.OutputDirectoryPlate;
                    UpdateStorageUsage();
                    RecreateFolderMonitors();
                    RefreshFolders();
                    RefreshOutputFolders();
                    SelectLatestResult();
                    RefreshRecentCaptures();
                    RefreshPersistentStatistics();
                }
                catch (Exception ex)
                {
                    LoggerManager.LogError("Không thể tải lại dịch vụ theo đường dẫn mới", ex);
                }
                finally
                {
                    IsServiceRunning = DocumentProcessingManager.Instance.IsRunning;
                    IsServiceChanging = false;
                }
            }
        }

        private static void ReloadProcessingServices(VMDocumentConfig config, bool shouldRestart)
        {
            DocumentProcessingManager.Instance.Stop();
            config.AutoStart = false;
            DocumentProcessingManager.Instance.Initialize(config);
            if (shouldRestart)
                DocumentProcessingManager.Instance.Start();
        }

        private void RecreateFolderMonitors()
        {
            _folderMonitor.OnFolderChanged -= FolderMonitor_OnFolderChanged;
            _folderMonitor.Dispose();
            _inputCaptureMonitor.CapturesChanged -= InputCaptureMonitor_CapturesChanged;
            _inputCaptureMonitor.Dispose();

            _folderMonitor = new OutputFolderMonitorService(OutputDirectory, _dbHelper);
            _folderMonitor.OnFolderChanged += FolderMonitor_OnFolderChanged;
            _folderMonitor.Start();

            _inputCaptureMonitor = new InputCaptureMonitorService(InputDirectory);
            _inputCaptureMonitor.CapturesChanged += InputCaptureMonitor_CapturesChanged;
            _inputCaptureMonitor.Start();
        }

        public void Dispose()
        {
            _isDisposed = true;
            _searchDebounceTimer?.Stop();
            _newFolderBadgeTimer?.Stop();
            _storageUsageTimer?.Stop();
            _storageCounter?.Dispose();
            DocumentProcessingManager.Instance.OnProcessingStatusChanged -= Instance_OnProcessingStatusChanged;
            DocumentProcessingManager.Instance.OnProcessingUpdated -= Instance_OnProcessingUpdated;
            DocumentProcessingManager.Instance.RunningStateChanged -= Instance_RunningStateChanged;
            _folderMonitor.Stop();
            _folderMonitor.Dispose();
            _inputCaptureMonitor.CapturesChanged -= InputCaptureMonitor_CapturesChanged;
            _inputCaptureMonitor.Dispose();
            if (_rtspStreamService != null)
            {
                _rtspStreamService.FrameReceived -= OnDocumentCameraFrameReceived;
                _rtspStreamService.StatusChanged -= OnDocumentCameraStatusChanged;
                _rtspStreamService.Dispose();
                _rtspStreamService = null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
