using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;

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
        private static readonly Lazy<ApiManager> _instance = new Lazy<ApiManager>(() => new ApiManager());
        public static ApiManager Instance => _instance.Value;

        private HttpClient _httpClient;

        // Backend Domain (The Center Server entry point)
        private string _baseUrl = "http://localhost:8100";
        private string _streamApiUrl = "http://localhost:3000/streams";
        private string _backendToken;

        // Multi-service Endpoint Registry
        private List<EndpointProfile> _endpointRegistry = new List<EndpointProfile>();

        private string _networkMode = "Public"; // "Public" or "Internal"

        // Local cache for primary storage (backward compatibility)
        private string _storageUrl = "http://localhost:8012";
        private string _storageToken;
        private string _assetsUrl;
        private string _assetsToken;
        private string _reportUrl;
        private string _reportToken;
        private string _mapUrl = "https://a.tile.Topenstreetmap.org";
        private string _redisUrl;

       
        public string BaseUrl => _baseUrl;
        public string StreamApiUrl => _streamApiUrl;
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

        private ApiManager()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            LoadConfig();
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
                        _baseUrl = config.ApiUrl;
                        if (!string.IsNullOrWhiteSpace(config.StreamApiUrl))
                            _streamApiUrl = config.StreamApiUrl.TrimEnd('/');
                        _networkMode = config.NetworkMode;
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

        public async Task<LoginResult> LoginAsync(string username, string password)
        {
            LoggerManager.LogDebug($"Đang gọi API Đăng nhập cho user: {username} tại {_baseUrl}");
            try
            {
                var credentials = new { username = username, password = password };
                var json = JsonConvert.SerializeObject(credentials);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/auth/login", content);
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<LoginResponse>(resultJson);

                    SetBackendToken(result.access_token);

                    // Auto-discover other service endpoints (Storage, Logs, etc.)
                    await DiscoverEndpointsAsync();
                    LoggerManager.LogInfo($"Gửi yêu cầu đăng nhập thành công cho: {username}");
                    return new LoginResult(true, result.user_id, "Success");
                }

                LoggerManager.LogWarn($"Đăng nhập không thành công (HTTP {response.StatusCode}) cho user: {username}");
                return new LoginResult(false, null, "Invalid username or password");
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, $"Lỗi nghiêm trọng khi gọi LoginAsync cho {username}");
                return new LoginResult(false, null, ex.Message);
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
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/client-profiles/me/authorized", cancellationToken);
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
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/user/profiles", cancellationToken);
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
                var response = await _httpClient.GetAsync(url, cancellationToken);
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
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/auth/me", cancellationToken);
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

        // ============ Storage / Playback Server API ============
        public List<EndpointProfile> GetDiscoveredEndpoints() => _endpointRegistry;

        public string GetEndpointUrl(string keyword)
        {
            var profile = _endpointRegistry.FirstOrDefault(x => x.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            if (profile == null) return null;

            return _networkMode == "Public" ? profile.PublicUrl : profile.InternalUrl;
        }

        public string GetEndpointToken(string keyword)
        {
            var profile = _endpointRegistry.FirstOrDefault(x => x.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            return (profile != null && !string.IsNullOrEmpty(profile.Token)) ? profile.Token : _backendToken;
        }

        public async Task<bool> DiscoverEndpointsAsync()
        {
            LoggerManager.LogDebug($"Bắt đầu khám phá Service Endpoints tại {_baseUrl}/api/system/endpoints");
            try
            {
                // Call the unified discovery endpoint on Center Manager
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/system/endpoints");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var discoveryData = JsonConvert.DeserializeObject<SystemEndpoints>(json);

                    if (discoveryData != null && discoveryData.Endpoints != null)
                    {
                        _endpointRegistry = discoveryData.Endpoints;

                        // Sync primary storage fields for backward compatibility
                        var storageEp = _endpointRegistry.FirstOrDefault(x => x.Keyword.Equals("Storage", StringComparison.OrdinalIgnoreCase));
                        if (storageEp != null)
                        {
                            _storageUrl = _networkMode == "Public" ? storageEp.PublicUrl : storageEp.InternalUrl;
                            _storageToken = string.IsNullOrEmpty(storageEp.Token) ? "your-super-secret-private-token-2026" : storageEp.Token;
                        }

                        // Sync New Endpoints
                        var assetsEp = _endpointRegistry.FirstOrDefault(x => x.Keyword.Equals("Assets", StringComparison.OrdinalIgnoreCase));
                        if (assetsEp != null)
                        {
                            _assetsUrl = _networkMode == "Public" ? assetsEp.PublicUrl : assetsEp.InternalUrl;
                            _assetsToken = string.IsNullOrEmpty(assetsEp.Token) ? "your-super-secret-private-token-2026" : assetsEp.Token;
                        }

                        var reportEp = _endpointRegistry.FirstOrDefault(x => x.Keyword.Equals("Report", StringComparison.OrdinalIgnoreCase));
                        if (reportEp != null)
                        {
                            _reportUrl = _networkMode == "Public" ? reportEp.PublicUrl : reportEp.InternalUrl;
                            _reportToken = string.IsNullOrEmpty(reportEp.Token) ? "your-super-secret-private-token-2026" : reportEp.Token;
                        }

                        var mapEp = _endpointRegistry.FirstOrDefault(x => x.Keyword.Equals("Map", StringComparison.OrdinalIgnoreCase));
                        if (mapEp != null)
                        {
                            _mapUrl = _networkMode == "Public" ? mapEp.PublicUrl : mapEp.InternalUrl;
                        }

                        var redisEp = _endpointRegistry.FirstOrDefault(x => x.Keyword.Equals("Redis", StringComparison.OrdinalIgnoreCase));
                        if (redisEp != null)
                        {
                            _redisUrl = _networkMode == "Public" ? redisEp.PublicUrl : redisEp.InternalUrl;
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
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi nghiêm trọng trong DiscoverEndpointsAsync");
            }
            return false;
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

        public async Task<List<DeviceStatusResponse>> GetDeviceStatusBatchAsync(List<string> deviceIds, CancellationToken cancellationToken = default)
        {
            try
            {
                var body = new { device_ids = deviceIds };
                var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/devices/status/batch", content, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<DeviceStatusResponse>>(resultJson);
                }
                return new List<DeviceStatusResponse>();
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi gọi GetDeviceStatusBatchAsync");
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

        public class DeviceStatusResponse
        {
            [JsonProperty("device_id")]
            public string DeviceId { get; set; }
            [JsonProperty("status")]
            public string Status { get; set; }
            [JsonProperty("is_online")]
            public bool? IsOnline { get; set; }
        }
    }
}















