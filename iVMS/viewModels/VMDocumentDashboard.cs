using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.IO;
using System.Windows.Input;
using V3SClient.libs;
using V3SClient.ucs;

namespace V3SClient.viewModels
{
    public class ProcessingRecordModel : VMBase
    {
        public int Id { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string Status { get; set; }
        public string DocumentType { get; set; }
        public string LicensePlate { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalRuns { get; set; }
        public int ErrorCount { get; set; }
        public string DisplayStatus 
        {
            get 
            {
                if (!string.IsNullOrEmpty(Status) && Status.ToLower().Contains("lỗi")) 
                {
                    if (ErrorCount > 1) return $"{Status} ({ErrorCount} lần)";
                    return Status;
                }
                return Status;
            }
        }
    }

    public class VMDocumentDashboard : VMBase
    {
        private ObservableCollection<ProcessingRecordModel> _records;
        public ObservableCollection<ProcessingRecordModel> Records
        {
            get => _records;
            set
            {
                _records = value;
                OnPropertyChanged();
            }
        }

        private int _totalProcessed;
        public int TotalProcessed
        {
            get => _totalProcessed;
            set
            {
                _totalProcessed = value;
                OnPropertyChanged();
            }
        }

        private int _totalErrors;
        public int TotalErrors
        {
            get => _totalErrors;
            set
            {
                _totalErrors = value;
                OnPropertyChanged();
            }
        }

        private DateTime? _filterStartDate;
        public DateTime? FilterStartDate
        {
            get => _filterStartDate;
            set
            {
                _filterStartDate = value;
                OnPropertyChanged();
            }
        }

        private DateTime? _filterEndDate;
        public DateTime? FilterEndDate
        {
            get => _filterEndDate;
            set
            {
                _filterEndDate = value;
                OnPropertyChanged();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ToggleRunningCommand { get; }
        public ICommand OpenImageCommand { get; }

        public bool IsRunning => V3SClient.Services.DocumentProcessingManager.Instance.IsRunning;

        public VMDocumentDashboard()
        {
            Records = new ObservableCollection<ProcessingRecordModel>();
            RefreshCommand = new RelayCommand(LoadData);
            ToggleRunningCommand = new RelayCommand(async (_) => 
            {
                if (V3SClient.Services.DocumentProcessingManager.Instance.IsRunning)
                {
                    await System.Threading.Tasks.Task.Run(() => V3SClient.Services.DocumentProcessingManager.Instance.Stop());
                }
                else
                {
                    await System.Threading.Tasks.Task.Run(() => V3SClient.Services.DocumentProcessingManager.Instance.Start());
                }
                OnPropertyChanged(nameof(IsRunning));
            });
            OpenImageCommand = new RelayCommand(OpenImage);
            
            // Mặc định xem trong 7 ngày gần nhất
            FilterStartDate = DateTime.Today.AddDays(-1);
            FilterEndDate = DateTime.Today.AddDays(1).AddSeconds(-1);
            
            LoadData(null);
        }

        public void SubscribeEvents()
        {
            V3SClient.Services.DocumentProcessingManager.Instance.OnProcessingUpdated += OnProcessingUpdated;
        }

        public void UnsubscribeEvents()
        {
            V3SClient.Services.DocumentProcessingManager.Instance.OnProcessingUpdated -= OnProcessingUpdated;
        }

        private void OnProcessingUpdated()
        {
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => LoadData(null)));
            }
        }

        private void OpenImage(object obj)
        {
            if (obj is string filePath && File.Exists(filePath))
            {
                try
                {
                    var viewer = new V3SClient.ucs.ucImageViewer();
                    viewer.LoadImage(filePath);
                    var window = new System.Windows.Window
                    {
                        Title = "Xem Ảnh: " + Path.GetFileName(filePath),
                        Content = viewer,
                        Width = 800,
                        Height = 600,
                        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
                    };
                    window.ShowDialog();
                }
                catch (Exception ex)
                {
                    LoggerManager.LogError("Lỗi khi mở ảnh", ex);
                    ToastManager.ShowToast("Lỗi", "Không thể mở ảnh này.", ToastType.Error);
                }
            }
            else
            {
                ToastManager.ShowToast("Lỗi", "File ảnh không tồn tại.", ToastType.Warning);
            }
        }

        private bool TryLoadJobData(SQLiteConnection connection)
        {
            using (var countCommand = new SQLiteCommand("SELECT COUNT(*) FROM ProcessingJobs;", connection))
            {
                if (Convert.ToInt32(countCommand.ExecuteScalar()) == 0) return false;
            }

            const string reportTime = "COALESCE(CompletedAt, UpdatedAt, CreatedAt)";
            string dateWhere = " WHERE 1 = 1";
            if (FilterStartDate.HasValue) dateWhere += $" AND {reportTime} >= @StartDate";
            if (FilterEndDate.HasValue) dateWhere += $" AND {reportTime} <= @EndDate";

            int totalJobs;
            int errorJobs;
            using (var command = new SQLiteCommand($@"
                SELECT COUNT(*), SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END)
                FROM ProcessingJobs {dateWhere};", connection))
            {
                AddDateParameters(command);
                using (var reader = command.ExecuteReader())
                {
                    reader.Read();
                    totalJobs = Convert.ToInt32(reader.GetValue(0));
                    errorJobs = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                }
            }

            var list = new ObservableCollection<ProcessingRecordModel>();
            using (var command = new SQLiteCommand($@"
                SELECT Id, SourcePath, SourceType, Status, LicensePlate, OutputFolder,
                       AttemptCount, LastError, CreatedAt
                FROM ProcessingJobs {dateWhere}
                ORDER BY {reportTime} DESC LIMIT 500;", connection))
            {
                AddDateParameters(command);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string sourcePath = reader["SourcePath"]?.ToString() ?? string.Empty;
                        string outputPath = reader["OutputFolder"] == DBNull.Value
                            ? string.Empty : reader["OutputFolder"].ToString();
                        string status = reader["Status"]?.ToString() ?? string.Empty;
                        list.Add(new ProcessingRecordModel
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            FilePath = string.IsNullOrWhiteSpace(outputPath) ? sourcePath : outputPath,
                            FileName = Path.GetFileName(sourcePath),
                            Status = ToDisplayStatus(status),
                            DocumentType = reader["SourceType"] == DBNull.Value ? string.Empty : reader["SourceType"].ToString(),
                            LicensePlate = reader["LicensePlate"] == DBNull.Value ? string.Empty : reader["LicensePlate"].ToString(),
                            ErrorMessage = reader["LastError"] == DBNull.Value ? string.Empty : reader["LastError"].ToString(),
                            CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                            TotalRuns = Convert.ToInt32(reader["AttemptCount"]),
                            ErrorCount = status == "Failed" ? 1 : 0
                        });
                    }
                }
            }

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                Records = list;
                TotalProcessed = totalJobs;
                TotalErrors = errorJobs;
            }));
            return true;
        }

        private void AddDateParameters(SQLiteCommand command)
        {
            if (FilterStartDate.HasValue) command.Parameters.AddWithValue("@StartDate", FilterStartDate.Value);
            if (FilterEndDate.HasValue) command.Parameters.AddWithValue("@EndDate", FilterEndDate.Value);
        }

        private static string ToDisplayStatus(string status)
        {
            switch (status)
            {
                case "Discovered": return "Đã phát hiện";
                case "Queued": return "Đang chờ";
                case "Processing": return "Đang xử lý";
                case "RetryPending": return "Chờ xử lý lại";
                case "Completed": return "Thành công";
                case "Failed": return "Lỗi xử lý";
                default: return status;
            }
        }

        public void LoadData(object obj)
        {
            try
            {
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V3SClient", "Data");
                string dbPath = Path.Combine(appDataPath, "DocumentProcessing.db");
                string connectionString = $"Data Source={dbPath};Version=3;";

                if (!File.Exists(dbPath)) return;

                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    if (TryLoadJobData(connection)) return;

                    // Lọc theo thời gian
                    string dateFilter = "";
                    if (FilterStartDate.HasValue && FilterEndDate.HasValue)
                    {
                        dateFilter = $" AND CreatedAt >= '{FilterStartDate.Value:yyyy-MM-dd HH:mm:ss}' AND CreatedAt <= '{FilterEndDate.Value:yyyy-MM-dd HH:mm:ss}'";
                    }
                    else if (FilterStartDate.HasValue)
                    {
                        dateFilter = $" AND CreatedAt >= '{FilterStartDate.Value:yyyy-MM-dd HH:mm:ss}'";
                    }
                    else if (FilterEndDate.HasValue)
                    {
                        dateFilter = $" AND CreatedAt <= '{FilterEndDate.Value:yyyy-MM-dd HH:mm:ss}'";
                    }

                    // Query 1: Lấy thông kê tổng quan (cho các file unique)
                    int totalFiles = 0;
                    int errorFiles = 0;
                    string statQuery = $@"
                        SELECT COUNT(*) as Total, 
                               SUM(CASE WHEN Status LIKE '%lỗi%' OR Status LIKE '%Lỗi%' THEN 1 ELSE 0 END) as Errors
                        FROM ProcessingRecords
                        WHERE Id IN (SELECT MAX(Id) FROM ProcessingRecords GROUP BY FileName) {dateFilter}";
                    
                    using (var cmdStat = new SQLiteCommand(statQuery, connection))
                    using (var readerStat = cmdStat.ExecuteReader())
                    {
                        if (readerStat.Read())
                        {
                            totalFiles = readerStat["Total"] != DBNull.Value ? Convert.ToInt32(readerStat["Total"]) : 0;
                            errorFiles = readerStat["Errors"] != DBNull.Value ? Convert.ToInt32(readerStat["Errors"]) : 0;
                        }
                    }

                    // Query 2: Lấy danh sách cho DataGrid (kèm số lần chạy/lỗi)
                    string query = $@"
                        SELECT 
                            r.*, 
                            Agg.TotalRuns, 
                            Agg.ErrorCount
                        FROM ProcessingRecords r
                        JOIN (
                            SELECT FileName, COUNT(*) as TotalRuns, SUM(CASE WHEN Status LIKE '%lỗi%' OR Status LIKE '%Lỗi%' THEN 1 ELSE 0 END) as ErrorCount
                            FROM ProcessingRecords
                            GROUP BY FileName
                        ) Agg ON r.FileName = Agg.FileName
                        WHERE r.Id IN (
                            SELECT MAX(Id) FROM ProcessingRecords GROUP BY FileName
                        ) {dateFilter}
                        ORDER BY r.CreatedAt DESC 
                        LIMIT 500";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            var list = new ObservableCollection<ProcessingRecordModel>();

                            while (reader.Read())
                            {
                                var record = new ProcessingRecordModel
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    FilePath = reader["FilePath"].ToString(),
                                    FileName = reader["FileName"].ToString(),
                                    Status = reader["Status"].ToString(),
                                    DocumentType = reader["DocumentType"] != DBNull.Value ? reader["DocumentType"].ToString() : "",
                                    LicensePlate = reader["LicensePlate"] != DBNull.Value ? reader["LicensePlate"].ToString() : "",
                                    ErrorMessage = reader["ErrorMessage"] != DBNull.Value ? reader["ErrorMessage"].ToString() : "",
                                    CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                                    TotalRuns = Convert.ToInt32(reader["TotalRuns"]),
                                    ErrorCount = reader["ErrorCount"] != DBNull.Value ? Convert.ToInt32(reader["ErrorCount"]) : 0
                                };
                                list.Add(record);
                            }

                            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                Records = list;
                                TotalProcessed = totalFiles;
                                TotalErrors = errorFiles;
                            }));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi tải dữ liệu Dashboard", ex);
            }
        }
    }
}
