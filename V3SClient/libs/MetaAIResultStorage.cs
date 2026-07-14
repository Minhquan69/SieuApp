using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using V3SClient.models;

namespace V3SClient.libs
{
    public class MetaAIResultStorage
    {
        private static readonly Lazy<MetaAIResultStorage> _instance = new Lazy<MetaAIResultStorage>(() => new MetaAIResultStorage());
        public static MetaAIResultStorage Instance => _instance.Value;

        private readonly string _dataFolder;
        private string _filePath;
        private string _configPath;
        private readonly object _lock = new object();
        private Timer _autoSaveTimer;

        public int MaxLoadMinutes { get; set; } = 5; // Cấu hình thời gian load mặc định
        public int MaxItemsInMemory { get; set; } = 100; // Giới hạn số lượng bản ghi trong RAM
        
        // Cấu hình lọc Metadata
        public List<string> AllowedClasses { get; set; } = new List<string> { "person", "car", "bus", "truck", "motorcycle", "bicycle", "face", "plate","anomaly" };
        public List<string> AllowedImageClasses { get; set; } = new List<string> { "plate" };
        public List<string> AllowedEvents { get; set; } = new List<string> { "object_appear" };
        public float MinConfidence { get; set; } = 0.5f;
        public bool RoiInfoShow { get; set; } = true;

        public List<MetaAIResult> GlobalResults { get; private set; } = new List<MetaAIResult>();

        private MetaAIResultStorage()
        {
            _dataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Meta");
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "metaai_settings.json");
            
            if (!Directory.Exists(_dataFolder))
                Directory.CreateDirectory(_dataFolder);

            LoadConfig();
            CleanupOldFiles();
            EnsureTodayFilePath();
            LoadData();
            StartAutoSave();
        }

        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    var config = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (config != null)
                    {
                        if (config.ContainsKey("MaxLoadMinutes")) MaxLoadMinutes = Convert.ToInt32(config["MaxLoadMinutes"]);
                        if (config.ContainsKey("MaxItemsInMemory")) MaxItemsInMemory = Convert.ToInt32(config["MaxItemsInMemory"]);
                        if (config.ContainsKey("MinConfidence")) MinConfidence = Convert.ToSingle(config["MinConfidence"]);
                        if (config.ContainsKey("RoiInfoShow")) RoiInfoShow = Convert.ToBoolean(config["RoiInfoShow"]);
                        
                        if (config.ContainsKey("AllowedClasses") && config["AllowedClasses"] is Newtonsoft.Json.Linq.JArray classes)
                            AllowedClasses = classes.Select(c => c.ToString()).ToList();
                            
                        if (config.ContainsKey("AllowedImageClasses") && config["AllowedImageClasses"] is Newtonsoft.Json.Linq.JArray imgClasses)
                            AllowedImageClasses = imgClasses.Select(c => c.ToString()).ToList();
                            
                        if (config.ContainsKey("AllowedEvents") && config["AllowedEvents"] is Newtonsoft.Json.Linq.JArray events)
                            AllowedEvents = events.Select(e => e.ToString()).ToList();
                    }
                }
            }
            catch { }
        }

        public void SaveConfig()
        {
            try
            {
                var config = new Dictionary<string, object>
                {
                    { "MaxLoadMinutes", MaxLoadMinutes },
                    { "MaxItemsInMemory", MaxItemsInMemory },
                    { "AllowedClasses", AllowedClasses },
                    { "AllowedImageClasses", AllowedImageClasses },
                    { "AllowedEvents", AllowedEvents },
                    { "MinConfidence", MinConfidence },
                    { "RoiInfoShow", RoiInfoShow }
                };
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch { }
        }

        private void EnsureTodayFilePath()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            _filePath = Path.Combine(_dataFolder, $"{today}.json");
        }

        public void AddResults(List<MetaAIResult> newResults)
        {
            if (newResults == null || newResults.Count == 0)
                return;

            lock (_lock)
            {
                GlobalResults.AddRange(newResults);
                
                // Prune if exceeds max items
                if (GlobalResults.Count > MaxItemsInMemory)
                {
                    int toRemove = GlobalResults.Count - MaxItemsInMemory;
                    GlobalResults.RemoveRange(0, toRemove);
                }
            }
        }

        public void SaveData()
        {
            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(_dataFolder))
                        Directory.CreateDirectory(_dataFolder);

                    string json = JsonConvert.SerializeObject(GlobalResults, Formatting.Indented);
                    File.WriteAllText(_filePath, json);
                }
            }
            catch (Exception ex)
            {
                // Handle error or log
                Debug.WriteLine("Error saving AI result data: " + ex.Message);
            }
        }

        public void LoadData()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(_filePath))
                    {
                        string json = File.ReadAllText(_filePath);
                        var allResults = JsonConvert.DeserializeObject<List<MetaAIResult>>(json) ?? new List<MetaAIResult>();
                        
                        // Lọc theo thời gian (MaxLoadMinutes)
                        DateTime cutoff = DateTime.Now.AddMinutes(-MaxLoadMinutes);
                        GlobalResults = allResults.Where(r => 
                        {
                            if (DateTime.TryParseExact(r.TimeStamp, "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out DateTime dt))
                                return dt >= cutoff;
                            return false; 
                        }).ToList();

                        // Đảm bảo không vượt quá giới hạn RAM ngay từ đầu
                        if (GlobalResults.Count > MaxItemsInMemory)
                        {
                            GlobalResults = GlobalResults.Skip(GlobalResults.Count - MaxItemsInMemory).ToList();
                        }
                    }
                    else
                    {
                        GlobalResults = new List<MetaAIResult>();
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle error or log
                Console.WriteLine("Error loading AI result data: " + ex.Message);
            }
        }

        private void StartAutoSave()
        {
            _autoSaveTimer = new Timer(_ =>
            {
                SaveData();
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        public void StopAutoSave()
        {
            _autoSaveTimer?.Dispose();
        }

        public void ResetDataIfNewDay()
        {
            lock (_lock)
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd");

                // Remove old files
                if (Directory.Exists(_dataFolder))
                {
                    foreach (var file in Directory.GetFiles(_dataFolder))
                    {
                        if (!file.Contains(today))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }

                GlobalResults.Clear();
                EnsureTodayFilePath();
                SaveData();
            }
        }
        private void CleanupOldFiles(int daysToKeep = 3)
        {
            try
            {
                if (!Directory.Exists(_dataFolder))
                    return;

                var files = Directory.GetFiles(_dataFolder, "*.json");

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < DateTime.Now.AddDays(-daysToKeep))
                    {
                        try
                        {
                            fileInfo.Delete();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Cannot delete old file: " + fileInfo.Name + " - " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error cleaning old files: " + ex.Message);
            }
        }

    }
}
