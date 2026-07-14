using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using V3SClient.libs;
using V3SClient.models;

namespace V3SClient.Services
{
    public sealed class InputCaptureMonitorService : IDisposable
    {
        private const int MaxCachedCaptures = 10;
        private readonly string _inputDirectory;
        private readonly object _cacheLock = new object();
        private readonly List<PlateImageItem> _recentCaptures = new List<PlateImageItem>();
        private FileSystemWatcher _watcher;
        private CancellationTokenSource _refreshCts;
        private bool _isDisposed;

        public event Action CapturesChanged;

        public InputCaptureMonitorService(string inputDirectory)
        {
            _inputDirectory = inputDirectory;
        }

        public void Start()
        {
            if (_watcher != null || string.IsNullOrWhiteSpace(_inputDirectory)) return;

            if (!Directory.Exists(_inputDirectory))
                Directory.CreateDirectory(_inputDirectory);

            MergeCurrentCaptures();
            _watcher = new FileSystemWatcher(_inputDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnChanged;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (_isDisposed) return;

            var cts = new CancellationTokenSource();
            var previousCts = Interlocked.Exchange(ref _refreshCts, cts);
            CancelAndDispose(previousCts);
            var token = cts.Token;
            Task.Delay(250, token).ContinueWith(t =>
            {
                if (_isDisposed || t.IsCanceled) return;
                MergeCurrentCaptures();
                CapturesChanged?.Invoke();
                if (ReferenceEquals(_refreshCts, cts))
                    Interlocked.CompareExchange(ref _refreshCts, null, cts);
                CancelAndDispose(cts);
            }, TaskScheduler.Default);
        }

        public List<PlateImageItem> GetRecentCaptures(int limit = 10)
        {
            MergeCurrentCaptures();
            lock (_cacheLock)
            {
                return _recentCaptures
                    .OrderByDescending(item => item.CaptureTime)
                    .Take(Math.Max(0, limit))
                    .ToList();
            }
        }

        private void MergeCurrentCaptures()
        {
            if (!Directory.Exists(_inputDirectory)) return;
            try
            {
                var files = Directory.EnumerateFiles(_inputDirectory, "*.*", SearchOption.AllDirectories)
                    .Where(IsImage)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTime)
                    .Take(MaxCachedCaptures)
                    .ToList();

                List<FileInfo> filesToLoad;
                lock (_cacheLock)
                {
                    filesToLoad = files.Where(file =>
                    {
                        var cached = _recentCaptures.FirstOrDefault(item =>
                            string.Equals(item.FilePath, file.FullName, StringComparison.OrdinalIgnoreCase));
                        return cached == null || cached.Thumbnail == null;
                    }).ToList();
                }

                var loadedItems = filesToLoad.Select(CreateItem).ToList();
                lock (_cacheLock)
                {
                    foreach (var item in loadedItems)
                    {
                        int existingIndex = _recentCaptures.FindIndex(cached =>
                            string.Equals(cached.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase));
                        if (existingIndex < 0)
                            _recentCaptures.Add(item);
                        else if (_recentCaptures[existingIndex].Thumbnail == null || item.Thumbnail != null)
                            _recentCaptures[existingIndex] = item;
                    }

                    var ordered = _recentCaptures
                        .OrderByDescending(item => item.CaptureTime)
                        .Take(MaxCachedCaptures)
                        .ToList();
                    _recentCaptures.Clear();
                    _recentCaptures.AddRange(ordered);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Không thể tải danh sách ảnh chụp mới", ex);
            }
        }

        private static bool IsImage(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp";
        }

        private static PlateImageItem CreateItem(FileInfo file)
        {
            var item = new PlateImageItem
            {
                FilePath = file.FullName,
                FileName = file.Name,
                CaptureTime = file.LastWriteTime,
                CaptureRole = "Ảnh mới"
            };
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 220;
                bitmap.UriSource = new Uri(file.FullName, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                item.Thumbnail = bitmap;
            }
            catch
            {
                item.ErrorMessage = "Ảnh đang được ghi";
            }
            return item;
        }

        public void Dispose()
        {
            _isDisposed = true;
            var cts = Interlocked.Exchange(ref _refreshCts, null);
            CancelAndDispose(cts);
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnChanged;
                _watcher.Changed -= OnChanged;
                _watcher.Deleted -= OnChanged;
                _watcher.Renamed -= OnChanged;
                _watcher.Dispose();
                _watcher = null;
            }
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
