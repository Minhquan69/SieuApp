using System;
using System.IO;
using System.Windows.Input;
using Newtonsoft.Json;
using V3SClient.libs;
using V3SClient.ucs;
using VehicleDocumentProcessing.WPF.Services;
using VehicleDocumentProcessing.WPF.Models;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using V3SClient.Services;

namespace V3SClient.viewModels
{
    public class VMDocumentConfig : VMBase
    {
        private string _configFilePath;
        private LLMConfig _llmConfig;

        public string ApiUrl
        {
            get => _llmConfig.ApiUrl;
            set
            {
                _llmConfig.ApiUrl = value;
                OnPropertyChanged();
            }
        }

        public string ApiKey
        {
            get => _llmConfig.ApiKey;
            set
            {
                _llmConfig.ApiKey = value;
                OnPropertyChanged();
            }
        }

        public string Model
        {
            get => string.IsNullOrWhiteSpace(_llmConfig.Model) ? LLMConfig.DefaultModel : _llmConfig.Model;
            set
            {
                _llmConfig.Model = string.IsNullOrWhiteSpace(value) ? LLMConfig.DefaultModel : value.Trim();
                OnPropertyChanged();
            }
        }

        private string _inputDirectoryPlate;
        public string InputDirectoryPlate
        {
            get => _inputDirectoryPlate;
            set
            {
                _inputDirectoryPlate = value;
                OnPropertyChanged();
            }
        }

        private string _inputDirectoryDocument;
        public string InputDirectoryDocument
        {
            get => _inputDirectoryDocument;
            set
            {
                _inputDirectoryDocument = value;
                OnPropertyChanged();
            }
        }

        private string _outputDirectoryPlate;
        public string OutputDirectoryPlate
        {
            get => _outputDirectoryPlate;
            set
            {
                _outputDirectoryPlate = value;
                OnPropertyChanged();
            }
        }

        private string _outputDirectoryDocument;
        public string OutputDirectoryDocument
        {
            get => _outputDirectoryDocument;
            set
            {
                _outputDirectoryDocument = value;
                OnPropertyChanged();
            }
        }

        private string _documentRtspUrl = "rtsp://127.0.0.1:8554/mobilestream";
        public string DocumentRtspUrl
        {
            get => string.IsNullOrWhiteSpace(_documentRtspUrl) ? "rtsp://127.0.0.1:8554/mobilestream" : _documentRtspUrl;
            set
            {
                _documentRtspUrl = string.IsNullOrWhiteSpace(value) ? "rtsp://127.0.0.1:8554/mobilestream" : value.Trim();
                OnPropertyChanged();
            }
        }

        private NamingRule _plateNamingRule;
        public NamingRule PlateNamingRule
        {
            get => _plateNamingRule;
            set
            {
                _plateNamingRule = value;
                OnPropertyChanged();
            }
        }

        private NamingRule _documentNamingRule;
        public NamingRule DocumentNamingRule
        {
            get => _documentNamingRule;
            set
            {
                _documentNamingRule = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<NamingFieldDefinition> _availablePlateFields;
        public ObservableCollection<NamingFieldDefinition> AvailablePlateFields
        {
            get => _availablePlateFields;
            set { _availablePlateFields = value; OnPropertyChanged(); }
        }

        private ObservableCollection<NamingFieldDefinition> _availableDocumentFields;
        public ObservableCollection<NamingFieldDefinition> AvailableDocumentFields
        {
            get => _availableDocumentFields;
            set { _availableDocumentFields = value; OnPropertyChanged(); }
        }

        private NamingFieldDefinition _selectedPlateField;
        public NamingFieldDefinition SelectedPlateField
        {
            get => _selectedPlateField;
            set { _selectedPlateField = value; OnPropertyChanged(); }
        }

        private NamingFieldDefinition _selectedDocumentField;
        public NamingFieldDefinition SelectedDocumentField
        {
            get => _selectedDocumentField;
            set { _selectedDocumentField = value; OnPropertyChanged(); }
        }

        private string _plateTextToAdd;
        public string PlateTextToAdd
        {
            get => _plateTextToAdd;
            set { _plateTextToAdd = value; OnPropertyChanged(); }
        }

        private string _documentTextToAdd;
        public string DocumentTextToAdd
        {
            get => _documentTextToAdd;
            set { _documentTextToAdd = value; OnPropertyChanged(); }
        }

        private string _plateNamingPreview;
        public string PlateNamingPreview
        {
            get => _plateNamingPreview;
            set { _plateNamingPreview = value; OnPropertyChanged(); }
        }

        private string _documentNamingPreview;
        public string DocumentNamingPreview
        {
            get => _documentNamingPreview;
            set { _documentNamingPreview = value; OnPropertyChanged(); }
        }

        private bool _isCopyMode;
        public bool IsCopyMode
        {
            get => _isCopyMode;
            set { _isCopyMode = value; OnPropertyChanged(); }
        }

        public ICommand AddPlateFieldCommand { get; private set; }
        public ICommand AddPlateTextCommand { get; private set; }
        public ICommand RemovePlateSegmentCommand { get; private set; }
        public ICommand MovePlateSegmentUpCommand { get; private set; }
        public ICommand MovePlateSegmentDownCommand { get; private set; }

        public ICommand AddDocumentFieldCommand { get; private set; }
        public ICommand AddDocumentTextCommand { get; private set; }
        public ICommand RemoveDocumentSegmentCommand { get; private set; }
        public ICommand MoveDocumentSegmentUpCommand { get; private set; }
        public ICommand MoveDocumentSegmentDownCommand { get; private set; }

        public ICommand AddReplacementRuleCommand { get; private set; }
        public ICommand RemoveReplacementRuleCommand { get; private set; }

        private ProcessingMode _processingMode = V3SClient.Services.ProcessingMode.Combined;
        public ProcessingMode ProcessingMode
        {
            get => _processingMode;
            set
            {
                _processingMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPlateModeEnabled));
                OnPropertyChanged(nameof(IsDocumentModeEnabled));
                OnPropertyChanged(nameof(IsCombinedMode));
                OnPropertyChanged(nameof(UsesSeparateInputDirectories));
                OnPropertyChanged(nameof(ShowPlateInputDirectory));
                OnPropertyChanged(nameof(ShowDocumentInputDirectory));
                OnPropertyChanged(nameof(UseSeparateDirectories));
            }
        }

        private InputLayout _inputLayout = V3SClient.Services.InputLayout.Shared;
        public InputLayout InputLayout
        {
            get => _inputLayout;
            set
            {
                _inputLayout = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UsesSeparateInputDirectories));
                OnPropertyChanged(nameof(ShowDocumentInputDirectory));
                OnPropertyChanged(nameof(UseSeparateDirectories));
            }
        }

        public bool IsPlateModeEnabled => ProcessingMode != V3SClient.Services.ProcessingMode.DocumentOnly;
        public bool IsDocumentModeEnabled => ProcessingMode != V3SClient.Services.ProcessingMode.PlateOnly;
        public bool IsCombinedMode => ProcessingMode == V3SClient.Services.ProcessingMode.Combined;
        public bool UsesSeparateInputDirectories => IsCombinedMode && InputLayout == V3SClient.Services.InputLayout.Separate;
        public bool ShowPlateInputDirectory => IsPlateModeEnabled;
        public bool ShowDocumentInputDirectory => ProcessingMode == V3SClient.Services.ProcessingMode.DocumentOnly || UsesSeparateInputDirectories;

        // Thuộc tính tương thích cấu hình cũ. Code mới dùng ProcessingMode + InputLayout.
        public bool UseSeparateDirectories
        {
            get => UsesSeparateInputDirectories;
            set
            {
                if (value)
                {
                    ProcessingMode = V3SClient.Services.ProcessingMode.Combined;
                    InputLayout = V3SClient.Services.InputLayout.Separate;
                }
                else if (ProcessingMode == V3SClient.Services.ProcessingMode.Combined)
                {
                    InputLayout = V3SClient.Services.InputLayout.Shared;
                }
                OnPropertyChanged();
            }
        }

        private bool _autoStart;
        public bool AutoStart
        {
            get => _autoStart;
            set
            {
                _autoStart = value;
                OnPropertyChanged();
            }
        }

        private bool _enableImageSync;
        public bool EnableImageSync
        {
            get => _enableImageSync;
            set { _enableImageSync = value; OnPropertyChanged(); }
        }

        private string _imageSyncBaseUrl;
        public string ImageSyncBaseUrl
        {
            get => _imageSyncBaseUrl;
            set { _imageSyncBaseUrl = value; OnPropertyChanged(); }
        }

        private string _imageSyncToken;
        public string ImageSyncToken
        {
            get => _imageSyncToken;
            set { _imageSyncToken = value; OnPropertyChanged(); }
        }

        private int _imageSyncIntervalSeconds = 5;
        public int ImageSyncIntervalSeconds
        {
            get => _imageSyncIntervalSeconds;
            set { _imageSyncIntervalSeconds = Math.Max(1, value); OnPropertyChanged(); }
        }

        private int _imageSyncPageSize = 100;
        public int ImageSyncPageSize
        {
            get => _imageSyncPageSize;
            set { _imageSyncPageSize = value; OnPropertyChanged(); }
        }

        private int _imageSyncDownloadParallelism = 4;
        public int ImageSyncDownloadParallelism
        {
            get => _imageSyncDownloadParallelism;
            set { _imageSyncDownloadParallelism = Math.Max(1, Math.Min(16, value)); OnPropertyChanged(); }
        }

        private int _maxParallelProcessingJobs = 2;
        public int MaxParallelProcessingJobs
        {
            get => _maxParallelProcessingJobs;
            set { _maxParallelProcessingJobs = Math.Max(1, Math.Min(8, value)); OnPropertyChanged(); }
        }

        private int _maxRetries;
        public int MaxRetries
        {
            get => _maxRetries;
            set
            {
                _maxRetries = value;
                OnPropertyChanged();
            }
        }

        private int _retryWaitTime;
        public int RetryWaitTime
        {
            get => _retryWaitTime;
            set
            {
                _retryWaitTime = value;
                OnPropertyChanged();
            }
        }

        private double _confidenceThreshold = 0.80;
        public double ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set { _confidenceThreshold = value; OnPropertyChanged(); }
        }

        private int _folderDebounceMs = 3000;
        public int FolderDebounceMs
        {
            get => _folderDebounceMs;
            set { _folderDebounceMs = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand BrowseDirectoryCommand { get; }
        public bool ReloadServicesOnSave { get; set; } = true;

        public VMDocumentConfig()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V3SClient", "Data");
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }
            _configFilePath = Path.Combine(appDataPath, "DocumentProcessingConfig.json");

            _llmConfig = new LLMConfig();
            SaveCommand = new RelayCommand(SaveConfig);
            TestConnectionCommand = new RelayCommand(TestConnection);
            BrowseDirectoryCommand = new RelayCommand(BrowseDirectory);

            InitializeAvailableFields();
            InitializeCommands();

            PlateNamingRule = new NamingRule();
            PlateNamingRule.Segments.CollectionChanged += (s, e) => UpdatePreviews();
            PlateNamingRule.PropertyChanged += (s, e) => UpdatePreviews();

            DocumentNamingRule = new NamingRule();
            DocumentNamingRule.Segments.CollectionChanged += (s, e) => UpdatePreviews();
            DocumentNamingRule.PropertyChanged += (s, e) => UpdatePreviews();

            LoadConfig();
        }

        private void BrowseDirectory(object parameter)
        {
            string target = parameter as string;
            string currentPath;
            switch (target)
            {
                case nameof(InputDirectoryPlate): currentPath = InputDirectoryPlate; break;
                case nameof(InputDirectoryDocument): currentPath = InputDirectoryDocument; break;
                case nameof(OutputDirectoryPlate): currentPath = OutputDirectoryPlate; break;
                case nameof(OutputDirectoryDocument): currentPath = OutputDirectoryDocument; break;
                default: return;
            }

            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Chọn thư mục";
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
                    dialog.SelectedPath = currentPath;

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                switch (target)
                {
                    case nameof(InputDirectoryPlate): InputDirectoryPlate = dialog.SelectedPath; break;
                    case nameof(InputDirectoryDocument): InputDirectoryDocument = dialog.SelectedPath; break;
                    case nameof(OutputDirectoryPlate): OutputDirectoryPlate = dialog.SelectedPath; break;
                    case nameof(OutputDirectoryDocument): OutputDirectoryDocument = dialog.SelectedPath; break;
                }
            }
        }

        private void InitializeAvailableFields()
        {
            AvailablePlateFields = new ObservableCollection<NamingFieldDefinition>
            {
                new NamingFieldDefinition { Key = "bien_so", DisplayName = "Biển số xe", Description = "Ký tự trên biển số", SampleValue = "30A-12345" },
                new NamingFieldDefinition { Key = "mau_bien_so", DisplayName = "Màu biển số", Description = "X=Xanh, V=Vàng, T=Trắng, D=Đỏ", SampleValue = "T" },
                new NamingFieldDefinition { Key = "loai_xe", DisplayName = "Loại xe", Description = "xe con, xe tải, xe buýt", SampleValue = "xe_con" },
                new NamingFieldDefinition { Key = "mau_xe", DisplayName = "Màu xe", Description = "Màu sơn xe nhìn thấy", SampleValue = "den" },
                new NamingFieldDefinition { Key = "goc_chup", DisplayName = "Góc chụp", Description = "truoc, sau, bsx", SampleValue = "truoc" },
                new NamingFieldDefinition { Key = "nhan_hieu", DisplayName = "Nhãn hiệu xe", Description = "Thương hiệu xe", SampleValue = "Toyota" },
                new NamingFieldDefinition { Key = "loai_giay_to", DisplayName = "Loại giấy tờ", Description = "Luôn = 'Ảnh xe ô tô'", SampleValue = "Anh_xe_o_to" },
                new NamingFieldDefinition { Key = "{timestamp}", DisplayName = "Thời gian", Description = "Tự động: HHmmss", SampleValue = "143025" },
                new NamingFieldDefinition { Key = "{date}", DisplayName = "Ngày", Description = "Tự động: yyyyMMdd", SampleValue = "20260702" },
                new NamingFieldDefinition { Key = "{guid}", DisplayName = "Mã ngẫu nhiên", Description = "6 ký tự random", SampleValue = "a3f2c1" }
            };

            AvailableDocumentFields = new ObservableCollection<NamingFieldDefinition>
            {
                new NamingFieldDefinition { Key = "bien_so", DisplayName = "Biển số xe", Description = "Biển số trích từ giấy tờ", SampleValue = "30A-12345" },
                new NamingFieldDefinition { Key = "loai_giay_to", DisplayName = "Loại giấy tờ", Description = "CCCD, GPLX, ĐKOT, Đơn A4", SampleValue = "CCCD" },
                new NamingFieldDefinition { Key = "ten_don", DisplayName = "Tên đơn", Description = "Tên tiêu đề (chỉ Đơn A4)", SampleValue = "Don_xin_cap_lai" },
                new NamingFieldDefinition { Key = "chu_xe", DisplayName = "Chủ xe", Description = "Tên chủ xe", SampleValue = "Nguyen_Van_A" },
                new NamingFieldDefinition { Key = "so_giay_dang_ky", DisplayName = "Số giấy ĐK", Description = "Số chứng nhận ĐK", SampleValue = "ABC123" },
                new NamingFieldDefinition { Key = "nhan_hieu", DisplayName = "Nhãn hiệu", Description = "Nhãn hiệu xe trên giấy", SampleValue = "Honda" },
                new NamingFieldDefinition { Key = "{timestamp}", DisplayName = "Thời gian", Description = "Tự động: HHmmss", SampleValue = "143025" },
                new NamingFieldDefinition { Key = "{date}", DisplayName = "Ngày", Description = "Tự động: yyyyMMdd", SampleValue = "20260702" },
                new NamingFieldDefinition { Key = "{guid}", DisplayName = "Mã ngẫu nhiên", Description = "6 ký tự random", SampleValue = "a3f2c1" }
            };
        }

        private void InitializeCommands()
        {
            AddPlateFieldCommand = new RelayCommand(obj =>
            {
                if (SelectedPlateField != null)
                {
                    PlateNamingRule.Segments.Add(new NamingSegment { SegmentType = "Field", Value = SelectedPlateField.Key, DisplayName = SelectedPlateField.DisplayName });
                }
            });

            AddPlateTextCommand = new RelayCommand(obj =>
            {
                if (!string.IsNullOrWhiteSpace(PlateTextToAdd))
                {
                    PlateNamingRule.Segments.Add(new NamingSegment { SegmentType = "Text", Value = PlateTextToAdd, DisplayName = PlateTextToAdd });
                    PlateTextToAdd = string.Empty;
                }
            });

            RemovePlateSegmentCommand = new RelayCommand(obj =>
            {
                if (obj is NamingSegment segment) PlateNamingRule.Segments.Remove(segment);
            });

            MovePlateSegmentUpCommand = new RelayCommand(obj =>
            {
                if (obj is NamingSegment segment)
                {
                    int index = PlateNamingRule.Segments.IndexOf(segment);
                    if (index > 0) PlateNamingRule.Segments.Move(index, index - 1);
                }
            });

            MovePlateSegmentDownCommand = new RelayCommand(obj =>
            {
                if (obj is NamingSegment segment)
                {
                    int index = PlateNamingRule.Segments.IndexOf(segment);
                    if (index >= 0 && index < PlateNamingRule.Segments.Count - 1) PlateNamingRule.Segments.Move(index, index + 1);
                }
            });

            // Document Commands
            AddDocumentFieldCommand = new RelayCommand(obj =>
            {
                if (SelectedDocumentField != null)
                {
                    DocumentNamingRule.Segments.Add(new NamingSegment { SegmentType = "Field", Value = SelectedDocumentField.Key, DisplayName = SelectedDocumentField.DisplayName });
                }
            });

            AddDocumentTextCommand = new RelayCommand(obj =>
            {
                if (!string.IsNullOrWhiteSpace(DocumentTextToAdd))
                {
                    DocumentNamingRule.Segments.Add(new NamingSegment { SegmentType = "Text", Value = DocumentTextToAdd, DisplayName = DocumentTextToAdd });
                    DocumentTextToAdd = string.Empty;
                }
            });

            RemoveDocumentSegmentCommand = new RelayCommand(obj =>
            {
                if (obj is NamingSegment segment) DocumentNamingRule.Segments.Remove(segment);
            });

            MoveDocumentSegmentUpCommand = new RelayCommand(obj =>
            {
                if (obj is NamingSegment segment)
                {
                    int index = DocumentNamingRule.Segments.IndexOf(segment);
                    if (index > 0) DocumentNamingRule.Segments.Move(index, index - 1);
                }
            });

            MoveDocumentSegmentDownCommand = new RelayCommand(obj =>
            {
                if (obj is NamingSegment segment)
                {
                    int index = DocumentNamingRule.Segments.IndexOf(segment);
                    if (index >= 0 && index < DocumentNamingRule.Segments.Count - 1) DocumentNamingRule.Segments.Move(index, index + 1);
                }
            });

            AddReplacementRuleCommand = new RelayCommand(obj =>
            {
                if (obj is NamingSegment segment)
                {
                    segment.ReplacementRules.Add(new CharReplacementRule { SourceChars = "", TargetChar = "" });
                }
            });

            RemoveReplacementRuleCommand = new RelayCommand(obj =>
            {
                if (obj is CharReplacementRule rule)
                {
                    foreach (var seg in PlateNamingRule.Segments)
                    {
                        if (seg.ReplacementRules.Remove(rule)) return;
                    }
                    foreach (var seg in DocumentNamingRule.Segments)
                    {
                        seg.ReplacementRules.Remove(rule);
                    }
                }
            });
        }

        private void UpdatePreviews()
        {
            PlateNamingPreview = GeneratePreview(PlateNamingRule, AvailablePlateFields);
            DocumentNamingPreview = GeneratePreview(DocumentNamingRule, AvailableDocumentFields);
        }

        private string GeneratePreview(NamingRule rule, ObservableCollection<NamingFieldDefinition> availableFields)
        {
            if (rule == null || rule.Segments.Count == 0) return string.Empty;

            var parts = new System.Collections.Generic.List<string>();
            foreach (var segment in rule.Segments)
            {
                if (segment.SegmentType == "Text")
                {
                    parts.Add(segment.Value);
                }
                else if (segment.SegmentType == "Field")
                {
                    var field = availableFields.FirstOrDefault(f => f.Key == segment.Value);
                    string sampleVal = field != null ? field.SampleValue : $"{{{segment.Value}}}";
                    
                    // Apply character replacements in preview for this specific segment
                    foreach (var replacement in segment.ReplacementRules)
                    {
                        if (!string.IsNullOrEmpty(replacement.SourceChars))
                        {
                            foreach (char c in replacement.SourceChars)
                            {
                                sampleVal = sampleVal.Replace(c.ToString(), replacement.TargetChar ?? "");
                            }
                        }
                    }
                    parts.Add(sampleVal);
                }
            }
            
            return string.Join(rule.Separator ?? "_", parts) + ".jpg";
        }

        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var config = JsonConvert.DeserializeObject<dynamic>(json);
                    
                    if (config != null)
                    {
                        ApiUrl = config.ApiUrl ?? "";
                        ApiKey = config.ApiKey ?? "";
                        Model = config.Model == null ? LLMConfig.DefaultModel : config.Model.ToString();
                        InputDirectoryPlate = config.InputDirectoryPlate ?? "C:\\V3SClient_Input\\BienSo";
                        InputDirectoryDocument = config.InputDirectoryDocument ?? "C:\\V3SClient_Input\\GiayTo";
                        OutputDirectoryPlate = config.OutputDirectoryPlate ?? "C:\\V3SClient_Output\\BienSo";
                        OutputDirectoryDocument = config.OutputDirectoryDocument ?? "C:\\V3SClient_Output\\GiayTo";
                        DocumentRtspUrl = config.DocumentRtspUrl ?? config.RtspUrl ?? "rtsp://127.0.0.1:8554/mobilestream";
                        if (config.PlateNamingRule != null)
                        {
                            var ruleObj = config.PlateNamingRule;
                            PlateNamingRule = new NamingRule { Separator = ruleObj.Separator };
                            if (ruleObj.Segments != null)
                            {
                                foreach (var seg in ruleObj.Segments)
                                {
                                    var newSeg = new NamingSegment 
                                    { 
                                        SegmentType = seg.SegmentType, 
                                        Value = seg.Value, 
                                        DisplayName = seg.DisplayName
                                    };
                                    
                                    if (seg.ReplacementRules != null)
                                    {
                                        foreach (var r in seg.ReplacementRules)
                                        {
                                            newSeg.ReplacementRules.Add(new CharReplacementRule { SourceChars = (string)r.SourceChars, TargetChar = (string)r.TargetChar });
                                        }
                                    }
                                    else if (seg.ReplaceSourceChars != null)
                                    {
                                        newSeg.ReplacementRules.Add(new CharReplacementRule
                                        {
                                            SourceChars = (string)seg.ReplaceSourceChars,
                                            TargetChar = (string)seg.ReplaceTargetChar ?? ""
                                        });
                                    }
                                    PlateNamingRule.Segments.Add(newSeg);
                                }
                            }
                        }
                        else
                        {
                            // Backward compatibility: create default rule from old template if exists
                            CreateDefaultPlateRule(config.NamingTemplate?.ToString(), config.NamingSeparator?.ToString());
                        }

                        if (config.DocumentNamingRule != null)
                        {
                            var ruleObj = config.DocumentNamingRule;
                            DocumentNamingRule = new NamingRule { Separator = ruleObj.Separator };
                            if (ruleObj.Segments != null)
                            {
                                foreach (var seg in ruleObj.Segments)
                                {
                                    var newSeg = new NamingSegment 
                                    { 
                                        SegmentType = seg.SegmentType, 
                                        Value = seg.Value, 
                                        DisplayName = seg.DisplayName
                                    };
                                    
                                    if (seg.ReplacementRules != null)
                                    {
                                        foreach (var r in seg.ReplacementRules)
                                        {
                                            newSeg.ReplacementRules.Add(new CharReplacementRule { SourceChars = (string)r.SourceChars, TargetChar = (string)r.TargetChar });
                                        }
                                    }
                                    else if (seg.ReplaceSourceChars != null)
                                    {
                                        newSeg.ReplacementRules.Add(new CharReplacementRule
                                        {
                                            SourceChars = (string)seg.ReplaceSourceChars,
                                            TargetChar = (string)seg.ReplaceTargetChar ?? ""
                                        });
                                    }
                                    DocumentNamingRule.Segments.Add(newSeg);
                                }
                            }
                        }
                        else
                        {
                            CreateDefaultDocumentRule(config.NamingTemplate?.ToString(), config.NamingSeparator?.ToString());
                        }

                        string savedMode = config.ProcessingMode?.ToString();
                        V3SClient.Services.ProcessingMode parsedMode;
                        ProcessingMode = Enum.TryParse(savedMode, true, out parsedMode)
                            ? parsedMode
                            : V3SClient.Services.ProcessingMode.Combined;

                        string savedLayout = config.InputLayout?.ToString();
                        V3SClient.Services.InputLayout parsedLayout;
                        if (Enum.TryParse(savedLayout, true, out parsedLayout))
                        {
                            InputLayout = parsedLayout;
                        }
                        else
                        {
                            bool oldSeparate = config.UseSeparateDirectories ?? false;
                            InputLayout = oldSeparate
                                ? V3SClient.Services.InputLayout.Separate
                                : V3SClient.Services.InputLayout.Shared;
                        }
                        AutoStart = config.AutoStart ?? true;
                        MaxRetries = config.MaxRetries ?? 3;
                        RetryWaitTime = config.RetryWaitTime ?? 5;
                        IsCopyMode = config.IsCopyMode ?? false;
                        ConfidenceThreshold = config.ConfidenceThreshold ?? 0.80;
                        FolderDebounceMs = config.FolderDebounceMs ?? 3000;
                        EnableImageSync = config.EnableImageSync ?? false;
                        ImageSyncBaseUrl = config.ImageSyncBaseUrl ?? "";
                        ImageSyncToken = config.ImageSyncToken ?? "";
                        ImageSyncIntervalSeconds = config.ImageSyncIntervalSeconds ?? 5;
                        ImageSyncPageSize = config.ImageSyncPageSize ?? 100;
                        ImageSyncDownloadParallelism = config.ImageSyncDownloadParallelism ?? 4;
                        MaxParallelProcessingJobs = config.MaxParallelProcessingJobs ?? 2;
                    }
                }
                else
                {
                    // Default values
                    ApiUrl = "https://api.openai.com/v1/chat/completions";
                    ApiKey = "";
                    Model = LLMConfig.DefaultModel;
                    InputDirectoryPlate = "C:\\V3SClient_Input\\BienSo";
                    InputDirectoryDocument = "C:\\V3SClient_Input\\GiayTo";
                    OutputDirectoryPlate = "C:\\V3SClient_Output\\BienSo";
                    OutputDirectoryDocument = "C:\\V3SClient_Output\\GiayTo";
                    DocumentRtspUrl = "rtsp://127.0.0.1:8554/mobilestream";
                    CreateDefaultPlateRule(null, null);
                    CreateDefaultDocumentRule(null, null);
                    ProcessingMode = V3SClient.Services.ProcessingMode.Combined;
                    InputLayout = V3SClient.Services.InputLayout.Shared;
                    AutoStart = true;
                    MaxRetries = 3;
                    RetryWaitTime = 5;
                    IsCopyMode = false;
                    ConfidenceThreshold = 0.80;
                    FolderDebounceMs = 3000;
                    EnableImageSync = false;
                    ImageSyncBaseUrl = "";
                    ImageSyncToken = "";
                    ImageSyncIntervalSeconds = 5;
                    ImageSyncPageSize = 100;
                    ImageSyncDownloadParallelism = 4;
                    MaxParallelProcessingJobs = 2;
                }
                
                // Hook events again after loading
                PlateNamingRule.Segments.CollectionChanged -= (s, e) => UpdatePreviews();
                PlateNamingRule.Segments.CollectionChanged += (s, e) => UpdatePreviews();
                PlateNamingRule.PropertyChanged -= (s, e) => UpdatePreviews();
                PlateNamingRule.PropertyChanged += (s, e) => UpdatePreviews();

                DocumentNamingRule.Segments.CollectionChanged -= (s, e) => UpdatePreviews();
                DocumentNamingRule.Segments.CollectionChanged += (s, e) => UpdatePreviews();
                DocumentNamingRule.PropertyChanged -= (s, e) => UpdatePreviews();
                DocumentNamingRule.PropertyChanged += (s, e) => UpdatePreviews();
                
                UpdatePreviews();
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi tải cấu hình Document Processing", ex);
            }
        }

        private void CreateDefaultPlateRule(string oldTemplate, string oldSeparator)
        {
            PlateNamingRule = new NamingRule { Separator = oldSeparator ?? "_" };
            PlateNamingRule.Segments.Add(new NamingSegment { SegmentType = "Text", Value = "BSX", DisplayName = "BSX" });
            PlateNamingRule.Segments.Add(new NamingSegment { SegmentType = "Field", Value = "mau_bien_so", DisplayName = "Màu biển số" });
            var bienSoSegment = new NamingSegment { SegmentType = "Field", Value = "bien_so", DisplayName = "Biển số xe" };
            bienSoSegment.ReplacementRules.Add(new CharReplacementRule { SourceChars = "- .", TargetChar = "" });
            PlateNamingRule.Segments.Add(bienSoSegment);
            PlateNamingRule.Segments.Add(new NamingSegment { SegmentType = "Field", Value = "{timestamp}", DisplayName = "Thời gian" });
        }

        private void CreateDefaultDocumentRule(string oldTemplate, string oldSeparator)
        {
            DocumentNamingRule = new NamingRule { Separator = oldSeparator ?? "_" };
            DocumentNamingRule.Segments.Add(new NamingSegment { SegmentType = "Field", Value = "loai_giay_to", DisplayName = "Loại giấy tờ" });
            DocumentNamingRule.Segments.Add(new NamingSegment { SegmentType = "Field", Value = "bien_so", DisplayName = "Biển số xe" });
            DocumentNamingRule.Segments.Add(new NamingSegment { SegmentType = "Field", Value = "{timestamp}", DisplayName = "Thời gian" });
        }

        private void SaveConfig(object obj)
        {
            try
            {
                var config = new
                {
                    ApiUrl = this.ApiUrl,
                    ApiKey = this.ApiKey,
                    Model = this.Model,
                    InputDirectoryPlate = this.InputDirectoryPlate,
                    InputDirectoryDocument = this.InputDirectoryDocument,
                    OutputDirectoryPlate = this.OutputDirectoryPlate,
                    OutputDirectoryDocument = this.OutputDirectoryDocument,
                    DocumentRtspUrl = this.DocumentRtspUrl,
                    PlateNamingRule = new
                    {
                        Separator = this.PlateNamingRule.Separator,
                        Segments = this.PlateNamingRule.Segments.Select(s => new { SegmentType = s.SegmentType, Value = s.Value, DisplayName = s.DisplayName, ReplacementRules = s.ReplacementRules.Select(r => new { r.SourceChars, r.TargetChar }).ToList() }).ToList()
                    },
                    DocumentNamingRule = new
                    {
                        Separator = this.DocumentNamingRule.Separator,
                        Segments = this.DocumentNamingRule.Segments.Select(s => new { SegmentType = s.SegmentType, Value = s.Value, DisplayName = s.DisplayName, ReplacementRules = s.ReplacementRules.Select(r => new { r.SourceChars, r.TargetChar }).ToList() }).ToList()
                    },
                    ProcessingMode = this.ProcessingMode.ToString(),
                    InputLayout = this.InputLayout.ToString(),
                    UseSeparateDirectories = this.UseSeparateDirectories,
                    AutoStart = this.AutoStart,
                    MaxRetries = this.MaxRetries,
                    RetryWaitTime = this.RetryWaitTime,
                    IsCopyMode = this.IsCopyMode,
                    ConfidenceThreshold = this.ConfidenceThreshold,
                    FolderDebounceMs = this.FolderDebounceMs,
                    EnableImageSync = this.EnableImageSync,
                    ImageSyncBaseUrl = this.ImageSyncBaseUrl,
                    ImageSyncToken = this.ImageSyncToken,
                    ImageSyncIntervalSeconds = Math.Max(1, this.ImageSyncIntervalSeconds),
                    ImageSyncPageSize = Math.Max(1, Math.Min(500, this.ImageSyncPageSize)),
                    ImageSyncDownloadParallelism = Math.Max(1, Math.Min(16, this.ImageSyncDownloadParallelism)),
                    MaxParallelProcessingJobs = Math.Max(1, Math.Min(8, this.MaxParallelProcessingJobs))
                };

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                if (ReloadServicesOnSave)
                    Task.Run(() => DocumentProcessingManager.Instance.ReloadConfig(this));
                
                ToastManager.ShowToast("Thành công", "Đã lưu cấu hình phân loại tài liệu.", ToastType.Info);
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi lưu cấu hình Document Processing", ex);
                ToastManager.ShowToast("Lỗi", "Không thể lưu cấu hình.", ToastType.Error);
            }
        }

        private async void TestConnection(object obj)
        {
            try
            {
                ToastManager.ShowToast("Đang kiểm tra", "Đang gửi yêu cầu kiểm tra kết nối LLM...", ToastType.Info);
                
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    var payload = new
                    {
                        model = this.Model,
                        messages = new[]
                        {
                            new { role = "user", content = "Hello" }
                        }
                    };
                    
                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{this.ApiUrl}/v1/chat/completions");
                    if (!string.IsNullOrEmpty(this.ApiKey))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", this.ApiKey);
                    }
                    
                    string jsonPayload = JsonConvert.SerializeObject(payload);
                    request.Content = new System.Net.Http.StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                    
                    var response = await client.SendAsync(request);
                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    if (response.IsSuccessStatusCode)
                    {
                        ToastManager.ShowToast("Thành công", "Kết nối AI thành công!", ToastType.Success);
                    }
                    else
                    {
                        LoggerManager.LogError($"Lỗi kiểm tra kết nối AI: {response.StatusCode} - {responseContent}", null);
                        ToastManager.ShowToast("Lỗi kết nối", $"Lỗi API: {response.StatusCode}", ToastType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi Exception khi kiểm tra kết nối AI", ex);
                ToastManager.ShowToast("Lỗi kết nối", "Không thể kết nối đến máy chủ AI.", ToastType.Error);
            }
        }
    }
}
