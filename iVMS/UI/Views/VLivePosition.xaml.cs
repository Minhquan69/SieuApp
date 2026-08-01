using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using Newtonsoft.Json.Linq;
using V3SClient.viewModels;
using V3SClient.ucs;
using System.Windows.Threading;
using System.IO;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using GMap.NET;
using V3SClient.libs;
using System.Diagnostics;

namespace V3SClient.UI.Views
{
    public partial class VLivePosition : Page, INotifyPropertyChanged
    {
        private bool _isTrackingEnabled = false;
        private bool _isFovVisible = true;
        private string _trackingCameraId = null;
        private System.Windows.Threading.DispatcherTimer _timer;

        private bool _isMapLoaded = false;
        private Microsoft.Web.WebView2.Core.CoreWebView2Environment _webViewEnv;
        private double _currentZoom = 12.0;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public class PositionPoint
        {
            public double Lat { get; set; }
            public double Lng { get; set; }
            public double OffsetX { get; set; } = 0;
            public double OffsetY { get; set; } = 0;
            public PositionPoint(double lat, double lng) { Lat = lat; Lng = lng; }
        }

        Dictionary<string, PositionPoint> _camerasPosition = new Dictionary<string, PositionPoint>();
        public PositionPoint _startPostion = new PositionPoint(20.995250, 105.903059);
        public ObservableCollection<models.Camera> CameraList { get; set; }
        public MapViewModel_v3 Sidebar { get; } = new MapViewModel_v3();

        private ObservableCollection<viewModels.VMTalkGroup> _rawGroupList;

        public VLivePosition(ObservableCollection<viewModels.VMTalkGroup> cam_group_list)
        {
            InitializeComponent();
            DataContext = this;
            _rawGroupList = cam_group_list;
            CameraList = new ObservableCollection<models.Camera>(
                cam_group_list.Where(group => group != null && group.Cameras.Count > 0)
                              .SelectMany(group => group.Cameras));
            Sidebar.Refresh(_rawGroupList, CameraList);
            Sidebar.CameraStatesChanged += Sidebar_CameraStatesChanged;

            this.Loaded += LoadMapAsync;
            this.Unloaded += VLivePosition_Unloaded;
        }

        private void Sidebar_CameraStatesChanged(object sender, System.EventArgs e)
        {
            if (!_isMapLoaded) return;
            Dispatcher.BeginInvoke(new System.Action(SendCamerasToMap));
        }
        private void InitFakeCameras()
        {
            CameraList.Clear();

            // Tọa độ gốc (Hồ Gươm - Hà Nội)
            double baseLat = _startPostion.Lat;
            double baseLng = _startPostion.Lng;

            System.Diagnostics.Debug.WriteLine("Base: " + baseLat + ", " + baseLng);

            CameraList.Add(new models.Camera
            {
                camID = $"CAM_{0:000}",
                groupID = $"GROUP_{(0 % 3) + 1}",
                name = $"Camera {0}",
                long_Name = $"Camera giám sát khu vực {0}",
                description = $"Fake camera số {0}",
                type = "Outdoor",
                is_Live = true,
                rtps = $"rtsp://192.168.1.{0}/stream",
                is_Master = true,

                // Tọa độ gốc
                Latitude = baseLat,
                Longitude = baseLng,

                is_online = (0 % 2 == 0),
                Status = (0 % 2 == 0) ? "online" : "offline",

                IsChecked = false,
                AllowSelecting = Visibility.Visible,

                ExtraMetadata = new
                {
                    Zone = $"Zone {(0 % 5) + 1}",
                    Floor = (0 % 10) + 1
                }
            });

            // Bán kính dao động (đơn vị: độ)
            // 0.002 ≈ 200m
            double radius = 0.2;

            Random rand = new Random();

            for (int i = 1; i <= 10; i++)
            {
                // Tạo tọa độ ngẫu nhiên quanh điểm gốc
                double latOffset = (rand.NextDouble() - 0.5) * 2 * radius;
                double lngOffset = (rand.NextDouble() - 0.5) * 2 * radius;

                CameraList.Add(new models.Camera
                {
                    camID = $"CAM_{i:000}",
                    groupID = $"GROUP_{(i % 3) + 1}",
                    name = $"Camera {i}",
                    long_Name = $"Camera giám sát khu vực {i}",
                    description = $"Fake camera số {i}",
                    type = (i % 2 == 0) ? "Outdoor" : "Indoor",
                    is_Live = true,
                    rtps = $"rtsp://192.168.1.{100 + i}/stream",
                    is_Master = (i == 1),

                    // Tọa độ ngẫu nhiên gần điểm gốc
                    Latitude = baseLat + latOffset,
                    Longitude = baseLng + lngOffset,

                    is_online = (i % 2 == 0),
                    Status = (i % 2 == 0) ? "online" : "offline",

                    IsChecked = false,
                    AllowSelecting = Visibility.Visible,

                    ExtraMetadata = new
                    {
                        Zone = $"Zone {(i % 5) + 1}",
                        Floor = (i % 10) + 1
                    }
                });
            }
        }

        // ========= WEBVIEW2 INIT =========
        private async void InitializeWebViewAsync()
        {
            try
            {
                var env = await V3SClient.libs.WebViewEnvHelper.GetSharedEnvironmentAsync();
                _webViewEnv = env;
                await mapWebView.EnsureCoreWebView2Async(env);
                
                // Disable DevTools and Context Menus for security
                mapWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                mapWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                mapWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                mapWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                mapWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

                mapWebView.Source = new Uri("http://ivista.map/base_map.html?mode=live");
                System.Diagnostics.Debug.WriteLine("[VLivePosition] WebView2 initialized with Embedded Resources.");
            }
            catch (Exception ex)
            {
                LoggerManager.LogError("Không thể khởi tạo WebView2 cho E-Map", ex);
                Debug.WriteLine("Cần cài WebView2 Runtime để hiển thị bản đồ.\n" + ex.Message);
            }
        }

        // ========= MESSAGE FROM JS =========
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = e.WebMessageAsJson;
            try
            {
                var cmd = JObject.Parse(message);
                string action = cmd["action"]?.ToString();

                if (action == "mapLoaded")
                {
                    _isMapLoaded = true;
                    ConfigureOfflineMap();
                    
                    // Follow the web behavior: open around a real camera coordinate, never a fabricated marker.
                    var defaultCenter = GetFirstCameraPosition();
                    System.Diagnostics.Debug.WriteLine("Center"+ defaultCenter.Lat+defaultCenter.Lng);
                    var centerMsg = new { action = "flyTo", lng = defaultCenter.Lng, lat = defaultCenter.Lat };
                    mapWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(centerMsg));
                    SendCamerasToMap();
                }
                else if (action == "markerClicked")
                {
                    string camId = cmd["data"]?.ToString();
                    Dispatcher.Invoke(() => HandleMarkerClick(camId));
                }
                else if (action == "toggleCameraSidebar")
                {
                    Dispatcher.Invoke(() => ToggleSidebar_Click(null, null));
                }
                else if (action == "trackingChanged")
                {
                    // Tracking giờ xử lý hoàn toàn trong JS, chỉ cần đồng bộ state
                    var data = cmd["data"];
                    bool tracking = data["tracking"]?.ToObject<bool>() ?? false;
                    string camId = data["camId"]?.ToString();
                    _trackingCameraId = camId;
                }
                else if (action == "zoomChanged")
                {
                    if (cmd["data"] != null)
                    {
                        var newZoom = cmd["data"].ToObject<double>();
                        if (Math.Abs(newZoom - _currentZoom) > 0.05) // Sensible delta to trigger rebuild
                        {
                            _currentZoom = newZoom;
                            if (_isMapLoaded)
                                Dispatcher.InvokeAsync(() => SendCamerasToMap());
                        }
                    }
                }
                else if (action == "console" || action == "error")
                {
                    System.Diagnostics.Debug.WriteLine($"JS {action.ToUpper()}: {cmd["data"]?.ToString()}");
                }
            }
            catch { }
        }

        private void ConfigureOfflineMap()
        {
            try 
            {
                string baseUrl = ApiManager.Instance.MapUrl;
                System.Diagnostics.Debug.WriteLine($"[VLivePosition] ConfigureOfflineMap: Base MapUrl='{baseUrl}'");
                if (string.IsNullOrEmpty(baseUrl)) return;

                string url = baseUrl;
                // Format URL correctly for OSM vs Local Tile Server
                if (baseUrl.Contains("openstreetmap.org") || baseUrl.Contains("tile.osm.org"))
                {
                    if (!url.Contains("{z}")) url = url.TrimEnd('/') + "/{z}/{x}/{y}.png";
                }
                else
                {
                    // Local tile server usually has pattern: /{z}/{x}/{y}.png
                    if (!url.Contains("{z}"))
                    {
                        url = url.TrimEnd('/');
                        // The iVista tile service exposes tiles below /tile, both in
                        // production and on the local :8090 deployment.
                        if ((url.Contains("map.ivistatech.vn") || url.Contains(":8090"))
                            && !url.EndsWith("/tile", StringComparison.OrdinalIgnoreCase))
                        {
                            url += "/tile";
                        }
                        url += "/{z}/{x}/{y}.png";
                    }
                }

                // IMPORTANT: Wrap in proxy if it's an external URL to bypass CORS
                if (url.StartsWith("http") && !url.Contains("proxy.ivista"))
                {
                    // DO NOT escape the whole URL because MapLibre needs to find {z}, {x}, {y} as clear text
                    url = "http://proxy.ivista/" + url;
                }

                System.Diagnostics.Debug.WriteLine($"[VLivePosition] ConfigureOfflineMap: Applied Tile URL='{url}'");

                var msg = new
                {
                    action = "setTileUrl",
                    urls = new[] { url }
                };
                mapWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(msg));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VLivePosition] ConfigureOfflineMap Exception: {ex.Message}");
            }
        }

        private void CoreWebView2_WebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            string uri = e.Request.Uri;

            // 1. Xử lý tài nguyên nội bộ (Embedded Resources)
            if (uri.StartsWith("http://ivista.map/"))
            {
                var deferral = e.GetDeferral();
                Task.Run(() =>
                {
                    try
                    {
                        // Lấy đường dẫn tương đối từ URL (vd: js/module_core.js)
                        string path = uri.Replace("http://ivista.map/", "");
                        if (path.Contains("?")) path = path.Split('?')[0];

                        // Chuẩn hóa đường dẫn để tìm kiếm (thay / và \ thành .)
                        string normalizedPath = path.Replace("/", ".").Replace("\\", ".");

                        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                        
                        // Tìm kiếm thông minh trong danh sách Resource Names
                        string resourceName = assembly.GetManifestResourceNames()
                            .FirstOrDefault(r => {
                                string normalizedR = r.Replace("/", ".").Replace("\\", ".");
                                return normalizedR.EndsWith(".map_assets." + normalizedPath, StringComparison.OrdinalIgnoreCase)
                                    || normalizedR.Equals("map_assets." + normalizedPath, StringComparison.OrdinalIgnoreCase);
                            });

                        if (resourceName != null)
                        {
                            var stream = assembly.GetManifestResourceStream(resourceName);
                            string contentType = GetContentType(resourceName);
                            Dispatcher.Invoke(() =>
                            {
                                e.Response = mapWebView.CoreWebView2.Environment.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {contentType}");
                                deferral.Complete();
                            });
                        }
                        else
                        {
                            // DEBUG: Hiện thông báo danh sách Resource để chẩn đoán
                            if (path.EndsWith(".html"))
                            {
                                var allNames = assembly.GetManifestResourceNames();
                                string debugInfo = $"Không tìm thấy: map_assets.{normalizedPath}\n" +
                                                 $"Có tổng cộng {allNames.Length} resources.\n" +
                                                 $"10 cái đầu tiên:\n" + string.Join("\n", allNames.Take(10));
                                Dispatcher.Invoke(() => {
                                    System.Windows.MessageBox.Show(debugInfo, "Lỗi nạp Resource");
                                });
                            }

                            Dispatcher.Invoke(() =>
                            {
                                e.Response = mapWebView.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                                deferral.Complete();
                            });
                        }
                    }
                    catch
                    {
                        deferral.Complete();
                    }
                });
                return;
            }

            // 2. Xử lý Proxy cho Map Tiles ngoại bộ (giữ nguyên logic cũ)
            if (e.Request.Uri.Contains("http://proxy.ivista/"))
            {
                string requestUri = e.Request.Uri;
                string realUrl = requestUri.Split(new[] { "http://proxy.ivista/" }, StringSplitOptions.None)[1];
                
                var deferral = e.GetDeferral();
                Task.Run(async () =>
                {
                    try
                    {
                        using (var client = new HttpClient())
                        {
                            client.Timeout = TimeSpan.FromSeconds(20);
                            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                            
                            var response = await client.GetAsync(realUrl);
                            var contentStream = await response.Content.ReadAsStreamAsync();
                            string contentType = response.Content.Headers.ContentType?.ToString() ?? "image/png";
                            
                            string headers = 
                                $"Content-Type: {contentType}\n" +
                                "Access-Control-Allow-Origin: *\n" +
                                "Access-Control-Allow-Methods: GET, OPTIONS\n" +
                                "Cache-Control: public, max-age=31536000";

                            Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    e.Response = _webViewEnv.CreateWebResourceResponse(
                                        contentStream,
                                        (int)response.StatusCode,
                                        response.ReasonPhrase,
                                        headers);
                                }
                                catch (Exception ex2)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VLivePosition] Dispatcher.Invoke Error: {ex2.Message}");
                                }
                            });

                            if (!response.IsSuccessStatusCode)
                                System.Diagnostics.Debug.WriteLine($"[VLivePosition] Proxy error: {(int)response.StatusCode} {response.ReasonPhrase} for {realUrl}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VLivePosition] Proxy Fatal Exception: {ex.Message} for {realUrl}");
                        if (_webViewEnv != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                e.Response = _webViewEnv.CreateWebResourceResponse(null, 504, "Gateway Timeout", "Content-Type: text/plain\nAccess-Control-Allow-Origin: *");
                            });
                        }
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                });
            }
        }

        private string GetContentType(string path)
        {
            if (path.EndsWith(".html")) return "text/html";
            if (path.EndsWith(".js")) return "application/javascript";
            if (path.EndsWith(".css")) return "text/css";
            if (path.EndsWith(".png")) return "image/png";
            if (path.EndsWith(".svg")) return "image/svg+xml";
            return "application/octet-stream";
        }

        private async void LoadMapAsync(object sender, RoutedEventArgs e)
        {
            // Khởi tạo bản đồ trước; không để dịch vụ lấy vị trí bên ngoài chặn giao diện.
            InitializeWebViewAsync();
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += UpdatedMarker;

            var pos = await GetPCPosition();
            _startPostion = pos;

            // Fly to initial position
        }
        private PositionPoint GetFirstCameraPosition()
        {
            if (CameraList != null && CameraList.Count > 0)
            {
                var first = CameraList.FirstOrDefault(camera => camera != null && IsValidCoordinate(camera.Latitude, camera.Longitude));
                if (first != null) return new PositionPoint(first.Latitude.Value, first.Longitude.Value);
            }
            return _startPostion;
            // Chạy demo simulation nếu cần (để test UI)
            //StartDemoSimulation();
        }
        private void SendCurrentLocationToMap(PositionPoint pos, bool flyTo)
        {
            if (!_isMapLoaded || mapWebView?.CoreWebView2 == null || pos == null) return;
            var msg = new
            {
                action = "setCurrentLocation",
                lng = pos.Lng,
                lat = pos.Lat,
                flyTo = flyTo
            };
            mapWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(msg));
        }
        // ========= DEMO SIMULATION =========
        private DispatcherTimer _demoTimer;
        private Random _rnd = new Random();

        public void StartDemoSimulation()
        {
            if (_demoTimer != null) return;
            
            _demoTimer = new DispatcherTimer();
            _demoTimer.Interval = TimeSpan.FromSeconds(3); // Cập nhật mỗi 3s
            _demoTimer.Tick += (s, e) =>
            {
                if (CameraList == null || CameraList.Count == 0) return;

                // 1. Chọn ngẫu nhiên 1 camera để thay đổi trạng thái/vị trí
                var cam = CameraList[_rnd.Next(CameraList.Count)];

                // Random Online/Offline
                bool isOnline = _rnd.Next(100) > 20; // 80% Online
                cam.is_online = isOnline;
                cam.Status = isOnline ? "online" : "offline";

                string camType = (!string.IsNullOrEmpty(cam.type) ? cam.type.ToLower() : "ip_cam");
                
                // Random di chuyển nhẹ (nếu đang online và là bodycam)
                if (isOnline && (camType.Contains("bodycam") || camType.Contains("body_cam")))
                {
                    double lat = cam.Latitude ?? _startPostion.Lat;
                    double lng = cam.Longitude ?? _startPostion.Lng;
                    
                    lat += (_rnd.NextDouble() - 0.5) * 0.001;
                    lng += (_rnd.NextDouble() - 0.5) * 0.001;
                    
                    cam.Latitude = lat;
                    cam.Longitude = lng;
                }

                // Thỉnh thoảng báo động (5% cơ hội mỗi 3s)
                if (isOnline && _rnd.Next(100) < 5)
                {
                    SetCameraAlarm(cam.camID, 8); // Báo động 8 giây
                }

                // Gửi cập nhật toàn bộ sang Map
                SendCamerasToMap();
            };
            _demoTimer.Start();
        }

        // ========= SEND CAMERAS TO MAP =========
        private void SendCamerasToMap()
        {
            if (CameraList == null || !_isMapLoaded) return;

            var camDataList = BuildLiveMapData();
            var msg = new { action = "updateCameras", data = camDataList };
            mapWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(msg));
        }

        // ========= REALTIME GPS UPDATE =========
        private void UpdatedMarker(object sender, EventArgs e)
        {
            if (_isMapLoaded)
            {
                SendCamerasToMap();
            }
            _timer.Stop();
        }

        // ========= NATIVE CLUSTERING =========
        private List<object> BuildLiveMapData()
        {
            var camDataList = new List<object>();
            if (CameraList == null || CameraList.Count == 0) return camDataList;

            var points = new Dictionary<string, PositionPoint>();
            var mappedCameras = new List<models.Camera>();
            foreach (models.Camera cam in CameraList)
            {
                if (cam == null || string.IsNullOrWhiteSpace(cam.camID)) continue;
                double lat = 0;
                double lng = 0;
                var hasCoordinate = IsValidCoordinate(cam.Latitude, cam.Longitude);
                string camType = "ip_cam"; // Mặc định là cam tĩnh

                if (!string.IsNullOrEmpty(cam.type))
                {
                    camType = cam.type.ToLower();
                }

                if (hasCoordinate)
                {
                    lat = cam.Latitude.Value;
                    lng = cam.Longitude.Value;
                }

                // LUẬT MỚI: Chỉ cập nhật vị trí realtime từ bản tin GPS nếu nó SẼ DI CHUYỂN (bodycam)
                if (camType.Contains("bodycam") || camType.Contains("body_cam"))
                {
                    if (_camerasPosition != null && _camerasPosition.ContainsKey(cam.camID))
                    {
                        var p = _camerasPosition[cam.camID];
                        if (IsValidCoordinate(p.Lat, p.Lng)) { lat = p.Lat; lng = p.Lng; hasCoordinate = true; }
                    }
                }

                // Same as the web E-Map: cameras without a valid location are not rendered.
                if (!hasCoordinate) continue;
                points[cam.camID] = new PositionPoint(lat, lng);
                mappedCameras.Add(cam);
            }

            double degreesPerPixel = 360.0 / (256.0 * Math.Pow(2, _currentZoom));
            // 32px is standard. Capped at 0.045 deg (~5km) to split dense areas into multiple small clusters.
            double threshold = Math.Min(32.0 * degreesPerPixel, 0.045); 
            bool shouldSpiderfy = _currentZoom >= 16.6; // Đặt thấp hơn maxZoom một chút để bẻ cụm ngay khi vặn đến kịch sàn

            var assigned = new HashSet<string>();
            var clusteredCamIds = new HashSet<string>();
            var list = points.OrderBy(p => p.Key).ToList();
            var clustersToDraw = new List<object>();

            var spiderfyOffsets = new Dictionary<string, (double x, double y)>();

            for (int i = 0; i < list.Count; i++)
            {
                if (assigned.Contains(list[i].Key)) continue;

                var clusterKeys = new List<string> { list[i].Key };
                assigned.Add(list[i].Key);

                double cosLat = Math.Cos(list[i].Value.Lat * Math.PI / 180.0);

                for (int j = i + 1; j < list.Count; j++)
                {
                    if (assigned.Contains(list[j].Key)) continue;
                    double dLat = list[j].Value.Lat - list[i].Value.Lat;
                    double dLng = (list[j].Value.Lng - list[i].Value.Lng) * cosLat;
                    if (dLat * dLat + dLng * dLng < threshold * threshold)
                    {
                        clusterKeys.Add(list[j].Key);
                        assigned.Add(list[j].Key);
                    }
                }

                if (clusterKeys.Count > 1)
                {
                    clusterKeys.Sort();
                    
                    if (shouldSpiderfy)
                    {
                        double step = 360.0 / clusterKeys.Count;
                        double radiusPixel = 45.0 + (clusterKeys.Count * 2);
                        for (int k = 0; k < clusterKeys.Count; k++)
                        {
                            double rad = step * k * Math.PI / 180.0;
                            spiderfyOffsets[clusterKeys[k]] = (radiusPixel * Math.Cos(rad), radiusPixel * Math.Sin(rad));
                        }
                    }
                    else
                    {
                        // --- ENHANCED REPRESENTATIVE CLUSTERING ---
                        // Only count members with valid (non-default) coordinates for centroid calculation
                        var validMembers = clusterKeys.Where(k => Math.Abs(points[k].Lat - _startPostion.Lat) > 0.00001 || Math.Abs(points[k].Lng - _startPostion.Lng) > 0.00001).ToList();
                        
                        double rawLat, rawLng;
                        if (validMembers.Count > 0)
                        {
                            rawLat = validMembers.Average(k => points[k].Lat);
                            rawLng = validMembers.Average(k => points[k].Lng);
                        }
                        else
                        {
                            rawLat = clusterKeys.Average(k => points[k].Lat);
                            rawLng = clusterKeys.Average(k => points[k].Lng);
                        }

                        // Select the best representative: 
                        // 1. Master camera priority
                        // 2. Closest valid camera to the corrected centroid
                        string repId = clusterKeys[0];
                        double minDistanceSq = double.MaxValue;
                        bool foundMaster = false;
                        double cCosLat = Math.Cos(rawLat * Math.PI / 180.0);

                        foreach (var k in clusterKeys)
                        {
                            var camRef = mappedCameras.FirstOrDefault(c => c.camID == k);
                            if (camRef != null && camRef.is_Master)
                            {
                                repId = k;
                                foundMaster = true;
                                break;
                            }

                            // Only consider valid cameras as representative if possible
                            bool isValid = validMembers.Contains(k);
                            if (!isValid && validMembers.Count > 0) continue; 

                            double dLat = points[k].Lat - rawLat;
                            double dLng = (points[k].Lng - rawLng) * cCosLat;
                            double distSq = dLat * dLat + dLng * dLng;
                            if (distSq < minDistanceSq)
                            {
                                minDistanceSq = distSq;
                                repId = k;
                            }
                        }

                        double cLat = points[repId].Lat;
                        double cLng = points[repId].Lng;

                        string clusterId = "cluster_" + string.Join("_", clusterKeys).GetHashCode().ToString("X");
                        foreach (var k in clusterKeys) clusteredCamIds.Add(k);

                        clustersToDraw.Add(new
                        {
                            camID = clusterId,
                            isCluster = true,
                            count = clusterKeys.Count,
                            lat = cLat,
                            lng = cLng,
                            cameras = clusterKeys
                        });
                        // ------------------------------------------
                    }
                }
            }

            foreach (models.Camera cam in mappedCameras)
            {
                // Device-status polling updates this shared Camera instance.
                // Do not mark every configured camera as online on the map.
                bool isOnline = cam.is_online == true;
                double heading = 0; double fov = 90;

                if (cam.ExtraMetadata != null)
                {
                    try
                    {
                        var meta = Newtonsoft.Json.Linq.JObject.FromObject(cam.ExtraMetadata);
                        if (meta["heading"] != null) heading = meta["heading"].Value<double>();
                        if (meta["fov"] != null) fov = meta["fov"].Value<double>();
                    }
                    catch { }
                }

                double offX = 0, offY = 0;
                if (spiderfyOffsets.ContainsKey(cam.camID))
                {
                    offX = spiderfyOffsets[cam.camID].x;
                    offY = spiderfyOffsets[cam.camID].y;
                }

                camDataList.Add(new
                {
                    camID = cam.camID,
                    isCluster = false,
                    isHidden = clusteredCamIds.Contains(cam.camID),
                    name = cam.name,
                    lat = points[cam.camID].Lat,
                    lng = points[cam.camID].Lng,
                    offsetX = offX,
                    offsetY = offY,
                    isOnline = isOnline,
                    isTracking = (_isTrackingEnabled && _trackingCameraId == cam.camID),
                    heading = heading,
                    fov = fov
                });
            }

            camDataList.AddRange(clustersToDraw);
            return camDataList;
        }
        private static bool IsValidCoordinate(double? latitude, double? longitude)
        {
            return latitude.HasValue && longitude.HasValue &&
                   latitude.Value >= -90 && latitude.Value <= 90 &&
                   longitude.Value >= -180 && longitude.Value <= 180 &&
                   (Math.Abs(latitude.Value) > 0.000001 || Math.Abs(longitude.Value) > 0.000001);
        }
        private static bool IsValidCoordinate(double latitude, double longitude)
        {
            return latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180 &&
                   (Math.Abs(latitude) > 0.000001 || Math.Abs(longitude) > 0.000001);
        }

        // ========= PUBLIC API (Called by other WPF modules) =========
        public void UpdateActiveCameras(List<models.Camera> activeCameras)
        {
            this.CameraList = new ObservableCollection<models.Camera>(activeCameras ?? new List<models.Camera>());
            Sidebar.Refresh(_rawGroupList, CameraList);
            Dispatcher.Invoke(() => SendCamerasToMap());
        }

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            bool collapsed = CameraSidebar.Visibility == Visibility.Visible;
            SidebarColumn.MinWidth = collapsed ? 0 : 210;
            SidebarColumn.MaxWidth = collapsed ? double.PositiveInfinity : 320;
            SidebarColumn.Width = collapsed ? new GridLength(0) : new GridLength(0.20, GridUnitType.Star);
            CameraSidebar.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            SidebarOpenButton.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            if (_isMapLoaded)
                _ = mapWebView.ExecuteScriptAsync("window.map && window.map.resize && window.map.resize();");
        }

        private void VLivePosition_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ActualWidth < 1000 && SidebarColumn.Width.Value > 0)
                ToggleSidebar_Click(null, null);
        }

        private void SidebarAllFilter_Click(object sender, RoutedEventArgs e) => Sidebar.AiOnly = false;
        private void SidebarAiFilter_Click(object sender, RoutedEventArgs e) => Sidebar.AiOnly = true;

        private void ToggleFov_Click(object sender, RoutedEventArgs e)
        {
            _isFovVisible = !_isFovVisible;
            if (_isMapLoaded)
            {
                _ = mapWebView.ExecuteScriptAsync(
                    "if(window.setFovVisible){window.setFovVisible(" + (_isFovVisible ? "true" : "false") + ");}");
            }
            FovVisibilityIcon.Kind = _isFovVisible
                ? MahApps.Metro.IconPacks.PackIconMaterialKind.EyeOutline
                : MahApps.Metro.IconPacks.PackIconMaterialKind.EyeOffOutline;
        }

        private void SidebarCamera_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.Tag as MapCameraItem_v3;
            FocusSidebarCamera(item);
        }

        // Clicking anywhere on a camera row mirrors the map-pin action.  The
        // separate camera icon remains dedicated to opening the live stream.
        private void SidebarCameraRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = (sender as FrameworkElement)?.Tag as MapCameraItem_v3;
            FocusSidebarCamera(item);
        }

        private void FocusSidebarCamera(MapCameraItem_v3 item)
        {
            if (item == null || !item.HasLocation || !_isMapLoaded) return;
            var message = new
            {
                action = "focusCamera",
                data = new { camId = item.Id, lat = item.Camera.Latitude.Value, lng = item.Camera.Longitude.Value }
            };
            mapWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(message));
        }

        private void SidebarLive_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as FrameworkElement)?.Tag as MapCameraItem_v3;
            if (item != null) HandleMarkerClick(item.Id);
        }

        public void CamerasPositionUpdating(Dictionary<string, PointLatLng> camerasPosition)
        {
            var updated = new Dictionary<string, PositionPoint>();
            foreach (var kvp in camerasPosition)
                updated[kvp.Key] = new PositionPoint(kvp.Value.Lat, kvp.Value.Lng);
            this._camerasPosition = updated;
            if (_timer != null) _timer.Start();
        }

        // ========= CAMERA FLOATING WINDOW =========
        private CameraFloatingWindow _cameraFloatingWindow;

        private void HandleMarkerClick(string camID)
        {
            var camera = CameraList.FirstOrDefault(c => c.camID == camID);
            if (camera != null)
            {
                if (_cameraFloatingWindow == null)
                    _cameraFloatingWindow = new CameraFloatingWindow();

                var parentWindow = System.Windows.Window.GetWindow(this);
                _cameraFloatingWindow.ShowCamera(camera, parentWindow);
            }
        }

        // ========= HELPERS =========
        private async Task<PositionPoint> GetPCPosition()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(3);
                    var resp = await client.GetAsync("http://ipinfo.io/json");
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                        var coords = json["loc"].ToString().Split(',');
                        return new PositionPoint(double.Parse(coords[0]), double.Parse(coords[1]));
                    }
                }
            }
            catch { }
            return _startPostion;
        }

        // ========= EMERGENCY ALARM =========
        public void SetCameraAlarm(string camId, int durationSeconds)
        {
            if (!_isMapLoaded) return;
            var msg = new { action = "triggerAlarm", camId = camId, duration = durationSeconds };
            Dispatcher.Invoke(() => {
                mapWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(msg));
            });
        }

        private void VLivePosition_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_cameraFloatingWindow != null)
            {
                _cameraFloatingWindow.ForceClose();
                _cameraFloatingWindow = null;
            }
            _timer?.Stop();
            _demoTimer?.Stop();
            Sidebar.CameraStatesChanged -= Sidebar_CameraStatesChanged;
            Sidebar.Dispose();
        }
    }
}
