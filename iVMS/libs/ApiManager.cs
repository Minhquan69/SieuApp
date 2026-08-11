using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.IO;

namespace V3SClient.libs
{
    public class EndpointProfile
    {
        [JsonProperty("keyword")]
        public string Keyword { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("internal_url")]
        public string InternalUrl { get; set; }

        [JsonProperty("public_url")]
        public string PublicUrl { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; } = "Active";
    }

    public class SystemEndpoints
    {
        [JsonProperty("endpoints")]
        public List<EndpointProfile> Endpoints { get; set; } = new List<EndpointProfile>();
    }
    public class ApiManager
    {
        public sealed class FrameDetectionCountsResponse
        {
            [JsonProperty("total_detection_count")]
            public int TotalDetectionCount { get; set; }

            [JsonProperty("camera_count")]
            public int CameraCount { get; set; }

            [JsonProperty("snapshot_totals")]
            public List<FrameDetectionSnapshotTotal> SnapshotTotals { get; set; } = new List<FrameDetectionSnapshotTotal>();

            public int AccumulatedDetectionCount => SnapshotTotals == null || SnapshotTotals.Count == 0
                ? TotalDetectionCount
                : SnapshotTotals.Sum(item => item == null ? 0 : item.TotalDetectionCount);
        }

        public sealed class FrameDetectionSnapshotTotal
        {
            [JsonProperty("total_detection_count")]
            public int TotalDetectionCount { get; set; }
        }

        public sealed class LiveFrameDetectionCountsResponse
        {
            [JsonProperty("total_detection_count")]
            public int TotalDetectionCount { get; set; }

            [JsonProperty("camera_count")]
            public int CameraCount { get; set; }
        }

        public sealed class CameraVehicleCountsResponse
        {
            [JsonProperty("custom_total")]
            public int CustomTotal { get; set; }

            [JsonProperty("today_total")]
            public int TodayTotal { get; set; }

            [JsonProperty("yesterday_total")]
            public int YesterdayTotal { get; set; }

            [JsonProperty("cameras")]
            public List<CameraVehicleCount> Cameras { get; set; } = new List<CameraVehicleCount>();

            /// <summary>
            /// Returns the custom-range value reported for the requested camera.
            /// Falling back to custom_total keeps this compatible with API
            /// deployments that return one camera but omit the cameras array.
            /// </summary>
            public int GetCustomCount(string cameraId)
            {
                var camera = Cameras == null ? null : Cameras.FirstOrDefault(item =>
                    item != null && string.Equals(item.CameraId, cameraId, StringComparison.OrdinalIgnoreCase));
                return camera == null ? CustomTotal : camera.CustomCount;
            }
        }

        public sealed class CameraVehicleCount
        {
            [JsonProperty("cam_id")]
            public string CameraId { get; set; }

            [JsonProperty("custom_count")]
            public int CustomCount { get; set; }
        }
        private static readonly Lazy<ApiManager> _instance = new Lazy<ApiManager>(() => new ApiManager());
        public static ApiManager Instance => _instance.Value;

        private HttpClient _httpClient;
        // Login must be able to reach the Center Manager even when a stale
        // Windows proxy setting makes the default HttpClient fail instantly.
        // Keep this isolated so all existing API traffic preserves its
        // current proxy behavior.
        private readonly HttpClient _loginHttpClient;
        // Isolated from login headers and main-backend connection pooling.
        private readonly HttpClient _deviceStatusHttpClient;

        // Backend Domain (The Center Server entry point)
        private string _baseUrl;
        private string _streamApiUrl;
        private string _deviceStatusApiUrl;
        private string _frameDetectionCountsApiUrl;
        private string _liveFrameDetectionCountsApiUrl;
        private string _cameraVehicleCountsApiUrl;
        private string _aiReportEndpointKeyword = "_aiEventReport";
        private string _deviceReportEndpointKeyword = "_deviceReport";
        private string _roiConfigEndpointKeyword = "_devicePTZ";
        private string _storageEndpointKeyword = "Storage";
        private string _assetsEndpointKeyword = "Assets";
        private string _reportEndpointKeyword = "Report";
        private string _mapEndpointKeyword = "Map";
        private string _roiConfigApiUrl;
        private string _roiConfigApiToken;
        private double _roiThresholdSeconds = 5;
        private double _roiMergeGapSeconds = 200;
        private string _cameraHealthTimeseriesApiUrl;
        private string _aiEventFeedPath;
        private string _aiEventSummaryPath;
        private string _aiEventRoiObjectsPath;
        private string _storageAccessUrlsApiUrl;
        private string _storageAccessUrlsApiToken;
        // This gateway key is deliberately separate from _backendToken.
        // _backendToken is replaced by the interactive-login JWT, whereas the
        // status gateway always expects its own X-API-Key.
        private string _deviceStatusApiKey;
        private string _vehicleStatsApiToken;
        private string _metadataWsUrl;
        private string _backendToken;

        // Multi-service Endpoint Registry
        private List<EndpointProfile> _endpointRegistry = new List<EndpointProfile>();

        private string _networkMode = "Public"; // "Public" or "Internal"

        // Local cache for primary storage (backward compatibility)
        private string _storageUrl;
        private string _storageToken;
        private string _assetsUrl;
        private string _assetsToken;
        private string _reportUrl;
        private string _reportToken;
        private string _mapUrl;
        private string _redisUrl;

       
        public string BaseUrl => _baseUrl;
        public string StreamApiUrl => _streamApiUrl;
        public string DeviceStatusApiUrl => _deviceStatusApiUrl;
        public string MetadataWsUrl => _metadataWsUrl;
        public string NetworkMode => _networkMode;
        public string StorageUrl => _storageUrl;
        public string BackendToken => _backendToken;
        public string StorageToken => _storageToken;
        public string AssetsUrl => _assetsUrl;
        public string AssetsToken => _assetsToken;
        public string ReportUrl => _reportUrl;
        public string ReportToken => _reportToken;
        public string RedisUrl => _redisUrl;
        public string MapUrl => _mapUrl;
     
        private const string ConfigFile = "server_config.json";
        private const string BackendTokenEnvironmentVariable = "IVISTA_BACKEND_TOKEN";
        private const string FrameDetectionCountsApiUrlEnvironmentVariable = "IVISTA_FRAME_DETECTION_COUNTS_API_URL";
        private const string LiveFrameDetectionCountsApiUrlEnvironmentVariable = "IVISTA_LIVE_FRAME_DETECTION_COUNTS_API_URL";
        private const string CameraVehicleCountsApiUrlEnvironmentVariable = "IVISTA_CAMERA_VEHICLE_COUNTS_API_URL";
        private const string AiEventFeedPathEnvironmentVariable = "IVISTA_AI_EVENT_FEED_PATH";

        private ApiManager()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            var loginHandler = new HttpClientHandler { UseProxy = false };
            _loginHttpClient = new HttpClient(loginHandler);
            _loginHttpClient.Timeout = TimeSpan.FromSeconds(12);
            var statusHandler = new HttpClientHandler { UseProxy = false };
            _deviceStatusHttpClient = new HttpClient(statusHandler);
            _deviceStatusHttpClient.Timeout = TimeSpan.FromSeconds(12);
            LoadConfig();
            LoadBackendTokenFromEnvironment();
            LoadFrameDetectionCountsApiUrlFromEnvironment();
        }

        /// <summary>
        /// Optional deployment token.  It is intentionally read from the
        /// per-user/process environment rather than app.config or source, so
        /// it is not committed with the client project or copied into builds.
        /// A normal interactive login still replaces it for that session.
        /// </summary>
        private void LoadBackendTokenFromEnvironment()
        {
            var token = Environment.GetEnvironmentVariable(BackendTokenEnvironmentVariable, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(token))
                token = Environment.GetEnvironmentVariable(BackendTokenEnvironmentVariable, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(token))
                SetBackendToken(token.Trim());
        }

        private void LoadFrameDetectionCountsApiUrlFromEnvironment()
        {
            var url = Environment.GetEnvironmentVariable(FrameDetectionCountsApiUrlEnvironmentVariable, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(url))
                url = Environment.GetEnvironmentVariable(FrameDetectionCountsApiUrlEnvironmentVariable, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(url))
                _frameDetectionCountsApiUrl = url.Trim().TrimEnd('/');

            var liveUrl = Environment.GetEnvironmentVariable(LiveFrameDetectionCountsApiUrlEnvironmentVariable, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(liveUrl))
                liveUrl = Environment.GetEnvironmentVariable(LiveFrameDetectionCountsApiUrlEnvironmentVariable, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(liveUrl))
                _liveFrameDetectionCountsApiUrl = NormalizeServiceBaseUrl(liveUrl);

            var vehicleCountsUrl = Environment.GetEnvironmentVariable(CameraVehicleCountsApiUrlEnvironmentVariable, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(vehicleCountsUrl))
                vehicleCountsUrl = Environment.GetEnvironmentVariable(CameraVehicleCountsApiUrlEnvironmentVariable, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(vehicleCountsUrl))
                _cameraVehicleCountsApiUrl = vehicleCountsUrl.Trim().TrimEnd('/');

            var feedPath = Environment.GetEnvironmentVariable(AiEventFeedPathEnvironmentVariable, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(feedPath))
                feedPath = Environment.GetEnvironmentVariable(AiEventFeedPathEnvironmentVariable, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(feedPath))
                _aiEventFeedPath = feedPath.Trim();
        }

        /// <summary>
        /// Reads the deployment-only status gateway configuration.  This file
        /// is explicitly gitignored and copied beside the executable only for
        /// local builds, so its API key never enters source control.
        /// </summary>
        private void LoadDeviceStatusLocalConfig()
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_status.local.json");
                if (!File.Exists(path))
                    return;

                var config = JsonConvert.DeserializeObject<DeviceStatusLocalConfig>(File.ReadAllText(path));
                if (config == null)
                    return;

                if (!string.IsNullOrWhiteSpace(config.ApiUrl))
                    _deviceStatusApiUrl = config.ApiUrl.Trim().TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(config.ApiKey))
                    _deviceStatusApiKey = config.ApiKey.Trim();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Cannot load device_status.local.json");
            }
        }

        public void LoadConfig()
        {
            try
            {
                var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFile);
                if (System.IO.File.Exists(configPath))
                {
                    string json = System.IO.File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<ClientConfig>(json);
                    if (config != null)
                    {
                        _baseUrl = string.IsNullOrWhiteSpace(config.ApiUrl) ? null : config.ApiUrl.Trim().TrimEnd('/');
                        if (!string.IsNullOrWhiteSpace(config.StreamApiUrl))
                            _streamApiUrl = config.StreamApiUrl.TrimEnd('/');
                        _storageUrl = string.IsNullOrWhiteSpace(config.StorageUrl)
                            ? null
                            : config.StorageUrl.Trim().TrimEnd('/');
                        _mapUrl = string.IsNullOrWhiteSpace(config.MapUrl)
                            ? null
                            : config.MapUrl.Trim().TrimEnd('/');
                        _metadataWsUrl = string.IsNullOrWhiteSpace(config.MetadataWsUrl)
                            ? null
                            : config.MetadataWsUrl.Trim();
                        _frameDetectionCountsApiUrl = string.IsNullOrWhiteSpace(config.FrameDetectionCountsApiUrl)
                            ? null
                            : config.FrameDetectionCountsApiUrl.Trim().TrimEnd('/');
                        _liveFrameDetectionCountsApiUrl = string.IsNullOrWhiteSpace(config.LiveFrameDetectionCountsApiUrl)
                            ? null
                            : NormalizeServiceBaseUrl(config.LiveFrameDetectionCountsApiUrl);
                        _cameraVehicleCountsApiUrl = string.IsNullOrWhiteSpace(config.CameraVehicleCountsApiUrl)
                            ? null
                            : config.CameraVehicleCountsApiUrl.Trim().TrimEnd('/');
                        if (!string.IsNullOrWhiteSpace(config.AiReportEndpointKeyword))
                            _aiReportEndpointKeyword = config.AiReportEndpointKeyword.Trim();
                        if (!string.IsNullOrWhiteSpace(config.DeviceReportEndpointKeyword))
                            _deviceReportEndpointKeyword = config.DeviceReportEndpointKeyword.Trim();
                        if (!string.IsNullOrWhiteSpace(config.RoiConfigEndpointKeyword))
                            _roiConfigEndpointKeyword = config.RoiConfigEndpointKeyword.Trim();
                        if (!string.IsNullOrWhiteSpace(config.StorageEndpointKeyword))
                            _storageEndpointKeyword = config.StorageEndpointKeyword.Trim();
                        if (!string.IsNullOrWhiteSpace(config.AssetsEndpointKeyword))
                            _assetsEndpointKeyword = config.AssetsEndpointKeyword.Trim();
                        if (!string.IsNullOrWhiteSpace(config.ReportEndpointKeyword))
                            _reportEndpointKeyword = config.ReportEndpointKeyword.Trim();
                        if (!string.IsNullOrWhiteSpace(config.MapEndpointKeyword))
                            _mapEndpointKeyword = config.MapEndpointKeyword.Trim();
                        _roiConfigApiUrl = string.IsNullOrWhiteSpace(config.RoiConfigApiUrl)
                            ? null
                            : config.RoiConfigApiUrl.Trim().TrimEnd('/');
                        _roiConfigApiToken = string.IsNullOrWhiteSpace(config.RoiConfigApiToken)
                            ? null
                            : config.RoiConfigApiToken.Trim();
                        _roiThresholdSeconds = config.RoiThresholdSeconds > 0 ? config.RoiThresholdSeconds : 5;
                        _roiMergeGapSeconds = config.RoiMergeGapSeconds >= 0 ? config.RoiMergeGapSeconds : 200;
                        _cameraHealthTimeseriesApiUrl = string.IsNullOrWhiteSpace(config.CameraHealthTimeseriesApiUrl)
                            ? null
                            : config.CameraHealthTimeseriesApiUrl.Trim().TrimEnd('/');
                        _vehicleStatsApiToken = string.IsNullOrWhiteSpace(config.VehicleStatsApiToken)
                            ? null
                            : config.VehicleStatsApiToken.Trim();
                        _aiEventFeedPath = string.IsNullOrWhiteSpace(config.AiEventFeedPath)
                            ? null
                            : config.AiEventFeedPath.Trim();
                        _aiEventSummaryPath = string.IsNullOrWhiteSpace(config.AiEventSummaryPath)
                            ? null
                            : config.AiEventSummaryPath.Trim();
                        _aiEventRoiObjectsPath = string.IsNullOrWhiteSpace(config.AiEventRoiObjectsPath)
                            ? null
                            : config.AiEventRoiObjectsPath.Trim();
                        _storageAccessUrlsApiUrl = string.IsNullOrWhiteSpace(config.StorageAccessUrlsApiUrl)
                            ? null
                            : config.StorageAccessUrlsApiUrl.Trim().TrimEnd('/');
                        _storageAccessUrlsApiToken = string.IsNullOrWhiteSpace(config.StorageAccessUrlsApiToken)
                            ? null
                            : config.StorageAccessUrlsApiToken.Trim();
                        _networkMode = config.NetworkMode;
                        // Device-status endpoint: local-config and environment variables take
                        // priority (they are loaded in the constructor before LoadConfig runs).
                        if (string.IsNullOrWhiteSpace(_deviceStatusApiUrl) && !string.IsNullOrWhiteSpace(config.DeviceStatusApiUrl))
                            _deviceStatusApiUrl = config.DeviceStatusApiUrl.Trim().TrimEnd('/');
                        if (string.IsNullOrWhiteSpace(_deviceStatusApiKey) && !string.IsNullOrWhiteSpace(config.DeviceStatusApiKey))
                            _deviceStatusApiKey = config.DeviceStatusApiKey.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
            }
        }

        public void SaveConfig(string apiUrl, string networkMode)
        {
            try
            {
                _baseUrl = apiUrl;
                _networkMode = networkMode;
                var config = new ClientConfig
                {
                    ApiUrl = apiUrl,
                    StreamApiUrl = _streamApiUrl,
                    StorageUrl = _storageUrl,
                    MapUrl = _mapUrl,
                    MetadataWsUrl = _metadataWsUrl,
                    FrameDetectionCountsApiUrl = _frameDetectionCountsApiUrl,
                    LiveFrameDetectionCountsApiUrl = _liveFrameDetectionCountsApiUrl,
                    CameraVehicleCountsApiUrl = _cameraVehicleCountsApiUrl,
                    AiReportEndpointKeyword = _aiReportEndpointKeyword,
                    StorageEndpointKeyword = _storageEndpointKeyword,
                    AssetsEndpointKeyword = _assetsEndpointKeyword,
                    ReportEndpointKeyword = _reportEndpointKeyword,
                    MapEndpointKeyword = _mapEndpointKeyword,
                    RoiConfigEndpointKeyword = _roiConfigEndpointKeyword,
                    RoiConfigApiUrl = _roiConfigApiUrl,
                    RoiConfigApiToken = _roiConfigApiToken,
                    RoiThresholdSeconds = _roiThresholdSeconds,
                    RoiMergeGapSeconds = _roiMergeGapSeconds,
                    CameraHealthTimeseriesApiUrl = _cameraHealthTimeseriesApiUrl,
                    AiEventFeedPath = _aiEventFeedPath,
                    StorageAccessUrlsApiUrl = _storageAccessUrlsApiUrl,
                    StorageAccessUrlsApiToken = _storageAccessUrlsApiToken,
                    DeviceReportEndpointKeyword = _deviceReportEndpointKeyword,
                    DeviceStatusApiKey = _deviceStatusApiKey,
                    NetworkMode = networkMode
                };
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFile);
                System.IO.File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        private static string NormalizeServiceBaseUrl(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
        }

        public void Configure(string host, int port)
        {
            _baseUrl = $"http://{host}:{port}";
        }

        public void SetBackendToken(string token)
        {
            _backendToken = token;
            // Note: Default header usually points to the main Backend. 
            // For Storage/Satellite servers, we use explicit headers in each request.
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _backendToken);
        }

        /// <summary>
        /// Removes every credential discovered for the current user.  Keeping
        /// endpoint tokens after returning to the login screen made a later
        /// session continue to use the previous user's authorization.
        /// </summary>
        public void ClearAuthentication()
        {
            _backendToken = null;
            _storageToken = null;
            _assetsToken = null;
            _reportToken = null;
            _endpointRegistry.Clear();
            _httpClient.DefaultRequestHeaders.Clear();
        }

        private const int AuthenticationRequestAttempts = 3;

        private async Task<HttpResponseMessage> SendWithTransientRetryAsync(
            Func<CancellationToken, Task<HttpResponseMessage>> operation,
            CancellationToken cancellationToken,
            string operationName,
            int maxAttempts = AuthenticationRequestAttempts,
            int timeoutSeconds = 12)
        {
            Exception lastException = null;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                        var response = await operation(timeout.Token).ConfigureAwait(false);
                        if (!IsTransientStatus(response.StatusCode) || attempt == maxAttempts)
                            return response;

                        response.Dispose();
                        LoggerManager.LogWarn(operationName + " temporarily failed; retry " + attempt + "/" + AuthenticationRequestAttempts + ".");
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lastException = new TimeoutException(operationName + " timed out.");
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                }

                if (attempt < maxAttempts)
                {
                    LoggerManager.LogWarn(operationName + " connection failed; retry " + attempt + "/" + maxAttempts + ".");
                    await Task.Delay(TimeSpan.FromMilliseconds(350 * attempt), cancellationToken).ConfigureAwait(false);
                }
            }

            throw new HttpRequestException(operationName + " could not connect after retry.", lastException);
        }

        private static bool IsTransientStatus(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.RequestTimeout ||
                   statusCode == (HttpStatusCode)429 ||
                   (int)statusCode >= 500;
        }

        public async Task<ApiManager.PlaybackSearchResult> GetPlaybackInfoAsync(string camId, System.DateTime start, System.DateTime end)
        {
            try
            {
                string storageUrl = GetEndpointUrl("Storage") ?? _baseUrl;
                string storageToken = GetEndpointToken("Storage");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", storageToken);

                var response = await _httpClient.GetAsync($"{storageUrl}/api/play/info/{camId}?start={start:s}&end={end:s}");
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<ApiManager.PlaybackSearchResult>(resultJson);
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting playback info: {ex.Message}");
                return null;
            }
            finally
            {
                // Restore default header for backend after specific request
                _httpClient.DefaultRequestHeaders.Clear();
                if (!string.IsNullOrEmpty(_backendToken))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _backendToken);
                }
            }
        }

        public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken)
        {
            LoggerManager.LogDebug($"Đang gọi API Đăng nhập cho user: {username} tại {_baseUrl}");
            try
            {
                var credentials = new { username = username, password = password };
                var json = JsonConvert.SerializeObject(credentials);
                using (var response = await SendWithTransientRetryAsync(async requestToken =>
                {
                    // A fresh connection avoids reusing a gateway connection that
                    // has already been closed while the login window was idle.
                    using (var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/auth/login"))
                    {
                        request.Headers.ConnectionClose = true;
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        return await _loginHttpClient.SendAsync(request, requestToken).ConfigureAwait(false);
                    }
                }, cancellationToken, "Login", maxAttempts: 2, timeoutSeconds: 3))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var resultJson = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<LoginResponse>(resultJson);

                        if (result == null || string.IsNullOrWhiteSpace(result.access_token))
                            return new LoginResult(false, null, "Máy chủ không trả về phiên đăng nhập hợp lệ.");

                        SetBackendToken(result.access_token);

                        // Load endpoint profiles before returning from login. The live
                        // dashboard can request AI vehicle counts immediately after
                        // login, so running discovery only in the background creates a
                        // race where the old configured URL/token is used.
                        var endpointsDiscovered = await DiscoverEndpointsAsync(cancellationToken)
                            .ConfigureAwait(false);
                        if (!endpointsDiscovered)
                            LoggerManager.LogWarn("Endpoint discovery failed after login; public AI report fallback will be used.");
                        LoggerManager.LogInfo($"Gửi yêu cầu đăng nhập thành công cho: {username}");
                        return new LoginResult(true, result.user_id, "Success");
                    }

                    LoggerManager.LogWarn($"Đăng nhập không thành công (HTTP {response.StatusCode}) cho user: {username}");
                    return new LoginResult(false, null, "Invalid username or password");
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi nghiêm trọng khi gọi LoginAsync cho {username}");
                ClearAuthentication();
                return new LoginResult(false, null, "Không thể kết nối tới máy chủ sau nhiều lần thử. Vui lòng kiểm tra Internet hoặc DNS và thử lại.");
            }
        }

        public async Task<List<ClientProfile>> GetClientProfilesAsync(CancellationToken cancellationToken)
        {
            LoggerManager.LogDebug($"Gọi API lấy danh sách Client Profiles: {_baseUrl}/api/v1/client-profiles");
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/client-profiles", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var list = JsonConvert.DeserializeObject<List<ClientProfile>>(resultJson);
                    LoggerManager.LogInfo($"Đã lấy {list?.Count ?? 0} profiles thành công.");
                    return list ?? new List<ClientProfile>();
                }
                LoggerManager.LogWarn($"Lấy profiles thất bại: HTTP {response.StatusCode}");
                return new List<ClientProfile>();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetClientProfilesAsync");
                return new List<ClientProfile>();
            }
        }

        public async Task<List<ClientProfile>> GetMyAuthorizedProfilesAsync(CancellationToken cancellationToken)
        {
            //LoggerManager.LogDebug($"Gọi API lấy profiles được gán cho user: {_baseUrl}/api/v1/client-profiles/me/authorized");
            try
            {
                var response = await SendWithTransientRetryAsync(
                    retryToken => _httpClient.GetAsync($"{_baseUrl}/api/v1/client-profiles/me/authorized", retryToken), cancellationToken, "Load authorized profiles", maxAttempts: 1, timeoutSeconds: 3);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var list = JsonConvert.DeserializeObject<List<ClientProfile>>(resultJson);
                    LoggerManager.LogInfo($"Đã lấy {list?.Count ?? 0} authorized profiles.");
                    return list ?? new List<ClientProfile>();
                }
                LoggerManager.LogWarn($"Lấy authorized profiles thất bại: HTTP {response.StatusCode}");
                return new List<ClientProfile>();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetMyAuthorizedProfilesAsync");
                return new List<ClientProfile>();
            }
        }

        /// <summary>
        /// Uses the same profile projection as the web client. Some deployments
        /// expose more than the legacy /client-profiles/me/authorized projection.
        /// The endpoint remains protected by the existing bearer session.
        /// </summary>
        public async Task<List<ClientProfile>> GetWebProfilesAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await SendWithTransientRetryAsync(
                    retryToken => _httpClient.GetAsync($"{_baseUrl}/api/user/profiles", retryToken), cancellationToken, "Load profiles", maxAttempts: 1, timeoutSeconds: 4);
                if (!response.IsSuccessStatusCode) return new List<ClientProfile>();
                var json = await response.Content.ReadAsStringAsync();
                var list = JsonConvert.DeserializeObject<List<ClientProfile>>(json);
                LoggerManager.LogInfo($"Đã lấy {list?.Count ?? 0} profiles theo web contract.");
                return list ?? new List<ClientProfile>();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetWebProfilesAsync");
                return new List<ClientProfile>();
            }
        }

        public async Task<List<AccountInfo>> GetAccountsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/users/all", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<AccountInfo>>(resultJson);
                }
                return new List<AccountInfo>();
            }
            catch
            {
                return new List<AccountInfo>();
            }
        }

        public async Task<ClientProfile> CreateClientProfileAsync(object profileData, CancellationToken cancellationToken)
        {
            try
            {
                var json = JsonConvert.SerializeObject(profileData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/client-profiles", content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<ClientProfile>(resultJson);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> UpdateClientProfileAsync(Guid id, object profileData, CancellationToken cancellationToken)
        {
            try
            {
                var json = JsonConvert.SerializeObject(profileData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync($"{_baseUrl}/api/v1/client-profiles/{id}", content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteClientProfileAsync(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/v1/client-profiles/{id}", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<CamInfo>> GetCamInfoAsync(CancellationToken cancellationToken, string profileId = null)
        {
            string url = $"{_baseUrl}/api/user/cameras";
            if (!string.IsNullOrEmpty(profileId))
            {
                url += $"?profile_id={profileId}";
            }
            LoggerManager.LogDebug($"Gọi API lấy danh sách Camera: {url}");

            try
            {
                var response = await SendWithTransientRetryAsync(
                    retryToken => _httpClient.GetAsync(url, retryToken), cancellationToken, "Load cameras");
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var list = JsonConvert.DeserializeObject<List<CamInfo>>(resultJson);
                    LoggerManager.LogInfo($"Đã lấy {list?.Count ?? 0} cameras thành công cho profile: {profileId}");
                    return list ?? new List<CamInfo>();
                }
                LoggerManager.LogWarn($"Lấy danh sách camera thất bại: HTTP {response.StatusCode} cho profile: {profileId}");
                return new List<CamInfo>();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi khi gọi GetCamInfoAsync cho profile {profileId}");
                return new List<CamInfo>();
            }
        }

        public async Task<UserMeResponse> GetMeAsync(CancellationToken cancellationToken)
        {
            LoggerManager.LogDebug($"Gọi API lấy thông tin User hiện tại: {_baseUrl}/api/v1/auth/me");
            try
            {
                var response = await SendWithTransientRetryAsync(
                    retryToken => _httpClient.GetAsync($"{_baseUrl}/api/v1/auth/me", retryToken), cancellationToken, "Load current user", maxAttempts: 1, timeoutSeconds: 3);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<UserMeResponse>(resultJson);
                }
                LoggerManager.LogWarn($"Lấy thông tin User thất bại: HTTP {response.StatusCode}");
                return null;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetMeAsync");
                return null;
            }
        }

        public async Task<Dictionary<Guid, BlacklistObjectFaceInfo>> GetAllBlacklistFaceInfoAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/knowledge/blacklist", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var list = JsonConvert.DeserializeObject<List<BlacklistObjectFaceInfo>>(resultJson);
                    return list.ToDictionary(x => x.Id, x => x);
                }
                return new Dictionary<Guid, BlacklistObjectFaceInfo>();
            }
            catch
            {
                return new Dictionary<Guid, BlacklistObjectFaceInfo>();
            }
        }

        public async Task<List<RoiInfo>> GetRoisAsync(string camId, CancellationToken cancellationToken)
        {
            try
            {
                // 1. Try V1 API first: /api/v1/rois/camera/{camId}
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/rois/camera/{camId}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var rois = JsonConvert.DeserializeObject<List<RoiInfo>>(resultJson);
                    if (rois != null && rois.Count > 0) return rois;
                }

                // 2. Fallback to Legacy for robustness (handles camera_code and more lenient scoping)
                var legacyResponse = await _httpClient.GetAsync($"{_baseUrl}/api/rois/{camId}", cancellationToken);
                if (legacyResponse.IsSuccessStatusCode)
                {
                    var resultJson = await legacyResponse.Content.ReadAsStringAsync();
                    // Legacy returns {"rois": [...]}
                    var wrapper = JsonConvert.DeserializeObject<RoiWrapper>(resultJson);
                    return wrapper?.Rois ?? new List<RoiInfo>();
                }

                return new List<RoiInfo>();
            }
            catch
            {
                return new List<RoiInfo>();
            }
        }

        public async Task<bool> DeleteRoiAsync(string roiId, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/v1/rois/{roiId}", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> CreateRoiAsync(object data, CancellationToken cancellationToken)
        {
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/rois/", content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UpdateRoiAsync(string roiId, object data, CancellationToken cancellationToken)
        {
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{_baseUrl}/api/v1/rois/{roiId}")
                {
                    Content = content
                };
                var response = await _httpClient.SendAsync(request, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<AIServiceInfo>> GetAIServicesAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/ai-services/", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<AIServiceInfo>>(resultJson);
                }
                return new List<AIServiceInfo>();
            }
            catch
            {
                return new List<AIServiceInfo>();
            }
        }

        public async Task<List<CameraGroupInfo>> GetCameraGroupsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/cameras/groups", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<CameraGroupInfo>>(resultJson);
                }
                return new List<CameraGroupInfo>();
            }
            catch
            {
                return new List<CameraGroupInfo>();
            }
        }

        public async Task<bool> CreateCameraGroupAsync(object data, CancellationToken cancellationToken)
        {
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/cameras/groups", content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UpdateCameraGroupAsync(Guid groupId, object data, CancellationToken cancellationToken)
        {
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{_baseUrl}/api/v1/cameras/groups/{groupId}")
                {
                    Content = content
                };
                var response = await _httpClient.SendAsync(request, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteCameraGroupAsync(Guid groupId, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/v1/cameras/groups/{groupId}", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public class CameraGroupInfo
        {
            [JsonProperty("id")]
            public Guid Id { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("code")]
            public string Code { get; set; }
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("description")]
            public string Description { get; set; }
            [JsonProperty("parent_id")]
            public Guid? ParentId { get; set; }
            [JsonProperty("extra_metadata")]
            public object ExtraMetadata { get; set; }
            [JsonProperty("created_at")]
            public DateTime CreatedAt { get; set; }
            [JsonProperty("updated_at")]
            public DateTime UpdatedAt { get; set; }
        }

        public async Task<string> GetCameraSnapshotAsync(string camId, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/rois/snapshot/{camId}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var jsonObj = JsonConvert.DeserializeObject<dynamic>(resultJson);
                    string imageUrl = jsonObj.image_url;

                    // If the URL is relative, prepend baseUrl
                    if (!string.IsNullOrEmpty(imageUrl) && !imageUrl.StartsWith("http"))
                    {
                        imageUrl = _baseUrl.TrimEnd('/') + "/" + imageUrl.TrimStart('/');
                    }
                    return imageUrl;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private class RoiWrapper
        {
            [JsonProperty("rois")]
            public List<RoiInfo> Rois { get; set; }
        }

        public async Task<List<CameraAIAssignmentInfo>> GetCameraAIConfigsAsync(string camId, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/camera-ai-assignments/camera/{camId}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<CameraAIAssignmentInfo>>(resultJson);
                }
                return new List<CameraAIAssignmentInfo>();
            }
            catch
            {
                return new List<CameraAIAssignmentInfo>();
            }
        }

        public async Task<bool> AssignCameraToAIAsync(string camId, Guid serviceId, object config, object bodyCamConfig, object aiParams, CancellationToken cancellationToken)
        {
            try
            {
                var body = new { camera_id = camId, ai_service_id = serviceId, config_json = config, bodycam_config = bodyCamConfig, ai_params = aiParams, is_active = true, is_enabled = true };
                var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/camera-ai-assignments/assign", content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }


        public async Task<bool> UpdateCameraAIConfigAsync(Guid configId, object updateData, CancellationToken cancellationToken)
        {
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(updateData), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{_baseUrl}/api/v1/camera-ai-assignments/{configId}")
                {
                    Content = content
                };
                var response = await _httpClient.SendAsync(request, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RemoveAIAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/v1/camera-ai-assignments/{assignmentId}", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        // ============ Camera CRUD Operations ============

        public async Task<List<CameraDetailInfo>> GetAllCamerasAsync(CancellationToken cancellationToken, string groupId = null)
        {
            try
            {
                string url = $"{_baseUrl}/api/v1/cameras/";
                if (!string.IsNullOrEmpty(groupId))
                    url += $"?group_id={groupId}";

                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<CameraDetailInfo>>(resultJson);
                }
                return new List<CameraDetailInfo>();
            }
            catch
            {
                return new List<CameraDetailInfo>();
            }
        }

        public async Task<bool> CreateCameraAsync(CameraCreateRequest data, CancellationToken cancellationToken)
        {
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/cameras/", content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UpdateCameraAsync(string cameraId, object data, CancellationToken cancellationToken)
        {
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{_baseUrl}/api/v1/cameras/{cameraId}")
                {
                    Content = content
                };
                var response = await _httpClient.SendAsync(request, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DeleteCameraAsync(string cameraId, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/v1/cameras/{cameraId}", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<CameraDetailInfo> GetCameraDetailAsync(string cameraId, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/cameras/{cameraId}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<CameraDetailInfo>(resultJson);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<CameraDependencyInfo> GetCameraDependenciesAsync(string cameraId, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/cameras/{cameraId}/dependencies", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<CameraDependencyInfo>(resultJson);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<MediaServerInfo>> GetMediaServersAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/media-servers/", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<MediaServerInfo>>(resultJson);
                }
                return new List<MediaServerInfo>();
            }
            catch
            {
                return new List<MediaServerInfo>();
            }
        }

        // ============ AI Search / Report API ============

        public async Task<string> GetFaceFrequencyAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_reportUrl)) return null;
                string token = string.IsNullOrEmpty(_reportToken) ? _backendToken : _reportToken;
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_reportUrl}/api/v1/reports/faces/frequency");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                
                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync();
                
                return null;
            }
            catch { return null; }
        }

        public async Task<string> GetTrafficLogsAsync(int pageSize)
        {
            try
            {
                if (string.IsNullOrEmpty(_reportUrl)) return null;
                string token = string.IsNullOrEmpty(_reportToken) ? _backendToken : _reportToken;

                var request = new HttpRequestMessage(HttpMethod.Get, $"{_reportUrl}/api/v1/reports/traffic/logs?page_size={pageSize}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync();

                return null;
            }
            catch { return null; }
        }

        public async Task<string> GetSearchTrajectoryAsync(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(_reportUrl)) return null;
                string token = string.IsNullOrEmpty(_reportToken) ? _backendToken : _reportToken;

                var request = new HttpRequestMessage(HttpMethod.Get, $"{_reportUrl}/api/v1/{path}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync();

                return null;
            }
            catch { return null; }
        }

        public async Task<TrajectoryDto> GetPlateTrajectoryAsync(string plateNumber, DateTime? start, DateTime? end, List<string> cameraIds, int limit, bool useNtp = false)
        {
            return await GetTrajectoryAsync($"reports/traffic/advanced/trajectory/{Uri.EscapeDataString(plateNumber)}", start, end, cameraIds, limit, useNtp);
        }

        public async Task<TrajectoryDto> GetPersonTrajectoryAsync(string personId, DateTime? start, DateTime? end, List<string> cameraIds, int limit, bool useNtp = false)
        {
            return await GetTrajectoryAsync($"reports/attendance/advanced/trajectory/{Uri.EscapeDataString(personId)}", start, end, cameraIds, limit, useNtp);
        }

        private async Task<TrajectoryDto> GetTrajectoryAsync(string relativePath, DateTime? start, DateTime? end, List<string> cameraIds, int limit, bool useNtp)
        {
            try
            {
                if (string.IsNullOrEmpty(_reportUrl)) return null;
                string token = string.IsNullOrEmpty(_reportToken) ? _backendToken : _reportToken;

                var qs = new List<string>();
                if (start.HasValue) qs.Add($"start_date={start.Value:s}");
                if (end.HasValue) qs.Add($"end_date={end.Value:s}");
                if (limit > 0) qs.Add($"limit={limit}");
                qs.Add($"use_ntp={(useNtp ? "true" : "false")}");
                qs.Add("img=true"); // Enable asset_id retrieval

                if (cameraIds != null && cameraIds.Count > 0)
                {
                    foreach (var camId in cameraIds)
                    {
                        qs.Add($"camera_ids={Uri.EscapeDataString(camId)}");
                    }
                }

                string fullUrl = $"{_reportUrl}/api/v1/{relativePath}";
                if (qs.Count > 0) fullUrl += "?" + string.Join("&", qs);

                var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<TrajectoryDto>(json);
                }
                return null;
            }
            catch { return null; }
        }

        public async Task<string> GetAssetAccessUrlAsync(string assetId)
        {
            try
            {
                if (string.IsNullOrEmpty(_storageUrl)) return null;

                var body = new
                {
                    asset_id = assetId,
                    access_scope = "external",
                    duration = 3600,
                    as_attachment = false,
                    use_proxy = true
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_storageUrl.TrimEnd('/')}/api/assets/get-access-url");
                request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                
                if (!string.IsNullOrEmpty(_storageToken))
                {
                    request.Headers.Add("X-Service-Token", _storageToken);
                }

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<dynamic>(json);
                    return result.access_url;
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>Gets a short-lived dashboard thumbnail URL using the same batch contract as the web app.</summary>
        public async Task<string> GetDashboardAssetAccessUrlAsync(string assetId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(assetId)) return null;

            // The web client uses a dedicated storage proxy route.  Keep the
            // configured route first, then use the Storage endpoint obtained
            // from Portal (public URL before internal URL).
            var storageRoutes = new List<string>();
            foreach (var route in new[]
            {
                _storageAccessUrlsApiUrl,
                GetEndpointProfile(_storageEndpointKeyword)?.PublicUrl,
                GetEndpointProfile(_storageEndpointKeyword)?.InternalUrl,
                _storageUrl
            })
            {
                if (string.IsNullOrWhiteSpace(route)) continue;
                var normalized = route.Trim().TrimEnd('/');
                if (!normalized.EndsWith("/api/storage/get-access-urls", StringComparison.OrdinalIgnoreCase))
                    normalized += "/api/storage/get-access-urls";
                if (!storageRoutes.Any(value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase)))
                    storageRoutes.Add(normalized);
            }
            if (storageRoutes.Count == 0) return null;

            try
            {
                var body = new { asset_ids = new[] { assetId }, access_scope = "file", duration = 3600, as_attachment = false, page = 1, page_size = 1 };
                var accessToken = !string.IsNullOrWhiteSpace(_storageAccessUrlsApiToken)
                    ? _storageAccessUrlsApiToken
                    : _storageToken;
                foreach (var storageAccessUrlsApiUrl in storageRoutes)
                {
                    try
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Post, storageAccessUrlsApiUrl))
                        using (var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                        {
                            requestTimeout.CancelAfter(TimeSpan.FromSeconds(7));
                            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                            if (!string.IsNullOrWhiteSpace(accessToken)) request.Headers.TryAddWithoutValidation("Authorization", accessToken);
                            using (var response = await _httpClient.SendAsync(request, requestTimeout.Token))
                            {
                                if (!response.IsSuccessStatusCode)
                                {
                                    LoggerManager.LogWarn($"Storage thumbnail access returned {(int)response.StatusCode} from {storageAccessUrlsApiUrl}.");
                                    continue;
                                }
                                var json = await response.Content.ReadAsStringAsync();
                                var payload = JsonConvert.DeserializeObject<dynamic>(json);
                                var items = payload?.items;
                                if (items == null || items.Count == 0) continue;
                                var accessUrl = (string)items[0].access_url;
                                if (string.IsNullOrWhiteSpace(accessUrl)) continue;

                                // The storage service returns a relative path (for example "/files/...").
                                // Browsers resolve it through the web proxy, but WPF BitmapImage requires an
                                // absolute URI, so anchor it to the responding storage service origin.
                                Uri absoluteUrl;
                                if (Uri.TryCreate(accessUrl, UriKind.Absolute, out absoluteUrl)) return absoluteUrl.AbsoluteUri;

                                Uri storageApiUri;
                                if (!Uri.TryCreate(storageAccessUrlsApiUrl, UriKind.Absolute, out storageApiUri)) continue;
                                var storageOrigin = new Uri(storageApiUri.GetLeftPart(UriPartial.Authority) + "/");
                                return new Uri(storageOrigin, accessUrl.TrimStart('/')).AbsoluteUri;
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        LoggerManager.LogWarn($"Storage thumbnail access timed out via {storageAccessUrlsApiUrl}; trying fallback.");
                    }
                }
                return null;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Dashboard storage thumbnail");
                return null;
            }
        }

        // ============ Storage / Playback Server API ============
        public List<EndpointProfile> GetDiscoveredEndpoints() => _endpointRegistry;

        private EndpointProfile GetEndpointProfile(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return null;
            return _endpointRegistry.FirstOrDefault(x => x != null &&
                !string.IsNullOrWhiteSpace(x.Keyword) &&
                x.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
        }

        public string GetEndpointUrl(string keyword)
        {
            if (string.Equals(keyword, "_playback", StringComparison.OrdinalIgnoreCase))
            {
                string source;
                string playbackUrl = PlaybackEndpointResolver_v3.Resolve(
                    _endpointRegistry,
                    _networkMode,
                    out source);
                LoggerManager.LogDebug($"Playback endpoint resolved from {source}: {playbackUrl}");
                return playbackUrl;
            }

            var profile = GetEndpointProfile(keyword);
            if (profile == null) return null;

            // Desktop clients reach services through their public endpoint;
            // never fall back to a discovered private LAN address.
            return string.IsNullOrWhiteSpace(profile.PublicUrl) ? null : profile.PublicUrl.TrimEnd('/');
        }

        public string GetEndpointToken(string keyword)
        {
            if (string.Equals(keyword, "_playback", StringComparison.OrdinalIgnoreCase))
            {
                string localPlaybackToken = PlaybackTokenStore.Load();
                if (!string.IsNullOrWhiteSpace(localPlaybackToken))
                {
                    return localPlaybackToken;
                }
            }

            var profile = GetEndpointProfile(keyword);
            return (profile != null && !string.IsNullOrEmpty(profile.Token)) ? profile.Token : _backendToken;
        }

        private string ResolveAiReportBaseUrl(out string token)
        {
            var report = GetEndpointProfile(_aiReportEndpointKeyword);
            if (report != null && !string.IsNullOrWhiteSpace(report.PublicUrl))
            {
                token = (report.Token ?? string.Empty).Trim();
                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = token.Substring("Bearer ".Length).Trim();
                // Desktop clients must use the endpoint's public URL. The
                // internal URL is only for service-to-service traffic.
                var reportUrl = report.PublicUrl;
                return reportUrl.TrimEnd('/');
            }

            token = (_vehicleStatsApiToken ?? _backendToken ?? string.Empty).Trim();
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = token.Substring("Bearer ".Length).Trim();

            // Until endpoint discovery completes, keep the public AI report
            // service as the safe fallback instead of using an internal host.
            return "https://report.ivistatech.vn";
        }

        private List<string> ResolveAiReportEndpointBases(out string token)
        {
            var report = GetEndpointProfile(_aiReportEndpointKeyword);
            var rawToken = report != null && !string.IsNullOrWhiteSpace(report.Token)
                ? report.Token
                : (_vehicleStatsApiToken ?? _backendToken ?? string.Empty);
            token = rawToken.Trim();
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = token.Substring("Bearer ".Length).Trim();

            var endpointBases = new List<string>();
            var publicUrl = report != null && !string.IsNullOrWhiteSpace(report.PublicUrl)
                ? report.PublicUrl.TrimEnd('/')
                : "https://report.ivistatech.vn";
            endpointBases.Add(publicUrl);

            if (report != null && !string.IsNullOrWhiteSpace(report.InternalUrl))
            {
                var internalUrl = report.InternalUrl.TrimEnd('/');
                if (!endpointBases.Any(url => string.Equals(url, internalUrl, StringComparison.OrdinalIgnoreCase)))
                    endpointBases.Add(internalUrl);
            }

            return endpointBases;
        }

        private async Task<string> GetAiReportJsonWithFallbackAsync(
            string routeAndQuery,
            string operationName,
            CancellationToken cancellationToken)
        {
            var endpointBases = ResolveAiReportEndpointBases(out var reportToken);
            for (var index = 0; index < endpointBases.Count; index++)
            {
                var endpointBase = endpointBases[index];
                var url = endpointBase + "/" + routeAndQuery.TrimStart('/');
                var source = index == 0 ? "public" : "internal";
                LoggerManager.LogDebug($"{operationName} endpoint ({source}, {_aiReportEndpointKeyword}): {url}");
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        if (!string.IsNullOrWhiteSpace(reportToken))
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reportToken);

                        using (var response = await _deviceStatusHttpClient.SendAsync(request, cancellationToken))
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            if (!response.IsSuccessStatusCode)
                            {
                                LoggerManager.LogWarn($"{operationName} returned {(int)response.StatusCode} via {source} endpoint; trying fallback.");
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(json))
                            {
                                LoggerManager.LogWarn($"{operationName} returned an empty response via {source} endpoint; trying fallback.");
                                continue;
                            }

                            try
                            {
                                var payload = Newtonsoft.Json.Linq.JToken.Parse(json) as Newtonsoft.Json.Linq.JObject;
                                var endpointError = payload?["error"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(endpointError))
                                {
                                    LoggerManager.LogWarn($"{operationName} returned an API error via {source} endpoint: {endpointError}");
                                    continue;
                                }
                            }
                            catch (JsonException ex)
                            {
                                LoggerManager.LogWarn($"{operationName} returned invalid JSON via {source} endpoint: {ex.Message}");
                                continue;
                            }

                            return json;
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    LoggerManager.LogWarn($"{operationName} timed out via {source} endpoint; trying fallback.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    LoggerManager.LogWarn($"{operationName} connection failed via {source} endpoint: {ex.Message}");
                }
                catch (Exception ex)
                {
                    LoggerManager.LogException(ex, $"{operationName} failed via {source} endpoint");
                }
            }

            return null;
        }

        public async Task<bool> DiscoverEndpointsAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            LoggerManager.LogDebug($"Bắt đầu khám phá Service Endpoints tại {_baseUrl}/api/system/endpoints");
            try
            {
                // Endpoint discovery is optional and must not hold the login
                // screen indefinitely when the discovery route is slow.
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    using (var response = await _httpClient.GetAsync($"{_baseUrl}/api/system/endpoints", timeout.Token))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            var discoveryData = JsonConvert.DeserializeObject<SystemEndpoints>(json);

                            if (discoveryData != null && discoveryData.Endpoints != null)
                            {
                                _endpointRegistry = discoveryData.Endpoints;

                                // Sync primary storage fields for backward compatibility
                                var storageEp = GetEndpointProfile(_storageEndpointKeyword);
                                if (storageEp != null)
                                {
                                    _storageUrl = GetEndpointUrl(storageEp.Keyword);
                                    _storageToken = string.IsNullOrEmpty(storageEp.Token) ? "your-super-secret-private-token-2026" : storageEp.Token;
                                }

                                // Sync New Endpoints
                                var assetsEp = GetEndpointProfile(_assetsEndpointKeyword);
                                if (assetsEp != null)
                                {
                                    _assetsUrl = GetEndpointUrl(assetsEp.Keyword);
                                    _assetsToken = string.IsNullOrEmpty(assetsEp.Token) ? "your-super-secret-private-token-2026" : assetsEp.Token;
                                }

                                var reportEp = GetEndpointProfile(_reportEndpointKeyword);
                                if (reportEp != null)
                                {
                                    _reportUrl = GetEndpointUrl(reportEp.Keyword);
                                    _reportToken = string.IsNullOrEmpty(reportEp.Token) ? "your-super-secret-private-token-2026" : reportEp.Token;
                                }

                                var mapEp = GetEndpointProfile(_mapEndpointKeyword);
                                if (mapEp != null)
                                {
                                    _mapUrl = GetEndpointUrl(mapEp.Keyword);
                                }

                                var redisEp = GetEndpointProfile("Redis");
                                if (redisEp != null)
                                {
                                    _redisUrl = GetEndpointUrl(redisEp.Keyword);
                                }

                                foreach (var ep in _endpointRegistry)
                                {
                                    LoggerManager.LogDebug($"Khám phá [{ep.Keyword}]: {ep.Name} -> {GetEndpointUrl(ep.Keyword)}");
                                }
                                return true;
                            }
                        }
                        else
                        {
                            LoggerManager.LogWarn($"Khám phá endpoint thất bại: HTTP {response.StatusCode}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi nghiêm trọng trong DiscoverEndpointsAsync");
            }
            return false;
        }

        private void StartEndpointDiscoveryInBackground()
        {
            Task.Run(async () =>
            {
                var discovered = await DiscoverEndpointsAsync(CancellationToken.None).ConfigureAwait(false);
                if (!discovered)
                    LoggerManager.LogWarn("Endpoint discovery was deferred after login and did not complete.");
            });
        }

        /// <summary>
        /// Search videos on a Storage Server and get RTSP URL for streaming.
        /// </summary>
        public async Task<PlaybackSearchResult> SearchPlaybackAsync(
            List<string> deviceIds, DateTime startTime, DateTime endTime, CancellationToken cancellationToken)
        {
            try
            {
                var requestBody = new
                {
                    device_ids = deviceIds,
                    start_time = startTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    end_time = endTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    source_filter = "h264",
                    target_codec = "h264"
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_storageUrl.TrimEnd('/')}/api/search");
                request.Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                if (!string.IsNullOrEmpty(_storageToken))
                {
                    request.Headers.Add("X-Service-Token", _storageToken);
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<PlaybackSearchResult>(resultJson);
                    return result;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new PlaybackSearchResult { TotalCount = 0, Sessions = new List<PlaybackSessionInfo>(), Videos = new Dictionary<string, List<PlaybackVideoInfo>>() };
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Storage search error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Activate a playback session on the specific Storage Server.
        /// </summary>
        public async Task<string> GetPlaybackPlayInfoAsync(string sessionId, CancellationToken cancellationToken)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_storageUrl.TrimEnd('/')}/api/play/info/{sessionId}");

                if (!string.IsNullOrEmpty(_storageToken))
                {
                    request.Headers.Add("X-Service-Token", _storageToken);
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var jsonObj = JsonConvert.DeserializeObject<dynamic>(resultJson);
                    return jsonObj.rtsp_url;
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Storage activation error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get download information from the Storage Server.
        /// </summary>
        public async Task<PlaybackDownloadInfo> GetPlaybackDownloadInfoAsync(string sessionId, CancellationToken cancellationToken)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_storageUrl.TrimEnd('/')}/api/download/info/{sessionId}");

                if (!string.IsNullOrEmpty(_storageToken))
                {
                    request.Headers.Add("X-Service-Token", _storageToken);
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<PlaybackDownloadInfo>(resultJson);
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Storage download info error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Renew a playback session after EOS or disconnect.
        /// Avoids re-searching by resetting the existing session's state.
        /// Returns the new RTSP URL if renewed, or null if the session has fully expired.
        /// </summary>
        public async Task<string> RenewPlaybackSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_storageUrl.TrimEnd('/')}/api/play/renew/{sessionId}");

                if (!string.IsNullOrEmpty(_storageToken))
                {
                    request.Headers.Add("X-Service-Token", _storageToken);
                }

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var jsonObj = JsonConvert.DeserializeObject<dynamic>(resultJson);
                    return jsonObj.rtsp_url;
                }
                return null; // Session fully expired, client must re-search
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Session renewal error: {ex.Message}");
                return null;
            }
        }


        public class LoginResponse
        {
            public bool success { get; set; }
            public string user_id { get; set; }
            public string access_token { get; set; }
        }

        public class UserMeResponse
        {
            [JsonProperty("id")]
            public int Id { get; set; }
            [JsonProperty("username")]
            public string Username { get; set; }
            [JsonProperty("email")]
            public string Email { get; set; }
            [JsonProperty("is_active")]
            public bool IsActive { get; set; }
            [JsonProperty("is_super_admin")]
            public bool IsSuperAdmin { get; set; }
            [JsonProperty("tenant_id")]
            public int? TenantId { get; set; }
            [JsonProperty("roles")]
            public List<string> Roles { get; set; } = new List<string>();
            [JsonProperty("permissions")]
            public List<string> Permissions { get; set; } = new List<string>();
            [JsonProperty("profile")]
            public UserProfileResponse Profile { get; set; }
        }

        public class UserProfileResponse
        {
            [JsonProperty("id")]
            public int Id { get; set; }
            [JsonProperty("full_name")]
            public string FullName { get; set; }
            [JsonProperty("phone")]
            public string Phone { get; set; }
            [JsonProperty("avatar_url")]
            public string AvatarUrl { get; set; }
        }

        public class AccountInfo
        {
            [JsonProperty("id")]
            public int Id { get; set; }
            [JsonProperty("username")]
            public string Username { get; set; }
            [JsonProperty("full_name")]
            public string FullName { get; set; }
            [JsonProperty("is_active")]
            public bool IsActive { get; set; }
        }

        public class ClientProfile
        {
            [JsonProperty("id")]
            public Guid Id { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("code")]
            public string Code { get; set; }
            [JsonProperty("description")]
            public string Description { get; set; }
            [JsonProperty("layout_config")]
            public object LayoutConfig { get; set; }

            [JsonProperty("camera_ids")]
            public List<string> CameraIds { get; set; } = new List<string>();

            [JsonProperty("user_ids")]
            public List<int> AccountIds { get; set; } = new List<int>();
        }


        public class RoiInfo
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("roi_type")]
            public string RoiType { get; set; }
            [JsonProperty("points")]
            public List<RoiPoint> Points { get; set; }
            [JsonProperty("rule")]
            public object Rule { get; set; }
            [JsonProperty("resolution")]
            public string Resolution { get; set; }
            [JsonProperty("is_active")]
            public bool IsActive { get; set; }
            [JsonProperty("ai_service_id")]
            public Guid? AiServiceId { get; set; }
        }

        public class RoiPoint
        {
            [JsonProperty("x")]
            public float X { get; set; }
            [JsonProperty("y")]
            public float Y { get; set; }
        }

        public class CameraAIAssignmentInfo
        {
            [JsonProperty("id")]
            public Guid Id { get; set; }
            [JsonProperty("camera_id")]
            public Guid CameraId { get; set; }
            [JsonProperty("ai_service_id")]
            public Guid ServiceId { get; set; }
            [JsonProperty("ai_params")]
            public AiParamsConfig AiParams { get; set; }
            [JsonProperty("is_active")]
            public bool IsActive { get; set; }
            [JsonProperty("is_enabled")]
            public bool IsEnabled { get; set; }
            [JsonProperty("bodycam_config")]
            public BodyCamConfig BodyCam { get; set; }
            [JsonProperty("assigned_udp_port")]
            public string AssignedUdpPort { get; set; }
        }

        public class BodyCamConfig
        {
            [JsonProperty("role")]
            public string Role { get; set; } = "client_device";
            [JsonProperty("feedback_mode")]
            public string FeedbackMode { get; set; } = "none";
            [JsonProperty("streams")]
            public BodyCamStreams Streams { get; set; } = new BodyCamStreams();
        }

        public class BodyCamStreams
        {
            [JsonProperty("media")]
            public bool Media { get; set; } = true;
            [JsonProperty("talk")]
            public bool Talk { get; set; } = false;
            [JsonProperty("gps")]
            public bool Gps { get; set; } = true;
        }

        public class AiParamsConfig
        {
            // Add specific AI params here if needed later
        }

        public class AIServiceInfo
        {
            [JsonProperty("id")]
            public Guid Id { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("node_id")]
            public string NodeId { get; set; }
            [JsonProperty("endpoint")]
            public string Endpoint { get; set; }
            public override string ToString()
            {
                return Name;
            }
        }

        public class CameraDetailInfo
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            [JsonProperty("camera_code")]
            public string CameraCode { get; set; }
            [JsonProperty("display_name_1")]
            public string DisplayName1 { get; set; }
            [JsonProperty("display_name_2")]
            public string DisplayName2 { get; set; }
            [JsonProperty("camera_type")]
            public string CameraType { get; set; }
            [JsonProperty("codec")]
            public string Codec { get; set; }
            [JsonProperty("operation_mode")]
            public string OperationMode { get; set; }
            [JsonProperty("source_ip")]
            public string SourceIp { get; set; }
            [JsonProperty("source_port")]
            public int? SourcePort { get; set; }
            [JsonProperty("source_stream_url")]
            public string SourceStreamUrl { get; set; }
            [JsonProperty("location_name")]
            public string LocationName { get; set; }
            [JsonProperty("latitude")]
            public double? Latitude { get; set; }
            [JsonProperty("longitude")]
            public double? Longitude { get; set; }
            [JsonProperty("description")]
            public string Description { get; set; }
            [JsonProperty("is_active")]
            public bool IsActive { get; set; }
            [JsonProperty("media_server_id")]
            public string MediaServerId { get; set; }
            [JsonProperty("media_server")]
            public MediaServerInfo MediaServer { get; set; }
            [JsonProperty("groups")]
            public List<ApiManager.CameraGroupInfo> Groups { get; set; }
            [JsonProperty("group_ids")]
            public List<string> GroupIds { get; set; }
            [JsonProperty("ai_node_name")]
            public string AiNodeName { get; set; }
            [JsonProperty("extra_metadata")]
            public object ExtraMetadata { get; set; }
            [JsonProperty("created_at")]
            public DateTime CreatedAt { get; set; }
            [JsonProperty("updated_at")]
            public DateTime UpdatedAt { get; set; }
        }

        public class CameraCreateRequest
        {
            [JsonProperty("camera_code")]
            public string CameraCode { get; set; }
            [JsonProperty("display_name_1")]
            public string DisplayName1 { get; set; }
            [JsonProperty("display_name_2")]
            public string DisplayName2 { get; set; }
            [JsonProperty("camera_type")]
            public string CameraType { get; set; }
            [JsonProperty("codec")]
            public string Codec { get; set; }
            [JsonProperty("operation_mode")]
            public string OperationMode { get; set; }
            [JsonProperty("source_ip")]
            public string SourceIp { get; set; }
            [JsonProperty("source_port")]
            public int? SourcePort { get; set; }
            [JsonProperty("source_stream_url")]
            public string SourceStreamUrl { get; set; }
            [JsonProperty("location_name")]
            public string LocationName { get; set; }
            [JsonProperty("latitude")]
            public double? Latitude { get; set; }
            [JsonProperty("longitude")]
            public double? Longitude { get; set; }
            [JsonProperty("description")]
            public string Description { get; set; }
            [JsonProperty("is_active")]
            public bool IsActive { get; set; }
            [JsonProperty("media_server_id")]
            public string MediaServerId { get; set; }
            [JsonProperty("group_ids")]
            public List<string> GroupIds { get; set; }
            [JsonProperty("rtsp_username")]
            public string RtspUsername { get; set; }
            [JsonProperty("rtsp_password")]
            public string RtspPassword { get; set; }
        }

        public class MediaServerInfo
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("public_endpoint")]
            public string PublicEndpoint { get; set; }
            [JsonProperty("internal_endpoint")]
            public string InternalEndpoint { get; set; }
            [JsonProperty("is_active")]
            public bool IsActive { get; set; }

            public override string ToString()
            {
                return Name;
            }
        }

        public class CameraDependencyInfo
        {
            [JsonProperty("camera_name")]
            public string CameraName { get; set; }
            [JsonProperty("camera_code")]
            public string CameraCode { get; set; }
            [JsonProperty("groups_count")]
            public int GroupsCount { get; set; }
            [JsonProperty("group_names")]
            public List<string> GroupNames { get; set; }
            [JsonProperty("ai_assignments_count")]
            public int AiAssignmentsCount { get; set; }
            [JsonProperty("ai_assignments")]
            public List<object> AiAssignments { get; set; }
            public override string ToString()
            {
                return base.ToString();
            }
        }

        public class TrajectoryDto
        {
            [JsonProperty("person_id")]
            public string PersonId { get; set; }
            [JsonProperty("person_name")]
            public string PersonName { get; set; }
            [JsonProperty("plate_number")]
            public string PlateNumber { get; set; }
            [JsonProperty("event_id")]
            public string EventId { get; set; }
            [JsonProperty("total_detections")]
            public int TotalDetections { get; set; }
            [JsonProperty("start_time")]
            public string StartTime { get; set; }
            [JsonProperty("end_time")]
            public string EndTime { get; set; }
            [JsonProperty("detections")]
            public List<DetectionDto> Detections { get; set; } = new List<DetectionDto>();
        }

        public class DetectionDto
        {
            [JsonProperty("timestamp")]
            public string Timestamp { get; set; }
            [JsonProperty("camera")]
            public string Camera { get; set; }
            [JsonProperty("gps")]
            public GpsDto Gps { get; set; }
            [JsonProperty("confidence")]
            public double? Confidence { get; set; }
            [JsonProperty("asset_id")]
            public string AssetId { get; set; }
        }

        /// <summary>
        /// A compact record returned by the AI event-center crop history API.
        /// It is deliberately separate from playback search DTOs: the live
        /// feed only needs the newest detection and its optional crop asset.
        /// </summary>
        public sealed class LiveAiEventFeedResponse
        {
            [JsonProperty("items")]
            public List<LiveAiEventFeedItem> Items { get; set; } = new List<LiveAiEventFeedItem>();
            [JsonProperty("total_items")]
            public int? TotalItems { get; set; }
            [JsonProperty("total_pages")]
            public int? TotalPages { get; set; }
        }

        public sealed class LiveAiEventFeedItem
        {
            [JsonProperty("detection_id")]
            public string DetectionId { get; set; }
            [JsonProperty("message_id")]
            public string MessageId { get; set; }
            [JsonProperty("cam_id")]
            public string CameraId { get; set; }
            [JsonProperty("object_id")]
            public string ObjectId { get; set; }
            [JsonProperty("event_time")]
            public string EventTime { get; set; }
            [JsonProperty("event_type")]
            public string EventType { get; set; }
            [JsonProperty("meta_type")]
            public string MetaType { get; set; }
            [JsonProperty("confidence")]
            public double Confidence { get; set; }
            [JsonProperty("asset_id")]
            public string AssetId { get; set; }
        }

        public class GpsDto
        {
            [JsonProperty("latitude")]
            public double Latitude { get; set; }
            [JsonProperty("longitude")]
            public double Longitude { get; set; }
        }

        public class PlaybackSearchResult
        {
            [JsonProperty("total_count")]
            public int TotalCount { get; set; }

            [JsonProperty("sessions")]
            public List<PlaybackSessionInfo> Sessions { get; set; } = new List<PlaybackSessionInfo>();

            [JsonProperty("videos")]
            public Dictionary<string, List<PlaybackVideoInfo>> Videos { get; set; } = new Dictionary<string, List<PlaybackVideoInfo>>();
        }

        public class PlaybackSessionInfo
        {
            [JsonProperty("device_id")]
            public string DeviceId { get; set; }

            [JsonProperty("count")]
            public int Count { get; set; }

            [JsonProperty("session_id")]
            public string SessionId { get; set; }
        }

        public class PlaybackVideoInfo
        {
            [JsonProperty("s")]
            public System.DateTime StartTime { get; set; }
            [JsonProperty("d")]
            public double Duration { get; set; }

            // Computed EndTime from StartTime + Duration
            public System.DateTime EndTime => StartTime.AddSeconds(Duration);
        }

        public class PlaybackDownloadInfo
        {
            [JsonProperty("session_id")]
            public string SessionId { get; set; }
            [JsonProperty("parts")]
            public List<PlaybackPartInfo> Parts { get; set; } = new List<PlaybackPartInfo>();
        }

        public class PlaybackPartInfo
        {
            [JsonProperty("part_index")]
            public int PartIndex { get; set; }
            [JsonProperty("total_size_bytes")]
            public long TotalSizeBytes { get; set; }
            [JsonProperty("download_url")]
            public string DownloadUrl { get; set; }
        }


        public async Task<bool> UpdateDeviceTalkStatusAsync(string deviceId, bool isTalking, CancellationToken cancellationToken = default)
        {
            try
            {
                var body = new { is_talking = isTalking };
                var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/devices/{deviceId}/talk", content, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi khi gọi UpdateDeviceTalkStatusAsync cho thiết bị {deviceId}");
                return false;
            }
        }

        /// <summary>
        /// Gets the latest AI crop events used by the live-monitoring feed.
        /// This is read-only and uses the same authenticated backend client as
        /// the existing camera and AI-summary requests.
        /// </summary>
        public async Task<LiveAiEventFeedResponse> GetLiveAiEventFeedAsync(
            System.DateTime startAt,
            System.DateTime endAt,
            IEnumerable<string> cameraIds,
            int page = 1,
            int pageSize = 10,
            string objectId = "",
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                // Match the web app: AI notifications are served by the
                // portal proxy, not by the configured private AI host.
                var reportBase = ResolveAiReportBaseUrl(out var reportToken);
                var reportProfile = GetEndpointProfile(_aiReportEndpointKeyword);
                var endpointBases = new List<string> { reportBase };
                if (reportProfile != null && !string.IsNullOrWhiteSpace(reportProfile.InternalUrl) &&
                    !string.Equals(reportProfile.InternalUrl.TrimEnd('/'), reportBase, StringComparison.OrdinalIgnoreCase))
                    endpointBases.Add(reportProfile.InternalUrl.TrimEnd('/'));
                var cameraIdsParameter = string.Join(",", (cameraIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                var query = string.Format(
                    "?start_at={0}&end_at={1}&object_id={2}&min_confidence=0&page={3}&page_size={4}&cam_ids={5}",
                    Uri.EscapeDataString(startAt.ToString("yyyy-MM-ddTHH:mm:ss")),
                    Uri.EscapeDataString(endAt.ToString("yyyy-MM-ddTHH:mm:ss")),
                    Uri.EscapeDataString(objectId ?? ""), page, pageSize,
                    Uri.EscapeDataString(cameraIdsParameter));
                foreach (var endpointBase in endpointBases)
                {
                    var url = endpointBase + "/api/object-crops/history" + query;
                    try
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            if (!string.IsNullOrWhiteSpace(reportToken))
                                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reportToken.Trim());
                            using (var response = await _deviceStatusHttpClient.SendAsync(request, cancellationToken))
                            {
                                var json = await response.Content.ReadAsStringAsync();
                                if (response.IsSuccessStatusCode)
                                    return JsonConvert.DeserializeObject<LiveAiEventFeedResponse>(json);
                                LoggerManager.LogWarn($"Live AI event feed returned {(int)response.StatusCode} via {endpointBase}: {json}");
                            }
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        LoggerManager.LogWarn($"Live AI event feed connection failed via {endpointBase}: {ex.Message}");
                    }
                }
                return null;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetLiveAiEventFeedAsync");
                return null;
            }
        }

        public sealed class CameraHealthTimeseriesResponse
        {
            [JsonProperty("data")]
            public List<CameraHealthTimeseriesPoint> Data { get; set; } = new List<CameraHealthTimeseriesPoint>();
        }

        public sealed class CameraHealthTimeseriesPoint
        {
            [JsonProperty("bucket_start")]
            public string BucketStart { get; set; }
            [JsonProperty("online")]
            public int Online { get; set; }
            [JsonProperty("unavailable")]
            public int Unavailable { get; set; }
            [JsonProperty("offline")]
            public int Offline { get; set; }
            [JsonProperty("unknown")]
            public int Unknown { get; set; }
            [JsonProperty("uptime_percent")]
            public double? UptimePercent { get; set; }
        }

        private string ResolveConfiguredEndpoint(string configuredValue)
        {
            if (string.IsNullOrWhiteSpace(configuredValue))
                return null;
            if (Uri.IsWellFormedUriString(configuredValue, UriKind.Absolute))
                return configuredValue.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(_baseUrl))
                return null;
            return _baseUrl.TrimEnd('/') + "/" + configuredValue.Trim().TrimStart('/');
        }

        /// <summary>
        /// Gets a vehicle total for one camera within the selected time range.
        /// The endpoint accepts a single <c>cam_id</c>.
        /// </summary>
        public async Task<CameraVehicleCountsResponse> GetCameraVehicleCountsAsync(
            System.DateTime startAt,
            System.DateTime endAt,
            string cameraId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(cameraId))
                return null;

            try
            {
                var reportBase = ResolveAiReportBaseUrl(out var reportToken);
                var reportProfile = GetEndpointProfile(_aiReportEndpointKeyword);
                var endpointBases = new List<string> { reportBase };
                // Some deployments publish the DNS endpoint before its reverse
                // proxy route is reachable from the desktop network. Keep the
                // public endpoint first, then fall back to the discovered LAN
                // endpoint so live statistics remain available.
                if (reportProfile != null && !string.IsNullOrWhiteSpace(reportProfile.InternalUrl) &&
                    !string.Equals(reportProfile.InternalUrl.TrimEnd('/'), reportBase, StringComparison.OrdinalIgnoreCase))
                    endpointBases.Add(reportProfile.InternalUrl.TrimEnd('/'));
                // The AI report service expects the complete camera list in one
                // request.  This is the same contract used by the web client;
                // do not call the legacy summary endpoint here.
                var query = string.Format(
                    "?start_date={0}&end_date={1}&cam_id={2}",
                    Uri.EscapeDataString(startAt.ToString("yyyy-MM-ddTHH:mm:ss")),
                    Uri.EscapeDataString(endAt.ToString("yyyy-MM-ddTHH:mm:ss")),
                    Uri.EscapeDataString(string.Join(",", cameraId.Split(',')
                        .Select(id => id.Trim())
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase))));
                foreach (var endpointBase in endpointBases)
                {
                    var url = endpointBase + "/api/camera-vehicle-counts" + query;
                    LoggerManager.LogDebug($"AI vehicle report endpoint: {url}; report={reportProfile?.Keyword ?? "fallback"}");
                    try
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            if (!string.IsNullOrWhiteSpace(reportToken))
                                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reportToken.Trim());
                            using (var response = await _deviceStatusHttpClient.SendAsync(request, cancellationToken))
                            {
                                var json = await response.Content.ReadAsStringAsync();
                                if (response.IsSuccessStatusCode)
                                    return JsonConvert.DeserializeObject<CameraVehicleCountsResponse>(json);
                                LoggerManager.LogWarn($"Camera vehicle counts returned {(int)response.StatusCode} via {endpointBase}: {json}");
                            }
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        LoggerManager.LogWarn($"Camera vehicle counts connection failed via {endpointBase}: {ex.Message}");
                    }
                }
                return null;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetCameraVehicleCountsAsync");
                return null;
            }
        }

        public async Task<FrameDetectionCountsResponse> GetFrameDetectionCountsAsync(System.DateTime startAt, System.DateTime endAt, IEnumerable<string> cameraIds = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var start = Uri.EscapeDataString(startAt.ToString("yyyy-MM-ddTHH:mm:ss"));
                var end = Uri.EscapeDataString(endAt.ToString("yyyy-MM-ddTHH:mm:ss"));
                var cameraIdsParameter = string.Join(",", (cameraIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                var cameras = Uri.EscapeDataString(cameraIdsParameter);
                var url = string.Format("{0}?start_at={1}&end_at={2}&cam_ids={3}&object_classes=", _frameDetectionCountsApiUrl, start, end, cameras);
                using (var response = await _deviceStatusHttpClient.GetAsync(url, cancellationToken))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        LoggerManager.LogWarn($"Frame detection counts returned {(int)response.StatusCode}.");
                        return null;
                    }
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<FrameDetectionCountsResponse>(json);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetFrameDetectionCountsAsync");
                return null;
            }
        }

        public async Task<LiveFrameDetectionCountsResponse> GetLiveFrameDetectionCountsAsync(IEnumerable<string> cameraIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var cameraIdsParameter = string.Join(",", (cameraIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                var cameras = Uri.EscapeDataString(cameraIdsParameter);
                var reportBase = ResolveAiReportBaseUrl(out var reportToken);
                var reportProfile = GetEndpointProfile(_aiReportEndpointKeyword);
                var endpointBases = new List<string> { reportBase };
                if (reportProfile != null && !string.IsNullOrWhiteSpace(reportProfile.InternalUrl) &&
                    !string.Equals(reportProfile.InternalUrl.TrimEnd('/'), reportBase, StringComparison.OrdinalIgnoreCase))
                    endpointBases.Add(reportProfile.InternalUrl.TrimEnd('/'));
                foreach (var endpointBase in endpointBases)
                {
                    var url = endpointBase + "/api/live-frame-detection-counts?cam_ids=" + cameras;
                    try
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            if (!string.IsNullOrWhiteSpace(reportToken))
                                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reportToken.Trim());
                            using (var response = await _deviceStatusHttpClient.SendAsync(request, cancellationToken))
                            {
                                var json = await response.Content.ReadAsStringAsync();
                                if (response.IsSuccessStatusCode)
                                    return JsonConvert.DeserializeObject<LiveFrameDetectionCountsResponse>(json);
                                LoggerManager.LogWarn($"Live frame detection counts returned {(int)response.StatusCode} via {endpointBase}: {json}");
                            }
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        LoggerManager.LogWarn($"Live frame detection connection failed via {endpointBase}: {ex.Message}");
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetLiveFrameDetectionCountsAsync");
                return null;
            }
        }

        private sealed class DeviceReportEndpointCandidate
        {
            public string BaseUrl { get; set; }
            public string Token { get; set; }
            public bool IsDiscoveredEndpoint { get; set; }
        }

        public sealed class CameraHealthAttentionCamera
        {
            [JsonProperty("camera_id")]
            public string CameraId { get; set; }
            [JsonProperty("cam_id")]
            public string CameraCode { get; set; }
            [JsonProperty("uptime_percent")]
            public double? UptimePercent { get; set; }
            [JsonProperty("unavailable_seconds")]
            public long? UnavailableSeconds { get; set; }
        }

        private List<DeviceReportEndpointCandidate> GetDeviceReportEndpointCandidates()
        {
            // Dashboard reports are served by the _deviceReport service. Keep
            // its public endpoint first and use the discovered LAN endpoint
            // only if the public route fails.
            var candidates = new List<DeviceReportEndpointCandidate>();
            var profile = GetEndpointProfile(_deviceReportEndpointKeyword);
            foreach (var baseUrl in new[] { profile?.PublicUrl, profile?.InternalUrl })
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) continue;
                var normalized = baseUrl.Trim().TrimEnd('/');
                if (candidates.Any(item => string.Equals(item.BaseUrl, normalized, StringComparison.OrdinalIgnoreCase)))
                    continue;

                candidates.Add(new DeviceReportEndpointCandidate
                {
                    BaseUrl = normalized,
                    Token = profile?.Token,
                    IsDiscoveredEndpoint = true
                });
            }

            return candidates;
        }

        private static void ApplyDeviceReportAuthorization(HttpRequestMessage request, DeviceReportEndpointCandidate endpoint)
        {
            if (request == null || endpoint == null || string.IsNullOrWhiteSpace(endpoint.Token)) return;
            var token = endpoint.Token.Trim();
            if (endpoint.IsDiscoveredEndpoint || token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var bearerToken = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? token.Substring(7).Trim()
                    : token;
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                return;
            }
            request.Headers.TryAddWithoutValidation("X-API-KEY", token);
        }

        /// <summary>Gets the aggregated online/offline trend used by the Dashboard chart.</summary>
        public async Task<CameraHealthTimeseriesResponse> GetCameraHealthTimeseriesAsync(
            DateTime from, DateTime to, string bucket, IEnumerable<string> cameraIds, CancellationToken cancellationToken = default)
        {
            try
            {
                var ids = string.Join(",", (cameraIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                var query = "?from=" + Uri.EscapeDataString(from.ToUniversalTime().ToString("o"))
                    + "&to=" + Uri.EscapeDataString(to.ToUniversalTime().ToString("o"))
                    + "&bucket=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(bucket) ? "2h" : bucket);
                if (!string.IsNullOrWhiteSpace(ids))
                    query += "&camera_ids=" + Uri.EscapeDataString(ids);

                foreach (var endpoint in GetDeviceReportEndpointCandidates())
                {
                    var url = endpoint.BaseUrl + "/api/uptime/timeseries" + query;
                    LoggerManager.LogDebug($"Camera health endpoint ({_deviceReportEndpointKeyword}): {url}");
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        ApplyDeviceReportAuthorization(request, endpoint);
                        using (var response = await _deviceStatusHttpClient.SendAsync(request, cancellationToken))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                LoggerManager.LogWarn($"Camera health timeseries returned {(int)response.StatusCode} from {endpoint.BaseUrl}.");
                                continue;
                            }
                            var json = await response.Content.ReadAsStringAsync();
                            return JsonConvert.DeserializeObject<CameraHealthTimeseriesResponse>(json);
                        }
                    }
                }
                return null;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetCameraHealthTimeseriesAsync");
                return null;
            }
        }

        public async Task<List<DeviceStatusResponse>> GetDeviceStatusBatchAsync(List<string> deviceIds, CancellationToken cancellationToken = default)
        {
            if (deviceIds == null || deviceIds.Count == 0)
                return new List<DeviceStatusResponse>();

            try
            {
                var body = new { device_ids = deviceIds };
                foreach (var endpoint in GetDeviceReportEndpointCandidates())
                {
                    var endpointUrl = endpoint.BaseUrl + "/api/v1/devices/status/batch";
                    LoggerManager.LogDebug($"Device status endpoint ({_deviceReportEndpointKeyword}): {endpointUrl}");
                    using (var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl))
                    {
                        // This nginx gateway closes pooled keep-alive sockets.
                        request.Headers.ConnectionClose = true;
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                        ApplyDeviceReportAuthorization(request, endpoint);
                        using (var response = await _deviceStatusHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                var resultJson = await response.Content.ReadAsStringAsync();
                                var result = JsonConvert.DeserializeObject<List<DeviceStatusResponse>>(resultJson) ?? new List<DeviceStatusResponse>();
                                LoggerManager.LogInfo($"Device status batch succeeded: {result.Count}/{deviceIds.Count} records.");
                                return result;
                            }
                            LoggerManager.LogWarn($"Device status batch returned {(int)response.StatusCode} from {endpointUrl}.");
                        }
                    }
                }
                return new List<DeviceStatusResponse>();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetDeviceStatusBatchAsync");
                return new List<DeviceStatusResponse>();
            }
        }

        /// <summary>Gets the lowest-uptime cameras from the same endpoint used by the web dashboard.</summary>
        public async Task<List<CameraHealthAttentionCamera>> GetCameraHealthAttentionCamerasAsync(
            int limit = 5, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var query = "?limit=" + Math.Max(1, limit) + "&offset=0&sort=uptime_percent&order=asc";
                foreach (var endpoint in GetDeviceReportEndpointCandidates())
                {
                    var endpointUrl = endpoint.BaseUrl + "/api/uptime/cameras" + query;
                    LoggerManager.LogDebug($"Camera attention endpoint ({_deviceReportEndpointKeyword}): {endpointUrl}");
                    using (var request = new HttpRequestMessage(HttpMethod.Get, endpointUrl))
                    {
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        ApplyDeviceReportAuthorization(request, endpoint);
                        using (var response = await _deviceStatusHttpClient.SendAsync(request, cancellationToken))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                LoggerManager.LogWarn($"Camera attention returned {(int)response.StatusCode} from {endpoint.BaseUrl}.");
                                continue;
                            }
                            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());
                            var items = payload["data"] as JArray
                                ?? payload["cameras"] as JArray
                                ?? payload["data"]?["cameras"] as JArray;
                            var cameras = items?.ToObject<List<CameraHealthAttentionCamera>>();
                            if (cameras != null)
                                return cameras;
                        }
                    }
                }
                return new List<CameraHealthAttentionCamera>();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggerManager.LogException(ex, "Lỗi khi gọi GetCameraHealthAttentionCamerasAsync"); }
            return new List<CameraHealthAttentionCamera>();
        }

        /// <summary>
        /// Camera state used by the Live and Map views. This route belongs to
        /// Portal directly and intentionally does not use _deviceReport.
        /// </summary>
        public async Task<List<DeviceStatusResponse>> GetPortalDeviceStatusBatchAsync(List<string> deviceIds, CancellationToken cancellationToken = default)
        {
            if (deviceIds == null || deviceIds.Count == 0)
                return new List<DeviceStatusResponse>();

            try
            {
                const string endpointUrl = "https://portal.ivistatech.vn/api/v1/devices/status/batch";
                var body = new { device_ids = deviceIds };
                LoggerManager.LogDebug($"Portal device status endpoint: {endpointUrl}");
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl))
                {
                    request.Headers.ConnectionClose = true;
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                    if (!string.IsNullOrWhiteSpace(_deviceStatusApiKey))
                        request.Headers.TryAddWithoutValidation("X-API-KEY", _deviceStatusApiKey.Trim());

                    using (var response = await _deviceStatusHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            LoggerManager.LogWarn($"Portal device status batch returned {(int)response.StatusCode} from {endpointUrl}.");
                            return new List<DeviceStatusResponse>();
                        }

                        var json = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<List<DeviceStatusResponse>>(json) ?? new List<DeviceStatusResponse>();
                        LoggerManager.LogInfo($"Portal device status batch succeeded: {result.Count}/{deviceIds.Count} records.");
                        return result;
                    }
                }
            }
            catch (OperationCanceledException) { return new List<DeviceStatusResponse>(); }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetPortalDeviceStatusBatchAsync");
                return new List<DeviceStatusResponse>();
            }
        }

        public async Task<bool> ChangeCameraTalkGroupAsync(string cameraId, string groupId, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.PutAsync($"{_baseUrl}/api/v1/cameras/{cameraId}/talk-group/{groupId}", null, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi khi gọi ChangeCameraTalkGroupAsync cho camera {cameraId} tới nhóm {groupId}");
                return false;
            }
        }
        public sealed class RoiObjectsResponse
        {
            [JsonProperty("over_threshold")]
            public List<RoiObjectItem> OverThreshold { get; set; } = new List<RoiObjectItem>();
            [JsonProperty("total_items")]
            public int? TotalItems { get; set; }
            [JsonProperty("total_pages")]
            public int? TotalPages { get; set; }
        }

        public sealed class RoiObjectItem
        {
            [JsonProperty("roi_id")]
            public string RoiId { get; set; }
            [JsonProperty("cam_id")]
            public string CameraId { get; set; }
            [JsonProperty("plate")]
            public string Plate { get; set; }
            [JsonProperty("label")]
            public string Label { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("detected_object_ids")]
            public string DetectedObjectIds { get; set; }
            [JsonProperty("object_type")]
            public string ObjectType { get; set; }
            [JsonProperty("entered_at")]
            public string EnteredAt { get; set; }
            [JsonProperty("exited_at")]
            public string ExitedAt { get; set; }
            [JsonProperty("dwell_ms")]
            public long? DwellMilliseconds { get; set; }
            [JsonProperty("dwell_seconds")]
            public double? DwellSeconds { get; set; }
            [JsonProperty("dwell_human")]
            public string DwellHuman { get; set; }
            [JsonProperty("object_key")]
            public string ObjectKey { get; set; }
            [JsonProperty("confidence")]
            public double? Confidence { get; set; }
            [JsonProperty("plate_confidence")]
            public double? PlateConfidence { get; set; }
            private string _cropAssetId;
            [JsonProperty("crop_asset_id")]
            public string CropAssetId
            {
                get { return !string.IsNullOrWhiteSpace(_cropAssetId) ? _cropAssetId : GetAssetIdFromCropImageUrl(CropImageUrl); }
                set { _cropAssetId = value; }
            }
            [JsonProperty("crop_image_url")]
            public string CropImageUrl { get; set; }
            [JsonProperty("playback_url")]
            public string PlaybackUrl { get; set; }
            [JsonProperty("event_time")]
            public string EventTime { get; set; }
            [JsonProperty("object_id")]
            public string ObjectId { get; set; }
            [JsonProperty("event_type")]
            public string EventType { get; set; }
            [JsonProperty("meta_type")]
            public string MetaType { get; set; }
            [JsonProperty("duration")]
            public string Duration { get; set; }
            [JsonProperty("asset_id")]
            public string AssetId { get; set; }

            private static string GetAssetIdFromCropImageUrl(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                var marker = "asset_id=";
                var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return null;
                var encoded = value.Substring(index + marker.Length).Split('&')[0];
                return string.IsNullOrWhiteSpace(encoded) ? null : Uri.UnescapeDataString(encoded);
            }
        }

        public sealed class AiEventCenterSummary
        {
            [JsonProperty("today")]
            public AiEventCenterSummaryKpi Today { get; set; }
            [JsonProperty("in_station")]
            public AiEventCenterSummaryKpi InStation { get; set; }
            [JsonProperty("last_7_days")]
            public AiEventCenterSummaryKpi Last7Days { get; set; }
            [JsonProperty("month")]
            public AiEventCenterSummaryKpi Month { get; set; }
        }

        public sealed class AiEventCenterSummaryKpi
        {
            [JsonProperty("value")]
            public int Value { get; set; }
            [JsonProperty("previous_value")]
            public int? PreviousValue { get; set; }
            [JsonProperty("trend")]
            public string Trend { get; set; }
            [JsonProperty("percentage")]
            public double? Percentage { get; set; }
        }

        /// <summary>
        /// Tab "Đã hoàn thành" — xe đã qua đủ công đoạn ROI.
        /// Khớp với /api/ai-event-center/roi-objects của web app.
        /// </summary>
        public async Task<RoiObjectsResponse> GetRoiObjectsAsync(
            System.DateTime referenceDate,
            IEnumerable<string> cameraIds,
            int page = 1,
            int pageSize = 10,
            string search = "",
            string roiId = null,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var cameraIdsParameter = string.Join(",", (cameraIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                var qs = new System.Text.StringBuilder();
                qs.Append("?date=").Append(Uri.EscapeDataString(referenceDate.ToString("yyyy-MM-dd")));
                qs.Append("&threshold_seconds=").Append(_roiThresholdSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                qs.Append("&merge_gap_seconds=").Append(_roiMergeGapSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                qs.Append("&search=").Append(Uri.EscapeDataString(search ?? ""));
                qs.Append("&duration_filter=all");
                // Request the crop asset identifiers used by the web event
                // table so thumbnails can be resolved through Storage.
                qs.Append("&img=true");
                qs.Append("&page=").Append(page);
                qs.Append("&page_size=").Append(pageSize);
                if (!string.IsNullOrWhiteSpace(cameraIdsParameter))
                    qs.Append("&cam_id=").Append(Uri.EscapeDataString(cameraIdsParameter));
                if (!string.IsNullOrWhiteSpace(roiId))
                    qs.Append("&roi_id=").Append(Uri.EscapeDataString(roiId));
                if (forceRefresh)
                    qs.Append("&refresh=true");

                var json = await GetAiReportJsonWithFallbackAsync(
                    "/api/roi-objects" + qs,
                    "AI Event ROI Objects",
                    cancellationToken);
                return string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonConvert.DeserializeObject<RoiObjectsResponse>(json);
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetRoiObjectsAsync");
                return null;
            }
        }

        // ============ AI Event Center — 3 API endpoints khớp với web app ============

        /// <summary>
        /// KPI tổng hợp (hôm nay, tháng, 7 ngày, trong trạm).
        /// Gọi thẳng /api/ai-event-center/summary nếu có cấu hình,
        /// fallback tổng hợp thủ công từ vehicle-counts + frame-counts.
        /// </summary>
        public async Task<AiEventCenterSummary> GetAiEventSummaryAsync(
            System.DateTime referenceDate,
            IEnumerable<string> cameraIds,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Resolve the service by _aiEventReport. The shared request helper
            // always tries public_url first, then internal_url on failure.
            try
            {
                var cameraIdsList = (cameraIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                var qs = new System.Text.StringBuilder();
                qs.Append("?date=").Append(Uri.EscapeDataString(referenceDate.ToString("yyyy-MM-dd")));
                if (cameraIdsList.Count > 0)
                    qs.Append("&cam_id=").Append(Uri.EscapeDataString(string.Join(",", cameraIdsList)));
                if (forceRefresh)
                    qs.Append("&refresh=true");

                var summaryJson = await GetAiReportJsonWithFallbackAsync(
                    "/api/ai-event-center/summary" + qs,
                    "AI Event Summary",
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(summaryJson))
                {
                    var result = JsonConvert.DeserializeObject<AiEventCenterSummary>(summaryJson);
                    // The summary endpoint may return current values without
                    // comparison percentages. Continue to the vehicle-counts
                    // fallback in that case so the UI can show prior-period data.
                    if (result != null &&
                        result.Today?.Percentage.HasValue == true &&
                        result.Last7Days?.Percentage.HasValue == true &&
                        result.Month?.Percentage.HasValue == true)
                        return result;
                }
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex) { LoggerManager.LogException(ex, "GetAiEventSummaryAsync via dedicated endpoint"); }

            // --- Fallback: tổng hợp thủ công từ vehicle-counts + frame-counts ---
            try
            {
                var cameraIdsList = (cameraIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var cameraIdsParameter = string.Join(",", cameraIdsList);

                // 1. Fetch camera-vehicle-counts
                var startAt = referenceDate.Date;
                var endAt = referenceDate.Date == System.DateTime.Today
                    ? System.DateTime.Now
                    : referenceDate.Date.AddDays(1).AddTicks(-1);
                var vehicleRoute = "/api/camera-vehicle-counts" +
                    $"?start_date={Uri.EscapeDataString(startAt.ToString("yyyy-MM-ddTHH:mm:ss"))}" +
                    $"&end_date={Uri.EscapeDataString(endAt.ToString("yyyy-MM-ddTHH:mm:ss"))}";
                if (!string.IsNullOrWhiteSpace(cameraIdsParameter))
                    vehicleRoute += $"&cam_id={Uri.EscapeDataString(cameraIdsParameter)}";

                Newtonsoft.Json.Linq.JObject vehiclePayload = null;
                var vehicleJson = await GetAiReportJsonWithFallbackAsync(
                    vehicleRoute,
                    "AI Event vehicle counts",
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(vehicleJson))
                    vehiclePayload = Newtonsoft.Json.Linq.JObject.Parse(vehicleJson);

                // 2. Fetch live-frame-detection-counts
                var frameRoute = "/api/live-frame-detection-counts";
                if (!string.IsNullOrWhiteSpace(cameraIdsParameter))
                    frameRoute += $"?cam_ids={Uri.EscapeDataString(cameraIdsParameter)}";

                int inRoiTotal = 0;
                var frameJson = await GetAiReportJsonWithFallbackAsync(
                    frameRoute,
                    "AI Event live frame counts",
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(frameJson))
                {
                    var framePayload = Newtonsoft.Json.Linq.JObject.Parse(frameJson);

                    string[] possibleKeys = { "total_detection_count", "total", "total_count", "count", "current_vehicle_total" };
                    bool found = false;
                    foreach (var key in possibleKeys)
                    {
                        if (framePayload[key] != null && int.TryParse(framePayload[key].ToString(), out int val))
                        {
                            inRoiTotal = val;
                            found = true;
                            break;
                        }
                    }

                    if (!found && framePayload["cameras"] != null)
                    {
                        var camerasToken = framePayload["cameras"];
                        if (camerasToken is Newtonsoft.Json.Linq.JArray camArray)
                        {
                            foreach (var cam in camArray)
                            {
                                if (cam["source_detection_count"] != null && int.TryParse(cam["source_detection_count"].ToString(), out int cVal))
                                    inRoiTotal += cVal;
                                else if (cam["count"] != null && int.TryParse(cam["count"].ToString(), out int cVal2))
                                    inRoiTotal += cVal2;
                            }
                        }
                        else if (camerasToken is Newtonsoft.Json.Linq.JObject camObj)
                        {
                            foreach (var prop in camObj.Properties())
                            {
                                if (prop.Value is Newtonsoft.Json.Linq.JObject subObj)
                                {
                                    if (subObj["source_detection_count"] != null && int.TryParse(subObj["source_detection_count"].ToString(), out int cVal))
                                        inRoiTotal += cVal;
                                    else if (subObj["count"] != null && int.TryParse(subObj["count"].ToString(), out int cVal2))
                                        inRoiTotal += cVal2;
                                }
                                else if (int.TryParse(prop.Value.ToString(), out int cVal))
                                {
                                    inRoiTotal += cVal;
                                }
                            }
                        }
                    }
                }

                if (vehiclePayload == null)
                    return null;

                int todayTotal = vehiclePayload["today_total"]?.ToObject<int>() ?? 0;
                int yesterdayTotal = vehiclePayload["yesterday_total"]?.ToObject<int>() ?? 0;
                int currentWeekTotal = vehiclePayload["current_week_total"]?.ToObject<int>() ?? 0;
                int previousWeekTotal = vehiclePayload["previous_week_total"]?.ToObject<int>() ?? 0;
                int currentMonthTotal = vehiclePayload["current_month_total"]?.ToObject<int>() ?? 0;
                int previousMonthTotal = vehiclePayload["previous_month_total"]?.ToObject<int>() ?? 0;

                AiEventCenterSummaryKpi MakeComparison(int current, int? previous)
                {
                    if (previous == null)
                        return new AiEventCenterSummaryKpi { Value = current, Percentage = null, Trend = "flat" };
                    if (previous.Value == 0)
                    {
                        return new AiEventCenterSummaryKpi
                        {
                            Value = current,
                            Percentage = current > 0 ? (double?)null : 0,
                            Trend = current > 0 ? "up" : "flat"
                        };
                    }
                    double pct = Math.Round(((current - previous.Value) / (double)previous.Value) * 100.0, 1);
                    return new AiEventCenterSummaryKpi
                    {
                        Value = current,
                        Percentage = pct,
                        Trend = pct > 0 ? "up" : (pct < 0 ? "down" : "flat")
                    };
                }

                return new AiEventCenterSummary
                {
                    Today = MakeComparison(todayTotal, yesterdayTotal),
                    InStation = MakeComparison(inRoiTotal, null),
                    Last7Days = MakeComparison(currentWeekTotal, previousWeekTotal),
                    Month = MakeComparison(currentMonthTotal, previousMonthTotal)
                };
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi tổng hợp AiEventSummary");
                return null;
            }
        }

        public class DeviceStatusResponse
        {
            [JsonProperty("device_id")]
            public string DeviceId { get; set; }
            [JsonProperty("status")]
            public string Status { get; set; }
            [JsonProperty("is_online")]
            public bool? IsOnline { get; set; }
        }

        private class DeviceStatusLocalConfig
        {
            [JsonProperty("api_url")]
            public string ApiUrl { get; set; }

            [JsonProperty("api_key")]
            public string ApiKey { get; set; }
        }
        /// <summary>
        /// Tab "Ghi nhận" — lịch sử crop AI theo khoảng thời gian.
        /// Khớp với /api/ai-event-center/object-crops/history của web app.
        /// </summary>
        public sealed class AiEventObjectCropHistoryResponse
        {
            [JsonProperty("items")]
            public List<AiEventObjectCropItem> Items { get; set; } = new List<AiEventObjectCropItem>();
            [JsonProperty("total_items")]
            public int? TotalItems { get; set; }
            [JsonProperty("total_pages")]
            public int? TotalPages { get; set; }
        }

        public sealed class AiEventObjectCropItem
        {
            [JsonProperty("message_id")]
            public string MessageId { get; set; }
            [JsonProperty("detection_id")]
            public string DetectionId { get; set; }
            [JsonProperty("cam_id")]
            public string CameraId { get; set; }
            [JsonProperty("event_time")]
            public string EventTime { get; set; }
            [JsonProperty("event_type")]
            public string EventType { get; set; }
            [JsonProperty("meta_type")]
            public string MetaType { get; set; }
            [JsonProperty("object_id")]
            public string ObjectId { get; set; }
            [JsonProperty("confidence")]
            public double? Confidence { get; set; }
            [JsonProperty("asset_id")]
            public string AssetId { get; set; }
            [JsonProperty("object_key")]
            public object ObjectKey { get; set; }
            [JsonProperty("plate")]
            public string Plate { get; set; }
            [JsonProperty("roi_id")]
            public string RoiId { get; set; }
            [JsonProperty("crop_asset_id")]
            public string CropAssetId { get; set; }
        }

        public async Task<AiEventObjectCropHistoryResponse> GetAiEventObjectCropHistoryAsync(
            System.DateTime startAt,
            System.DateTime endAt,
            IEnumerable<string> cameraIds,
            int page = 1,
            int pageSize = 10,
            string objectId = "",
            double minConfidence = 0.0,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var cameraIdsParameter = string.Join(",", (cameraIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                // Thời gian theo định dạng ISO+07:00 như web app
                string FormatVnTime(System.DateTime dt) =>
                    dt.ToString("yyyy-MM-ddTHH:mm:ss") + "+07:00";

                var qs = new System.Text.StringBuilder();
                qs.Append("?start_at=").Append(Uri.EscapeDataString(FormatVnTime(startAt)));
                qs.Append("&end_at=").Append(Uri.EscapeDataString(FormatVnTime(endAt)));
                qs.Append("&object_id=").Append(Uri.EscapeDataString(objectId ?? ""));
                qs.Append("&min_confidence=").Append(minConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture));
                qs.Append("&page=").Append(page);
                qs.Append("&page_size=").Append(pageSize);
                if (!string.IsNullOrWhiteSpace(cameraIdsParameter))
                    qs.Append("&cam_ids=").Append(Uri.EscapeDataString(cameraIdsParameter));

                var json = await GetAiReportJsonWithFallbackAsync(
                    "/api/object-crops/history" + qs,
                    "AI Event object crop history",
                    cancellationToken);
                return string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonConvert.DeserializeObject<AiEventObjectCropHistoryResponse>(json);
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetAiEventObjectCropHistoryAsync");
                return null;
            }
        }

        public async Task<JObject> GetRoiMonitoringSummaryAsync(DateTime start, DateTime end, IEnumerable<string> cameraIds, IEnumerable<string> roiIds, string bucket, double thresholdSeconds, CancellationToken cancellationToken = default(CancellationToken))
        {
            var query = "?start_at=" + Uri.EscapeDataString(start.ToString("o")) + "&end_at=" + Uri.EscapeDataString(end.ToString("o")) + "&cam_ids=" + Uri.EscapeDataString(string.Join(",", cameraIds ?? Enumerable.Empty<string>())) + "&roi_ids=" + Uri.EscapeDataString(string.Join(",", roiIds ?? Enumerable.Empty<string>())) + "&bucket=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(bucket) ? "auto" : bucket) + "&threshold_seconds=" + thresholdSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var json = await GetAiReportJsonWithFallbackAsync("/api/roi-monitoring/summary" + query, "ROI monitoring summary", cancellationToken);
            return string.IsNullOrWhiteSpace(json) ? null : JObject.Parse(json);
        }

        // ============ ROI Batch — lấy danh sách công đoạn theo nhóm camera ============

        public sealed class RoiBatchResponse
        {
            [JsonProperty("data")]
            public List<RoiBatchItem> Data { get; set; } = new List<RoiBatchItem>();
        }

        public sealed class RoiBatchItem
        {
            [JsonProperty("cam_id")]
            public string CameraId { get; set; }
            [JsonProperty("roi")]
            public RoiBatchRoiDetail Roi { get; set; }
        }

        public sealed class RoiBatchRoiDetail
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            [JsonProperty("roi_id")]
            public string RoiId { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
            // roiRule.name dùng làm fallback tên công đoạn
            [JsonProperty("roiRule")]
            public RoiBatchRoiRule RoiRule { get; set; }
        }

        public sealed class RoiBatchRoiRule
        {
            [JsonProperty("name")]
            public string Name { get; set; }
        }

        /// <summary>
        /// Lấy danh sách ROI (công đoạn) theo batch camera.
        /// Khớp với POST /api/rois/batch/cameras của web app.
        /// </summary>
        public async Task<RoiBatchResponse> GetRoiBatchByCamerasAsync(
            IEnumerable<string> cameraIds,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var camList = (cameraIds ?? Enumerable.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (camList.Count == 0) return new RoiBatchResponse();

                var body = new { active_only = true, cam_ids = camList, limit = 100, page = 0, scaled = true };
                var bodyJson = JsonConvert.SerializeObject(body);
                var profile = GetEndpointProfile(_roiConfigEndpointKeyword);
                var endpointBases = new List<string>();
                foreach (var candidate in new[] { profile?.PublicUrl, profile?.InternalUrl, _roiConfigApiUrl })
                {
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    var normalized = candidate.Trim().TrimEnd('/');
                    if (!endpointBases.Any(value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase)))
                        endpointBases.Add(normalized);
                }

                var authTokens = new List<string>();
                foreach (var candidate in new[] { profile?.Token, _roiConfigApiToken, _backendToken })
                {
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    var normalized = candidate.Trim();
                    if (!authTokens.Any(value => string.Equals(value, normalized, StringComparison.Ordinal)))
                        authTokens.Add(normalized);
                }
                if (authTokens.Count == 0) authTokens.Add(null);

                foreach (var endpointBase in endpointBases)
                {
                    foreach (var authToken in authTokens)
                    {
                        var url = endpointBase + "/api/rois/batch/cameras";
                        LoggerManager.LogDebug($"ROI configuration endpoint ({_roiConfigEndpointKeyword}): {url}");
                        using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                        {
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            if (!string.IsNullOrWhiteSpace(authToken))
                                request.Headers.TryAddWithoutValidation("Authorization", authToken);
                            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

                            using (var response = await _deviceStatusHttpClient.SendAsync(request, cancellationToken))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    var json = await response.Content.ReadAsStringAsync();
                                    return JsonConvert.DeserializeObject<RoiBatchResponse>(json) ?? new RoiBatchResponse();
                                }

                                LoggerManager.LogWarn($"GetRoiBatchByCamerasAsync returned {(int)response.StatusCode} from {url}.");
                                if ((int)response.StatusCode != 401 && (int)response.StatusCode != 403)
                                    break;
                            }
                        }
                    }
                }

                return null;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetRoiBatchByCamerasAsync");
                return null;
            }
        }
    }
}















