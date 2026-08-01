using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;

namespace V3SClient.libs
{
    public class SmartDownloadManager : INotifyPropertyChanged
    {
        public class DownloadTask : INotifyPropertyChanged
        {
            private double _progress;
            private string _status = "Pending";
            private string _speed;
            private long _downloadedBytes;
            private long _totalBytes;

            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string CameraNames { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string SavePath { get; set; }
            internal CancellationTokenSource CancellationSource { get; } = new CancellationTokenSource();

            public bool IsTerminal => Status == "Completed" || Status == "Failed" || Status == "Cancelled" || Status == "No data";
            public bool CanCancel => !IsTerminal;
            public string StatusText
            {
                get
                {
                    switch (Status)
                    {
                        case "Queued": return "Đang xếp hàng tải…";
                        case "Downloading...": return "Đang tải video…";
                        case "Searching...": return "Đang chuẩn bị dữ liệu…";
                        case "WaitingForNetwork": return "Mất kết nối mạng — đang chờ mạng trở lại…";
                        case "Merging...": return "Đang ghép video…";
                        default: return Status;
                    }
                }
            }

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
                    OnPropertyChanged("CanCancel");
                    OnPropertyChanged("IsTerminal");
                    OnPropertyChanged("StatusText");
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
                var handler = this.PropertyChanged;
                if (handler == null)
                    return;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(new Action(() => handler(this, new PropertyChangedEventArgs(name))));
                    return;
                }

                handler(this, new PropertyChangedEventArgs(name));
            }

            public long DownloadedBytes
            {
                get => _downloadedBytes;
                set { _downloadedBytes = value; OnPropertyChanged("DownloadedBytes"); }
            }

            public long TotalBytes
            {
                get => _totalBytes;
                set { _totalBytes = value; OnPropertyChanged("TotalBytes"); }
            }
        }

        private static readonly Lazy<SmartDownloadManager> _instance = new Lazy<SmartDownloadManager>(() => new SmartDownloadManager());
        private readonly HttpClient _httpClient;
        private readonly int _maxParallelDownloads = 3;

        public static SmartDownloadManager Instance => _instance.Value;
        public ObservableCollection<DownloadTask> Tasks { get; } = new ObservableCollection<DownloadTask>();
        private DownloadTask _activeDownload;

        /// <summary>Current non-terminal task, available to the shell overlay on every page.</summary>
        public DownloadTask ActiveDownload
        {
            get => _activeDownload;
            private set
            {
                if (ReferenceEquals(_activeDownload, value)) return;
                _activeDownload = value;
                OnPropertyChanged(nameof(ActiveDownload));
            }
        }

        /// <summary>
        /// A bounded direct-export request.  The playback export endpoint is
        /// reliable for short ranges, while a very long single request can
        /// remain open indefinitely on the server.  Long exports are therefore
        /// represented as a sequence of these requests and merged locally.
        /// </summary>
        public sealed class DirectDownloadChunk
        {
            public string Url { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
        }

        public bool HasActiveDownloads => Tasks.Any(task => task != null && !task.IsTerminal);

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler == null) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => handler(this, new PropertyChangedEventArgs(propertyName))));
                return;
            }
            handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private void AddTask(DownloadTask task)
        {
            Action add = () =>
            {
                Tasks.Add(task);
                task.PropertyChanged += OnTaskPropertyChanged;
                RefreshActiveDownload();
            };
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) add();
            else dispatcher.Invoke(add);
        }

        private void OnTaskPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Status" || e.PropertyName == "Progress" || e.PropertyName == "Speed")
                RefreshActiveDownload();
        }

        private void RefreshActiveDownload()
        {
            ActiveDownload = Tasks.FirstOrDefault(task => task != null && !task.IsTerminal);
            OnPropertyChanged(nameof(HasActiveDownloads));
        }

        public void Cancel(DownloadTask task)
        {
            if (task == null || !task.CanCancel)
                return;

            task.Status = "Cancelled";
            task.Speed = "Đã hủy";
            task.CancellationSource.Cancel();
        }

        public void CancelAllActive()
        {
            foreach (var task in Tasks.Where(item => item != null && item.CanCancel).ToArray())
                Cancel(task);
            RefreshActiveDownload();
        }

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

            AddTask(task);

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
            catch (OperationCanceledException)
            {
                task.Status = "Cancelled";
                task.Speed = "Đã hủy";
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

        public DownloadTask QueueDirectDownload(string url, string destinationPath, string cameraName, DateTime start, DateTime end, string token = null, string headerName = null)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("Download URL is required.", nameof(url));
            if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Destination path is required.", nameof(destinationPath));

            string savePath = Path.GetDirectoryName(destinationPath);
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }

            DownloadTask task = new DownloadTask
            {
                CameraNames = string.IsNullOrWhiteSpace(cameraName) ? Path.GetFileName(destinationPath) : cameraName,
                StartTime = start,
                EndTime = end,
                SavePath = savePath,
                Status = "Queued",
                Progress = 0,
                Speed = ""
            };

            AddTask(task);

            _ = Task.Run(async () =>
            {
                try
                {
                    task.Status = "Downloading...";
                    DateTime startedAt = DateTime.Now;
                    await DownloadFileStreamedAsync(
                        url,
                        destinationPath,
                        new Progress<long>(downloadedBytes =>
                        {
                            double elapsedSeconds = Math.Max(0.1, (DateTime.Now - startedAt).TotalSeconds);
                            double speedMb = downloadedBytes / 1048576.0 / elapsedSeconds;
                            task.Speed = $"{speedMb:F2} MB/s";
                        }),
                        task.CancellationSource.Token,
                        totalBytes =>
                        {
                            task.TotalBytes = totalBytes;
                            if (totalBytes > 0)
                            {
                                return new Progress<long>(downloadedBytes =>
                                {
                                    task.DownloadedBytes = downloadedBytes;
                                    task.Progress = Math.Min(100.0, downloadedBytes * 100.0 / totalBytes);
                                });
                            }

                            return new Progress<long>(downloadedBytes =>
                            {
                                task.DownloadedBytes = downloadedBytes;
                                task.Progress = 0;
                            });
                        },
                        token,
                        headerName).ConfigureAwait(false);

                    if (task.TotalBytes > 0)
                        task.DownloadedBytes = task.TotalBytes;
                    task.Progress = 100.0;
                    task.Status = "Completed";
                }
                catch (OperationCanceledException)
                {
                    task.Status = "Cancelled";
                    task.Speed = "Đã hủy";
                    TryDeletePartialFile(destinationPath);
                }
                catch (Exception ex)
                {
                    task.Status = "Failed";
                    task.Speed = ex.GetBaseException().Message;
                    Debug.WriteLine("Direct download error: " + ex.Message);
                }
            });

            return task;
        }

        /// <summary>
        /// Downloads a long playback export as independently bounded HTTP
        /// requests, then remuxes only local files.  This deliberately avoids
        /// letting FFmpeg read a multi-hour remote playlist, which can stall
        /// before it emits its first progress event.
        /// </summary>
        public DownloadTask QueueChunkedDirectDownload(IEnumerable<DirectDownloadChunk> requestedChunks, string destinationPath, string cameraName, DateTime start, DateTime end, string token = null, string headerName = null, bool mergeParts = true)
        {
            var chunks = (requestedChunks ?? Enumerable.Empty<DirectDownloadChunk>())
                .Where(chunk => chunk != null && !string.IsNullOrWhiteSpace(chunk.Url))
                .ToList();
            if (chunks.Count == 0)
                throw new ArgumentException("At least one export chunk is required.", nameof(requestedChunks));
            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("Destination path is required.", nameof(destinationPath));

            string savePath = Path.GetDirectoryName(destinationPath);
            if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

            var task = new DownloadTask
            {
                CameraNames = string.IsNullOrWhiteSpace(cameraName) ? Path.GetFileName(destinationPath) : cameraName,
                StartTime = start,
                EndTime = end,
                SavePath = savePath,
                Status = "Queued",
                Progress = 0,
                Speed = string.Empty
            };
            AddTask(task);
            _ = Task.Run(() => DownloadChunkedDirectAsync(task, chunks, destinationPath, token, headerName, mergeParts));
            return task;
        }

        private async Task DownloadChunkedDirectAsync(DownloadTask task, IList<DirectDownloadChunk> chunks, string destinationPath, string token, string headerName, bool mergeParts)
        {
            string partsDirectory = destinationPath + ".parts-" + task.Id;
            // Keep the MP4 suffix: FFmpeg chooses the output muxer from it.
            // A generic ".merging" suffix makes the concat step fail before
            // any media is processed.
            string mergedPartialPath = Path.Combine(
                Path.GetDirectoryName(destinationPath),
                Path.GetFileNameWithoutExtension(destinationPath) + ".merging.mp4");
            var partPaths = new List<string>();
            long completedBytes = 0;
            int savedSegmentCount = 0;
            DateTime startedAt = DateTime.UtcNow;

            try
            {
                Directory.CreateDirectory(partsDirectory);
                task.Status = "Downloading...";

                for (int index = 0; index < chunks.Count; index++)
                {
                    task.CancellationSource.Token.ThrowIfCancellationRequested();
                    var chunk = chunks[index];
                    string partPath = Path.Combine(partsDirectory, string.Format("part-{0:D3}.mp4", index + 1));
                    Exception lastError = null;
                    bool noDataForChunk = false;

                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        task.CancellationSource.Token.ThrowIfCancellationRequested();
                        await WaitForNetworkAsync(task, task.CancellationSource.Token).ConfigureAwait(false);
                        TryDeletePartialFile(partPath);
                        long currentPartBytes = 0;
                        long currentPartLength = 0;
                        try
                        {
                            task.Status = "Downloading...";
                            task.Speed = string.Format("Đang tải đoạn {0}/{1}…", index + 1, chunks.Count);
                            await DownloadFileStreamedAsync(
                                chunk.Url,
                                partPath,
                                new Progress<long>(downloaded =>
                                {
                                    double seconds = Math.Max(0.1, (DateTime.UtcNow - startedAt).TotalSeconds);
                                    task.Speed = string.Format("Đoạn {0}/{1} · {2:F2} MB/s", index + 1, chunks.Count, (completedBytes + downloaded) / 1048576d / seconds);
                                }),
                                task.CancellationSource.Token,
                                total =>
                                {
                                    currentPartLength = total;
                                    return new Progress<long>(downloaded =>
                                    {
                                        currentPartBytes = downloaded;
                                        task.DownloadedBytes = completedBytes + downloaded;
                                        if (total > 0)
                                        {
                                            task.TotalBytes = completedBytes + total;
                                            task.Progress = Math.Min(96d, ((index + (double)downloaded / total) / chunks.Count) * 96d);
                                        }
                                        else
                                        {
                                            task.Progress = Math.Min(96d, ((double)index / chunks.Count) * 96d);
                                        }
                                    });
                                },
                                token,
                                headerName).ConfigureAwait(false);

                            if (!File.Exists(partPath) || new FileInfo(partPath).Length == 0)
                                throw new InvalidOperationException("Máy chủ không trả về dữ liệu video cho đoạn này.");
                            lastError = null;
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            TryDeletePartialFile(partPath);
                            LoggerManager.LogException(ex, "Playback direct export segment " + (index + 1) + "/" + chunks.Count + " attempt " + attempt + " failed");
                            // A 404 from /export.mp4 means this bounded time
                            // interval has no archived media.  It is not a
                            // network failure: skip only this interval and
                            // continue with the rest of a long export.
                            if (IsNotFound(ex))
                            {
                                noDataForChunk = true;
                                lastError = null;
                                task.Speed = string.Format("Đoạn {0}/{1} không có dữ liệu — đang tiếp tục…", index + 1, chunks.Count);
                                break;
                            }
                            if (attempt < 3)
                            {
                                task.Status = "WaitingForNetwork";
                                task.Speed = string.Format("Đoạn {0}/{1} chưa phản hồi — đang thử lại…", index + 1, chunks.Count);
                                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), task.CancellationSource.Token).ConfigureAwait(false);
                            }
                        }
                    }

                    if (noDataForChunk)
                    {
                        task.Progress = Math.Min(96d, ((double)(index + 1) / chunks.Count) * 96d);
                        continue;
                    }
                    if (lastError != null)
                        throw new InvalidOperationException("Không thể tải đoạn " + (index + 1) + "/" + chunks.Count + " sau 3 lần thử.", lastError);

                    long completedPartLength = new FileInfo(partPath).Length;
                    completedBytes += completedPartLength;
                    task.DownloadedBytes = completedBytes;
                    task.TotalBytes = Math.Max(task.TotalBytes, completedBytes);
                    task.Progress = Math.Min(96d, ((double)(index + 1) / chunks.Count) * 96d);
                    if (mergeParts)
                    {
                        partPaths.Add(partPath);
                    }
                    else
                    {
                        string segmentPath = BuildSegmentDestinationPath(destinationPath, index + 1, chunks.Count);
                        if (File.Exists(segmentPath)) File.Delete(segmentPath);
                        File.Move(partPath, segmentPath);
                        savedSegmentCount++;
                    }
                }

                if (!mergeParts)
                {
                    if (completedBytes == 0)
                        throw new InvalidOperationException("Không tìm thấy dữ liệu video trong khoảng thời gian đã chọn.");
                    task.TotalBytes = completedBytes;
                    task.Progress = 100;
                    task.Speed = string.Format("Đã lưu {0} đoạn video vào Downloads", savedSegmentCount);
                    task.Status = "Completed";
                    TryDeleteDirectory(partsDirectory);
                    return;
                }

                if (partPaths.Count == 0)
                    throw new InvalidOperationException("Không tìm thấy dữ liệu video trong khoảng thời gian đã chọn.");

                task.Status = "Merging...";
                task.Progress = 97;
                task.Speed = "Đang ghép các đoạn video…";
                if (partPaths.Count == 1)
                    File.Copy(partPaths[0], mergedPartialPath, true);
                else
                    await MergeLocalVideoPartsAsync(partPaths, mergedPartialPath, task.CancellationSource.Token).ConfigureAwait(false);

                if (!File.Exists(mergedPartialPath) || new FileInfo(mergedPartialPath).Length == 0)
                    throw new InvalidOperationException("Không thể ghép các đoạn video đã tải.");

                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Move(mergedPartialPath, destinationPath);
                task.DownloadedBytes = new FileInfo(destinationPath).Length;
                task.TotalBytes = task.DownloadedBytes;
                task.Progress = 100;
                task.Speed = "Đã lưu vào Downloads";
                task.Status = "Completed";
                TryDeleteDirectory(partsDirectory);
            }
            catch (OperationCanceledException)
            {
                task.Status = "Cancelled";
                task.Speed = "Đã hủy";
                TryDeletePartialFile(mergedPartialPath);
                TryDeleteDirectory(partsDirectory);
            }
            catch (Exception ex)
            {
                task.Status = "Failed";
                task.Speed = ex.GetBaseException().Message;
                TryDeletePartialFile(mergedPartialPath);
                Debug.WriteLine("Chunked playback download error: " + ex);
            }
        }

        private static async Task MergeLocalVideoPartsAsync(IList<string> partPaths, string outputPath, CancellationToken cancellationToken)
        {
            string ffmpeg = ResolveFfmpegPath();
            if (string.IsNullOrWhiteSpace(ffmpeg))
                throw new FileNotFoundException("Không tìm thấy FFmpeg để ghép các đoạn video đã tải.");

            string manifestPath = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + ".txt");
            try
            {
                // FFmpeg's concat demuxer does not reliably accept a UTF-8 BOM
                // before its first `file` directive.  Write an explicit
                // BOM-free manifest.
                File.WriteAllLines(
                    manifestPath,
                    partPaths.Select(path => "file '" + path.Replace("\\", "/").Replace("'", "'\\''") + "'"),
                    new System.Text.UTF8Encoding(false));
                var errors = new System.Text.StringBuilder();
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpeg,
                        Arguments = "-hide_banner -nostats -loglevel error -y -fflags +genpts -f concat -safe 0 -i " + QuoteArgument(manifestPath) + " -c copy -movflags +faststart -f mp4 " + QuoteArgument(outputPath),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        WorkingDirectory = Path.GetDirectoryName(outputPath)
                    };
                    process.ErrorDataReceived += (sender, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) errors.AppendLine(args.Data); };
                    if (!process.Start()) throw new InvalidOperationException("Không thể khởi động FFmpeg để ghép video.");
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();
                    using (cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(); } catch { } }))
                    {
                        await Task.Run(() => process.WaitForExit(), cancellationToken).ConfigureAwait(false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("Không thể ghép các đoạn video: " + errors.ToString().Trim());
                }
            }
            finally
            {
                TryDeletePartialFile(manifestPath);
            }
        }

        private static string BuildSegmentDestinationPath(string destinationPath, int index, int total)
        {
            string directory = Path.GetDirectoryName(destinationPath);
            string name = Path.GetFileNameWithoutExtension(destinationPath);
            string extension = Path.GetExtension(destinationPath);
            return Path.Combine(directory, string.Format("{0}_part-{1:D2}-of-{2:D2}{3}", name, index, total, extension));
        }

        /// <summary>
        /// Exports every segment in a playback m3u8 playlist.  The legacy
        /// /export.mp4 endpoint is intentionally not used here because that API
        /// limits a single export to one hour.  FFmpeg follows every signed media
        /// URL exposed by the playlist and remuxes the complete requested range.
        /// </summary>
        public DownloadTask QueuePlaylistDownload(string playlistUrl, string destinationPath, string cameraName, DateTime start, DateTime end, string token = null, string headerName = null)
        {
            if (string.IsNullOrWhiteSpace(playlistUrl)) throw new ArgumentException("Playlist URL is required.", nameof(playlistUrl));
            if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Destination path is required.", nameof(destinationPath));

            string savePath = Path.GetDirectoryName(destinationPath);
            if (!Directory.Exists(savePath))
                Directory.CreateDirectory(savePath);

            var task = new DownloadTask
            {
                CameraNames = string.IsNullOrWhiteSpace(cameraName) ? Path.GetFileName(destinationPath) : cameraName,
                StartTime = start,
                EndTime = end,
                SavePath = savePath,
                Status = "Queued",
                Progress = 0,
                Speed = string.Empty
            };

            AddTask(task);
            _ = Task.Run(() => DownloadPlaylistAsync(task, playlistUrl, destinationPath, token, headerName));
            return task;
        }

        private async Task DownloadPlaylistAsync(DownloadTask task, string playlistUrl, string destinationPath, string token, string headerName)
        {
            string partialPath = Path.Combine(
                Path.GetDirectoryName(destinationPath),
                Path.GetFileNameWithoutExtension(destinationPath) + ".partial.mp4");
            try
            {
                task.Status = "Downloading...";
                string ffmpeg = ResolveFfmpegPath();
                if (string.IsNullOrWhiteSpace(ffmpeg))
                    throw new FileNotFoundException("Không tìm thấy FFmpeg để xuất toàn bộ playlist playback.");

                double requestedSeconds = Math.Max(1d, (task.EndTime - task.StartTime).TotalSeconds);
                Exception finalError = null;
                const int maxAttempts = 3;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    task.CancellationSource.Token.ThrowIfCancellationRequested();
                    await WaitForNetworkAsync(task, task.CancellationSource.Token).ConfigureAwait(false);
                    try
                    {
                        TryDeletePartialFile(partialPath);
                        await RunFfmpegPlaylistExportAsync(task, ffmpeg, playlistUrl, partialPath, requestedSeconds, token, headerName).ConfigureAwait(false);
                        finalError = null;
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        finalError = ex;
                        LoggerManager.LogException(ex, "Playback playlist export attempt " + attempt + "/" + maxAttempts + " failed for " + task.CameraNames);
                        TryDeletePartialFile(partialPath);
                        if (IsNetworkFailure(ex))
                        {
                            task.Speed = "Mất kết nối hoặc máy chủ tạm thời không phản hồi — đang chờ kết nối lại…";
                            await WaitForNetworkAsync(task, task.CancellationSource.Token).ConfigureAwait(false);
                        }
                        if (attempt < maxAttempts)
                        {
                            task.Speed = "Đang kết nối lại (lần " + (attempt + 1) + "/" + maxAttempts + ")…";
                            await Task.Delay(TimeSpan.FromSeconds(attempt * 2), task.CancellationSource.Token).ConfigureAwait(false);
                        }
                    }
                }

                if (finalError != null)
                    throw new InvalidOperationException("Không thể tải toàn bộ playlist sau " + maxAttempts + " lần thử.", finalError);

                if (!File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
                    throw new InvalidOperationException("FFmpeg không tạo được video từ playlist playback.");

                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Move(partialPath, destinationPath);
                task.DownloadedBytes = new FileInfo(destinationPath).Length;
                task.TotalBytes = task.DownloadedBytes;
                task.Progress = 100;
                task.Speed = "Đã lưu vào Downloads";
                task.Status = "Completed";
            }
            catch (OperationCanceledException)
            {
                task.Status = "Cancelled";
                task.Speed = "Đã hủy";
                TryDeletePartialFile(partialPath);
            }
            catch (Exception ex)
            {
                task.Status = "Failed";
                task.Speed = ex.GetBaseException().Message;
                TryDeletePartialFile(partialPath);
                Debug.WriteLine("Playlist download error: " + ex);
            }
        }

        private static async Task WaitForNetworkAsync(DownloadTask task, CancellationToken cancellationToken)
        {
            while (!NetworkInterface.GetIsNetworkAvailable())
            {
                task.Status = "WaitingForNetwork";
                task.Speed = "Mất kết nối mạng — đang chờ mạng trở lại…";
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }

            if (task.Status == "WaitingForNetwork")
            {
                task.Status = "Downloading...";
                task.Speed = "Đã có kết nối mạng — đang tiếp tục tải…";
            }
        }

        private static bool IsNetworkFailure(Exception exception)
        {
            var text = exception == null ? string.Empty : exception.ToString();
            return text.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("connection reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("could not resolve", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("i/o error", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsNotFound(Exception exception)
        {
            var text = exception == null ? string.Empty : exception.ToString();
            return text.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Not Found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task RunFfmpegPlaylistExportAsync(DownloadTask task, string ffmpeg, string playlistUrl, string partialPath, double requestedSeconds, string token, string headerName)
        {
            DateTime startedAt = DateTime.UtcNow;
            DateTime lastProgressAt = startedAt;
            long lastSize = 0;
            DateTime lastSizeAt = startedAt;
            var errors = new System.Text.StringBuilder();
            var args = new System.Text.StringBuilder();
            args.Append("-hide_banner -nostats -loglevel error -y ");
            // A long archive can contain thousands of segments.  Reconnect each
            // failed HTTP request instead of failing the entire export on one
            // transient storage/network error.
            args.Append("-reconnect 1 -reconnect_streamed 1 -reconnect_at_eof 1 -reconnect_delay_max 10 -rw_timeout 60000000 ");
            if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(headerName))
                args.Append("-headers ").Append(QuoteArgument(headerName + ": " + token + "\r\n")).Append(' ');
            args.Append("-i ").Append(QuoteArgument(playlistUrl))
                // fMP4 playlists can contain many thousands of fragments.  A
                // normal MP4 with +faststart causes FFmpeg to buffer an
                // unbounded amount of media before the output becomes usable.
                // Fragmented MP4 writes every fragment immediately, preserves
                // the original stream, and keeps long archive exports bounded.
                .Append(" -map 0:v:0? -c copy -movflags +frag_keyframe+empty_moov+default_base_moof -f mp4 -progress pipe:1 ")
                .Append(QuoteArgument(partialPath));

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = args.ToString(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(partialPath)
                };
                process.OutputDataReceived += (sender, eventArgs) =>
                {
                    if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                    {
                        lastProgressAt = DateTime.UtcNow;
                        UpdatePlaylistProgress(task, eventArgs.Data, requestedSeconds, partialPath, startedAt, ref lastSize, ref lastSizeAt);
                    }
                };
                process.ErrorDataReceived += (sender, eventArgs) =>
                {
                    if (!string.IsNullOrWhiteSpace(eventArgs.Data)) errors.AppendLine(eventArgs.Data);
                };

                if (!process.Start())
                    throw new InvalidOperationException("Không thể khởi động FFmpeg để tải playlist playback.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                using (task.CancellationSource.Token.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited) process.Kill();
                    }
                    catch { }
                }))
                {
                    var exited = Task.Run(() => process.WaitForExit());
                    while (!exited.IsCompleted)
                    {
                        await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(2), task.CancellationSource.Token)).ConfigureAwait(false);
                        task.CancellationSource.Token.ThrowIfCancellationRequested();

                        // Never leave the user with an infinite "connecting"
                        // state.  A healthy playback playlist emits progress
                        // as soon as the first fMP4 fragment is written.
                        if (!exited.IsCompleted && DateTime.UtcNow - lastProgressAt > TimeSpan.FromSeconds(35))
                        {
                            try
                            {
                                if (!process.HasExited) process.Kill();
                            }
                            catch { }
                            await exited.ConfigureAwait(false);
                            throw new TimeoutException("FFmpeg không nhận được dữ liệu playback trong 35 giây.");
                        }
                    }
                    await exited.ConfigureAwait(false);
                }

                task.CancellationSource.Token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("FFmpeg không thể xuất playlist: " + errors.ToString().Trim());
            }
        }

        private static void UpdatePlaylistProgress(DownloadTask task, string output, double requestedSeconds, string partialPath, DateTime startedAt, ref long lastSize, ref DateTime lastSizeAt)
        {
            int separator = output.IndexOf('=');
            if (separator <= 0) return;

            string key = output.Substring(0, separator);
            string value = output.Substring(separator + 1);
            double seconds;
            if (string.Equals(key, "out_time_us", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                task.Progress = Math.Min(99.5, Math.Max(0, seconds / 1000000d / requestedSeconds * 100d));
            }
            else if (string.Equals(key, "out_time_ms", StringComparison.OrdinalIgnoreCase) &&
                     double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                // FFmpeg versions differ: this key has historically been emitted
                // in either milliseconds or microseconds.
                seconds = seconds > requestedSeconds * 10000d ? seconds / 1000000d : seconds / 1000d;
                task.Progress = Math.Min(99.5, Math.Max(0, seconds / requestedSeconds * 100d));
            }

            if (!File.Exists(partialPath)) return;
            long size = new FileInfo(partialPath).Length;
            DateTime now = DateTime.UtcNow;
            double elapsed = Math.Max(0.1, (now - lastSizeAt).TotalSeconds);
            if (size >= lastSize && elapsed >= 0.25)
            {
                task.Speed = string.Format("{0:F2} MB/s", (size - lastSize) / 1048576d / elapsed);
                lastSize = size;
                lastSizeAt = now;
            }
            task.DownloadedBytes = size;
        }

        private static string ResolveFfmpegPath()
        {
            string configured = Environment.GetEnvironmentVariable("FFMPEG_PATH");
            var candidates = new[]
            {
                configured,
                @"C:\ffmpeg-8.1-essentials_build\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
            };
            return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static void TryDeletePartialFile(string destinationPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(destinationPath) && File.Exists(destinationPath))
                    File.Delete(destinationPath);
            }
            catch
            {
                // A file may still be held briefly by the cancelled HTTP stream.
            }
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
                    Directory.Delete(directoryPath, true);
            }
            catch
            {
                // Keep recoverable parts if another process still owns a file.
            }
        }

        private async Task DownloadFileStreamedAsync(
            string url,
            string destinationPath,
            IProgress<long> speedProgress,
            CancellationToken ct,
            Func<long, IProgress<long>> progressFactory,
            string token,
            string headerName)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(headerName))
                {
                    request.Headers.Add(headerName, token);
                }

                using (HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    long totalBytes = response.Content.Headers.ContentLength ?? 0;
                    IProgress<long> progress = progressFactory?.Invoke(totalBytes);

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
                            speedProgress?.Report(totalRead);
                            progress?.Report(totalRead);
                        }
                    }
                }
            }
        }
    }
}














