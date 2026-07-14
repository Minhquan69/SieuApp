using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using V3SClient.libs;
using V3SClient.models;

namespace V3SClient.Services
{
    public class DatabaseHelper
    {
        private readonly string _dbPath;
        private readonly string _connectionString;
        private static readonly object DatabaseWriteLock = new object();

        public DatabaseHelper(string databasePath = null)
        {
            var appDataPath = string.IsNullOrWhiteSpace(databasePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V3SClient", "Data")
                : Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }
            
            _dbPath = string.IsNullOrWhiteSpace(databasePath)
                ? Path.Combine(appDataPath, "DocumentProcessing.db")
                : Path.GetFullPath(databasePath);
            _connectionString = $"Data Source={_dbPath};Version=3;Default Timeout=15;Pooling=True;";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            if (!File.Exists(_dbPath))
                SQLiteConnection.CreateFile(_dbPath);

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var pragmas = new SQLiteCommand(@"
                    PRAGMA journal_mode=WAL;
                    PRAGMA synchronous=NORMAL;
                    PRAGMA busy_timeout=15000;
                    PRAGMA foreign_keys=ON;", connection))
                    pragmas.ExecuteNonQuery();

                using (var command = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS ProcessingRecords (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FilePath TEXT NOT NULL,
                        FileName TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        DocumentType TEXT,
                        LicensePlate TEXT,
                        ErrorMessage TEXT,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    );", connection))
                    command.ExecuteNonQuery();

                EnsureColumn(connection, "ProcessingRecords", "OriginalFilePath", "TEXT");
                EnsureColumn(connection, "ProcessingRecords", "OriginalFileName", "TEXT");
                EnsureColumn(connection, "ProcessingRecords", "SourceFolder", "TEXT");

                using (var command = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS ProcessingJobs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        JobKey TEXT NOT NULL UNIQUE,
                        SourcePath TEXT NOT NULL,
                        SourceType TEXT,
                        Status TEXT NOT NULL,
                        LicensePlate TEXT,
                        OutputFolder TEXT,
                        RequiredAssetCount INTEGER NOT NULL DEFAULT 0,
                        CompletedAssetCount INTEGER NOT NULL DEFAULT 0,
                        ErrorAssetCount INTEGER NOT NULL DEFAULT 0,
                        AttemptCount INTEGER NOT NULL DEFAULT 0,
                        LastError TEXT,
                        CreatedAt DATETIME NOT NULL,
                        StartedAt DATETIME,
                        CompletedAt DATETIME,
                        UpdatedAt DATETIME NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS ProcessingAssets (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        JobId INTEGER NOT NULL,
                        AssetKey TEXT NOT NULL,
                        Role TEXT,
                        SourcePath TEXT,
                        OutputPath TEXT,
                        Status TEXT NOT NULL,
                        ErrorMessage TEXT,
                        FileSize INTEGER NOT NULL DEFAULT 0,
                        CreatedAt DATETIME NOT NULL,
                        UpdatedAt DATETIME NOT NULL,
                        UNIQUE(JobId, AssetKey),
                        FOREIGN KEY(JobId) REFERENCES ProcessingJobs(Id)
                    );
                    CREATE INDEX IF NOT EXISTS IDX_Status ON ProcessingRecords (Status);
                    CREATE INDEX IF NOT EXISTS IDX_LicensePlate ON ProcessingRecords (LicensePlate);
                    CREATE INDEX IF NOT EXISTS IDX_SourceFolder ON ProcessingRecords (SourceFolder);
                    CREATE INDEX IF NOT EXISTS IDX_UpdatedAt ON ProcessingRecords (UpdatedAt);
                    CREATE INDEX IF NOT EXISTS IDX_Jobs_Status_UpdatedAt ON ProcessingJobs (Status, UpdatedAt);
                    CREATE INDEX IF NOT EXISTS IDX_Jobs_CompletedAt ON ProcessingJobs (CompletedAt);
                    CREATE INDEX IF NOT EXISTS IDX_Jobs_LicensePlate ON ProcessingJobs (LicensePlate);
                    CREATE INDEX IF NOT EXISTS IDX_Assets_Job_Status ON ProcessingAssets (JobId, Status);
                    CREATE INDEX IF NOT EXISTS IDX_Assets_OutputPath ON ProcessingAssets (OutputPath);", connection))
                    command.ExecuteNonQuery();

                EnsureColumn(connection, "ProcessingJobs", "ManualPlate", "TEXT");
            }
        }

        private static void EnsureColumn(SQLiteConnection connection, string tableName, string columnName, string columnType)
        {
            using (var command = new SQLiteCommand($"PRAGMA table_info({tableName});", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            using (var command = new SQLiteCommand($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType};", connection))
                command.ExecuteNonQuery();
        }

        private void InitializeDatabaseLegacy()
        {
            try
            {
                if (!File.Exists(_dbPath))
                {
                    SQLiteConnection.CreateFile(_dbPath);
                }

                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    string createTableQuery = @"
                        CREATE TABLE IF NOT EXISTS ProcessingRecords (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            FilePath TEXT NOT NULL,
                            FileName TEXT NOT NULL,
                            Status TEXT NOT NULL,
                            DocumentType TEXT,
                            LicensePlate TEXT,
                            ErrorMessage TEXT,
                            CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                            UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                        );";

                    using (var command = new SQLiteCommand(createTableQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }

                    // Tạo index
                    string createIndexQuery = @"
                        CREATE INDEX IF NOT EXISTS IDX_Status ON ProcessingRecords (Status);
                        CREATE INDEX IF NOT EXISTS IDX_LicensePlate ON ProcessingRecords (LicensePlate);
                        CREATE INDEX IF NOT EXISTS IDX_SourceFolder ON ProcessingRecords (SourceFolder);
                        CREATE INDEX IF NOT EXISTS IDX_UpdatedAt ON ProcessingRecords (UpdatedAt);
                    ";
                    using (var command = new SQLiteCommand(createIndexQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }

                    // Thêm cột nếu chưa tồn tại (Backward compatibility)
                    try {
                        using (var cmd = new SQLiteCommand("ALTER TABLE ProcessingRecords ADD COLUMN OriginalFilePath TEXT", connection)) { cmd.ExecuteNonQuery(); }
                    } catch { /* Column exists */ }
                    
                    try {
                        using (var cmd = new SQLiteCommand("ALTER TABLE ProcessingRecords ADD COLUMN OriginalFileName TEXT", connection)) { cmd.ExecuteNonQuery(); }
                    } catch { /* Column exists */ }
                    
                    try {
                        using (var cmd = new SQLiteCommand("ALTER TABLE ProcessingRecords ADD COLUMN SourceFolder TEXT", connection)) { cmd.ExecuteNonQuery(); }
                    } catch { /* Column exists */ }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi khởi tạo SQLite database", ex);
            }
        }

        public void InsertRecord(string originalFilePath, string targetFilePath, string status, string documentType = "", string licensePlate = "")
        {
            try
            {
                string originalFileName = Path.GetFileName(originalFilePath);
                string targetFileName = Path.GetFileName(targetFilePath);
                string sourceFolder = Directory.Exists(originalFilePath) ? originalFilePath : Path.GetDirectoryName(originalFilePath);

                lock (DatabaseWriteLock)
                {
                    using (var connection = new SQLiteConnection(_connectionString))
                    {
                        connection.Open();
                        string query = @"INSERT INTO ProcessingRecords (OriginalFilePath, OriginalFileName, FilePath, FileName, Status, DocumentType, LicensePlate, SourceFolder, CreatedAt, UpdatedAt)
                                     VALUES (@OriginalFilePath, @OriginalFileName, @FilePath, @FileName, @Status, @DocumentType, @LicensePlate, @SourceFolder, @CreatedAt, @UpdatedAt)";
                        using (var command = new SQLiteCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@OriginalFilePath", originalFilePath);
                            command.Parameters.AddWithValue("@OriginalFileName", originalFileName);
                            command.Parameters.AddWithValue("@FilePath", targetFilePath);
                            command.Parameters.AddWithValue("@FileName", targetFileName);
                            command.Parameters.AddWithValue("@Status", status);
                            command.Parameters.AddWithValue("@DocumentType", documentType);
                            command.Parameters.AddWithValue("@LicensePlate", licensePlate);
                            command.Parameters.AddWithValue("@SourceFolder", sourceFolder);
                            command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                            command.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError($"Lỗi khi InsertRecord: {originalFilePath}", ex);
            }
        }
        public string GetRecordStatus(string filePath)
        {
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    string query = "SELECT Status FROM ProcessingRecords WHERE FilePath = @FilePath ORDER BY Id DESC LIMIT 1";
                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@FilePath", filePath);
                        var result = command.ExecuteScalar();
                        return result?.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError($"Lỗi khi GetRecordStatus: {filePath}", ex);
                return null;
            }
        }

        public string GetFolderStatus(string folderPath)
        {
            try
            {
                // Kiểm tra xem có bản ghi "Thành công" nào cho folder này không
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    string query = "SELECT Status FROM ProcessingRecords WHERE SourceFolder = @Folder AND Status = 'Thành công' LIMIT 1";
                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Folder", folderPath);
                        var result = command.ExecuteScalar();
                        return result != null ? "Thành công" : null;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError($"Lỗi khi GetFolderStatus: {folderPath}", ex);
                return null;
            }
        }

        public void UpdatePlateFolder(string oldFolderPath, string newFolderPath, string oldPlate, string newPlate)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                using (var command = new SQLiteCommand(@"
                    UPDATE ProcessingRecords
                    SET LicensePlate = @NewPlate,
                        SourceFolder = CASE WHEN SourceFolder = @OldFolder THEN @NewFolder ELSE SourceFolder END,
                        FilePath = REPLACE(FilePath, @OldFolder, @NewFolder),
                        OriginalFilePath = REPLACE(OriginalFilePath, @OldFolder, @NewFolder),
                        UpdatedAt = @UpdatedAt
                    WHERE LicensePlate = @OldPlate OR SourceFolder = @OldFolder OR FilePath LIKE @OldFolderLike;", connection, transaction))
                {
                    command.Parameters.AddWithValue("@NewPlate", newPlate);
                    command.Parameters.AddWithValue("@OldPlate", oldPlate ?? string.Empty);
                    command.Parameters.AddWithValue("@OldFolder", oldFolderPath);
                    command.Parameters.AddWithValue("@NewFolder", newFolderPath);
                    command.Parameters.AddWithValue("@OldFolderLike", oldFolderPath + "%");
                    command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.ExecuteNonQuery();
                    transaction.Commit();
                }
            }
        }

        public void UpdateProcessedFilePath(string oldFilePath, string newFilePath)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"
                    UPDATE ProcessingRecords
                    SET FilePath = @NewPath, FileName = @NewName, UpdatedAt = @UpdatedAt
                    WHERE FilePath = @OldPath;", connection))
                {
                    command.Parameters.AddWithValue("@OldPath", oldFilePath);
                    command.Parameters.AddWithValue("@NewPath", newFilePath);
                    command.Parameters.AddWithValue("@NewName", Path.GetFileName(newFilePath));
                    command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.ExecuteNonQuery();
                }
            }
        }

        public long RegisterJob(string sourcePath, bool isFolder, string sourceType, int requiredAssetCount = 0)
        {
            string fullPath = NormalizePath(sourcePath);
            string jobKey = BuildJobKey(fullPath, isFolder);
            DateTime now = DateTime.Now;

            lock (DatabaseWriteLock)
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand(@"
                    INSERT OR IGNORE INTO ProcessingJobs
                        (JobKey, SourcePath, SourceType, Status, RequiredAssetCount, CreatedAt, UpdatedAt)
                    VALUES (@JobKey, @SourcePath, @SourceType, 'Discovered', @RequiredAssetCount, @Now, @Now);", connection))
                    {
                        command.Parameters.AddWithValue("@JobKey", jobKey);
                        command.Parameters.AddWithValue("@SourcePath", fullPath);
                        command.Parameters.AddWithValue("@SourceType", sourceType ?? string.Empty);
                        command.Parameters.AddWithValue("@RequiredAssetCount", Math.Max(0, requiredAssetCount));
                        command.Parameters.AddWithValue("@Now", now);
                        command.ExecuteNonQuery();
                    }

                    using (var command = new SQLiteCommand(@"
                    UPDATE ProcessingJobs
                    SET RequiredAssetCount = CASE WHEN @RequiredAssetCount > RequiredAssetCount THEN @RequiredAssetCount ELSE RequiredAssetCount END,
                        UpdatedAt = @Now
                    WHERE JobKey = @JobKey;
                    SELECT Id FROM ProcessingJobs WHERE JobKey = @JobKey LIMIT 1;", connection))
                    {
                        command.Parameters.AddWithValue("@JobKey", jobKey);
                        command.Parameters.AddWithValue("@RequiredAssetCount", Math.Max(0, requiredAssetCount));
                        command.Parameters.AddWithValue("@Now", now);
                        return Convert.ToInt64(command.ExecuteScalar());
                    }
                }
            }
        }

        public bool TryMarkJobQueued(long jobId)
        {
            lock (DatabaseWriteLock)
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand(@"
                        UPDATE ProcessingJobs SET Status = 'Queued', UpdatedAt = @Now
                        WHERE Id = @Id AND Status IN ('Discovered', 'RetryPending', 'Failed');", connection))
                    {
                        command.Parameters.AddWithValue("@Id", jobId);
                        command.Parameters.AddWithValue("@Now", DateTime.Now);
                        return command.ExecuteNonQuery() == 1;
                    }
                }
            }
        }

        public void RegisterJobAsset(long jobId, string sourcePath, string role = null)
        {
            if (jobId <= 0 || string.IsNullOrWhiteSpace(sourcePath)) return;
            string fullPath = NormalizePath(sourcePath);
            long fileSize = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
            DateTime now = DateTime.Now;
            lock (DatabaseWriteLock)
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand(@"
                        INSERT OR IGNORE INTO ProcessingAssets
                            (JobId, AssetKey, Role, SourcePath, Status, FileSize, CreatedAt, UpdatedAt)
                        VALUES (@JobId, @AssetKey, @Role, @SourcePath, 'Discovered', @FileSize, @Now, @Now);", connection))
                    {
                        command.Parameters.AddWithValue("@JobId", jobId);
                        command.Parameters.AddWithValue("@AssetKey", BuildAssetKey(fullPath));
                        command.Parameters.AddWithValue("@Role", role ?? ResolveRoleFromFileName(fullPath));
                        command.Parameters.AddWithValue("@SourcePath", fullPath);
                        command.Parameters.AddWithValue("@FileSize", fileSize);
                        command.Parameters.AddWithValue("@Now", now);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        public void MarkJobProcessing(long jobId)
        {
            UpdateJobStatus(jobId, "Processing", null, incrementAttempt: true);
        }

        public void MarkJobRetryPending(long jobId, string error)
        {
            UpdateJobStatus(jobId, "RetryPending", error, incrementAttempt: false);
        }

        public void MarkJobFailed(long jobId, string error)
        {
            UpdateJobStatus(jobId, "Failed", error, incrementAttempt: false);
        }

        public void MarkJobRejected(long jobId, string reason)
        {
            UpdateJobStatus(jobId, "Rejected", reason, incrementAttempt: false);
        }

        public void MarkJobIgnored(long jobId, string reason)
        {
            UpdateJobStatus(jobId, "Ignored", reason, incrementAttempt: false);
        }

        public bool PrepareManualRetry(long jobId, string manualPlate)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"
                    UPDATE ProcessingJobs
                    SET Status = 'Queued', ManualPlate = @ManualPlate, LastError = NULL, UpdatedAt = @Now
                    WHERE Id = @Id AND Status = 'Failed';", connection))
                {
                    command.Parameters.AddWithValue("@Id", jobId);
                    command.Parameters.AddWithValue("@ManualPlate", manualPlate ?? string.Empty);
                    command.Parameters.AddWithValue("@Now", DateTime.Now);
                    return command.ExecuteNonQuery() == 1;
                }
            }
        }

        public string GetManualPlate(long jobId)
        {
            if (jobId <= 0) return string.Empty;
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand("SELECT ManualPlate FROM ProcessingJobs WHERE Id = @Id LIMIT 1;", connection))
                {
                    command.Parameters.AddWithValue("@Id", jobId);
                    return command.ExecuteScalar()?.ToString() ?? string.Empty;
                }
            }
        }

        public int GetVisibleProcessingJobCount(string searchText = null, string statusFilter = null, string sourceTypeFilter = null)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"
                    SELECT COUNT(*)
                    FROM ProcessingJobs j
                    LEFT JOIN ProcessingAssets a ON a.JobId = j.Id
                    WHERE j.Status IN ('Discovered', 'Queued', 'Processing', 'RetryPending', 'Failed', 'Rejected', 'Completed')
                      AND (a.Id IS NOT NULL OR j.JobKey NOT LIKE 'folder|%' OR j.Status IN ('Failed', 'Rejected'))
                      AND (@Search = '' OR j.SourcePath LIKE @Search OR a.SourcePath LIKE @Search)
                      AND (@StatusFilter = ''
                           OR (@StatusFilter = 'SUCCESS' AND j.Status = 'Completed')
                           OR (@StatusFilter = 'ACTIVE' AND j.Status IN ('Discovered', 'Queued', 'Processing', 'RetryPending'))
                           OR (@StatusFilter = 'ERROR' AND j.Status IN ('Failed', 'Rejected')))
                      AND (@SourceTypeFilter = '' OR j.SourceType = @SourceTypeFilter);", connection))
                {
                    command.Parameters.AddWithValue("@Search", string.IsNullOrWhiteSpace(searchText) ? string.Empty : "%" + searchText.Trim() + "%");
                    command.Parameters.AddWithValue("@StatusFilter", statusFilter ?? string.Empty);
                    command.Parameters.AddWithValue("@SourceTypeFilter", sourceTypeFilter ?? string.Empty);
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        public int GetActiveProcessingJobCount(string sourceTypeFilter = null)
        {
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand(@"
                        SELECT COUNT(*)
                        FROM ProcessingJobs
                        WHERE Status IN ('Discovered', 'Queued', 'Processing', 'RetryPending')
                          AND (@SourceTypeFilter = '' OR SourceType = @SourceTypeFilter);", connection))
                    {
                        command.Parameters.AddWithValue("@SourceTypeFilter", sourceTypeFilter ?? string.Empty);
                        return Convert.ToInt32(command.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Không thể đếm danh sách đang xử lý", ex);
                return 0;
            }
        }

        public List<ProcessingJobStatusItem> GetVisibleProcessingJobs(int limit = 100, int offset = 0, string searchText = null, string statusFilter = null, string sourceTypeFilter = null)
        {
            var result = new List<ProcessingJobStatusItem>();
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var pragma = new SQLiteCommand("PRAGMA busy_timeout=2000;", connection))
                    pragma.ExecuteNonQuery();
                using (var command = new SQLiteCommand(@"
                    SELECT j.Id, j.JobKey, j.SourcePath, j.SourceType, j.Status,
                           j.LastError, COALESCE(a.UpdatedAt, j.UpdatedAt), COALESCE(a.FileSize, 0),
                           j.LicensePlate, j.OutputFolder,
                           COALESCE(NULLIF(a.SourcePath, ''), j.SourcePath) AS DisplayFilePath,
                           COALESCE(a.OutputPath, '') AS DisplayOutputFilePath
                    FROM ProcessingJobs j
                    LEFT JOIN ProcessingAssets a ON a.JobId = j.Id
                    WHERE j.Status IN ('Discovered', 'Queued', 'Processing', 'RetryPending', 'Failed', 'Rejected', 'Completed')
                      AND (a.Id IS NOT NULL OR j.JobKey NOT LIKE 'folder|%' OR j.Status IN ('Failed', 'Rejected'))
                      AND (@Search = '' OR j.SourcePath LIKE @Search OR a.SourcePath LIKE @Search)
                      AND (@StatusFilter = ''
                           OR (@StatusFilter = 'SUCCESS' AND j.Status = 'Completed')
                           OR (@StatusFilter = 'ACTIVE' AND j.Status IN ('Discovered', 'Queued', 'Processing', 'RetryPending'))
                           OR (@StatusFilter = 'ERROR' AND j.Status IN ('Failed', 'Rejected')))
                      AND (@SourceTypeFilter = '' OR j.SourceType = @SourceTypeFilter)
                    ORDER BY
                        CASE j.Status
                            WHEN 'Processing' THEN 0
                            WHEN 'Queued' THEN 1
                            WHEN 'Discovered' THEN 2
                            WHEN 'RetryPending' THEN 3
                            WHEN 'Completed' THEN 4
                            WHEN 'Failed' THEN 5
                            ELSE 6
                        END,
                        COALESCE(a.UpdatedAt, j.UpdatedAt) DESC,
                        a.Id ASC
                    LIMIT @Limit OFFSET @Offset;", connection))
                {
                    command.Parameters.AddWithValue("@Limit", Math.Max(1, Math.Min(500, limit)));
                    command.Parameters.AddWithValue("@Offset", Math.Max(0, offset));
                    command.Parameters.AddWithValue("@Search", string.IsNullOrWhiteSpace(searchText) ? string.Empty : "%" + searchText.Trim() + "%");
                    command.Parameters.AddWithValue("@StatusFilter", statusFilter ?? string.Empty);
                    command.Parameters.AddWithValue("@SourceTypeFilter", sourceTypeFilter ?? string.Empty);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string sourcePath = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                            bool isFolder = !reader.IsDBNull(1) && reader.GetString(1).StartsWith("folder|", StringComparison.OrdinalIgnoreCase);
                            long size = reader.IsDBNull(7) ? 0L : Convert.ToInt64(reader.GetValue(7));
                            string displayFilePath = reader.IsDBNull(10) ? sourcePath : reader.GetString(10);
                            if (Directory.Exists(displayFilePath))
                            {
                                try
                                {
                                    displayFilePath = Directory.EnumerateFiles(displayFilePath, "*.*", SearchOption.TopDirectoryOnly)
                                        .FirstOrDefault(IsImageFile) ?? displayFilePath;
                                }
                                catch { }
                            }
                            if (size <= 0 && File.Exists(displayFilePath))
                            {
                                try { size = new FileInfo(displayFilePath).Length; } catch { }
                            }

                            result.Add(new ProcessingJobStatusItem
                            {
                                Id = reader.GetInt64(0),
                                SourcePath = sourcePath,
                                SourceType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                Status = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                                ErrorMessage = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                                UpdatedAt = reader.IsDBNull(6) ? DateTime.MinValue : Convert.ToDateTime(reader.GetValue(6)),
                                FileSize = size,
                                IsFolder = isFolder,
                                LicensePlate = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                                OutputFolder = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                                DisplayFilePath = displayFilePath,
                                DisplayOutputFilePath = reader.IsDBNull(11) ? string.Empty : reader.GetString(11)
                            });
                        }
                    }
                }
            }
            return result;
        }

        public void MarkJobCompleted(long jobId, string licensePlate, string outputFolder)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"
                    UPDATE ProcessingJobs
                    SET Status = 'Completed', LicensePlate = @Plate, OutputFolder = @OutputFolder,
                        CompletedAt = @Now, UpdatedAt = @Now, LastError = NULL
                    WHERE Id = @Id;", connection))
                {
                    command.Parameters.AddWithValue("@Id", jobId);
                    command.Parameters.AddWithValue("@Plate", licensePlate ?? string.Empty);
                    command.Parameters.AddWithValue("@OutputFolder", outputFolder ?? string.Empty);
                    command.Parameters.AddWithValue("@Now", DateTime.Now);
                    command.ExecuteNonQuery();
                }
            }
        }

        public bool CompleteJobIfReady(long jobId, string licensePlate, string outputFolder)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"
                    UPDATE ProcessingJobs
                    SET Status = 'Completed', LicensePlate = @Plate, OutputFolder = @OutputFolder,
                        CompletedAt = @Now, UpdatedAt = @Now, LastError = NULL
                    WHERE Id = @Id
                      AND (RequiredAssetCount = 0 OR CompletedAssetCount >= RequiredAssetCount);", connection))
                {
                    command.Parameters.AddWithValue("@Id", jobId);
                    command.Parameters.AddWithValue("@Plate", licensePlate ?? string.Empty);
                    command.Parameters.AddWithValue("@OutputFolder", outputFolder ?? string.Empty);
                    command.Parameters.AddWithValue("@Now", DateTime.Now);
                    return command.ExecuteNonQuery() == 1;
                }
            }
        }

        public bool IsAssetCompleted(long jobId, string sourcePath)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"
                    SELECT 1 FROM ProcessingAssets
                    WHERE JobId = @JobId AND AssetKey = @AssetKey AND Status = 'Completed' LIMIT 1;", connection))
                {
                    command.Parameters.AddWithValue("@JobId", jobId);
                    command.Parameters.AddWithValue("@AssetKey", BuildAssetKey(sourcePath));
                    return command.ExecuteScalar() != null;
                }
            }
        }

        public void MarkAssetCompleted(long jobId, string sourcePath, string outputPath, string role)
        {
            long fileSize = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
            DateTime now = DateTime.Now;
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    using (var insert = new SQLiteCommand(@"
                        INSERT OR IGNORE INTO ProcessingAssets
                            (JobId, AssetKey, Role, SourcePath, OutputPath, Status, FileSize, CreatedAt, UpdatedAt)
                        VALUES (@JobId, @AssetKey, @Role, @SourcePath, @OutputPath, 'Completed', @FileSize, @Now, @Now);", connection, transaction))
                    {
                        insert.Parameters.AddWithValue("@JobId", jobId);
                        insert.Parameters.AddWithValue("@AssetKey", BuildAssetKey(sourcePath));
                        insert.Parameters.AddWithValue("@Role", role ?? string.Empty);
                        insert.Parameters.AddWithValue("@SourcePath", NormalizePath(sourcePath));
                        insert.Parameters.AddWithValue("@OutputPath", NormalizePath(outputPath));
                        insert.Parameters.AddWithValue("@FileSize", fileSize);
                        insert.Parameters.AddWithValue("@Now", now);
                        insert.ExecuteNonQuery();
                    }

                    using (var update = new SQLiteCommand(@"
                        UPDATE ProcessingAssets SET Role = @Role, OutputPath = @OutputPath, Status = 'Completed',
                            ErrorMessage = NULL, FileSize = @FileSize, UpdatedAt = @Now
                        WHERE JobId = @JobId AND AssetKey = @AssetKey;
                        UPDATE ProcessingJobs SET
                            CompletedAssetCount = (SELECT COUNT(*) FROM ProcessingAssets WHERE JobId = @JobId AND Status = 'Completed'),
                            ErrorAssetCount = (SELECT COUNT(*) FROM ProcessingAssets WHERE JobId = @JobId AND Status = 'Failed'),
                            UpdatedAt = @Now
                        WHERE Id = @JobId;", connection, transaction))
                    {
                        update.Parameters.AddWithValue("@JobId", jobId);
                        update.Parameters.AddWithValue("@AssetKey", BuildAssetKey(sourcePath));
                        update.Parameters.AddWithValue("@Role", role ?? string.Empty);
                        update.Parameters.AddWithValue("@OutputPath", NormalizePath(outputPath));
                        update.Parameters.AddWithValue("@FileSize", fileSize);
                        update.Parameters.AddWithValue("@Now", now);
                        update.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        public void RecoverInterruptedJobs()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"
                    UPDATE ProcessingJobs SET Status = 'RetryPending',
                        LastError = CASE WHEN Status = 'Processing' THEN 'Ứng dụng dừng khi job đang xử lý' ELSE LastError END,
                        UpdatedAt = @Now
                    WHERE Status IN ('Queued', 'Processing');", connection))
                {
                    command.Parameters.AddWithValue("@Now", DateTime.Now);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void BackfillCompletedOutputFolders(string outputDirectory, string inputDirectory)
        {
            if (!Directory.Exists(outputDirectory)) return;
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var count = new SQLiteCommand("SELECT COUNT(*) FROM ProcessingJobs;", connection))
                {
                    if (Convert.ToInt32(count.ExecuteScalar()) > 0) return;
                }
            }

            foreach (string folder in Directory.EnumerateDirectories(outputDirectory))
            {
                string folderName = Path.GetFileName(folder);
                string matchingInput = string.IsNullOrWhiteSpace(inputDirectory)
                    ? null : Path.Combine(inputDirectory, folderName);
                string sourcePath = Directory.Exists(matchingInput) ? matchingInput : folder;
                var images = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(IsImageFile).ToList();
                if (images.Count == 0) continue;

                var remainingInputImages = Directory.Exists(matchingInput)
                    ? Directory.EnumerateFiles(matchingInput, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(IsImageFile).ToList()
                    : new List<string>();

                long jobId = RegisterJob(sourcePath, true, "Backfill", images.Count + remainingInputImages.Count);
                foreach (string image in images)
                    MarkAssetCompleted(jobId, image, image, ResolveRoleFromFileName(image));
                if (remainingInputImages.Count == 0)
                    MarkJobCompleted(jobId, folderName, folder);
                else
                    MarkJobRetryPending(jobId, "Phát hiện job xử lý dở; còn ảnh trong thư mục input");
            }
        }

        private void UpdateJobStatus(long jobId, string status, string error, bool incrementAttempt)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand($@"
                    UPDATE ProcessingJobs SET Status = @Status, LastError = @Error, UpdatedAt = @Now,
                        StartedAt = CASE WHEN @Status = 'Processing' THEN @Now ELSE StartedAt END,
                        AttemptCount = AttemptCount + {(incrementAttempt ? 1 : 0)}
                    WHERE Id = @Id;", connection))
                {
                    command.Parameters.AddWithValue("@Id", jobId);
                    command.Parameters.AddWithValue("@Status", status);
                    command.Parameters.AddWithValue("@Error", (object)error ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Now", DateTime.Now);
                    command.ExecuteNonQuery();
                }
            }
        }

        private static string BuildJobKey(string sourcePath, bool isFolder)
        {
            string normalized = NormalizePath(sourcePath).ToUpperInvariant();
            if (isFolder)
            {
                long creationTicks = Directory.Exists(sourcePath)
                    ? Directory.GetCreationTimeUtc(sourcePath).Ticks
                    : 0;
                return $"folder|{normalized}|{creationTicks}";
            }
            if (!File.Exists(sourcePath)) return "file|" + normalized;

            var file = new FileInfo(sourcePath);
            return $"file|{normalized}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        }

        private static string BuildAssetKey(string sourcePath)
        {
            return NormalizePath(sourcePath).ToUpperInvariant();
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return path.Trim(); }
        }

        private static bool IsImageFile(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveRoleFromFileName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path)?.ToLowerInvariant() ?? string.Empty;
            if (name.Contains("plate") || name.Contains("bsx")) return "LicensePlate";
            if (name.Contains("front") || name.Contains("truoc")) return "Front";
            if (name.Contains("rear") || name.Contains("back") || name.Contains("sau")) return "Rear";
            if (name.Contains("cabin") || name.Contains("khoang")) return "Cabin";
            if (name.Contains("under") || name.Contains("gam")) return "Undercarriage";
            if (name.Contains("cargo") || name.Contains("thung")) return "CargoBox";
            return "Other";
        }

        public (int success, int error, int unknownPlate) GetOverviewStatistics(string sourceTypeFilter = null)
        {
            int success = 0, error = 0, unknownPlate = 0;
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    using (var jobCountCommand = new SQLiteCommand("SELECT COUNT(*) FROM ProcessingJobs;", connection))
                    {
                        int jobCount = Convert.ToInt32(jobCountCommand.ExecuteScalar());
                        if (jobCount > 0)
                        {
                            using (var jobStatsCommand = new SQLiteCommand(@"
                                SELECT Status, LicensePlate, COUNT(*)
                                FROM ProcessingJobs
                                WHERE (@SourceTypeFilter = '' OR SourceType = @SourceTypeFilter)
                                GROUP BY Status, LicensePlate;", connection))
                            {
                                jobStatsCommand.Parameters.AddWithValue("@SourceTypeFilter", sourceTypeFilter ?? string.Empty);
                                using (var jobReader = jobStatsCommand.ExecuteReader())
                                {
                                    while (jobReader.Read())
                                    {
                                        string jobStatus = jobReader.IsDBNull(0) ? string.Empty : jobReader.GetString(0);
                                        string jobPlate = jobReader.IsDBNull(1) ? string.Empty : jobReader.GetString(1);
                                        int jobTotal = Convert.ToInt32(jobReader.GetValue(2));
                                        if (jobStatus == "Failed" || jobStatus == "Rejected")
                                            error += jobTotal;
                                        else if (jobStatus == "Completed")
                                        {
                                            if (string.IsNullOrWhiteSpace(jobPlate) || jobPlate.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0)
                                                unknownPlate += jobTotal;
                                            else
                                                success += jobTotal;
                                        }
                                    }
                                }
                            }
                            return (success, error, unknownPlate);
                        }
                    }

                    string query = @"
                        SELECT Status, LicensePlate, COUNT(*)
                        FROM ProcessingRecords
                        GROUP BY Status, LicensePlate;";
                    using (var command = new SQLiteCommand(query, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string status = reader.IsDBNull(0) ? "" : reader.GetString(0);
                            string plate = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            int count = reader.GetInt32(2);

                            if (status.Contains("Lỗi"))
                            {
                                error += count;
                            }
                            else if (status == "Thành công")
                            {
                                if (string.IsNullOrEmpty(plate) || plate.ToLower().Contains("unknown") || plate.Contains("Chưa nhận diện"))
                                {
                                    unknownPlate += count;
                                }
                                else
                                {
                                    success += count;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi GetOverviewStatistics", ex);
            }
            return (success, error, unknownPlate);
        }

        public (int success, int error, int unknownPlate) GetTodayStatistics(string sourceTypeFilter = null)
        {
            int success = 0, error = 0, unknownPlate = 0;
            try
            {
                using (var connection = new SQLiteConnection(_connectionString))
                {
                    connection.Open();
                    using (var command = new SQLiteCommand(@"
                        SELECT Status, LicensePlate, COUNT(*)
                        FROM ProcessingJobs
                        WHERE UpdatedAt >= @Today AND UpdatedAt < @Tomorrow
                          AND Status IN ('Completed', 'Failed', 'Rejected')
                          AND (@SourceTypeFilter = '' OR SourceType = @SourceTypeFilter)
                        GROUP BY Status, LicensePlate;", connection))
                    {
                        command.Parameters.AddWithValue("@Today", DateTime.Today);
                        command.Parameters.AddWithValue("@Tomorrow", DateTime.Today.AddDays(1));
                        command.Parameters.AddWithValue("@SourceTypeFilter", sourceTypeFilter ?? string.Empty);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string status = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                                string plate = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                                int count = Convert.ToInt32(reader.GetValue(2));
                                if (status == "Failed" || status == "Rejected") error += count;
                                else if (string.IsNullOrWhiteSpace(plate) || plate.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0) unknownPlate += count;
                                else success += count;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Lỗi khi GetTodayStatistics", ex);
            }
            return (success, error, unknownPlate);
        }
    }
}
