using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using V3SClient.libs;

namespace V3SClient.Services
{
    public class ImageSyncState
    {
        [JsonProperty("since")]
        public string Since { get; set; }

        [JsonProperty("last_sync_unix")]
        public double LastSyncUnix { get; set; }

        [JsonProperty("last_request_unix")]
        public double LastRequestUnix { get; set; }

        [JsonProperty("last_request_at")]
        public string LastRequestAt { get; set; }

        [JsonProperty("base_url")]
        public string BaseUrl { get; set; }
    }

    public class ImageSyncResult
    {
        public int Pages { get; set; }
        public int Items { get; set; }
        public int Downloads { get; set; }
    }

    public class ImageSyncService : IDisposable
    {
        private const string DefaultStateFile = ".capture_sync_state.json";
        private const string DefaultSince = "1970-01-01T00:00:00Z";
        public const string SyncInProgressFileName = ".image-sync-in-progress";
        public const string SyncReadyFileName = ".image-sync-ready";
        private static readonly HashSet<string> SupportedSyncExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".json"
        };

        private readonly HttpClient _httpClient;
        
        public ImageSyncService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private ImageSyncState LoadState(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    var state = JsonConvert.DeserializeObject<ImageSyncState>(content);
                    return state ?? new ImageSyncState();
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogWarn($"LoadState failed: {ex.Message}");
            }
            return new ImageSyncState();
        }

        private static string NormalizeSince(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return DefaultSince;

            var formats = new[]
            {
                "d/M/yyyy h:mm:ss tt", "dd/MM/yyyy h:mm:ss tt",
                "d/M/yyyy H:mm:ss", "dd/MM/yyyy HH:mm:ss"
            };
            DateTime parsed;
            if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out parsed))
            {
                return parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }

            DateTimeOffset parsedOffset;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out parsedOffset))
            {
                return parsedOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }

            return value.Trim();
        }

        private void WriteState(string path, ImageSyncState state)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string tmpPath = path + ".tmp";
                string json = JsonConvert.SerializeObject(state, Formatting.Indented);
                File.WriteAllText(tmpPath, json);
                
                if (File.Exists(path))
                {
                    File.Replace(tmpPath, path, null);
                }
                else
                {
                    File.Move(tmpPath, path);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogWarn($"WriteState failed: {ex.Message}");
            }
        }

        private string SafeOutputPath(string outputRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("empty relative path");

            relativePath = relativePath.Trim().TrimStart('/', '\\');
            string fullPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath));
            string fullRoot = Path.GetFullPath(outputRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"server returned unsafe relative path: {relativePath}");

            return fullPath;
        }

        private async Task<bool> DownloadFileAsync(JToken fileInfo, string outputRoot, string token, bool dryRun, CancellationToken cancellationToken)
        {
            string relativePath = fileInfo["relative_path"]?.ToString();
            string downloadUrl = fileInfo["download_url"]?.ToString();
            long? expectedSize = fileInfo["size"]?.Value<long?>();

            if (string.IsNullOrEmpty(relativePath) || string.IsNullOrEmpty(downloadUrl))
                return false;
            if (!SupportedSyncExtensions.Contains(Path.GetExtension(relativePath)))
                return false;

            string outputPath = SafeOutputPath(outputRoot, relativePath);

            if (File.Exists(outputPath) && expectedSize.HasValue)
            {
                try
                {
                    long fileSize = new FileInfo(outputPath).Length;
                    if (fileSize == expectedSize.Value)
                    {
                        // Skip existing
                        return false;
                    }
                }
                catch
                {
                    // Ignore errors checking size
                }
            }

            if (dryRun)
            {
                LoggerManager.LogDebug($"Would download {relativePath}");
                return false;
            }

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tmpPath = outputPath + ".tmp";
            
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl))
                {
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }

                    using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, true))
                        {
                            await contentStream.CopyToAsync(fileStream, 256 * 1024, cancellationToken);
                        }
                    }
                }

                if (File.Exists(outputPath))
                {
                    File.Replace(tmpPath, outputPath, null);
                }
                else
                {
                    File.Move(tmpPath, outputPath);
                }
                
                return true;
            }
            catch
            {
                if (File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                }
                throw;
            }
        }

        private async Task<int> DownloadFilesAsync(IEnumerable<JToken> fileInfos, string outputRoot, string token, bool dryRun, int maxParallelDownloads, CancellationToken cancellationToken)
        {
            var files = fileInfos.ToList();
            if (files.Count == 0)
                return 0;

            maxParallelDownloads = Math.Max(1, Math.Min(16, maxParallelDownloads));
            if (maxParallelDownloads == 1 || files.Count == 1)
            {
                int count = 0;
                foreach (var fileInfo in files)
                {
                    if (await DownloadFileAsync(fileInfo, outputRoot, token, dryRun, cancellationToken))
                        count++;
                }
                return count;
            }

            using (var semaphore = new SemaphoreSlim(maxParallelDownloads))
            {
                var tasks = files.Select(async fileInfo =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        return await DownloadFileAsync(fileInfo, outputRoot, token, dryRun, cancellationToken) ? 1 : 0;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToArray();

                var results = await Task.WhenAll(tasks);
                return results.Sum();
            }
        }

        public async Task<ImageSyncResult> SyncOnceAsync(string baseUrl, string token, string outputRoot, string stateFile = DefaultStateFile, string sinceOverride = null, int limit = 100, bool dryRun = false, int downloadParallelism = 4, CancellationToken cancellationToken = default)
        {
            var state = LoadState(stateFile);
            string since = NormalizeSince(sinceOverride ?? state.Since ?? DefaultSince);
            var requestStartedAt = DateTimeOffset.UtcNow;
            string cursor = null;
            int pages = 0;
            int items = 0;
            int downloads = 0;

            baseUrl = baseUrl.TrimEnd('/');

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var queryParams = new List<string> { $"limit={limit}" };
                if (!string.IsNullOrEmpty(cursor))
                {
                    queryParams.Add($"cursor={Uri.EscapeDataString(cursor)}");
                }
                else
                {
                    queryParams.Add($"since={Uri.EscapeDataString(since)}");
                }

                string url = $"{baseUrl}/sync/changes?{string.Join("&", queryParams)}";
                
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (!string.IsNullOrEmpty(token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }

                    using (var response = await _httpClient.SendAsync(request, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        var content = await response.Content.ReadAsStringAsync();
                        var payload = JObject.Parse(content);
                        
                        var pageItems = payload["items"] as JArray;
                        if (pageItems == null)
                            throw new Exception("sync response items must be a list");

                        pages++;
                        items += pageItems.Count;

                        var seen = new HashSet<string>();

                        foreach (var item in pageItems)
                        {
                            var files = item["files"] as JArray;
                            if (files == null) continue;

                            // Marker phải nằm tại thư mục thực sự chứa ảnh/JSON.
                            // API có thể trả cấu trúc nhiều cấp: yyyy-MM-dd/plate/file.
                            var itemFolders = files
                                .Select(file => file["relative_path"]?.ToString())
                                .Where(path => !string.IsNullOrWhiteSpace(path))
                                .Where(path => SupportedSyncExtensions.Contains(Path.GetExtension(path)))
                                .Where(path => path.IndexOfAny(new[] { '/', '\\' }) >= 0)
                                .Select(path => Path.GetDirectoryName(path.TrimStart('/', '\\')))
                                .Where(folder => !string.IsNullOrWhiteSpace(folder) && !Path.GetFileName(folder).StartsWith("."))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Select(folder => SafeOutputPath(outputRoot, folder))
                                .ToList();

                            if (!dryRun)
                            {
                                foreach (var folder in itemFolders)
                                {
                                    Directory.CreateDirectory(folder);
                                    File.WriteAllText(Path.Combine(folder, SyncInProgressFileName), DateTime.UtcNow.ToString("O"));
                                }
                            }

                            bool itemCompleted = false;
                            try
                            {
                                var downloadCandidates = new List<JToken>();
                                foreach (var fileInfo in files)
                                {
                                    string relativePath = fileInfo["relative_path"]?.ToString();
                                    string downloadUrl = fileInfo["download_url"]?.ToString();
                                    
                                    if (string.IsNullOrEmpty(relativePath) ||
                                        string.IsNullOrEmpty(downloadUrl) ||
                                        !SupportedSyncExtensions.Contains(Path.GetExtension(relativePath)) ||
                                        seen.Contains(relativePath))
                                        continue;

                                    seen.Add(relativePath);
                                    downloadCandidates.Add(fileInfo);
                                }

                                downloads += await DownloadFilesAsync(downloadCandidates, outputRoot, token, dryRun, downloadParallelism, cancellationToken);
                                itemCompleted = true;
                            }
                            finally
                            {
                                if (!dryRun && itemCompleted)
                                {
                                    foreach (var folder in itemFolders)
                                    {
                                        string marker = Path.Combine(folder, SyncInProgressFileName);
                                        if (File.Exists(marker)) File.Delete(marker);
                                        File.WriteAllText(Path.Combine(folder, SyncReadyFileName), DateTime.UtcNow.ToString("O"));
                                    }
                                }
                            }
                        }

                        cursor = payload["next_cursor"]?.ToString();
                        bool hasMore = payload["has_more"]?.Value<bool>() ?? false;

                        if (!hasMore)
                        {
                            string nextSince = payload["next_since"]?.ToString();
                            if (!dryRun)
                            {
                                state.Since = !string.IsNullOrWhiteSpace(nextSince)
                                    ? NormalizeSince(nextSince)
                                    : requestStartedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                                state.LastRequestUnix = requestStartedAt.ToUnixTimeSeconds();
                                state.LastRequestAt = requestStartedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                                state.LastSyncUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                                state.BaseUrl = baseUrl;
                                WriteState(stateFile, state);
                            }
                            
                            return new ImageSyncResult
                            {
                                Pages = pages,
                                Items = items,
                                Downloads = downloads
                            };
                        }

                        if (string.IsNullOrEmpty(cursor))
                        {
                            throw new Exception("server returned has_more=true without next_cursor");
                        }
                    }
                }
            }
        }
    }
}
