using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using V3SClient.libs;

namespace V3SClient.ucs
{
    public partial class UCTrajectoryMap : UserControl
    {
        private CoreWebView2Environment _webViewEnv;
        private bool _isMapLoaded = false;
        private object _pendingTrajectoryData = null; // Queue data if map not ready

        public class PositionPoint
        {
            public double Lat { get; set; }
            public double Lng { get; set; }
            public PositionPoint(double lat, double lng) { Lat = lat; Lng = lng; }
        }

        public PositionPoint DefaultCenter { get; set; } = new PositionPoint(20.995250, 105.903059);

        public UCTrajectoryMap()
        {
            InitializeComponent();
            this.Loaded += UCTrajectoryMap_Loaded;
        }

        private async void UCTrajectoryMap_Loaded(object sender, RoutedEventArgs e)
        {
            if (_webViewEnv != null) return; // Prevent double init
            await InitializeWebViewAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                var env = await V3SClient.libs.WebViewEnvHelper.GetSharedEnvironmentAsync();
                _webViewEnv = env;
                await mapWebView.EnsureCoreWebView2Async(env);

                mapWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                mapWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                mapWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                mapWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                mapWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

                mapWebView.Source = new Uri("http://ivista.map/base_map.html?mode=trajectory");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cần cài Web Runtime để hiển thị bản đồ.\n" + ex.Message);
            }
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var cmd = JObject.Parse(e.WebMessageAsJson);
                string action = cmd["action"]?.ToString();

                if (action == "mapLoaded")
                {
                    _isMapLoaded = true;
                    ConfigureOfflineMap();

                    var centerMsg = new { action = "flyTo", lng = DefaultCenter.Lng, lat = DefaultCenter.Lat };
                    mapWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(centerMsg));

                    // Replay pending trajectory data that arrived before map was ready
                    if (_pendingTrajectoryData != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[UCTrajectoryMap] Replaying pending trajectory data after map loaded.");
                        var pending = _pendingTrajectoryData;
                        _pendingTrajectoryData = null;
                        ShowTrajectory(pending);
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
                if (string.IsNullOrEmpty(baseUrl)) return;

                string url = baseUrl;
                if (baseUrl.Contains("openstreetmap.org") || baseUrl.Contains("tile.osm.org"))
                {
                    if (!url.Contains("{z}")) url = url.TrimEnd('/') + "/{z}/{x}/{y}.png";
                }
                else
                {
                    if (!url.Contains("{z}"))
                    {
                        url = url.TrimEnd('/');
                        if ((url.Contains("map.ivistatech.vn") || url.Contains(":8090"))
                            && !url.EndsWith("/tile", StringComparison.OrdinalIgnoreCase))
                        {
                            url += "/tile";
                        }
                        url += "/{z}/{x}/{y}.png";
                    }
                }

                if (url.StartsWith("http") && !url.Contains("proxy.ivista"))
                    url = "http://proxy.ivista/" + url;

                var msg = new { action = "setTileUrl", urls = new[] { url } };
                mapWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(msg));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UCTrajectoryMap] ConfigureOfflineMap Exception: {ex.Message}");
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
                                //Dispatcher.Invoke(() => {
                                //    System.Windows.MessageBox.Show(debugInfo, "Lỗi nạp Resource");
                                //});
                                System.Diagnostics.Debug.WriteLine(debugInfo);
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

            // 2. Xử lý Proxy cho Map Tiles ngoại bộ
            if (uri.Contains("http://proxy.ivista/"))
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
                                catch { }
                            });
                        }
                    }
                    catch
                    {
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

        public void ShowTrajectory(object trajectoryData)
        {
            if (!_isMapLoaded || mapWebView?.CoreWebView2 == null)
            {
                // Map not ready yet — queue the data for replay when mapLoaded fires
                System.Diagnostics.Debug.WriteLine($"[UCTrajectoryMap] Map not loaded yet (_isMapLoaded={_isMapLoaded}). Queuing trajectory data.");
                _pendingTrajectoryData = trajectoryData;
                return;
            }

            string json = JsonConvert.SerializeObject(new { action = "trajectoryResult", data = trajectoryData });
            System.Diagnostics.Debug.WriteLine($"[UCTrajectoryMap] Posting trajectoryResult to WebView2, payload length={json.Length}");
            mapWebView.CoreWebView2.PostWebMessageAsJson(json);
        }

        public void ClearTrajectory()
        {
            if (!_isMapLoaded || mapWebView?.CoreWebView2 == null) return;
            var msg = new { action = "clearTrajectory" };
            mapWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(msg));
        }

        public void FocusPoint(int index)
        {
            if (!_isMapLoaded || mapWebView?.CoreWebView2 == null) return;
            var msg = new { action = "focusPoint", index = index };
            mapWebView.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(msg));
        }

        public void RunDemoSimulation()
        {
            if (!_isMapLoaded || mapWebView?.CoreWebView2 == null) return;

            // Tạo dữ liệu giả lập chi tiết giống project MAP cũ
            var points = new System.Collections.Generic.List<object>();
            DateTime baseTime = DateTime.Now.AddHours(-2);
            string demoId = "SIM-9999";

            var demoPoints = new[]
            {
                new { lat = 21.036780, lng = 105.782000, camera = "CAM-CG-01", desc = "Bắt đầu lộ trình" },
                new { lat = 21.033300, lng = 105.814500, camera = "CAM-BD-01", desc = "Di chuyển qua Ba Đình" },
                new { lat = 21.028511, lng = 105.804817, camera = "CAM-HK-01", desc = "Ghi nhận tại Hoàn Kiếm" },
                new { lat = 21.018000, lng = 105.852000, camera = "CAM-HBT-01", desc = "Điểm qua Hai Bà Trưng" },
                new { lat = 21.046000, lng = 105.908000, camera = "CAM-LB-01", desc = "Điểm cuối demo" },
                new { lat = 21.050000, lng = 105.830000, camera = "CAM-TN-01", desc = "Qua Tây Hồ" },
                new { lat = 21.027000, lng = 105.834000, camera = "CAM-DH-01", desc = "Đống Đa" },
                new { lat = 21.010000, lng = 105.820000, camera = "CAM-TX-01", desc = "Thanh Xuân" },
                new { lat = 21.015000, lng = 105.790000, camera = "CAM-CG-02", desc = "Cầu Giấy lần 2" },
                new { lat = 21.035000, lng = 105.860000, camera = "CAM-LB-02", desc = "Long Biên" }
            };

            for (int i = 0; i < demoPoints.Length; i++)
            {
                var pt = demoPoints[i];
                string timeStr = baseTime.AddMinutes(i * 20).ToString("yyyy-MM-dd HH:mm:ss");
                
                points.Add(new
                {
                    index = i,
                    lat = pt.lat,
                    lng = pt.lng,
                    camera = pt.camera,
                    timestamp = timeStr,
                    time = timeStr,
                    id = demoId,
                    desc = pt.desc,
                    address = pt.desc,
                    imageUrl = $"https://picsum.photos/seed/traj-demo-{i + 1}/640/360",
                    image = $"https://picsum.photos/seed/traj-demo-{i + 1}/640/360",
                    popup = $"{pt.camera}<br/>{timeStr}"
                });
            }

            var demoData = new
            {
                id = demoId,
                total = points.Count,
                start = baseTime.ToString("yyyy-MM-dd HH:mm:ss"),
                end = baseTime.AddMinutes(demoPoints.Length * 20).ToString("yyyy-MM-dd HH:mm:ss"),
                points = points
            };

            ShowTrajectory(demoData);
        }
    }
}
