using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;

namespace V3SClient.libs
{
    public class SmartDownloadManager
    {
        public class DownloadTask : INotifyPropertyChanged
        {
            private double _progress;
            private string _status = "Pending";
            private string _speed;

            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string CameraNames { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string SavePath { get; set; }

            public double Progress
            {
                get => _progress;
                set
                {
                    _progress = value;
                    OnPropertyChanged("Progress");
                }
            }

            public string Status
            {
                get => _status;
                set
                {
                    _status = value;
                    OnPropertyChanged("Status");
                }
            }

            public string Speed
            {
                get => _speed;
                set
                {
                    _speed = value;
                    OnPropertyChanged("Speed");
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name)
            {
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        private static readonly Lazy<SmartDownloadManager> _instance = new Lazy<SmartDownloadManager>(() => new SmartDownloadManager());
        private readonly HttpClient _httpClient;
        private readonly int _maxParallelDownloads = 3;

        public static SmartDownloadManager Instance => _instance.Value;
        public ObservableCollection<DownloadTask> Tasks { get; } = new ObservableCollection<DownloadTask>();

        private SmartDownloadManager()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(30.0);
        }

        public async Task StartSmartDownloadAsync(List<string> cameraIds, DateTime start, DateTime end, string savePath, IProgress<(string cameraName, double progress, string speed)> progressReport = null, CancellationToken ct = default(CancellationToken))
        {
            DownloadTask task = new DownloadTask
            {
                CameraNames = string.Join(", ", cameraIds),
                StartTime = start,
                EndTime = end,
                SavePath = savePath,
                Status = "Searching..."
            };

            Application.Current.Dispatcher.Invoke(() =>
            {
                Tasks.Add(task);
            });

            try
            {
                ApiManager.PlaybackSearchResult searchResult = await ApiManager.Instance.SearchPlaybackAsync(cameraIds, start, end, ct);
                if (searchResult == null || !searchResult.Sessions.Any())
                {
                    task.Status = "No data";
                    throw new Exception("No video segments found for the requested period.");
                }

                task.Status = "Downloading...";
                List<Task> tasks = new List<Task>();
                foreach (ApiManager.PlaybackSessionInfo session in searchResult.Sessions)
                {
                    string camName = ((cameraIds.Count == 1) ? "Video" : (session.DeviceId ?? "Camera"));
                    tasks.Add(DownloadSessionAsync(session, camName, savePath, progressReport, task, ct));
                }
                await Task.WhenAll(tasks);
                task.Status = "Completed";
                task.Progress = 100.0;
            }
            catch (Exception ex)
            {
                task.Status = "Failed";
                Debug.WriteLine("SmartDownload Error: " + ex.Message);
                throw;
            }
        }

        private async Task DownloadSessionAsync(ApiManager.PlaybackSessionInfo session, string cameraName, string savePath, IProgress<(string cameraName, double progress, string speed)> progressReport, DownloadTask parentTask, CancellationToken ct)
        {
            ApiManager.PlaybackDownloadInfo downloadInfo = await ApiManager.Instance.GetPlaybackDownloadInfoAsync(session.SessionId, ct);
            if (downloadInfo == null || !downloadInfo.Parts.Any())
            {
                return;
            }

            long totalBytes = downloadInfo.Parts.Sum((ApiManager.PlaybackPartInfo p) => p.TotalSizeBytes);
            long totalDownloaded = 0L;
            DateTime startTime = DateTime.Now;
            string timeRange = $"{parentTask.StartTime:yyyyMMddHHmmss}_{parentTask.EndTime:yyyyMMddHHmmss}";
            string deviceId = session.DeviceId ?? "Unknown";

            foreach (ApiManager.PlaybackPartInfo part in downloadInfo.Parts)
            {
                string extension = ".mp4";
                string partIdx = part.PartIndex.ToString("D3");
                string fileName = "video_" + deviceId + "_" + timeRange + "_part" + partIdx + extension;

                await DownloadFileStreamedAsync(
                    url: ApiManager.Instance.StorageUrl.TrimEnd('/') + part.DownloadUrl,
                    destinationPath: Path.Combine(savePath, fileName),
                    progress: new Progress<long>(delegate (long read)
                    {
                        long currentTotal = totalDownloaded + read;
                        double percent = (double)currentTotal / (double)totalBytes * 100.0;
                        double totalSeconds = (DateTime.Now - startTime).TotalSeconds;
                        double speedMb = ((totalSeconds > 0.0) ? ((double)currentTotal / 1048576.0 / totalSeconds) : 0.0);
                        string speedText = $"{speedMb:F2} MB/s";

                        parentTask.Progress = percent;
                        parentTask.Speed = speedText;
                        progressReport?.Report((cameraName, percent, speedText));
                    }),
                    ct: ct);

                totalDownloaded += part.TotalSizeBytes;
            }
        }

        public async Task DownloadFileStreamedAsync(string url, string destinationPath, IProgress<long> progress = null, CancellationToken ct = default(CancellationToken))
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                string storageToken = ApiManager.Instance.StorageToken;
                if (!string.IsNullOrEmpty(storageToken))
                {
                    request.Headers.Add("X-Service-Token", storageToken);
                }

                using (HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
                    {
                        byte[] buffer = new byte[65536];
                        long totalRead = 0L;
                        while (true)
                        {
                            int read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct);
                            if (read <= 0) break;

                            await fileStream.WriteAsync(buffer, 0, read, ct);
                            totalRead += read;
                            progress?.Report(totalRead);
                        }
                    }
                }
            }
        }
    }
}















