using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.UI.Pages
{
    public partial class VAISearchPage : Page
    {
        private ObservableCollection<VMTalkGroup> _camGroupList;
        private List<ApiManager.DetectionDto> _lastDetections;
        private List<MapPoint> _lastMapPoints;
        private List<string> _selectedCameraIds = new List<string>();
        private List<models.Camera> _selectedCameras = new List<models.Camera>();

        private ViewVAISearch _viewVAISearch;
        private LeftMenu _leftMenu;

        private sealed class MapPoint
        {
            public int Index { get; set; }
            public double Lat { get; set; }
            public double Lng { get; set; }
            public string Popup { get; set; } = string.Empty;
            public string Camera { get; set; } = string.Empty;
            public string Timestamp { get; set; } = string.Empty;
            public string Time { get; set; } = string.Empty;
            public string Id { get; set; } = string.Empty;
            public string Desc { get; set; } = string.Empty;
            public string ImageUrl { get; set; } = string.Empty;
        }

        private sealed class TimelineItem
        {
            public int Index { get; set; }
            public int No { get; set; }
            public DateTime Timestamp { get; set; }
            public string Camera { get; set; } = string.Empty;
        }

        public VAISearchPage() : this(GlobalSystem.Instance.CameraGroups.CamGroupList)
        {
        }

        public VAISearchPage(ObservableCollection<VMTalkGroup> cam_group_list)
        {
            InitializeComponent();
            _camGroupList = cam_group_list;
            
            _leftMenu = new LeftMenu(cam_group_list, 230);

            _viewVAISearch = new ViewVAISearch();
            
            _leftMenu.Event_Nodes_Camera_Selected_Changed += LeftMenu_Event_Nodes_Camera_Selected_Changed;
            _viewVAISearch.SearchRequested += ViewVAISearch_SearchRequested;

            frmLeftMenu.Navigate(_leftMenu);
            
            // Initial UI state
            _viewVAISearch.UpdateSelectedCameraCount(0);
        }

        private void DemoSimulator_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Gọi hàm giả lập từ UCTrajectoryMap
                TrajectoryMap.RunDemoSimulation();

                // Cập nhật giao diện phụ
                SummaryNameText.Text = "ĐỐI TƯỢNG GIẢ LẬP";
                SummaryIdText.Text = "SIM-9999";
                SummaryPlateText.Text = "30A-123.45";

                // Xóa danh sách timeline cũ (nếu có)
                TimelineList.ItemsSource = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Demo Simulator Error: " + ex.Message);
            }
        }

        private void EnableSelectionRecursive(VMTalkGroup group)
        {
            if (group.Cameras != null)
            {
                foreach (var c in group.Cameras)
                {
                    c.AllowSelecting = Visibility.Visible;
                    c.IsChecked = false; // Reset selection
                }
            }
            if (group.SubGroups != null)
            {
                foreach (var s in group.SubGroups) EnableSelectionRecursive(s);
            }
        }

        private void EnableSelectionRecursive(AreaNode area)
        {
            if (area.Units != null)
            {
                foreach (var u in area.Units) EnableSelectionRecursive(u);
            }
        }

        private void EnableSelectionRecursive(UnitNode unit)
        {
            if (unit.Cams != null)
            {
                foreach (var c in unit.Cams)
                {
                    c.AllowSelecting = Visibility.Visible;
                    c.IsChecked = false; // Reset selection
                }
            }
            if (unit.SubUnits != null)
            {
                foreach (var s in unit.SubUnits) EnableSelectionRecursive(s);
            }
        }

        private void LeftMenu_Event_Nodes_Camera_Selected_Changed(object sender, List<models.Camera> selectedCameras)
        {
            _selectedCameras = selectedCameras ?? new List<models.Camera>();
            _selectedCameraIds = _selectedCameras.Select(c => c.camID).ToList();
            _viewVAISearch.UpdateSelectedCameraCount(_selectedCameraIds.Count);
        }

        private async void ViewVAISearch_SearchRequested(object sender, VAISearchArgs e)
        {
            try
            {
                LoggerManager.LogInfo($"Yêu cầu tìm kiếm AI: Loại={e.SearchType}, Query={e.Query}, Start={e.StartTime}, End={e.EndTime}");
                _viewVAISearch.ClearStatus();
                _viewVAISearch.AppendStatus("Đang tìm kiếm...", System.Windows.Media.Colors.LightBlue);

                if (e.SearchType == VAISearchType.Simulator)
                {
                    DemoSimulator_Click(null, null);
                    _viewVAISearch.ClearStatus();
                    _viewVAISearch.AppendStatus("Đã hiển thị dữ liệu giả lập.", System.Windows.Media.Colors.LightGreen);
                    return;
                }

                ApiManager.TrajectoryDto result = null;
                if (e.SearchType == VAISearchType.Face)
                {
                    result = await ApiManager.Instance.GetPersonTrajectoryAsync(e.Query, e.StartTime, e.EndTime, _selectedCameraIds, e.Limit);
                }
                else
                {
                    result = await ApiManager.Instance.GetPlateTrajectoryAsync(e.Query, e.StartTime, e.EndTime, _selectedCameraIds, e.Limit);
                }

                if (result == null || result.Detections == null || result.Detections.Count == 0)
                {
                    LoggerManager.LogInfo("Không tìm thấy kết quả phù hợp cho yêu cầu tìm kiếm AI.");
                    _viewVAISearch.SetStatus("Không tìm thấy dữ liệu lộ trình.", true);
                    ClearMapAndTimeline();
                    return;
                }

                LoggerManager.LogInfo($"Tìm thấy {result.Detections.Count} vị trí trong lộ trình.");
                _viewVAISearch.SetStatus($"tìm thấy {result.Detections.Count} vị trí.");
                await ApplyTrajectoryToUiAsync(result);
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi thực hiện tìm kiếm lộ trình AI");
                _viewVAISearch.SetStatus("Có lỗi hệ thống khi kết nối tới máy chủ AI. Vui lòng thử lại sau.", true);
            }
        }

        private void Loaded_Handler(object sender, RoutedEventArgs e)
        {
            _leftMenu.SetBottomContent(_viewVAISearch);

            // Subscribe to data changes to handle late-loading scenarios
            if (_camGroupList != null)
            {
                _camGroupList.CollectionChanged -= CamGroupList_CollectionChanged;
                _camGroupList.CollectionChanged += CamGroupList_CollectionChanged;
            }

            if (GlobalUserInfo.Instance.AreaTree != null)
            {
                GlobalUserInfo.Instance.AreaTree.CollectionChanged -= AreaTree_CollectionChanged;
                GlobalUserInfo.Instance.AreaTree.CollectionChanged += AreaTree_CollectionChanged;
            }

            // Also try activating immediately (using Dispatcher for UI stability)
            Dispatcher.BeginInvoke(new Action(() => ActivateCameraSelection()), 
                                 System.Windows.Threading.DispatcherPriority.Background);
        }

        private void CamGroupList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            ActivateCameraSelection();
        }

        private void AreaTree_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            ActivateCameraSelection();
        }

        public void ActivateCameraSelection()
        {
            if (_camGroupList != null)
            {
                foreach (var g in _camGroupList) EnableSelectionRecursive(g);
            }

            if (GlobalUserInfo.Instance.AreaTree != null)
            {
                foreach (var area in GlobalUserInfo.Instance.AreaTree)
                    EnableSelectionRecursive(area);
            }
        }




        private void ClearMapAndTimeline()
        {
            TrajectoryMap.ClearTrajectory();
            TimelineList.ItemsSource = null;
            SummaryIdText.Text = "—";
            SummaryPlateText.Text = "—";
            SummaryNameText.Text = "không có dữ liệu";
            ResetAvatar();
        }

        private void ResetAvatar()
        {
            try
            {
                ObjectAvatar.Source = new BitmapImage(new Uri("pack://application:,,,/images/user.png"));
                ObjectAvatar.Margin = new Thickness(12);
                ObjectAvatar.Opacity = 0.5;
            }
            catch { }
        }

        private async Task ApplyTrajectoryToUiAsync(ApiManager.TrajectoryDto dto)
        {
            _lastDetections = dto.Detections.ToList();

            string objId = dto.PersonId ?? dto.PlateNumber ?? "Unknown";

            SummaryIdText.Text = dto.PersonId ?? "Unknown";
            SummaryPlateText.Text = dto.PlateNumber ?? "N/A";
            
            SummaryNameText.Text = !string.IsNullOrEmpty(dto.PersonName) ? dto.PersonName :
                                  (!string.IsNullOrEmpty(dto.PersonId) ? "ID: " + dto.PersonId : "Biển số: " + dto.PlateNumber);

            // Build a lookup of ALL known cameras (for GPS fallback)
            var allCameras = GlobalSystem.Instance.CameraList ?? new List<models.Camera>();

            var timelineData = new List<TimelineItem>();
            var mapPoints = new List<MapPoint>();
            int gpsFromServer = 0, gpsFromFallback = 0, gpsSkipped = 0;

            for (int i = 0; i < dto.Detections.Count; i++)
            {
                var d = dto.Detections[i];
                DateTime.TryParse(d.Timestamp, out var dt);
                string timeStr = dt.ToString("yyyy-MM-dd HH:mm:ss");
                string cameraName = d.Camera ?? "Unknown Camera";

                LoggerManager.LogDebug($"[VAISearch] Detection #{i}: camera='{d.Camera}', timestamp='{d.Timestamp}', gps={( d.Gps != null ? $"{d.Gps.Latitude},{d.Gps.Longitude}" : "NULL" )}, asset_id='{d.AssetId}'");

                timelineData.Add(new TimelineItem
                {
                    Index = i,
                    No = i + 1,
                    Timestamp = dt,
                    Camera = cameraName
                });

                double? resolvedLat = null;
                double? resolvedLng = null;

                // Priority 1: Use GPS from server detection
                if (d.Gps != null && (d.Gps.Latitude != 0 || d.Gps.Longitude != 0))
                {
                    resolvedLat = d.Gps.Latitude;
                    resolvedLng = d.Gps.Longitude;

                    // Auto-detect swapped lat/lng: valid latitude must be [-90, 90]
                    // If "latitude" > 90, it's actually a longitude value (backend returned them swapped)
                    if (resolvedLat.Value > 90 || resolvedLat.Value < -90)
                    {
                        LoggerManager.LogDebug($"[VAISearch] Detection #{i}: Swapping lat/lng (server returned lat={resolvedLat}, lng={resolvedLng} — lat out of range)");
                        var temp = resolvedLat;
                        resolvedLat = resolvedLng;
                        resolvedLng = temp;
                    }

                    gpsFromServer++;
                }
                else
                {
                    // Priority 2: Fallback to camera GPS from GlobalSystem (all cameras)
                    var cam = FindCameraByIdentifier(d.Camera, allCameras);
                    if (cam == null)
                    {
                        // Priority 3: Fallback to selected cameras
                        cam = FindCameraByIdentifier(d.Camera, _selectedCameras);
                    }

                    if (cam != null && cam.Latitude.HasValue && cam.Longitude.HasValue 
                        && (cam.Latitude.Value != 0 || cam.Longitude.Value != 0))
                    {
                        resolvedLat = cam.Latitude.Value;
                        resolvedLng = cam.Longitude.Value;
                        gpsFromFallback++;
                        LoggerManager.LogDebug($"[VAISearch] Detection #{i} GPS resolved from camera '{cam.name}': {resolvedLat},{resolvedLng}");
                    }
                    else
                    {
                        gpsSkipped++;
                        LoggerManager.LogDebug($"[VAISearch] Detection #{i} SKIPPED: no GPS available for '{d.Camera}'");
                    }
                }

                if (resolvedLat.HasValue && resolvedLng.HasValue)
                {
                    mapPoints.Add(new MapPoint
                    {
                        Index = i,
                        Lat = resolvedLat.Value,
                        Lng = resolvedLng.Value,
                        Popup = $"Camera: {cameraName}<br/>Time: {timeStr}",
                        Camera = cameraName,
                        Timestamp = timeStr,
                        Time = timeStr,
                        Id = objId,
                        Desc = cameraName,
                        ImageUrl = "" // Will be resolved below
                    });
                }
            }

            // Batch-fetch image URLs for all detections that have asset_id
            _viewVAISearch.AppendStatus("Đang tải ảnh...", System.Windows.Media.Colors.LightBlue);
            var imageUrlMap = new Dictionary<int, string>(); // index -> imageUrl
            var fetchTasks = new List<Task>();
            foreach (var mp in mapPoints)
            {
                var detection = dto.Detections.ElementAtOrDefault(mp.Index);
                if (detection != null && !string.IsNullOrEmpty(detection.AssetId))
                {
                    int idx = mp.Index;
                    string assetId = detection.AssetId;
                    fetchTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            string url = await ApiManager.Instance.GetAssetAccessUrlAsync(assetId);
                            if (!string.IsNullOrEmpty(url))
                            {
                                lock (imageUrlMap) { imageUrlMap[idx] = url; }
                            }
                        }
                        catch { }
                    }));
                }
            }

            if (fetchTasks.Count > 0)
            {
                await Task.WhenAll(fetchTasks);
            }

            // Apply fetched image URLs to map points
            foreach (var mp in mapPoints)
            {
                if (imageUrlMap.TryGetValue(mp.Index, out var url))
                {
                    mp.ImageUrl = url;
                }
            }

            _lastMapPoints = mapPoints;
            TimelineList.ItemsSource = timelineData;

            LoggerManager.LogInfo($"[VAISearch] ApplyTrajectoryToUi: detections={dto.Detections.Count}, mapPoints={mapPoints.Count} (server={gpsFromServer}, fallback={gpsFromFallback}, skipped={gpsSkipped})");

            if (mapPoints.Count == 0 && dto.Detections.Count > 0)
            {
                _viewVAISearch.AppendStatus($"⚠ Tìm thấy {dto.Detections.Count} kết quả nhưng không có tọa độ GPS để hiển thị trên bản đồ.", System.Windows.Media.Colors.Orange);
                LoggerManager.LogWarn($"[VAISearch] All {dto.Detections.Count} detections have no GPS data. Map will be empty.");
            }
            else
            {
                _viewVAISearch.AppendStatus($"Hiển thị {mapPoints.Count}/{dto.Detections.Count} điểm trên bản đồ.", System.Windows.Media.Colors.LightGreen);
            }

            // Build trajectory data for map
            var trajectoryData = new
            {
                id = objId,
                total = mapPoints.Count,
                start = timelineData.FirstOrDefault()?.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") ?? "—",
                end = timelineData.LastOrDefault()?.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") ?? "—",
                points = mapPoints.Select(p => new
                {
                    lat = p.Lat,
                    lng = p.Lng,
                    popup = p.Popup,
                    index = p.Index,
                    camera = p.Camera,
                    timestamp = p.Timestamp,
                    time = p.Time,
                    id = p.Id,
                    desc = p.Desc,
                    address = p.Desc,
                    imageUrl = !string.IsNullOrEmpty(p.ImageUrl) ? p.ImageUrl : (string)null,
                    image = !string.IsNullOrEmpty(p.ImageUrl) ? p.ImageUrl : (string)null
                }).ToList()
            };

            TrajectoryMap.ShowTrajectory(trajectoryData);
        }

        /// <summary>
        /// Find a camera by its identifier (ID, name, code, or long_Name) from a list of cameras.
        /// </summary>
        private models.Camera FindCameraByIdentifier(string cameraIdentifier, List<models.Camera> cameras)
        {
            if (string.IsNullOrEmpty(cameraIdentifier) || cameras == null || cameras.Count == 0)
                return null;

            return cameras.FirstOrDefault(c =>
                string.Equals(c.camID, cameraIdentifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.name, cameraIdentifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.long_Name, cameraIdentifier, StringComparison.OrdinalIgnoreCase));
        }

        private async void TimelineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(TimelineList.SelectedItem is TimelineItem item)) return;
            TrajectoryMap.FocusPoint(item.Index);

            // Fetch and show image if asset_id exists
            var detection = _lastDetections?.ElementAtOrDefault(item.Index);
            if (detection != null && !string.IsNullOrEmpty(detection.AssetId))
            {
                string imageUrl = await ApiManager.Instance.GetAssetAccessUrlAsync(detection.AssetId);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    try
                    {
                        ObjectAvatar.Source = new BitmapImage(new Uri(imageUrl));
                        ObjectAvatar.Margin = new Thickness(0);
                        ObjectAvatar.Opacity = 1.0;
                    }
                    catch { ResetAvatar(); }
                }
                else ResetAvatar();
            }
            else ResetAvatar();
        }


        private void ObjectAvatar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Only expand if it's not the default placeholder (default has Margin=12)
            if (ObjectAvatar.Margin.Top == 0 && ObjectAvatar.Source != null)
            {
                LargeImage.Source = ObjectAvatar.Source;
                ImageOverlay.Visibility = Visibility.Visible;
                TrajectoryMap.Visibility = Visibility.Hidden; // Fix WebView2 airspace issue
            }
        }

        private void CloseOverlay_Click(object sender, RoutedEventArgs e)
        {
            ImageOverlay.Visibility = Visibility.Collapsed;
            TrajectoryMap.Visibility = Visibility.Visible;
        }

        private void Overlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Close when clicking the background
            ImageOverlay.Visibility = Visibility.Collapsed;
            TrajectoryMap.Visibility = Visibility.Visible;
        }
    }
}
