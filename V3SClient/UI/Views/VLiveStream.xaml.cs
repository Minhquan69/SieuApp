using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GLib;
using GMap.NET;
using V3SClient.libs;
using V3SClient.models;
using V3SClient.UI.Pages;
using V3SClient.ucs;
using V3SClient.viewModels;
using V3SClient.window;
using Task = System.Threading.Tasks.Task;

namespace V3SClient.UI.Views
{
    public enum LayoutPreset
    {
        layout_1x1,
        layout_2x2,
        layout_6_1big,
        layout_3x3,
        layout_15_1big,
        layout_6x6,
        layout_custom
    }
    /// <summary>
    /// Interaction logic for live_page.xaml
    /// </summary>
    public partial class VLiveStream : Page, IDisposable
    {
        //private RightPlayback logPage { get; set; }
        //Jack 05-10-2025 add Layout camere
        private (int row, int col)? _selectedCell = null;
        public event Action<(int row, int col)> CameraSelected;
        public event Action<ViewCamera> CamDrop;

        private double _lastRightMenuWidth = 400;

        public int Rows { get; set; } = 5;
        public int Cols { get; set; } = 6;

        private System.Timers.Timer _timer;
        Dictionary<string, GMap.NET.PointLatLng> _gpsBuffer { get; set; } = new Dictionary<string, GMap.NET.PointLatLng>();

        public event EventHandler<Dictionary<string, GMap.NET.PointLatLng>> ForwardGPSBuffer;

        public bool IsAIMode { get; set; } = false;

        public ObservableCollection<viewModels.VMTalkGroup> CamGroupList { get; set; } =
            new ObservableCollection<viewModels.VMTalkGroup>();

        public ObservableCollection<models.Camera> CameraList { get; set; } =
            new ObservableCollection<models.Camera>();
        public VMTalkGroup Selected_Voice_Group { get; set; }
        private readonly bool _talkOptionEnabled;

        public LeftMenu leftMenu { get; set; }
        private SystemAndMap _systemAndMap { get; set; }

        public VLiveStream(
            ObservableCollection<viewModels.VMTalkGroup> cam_group_list,
            bool collapseLeftMenu = false,
            bool enableGroupTalk = false)
        {

            InitializeComponent();

            _talkOptionEnabled = enableGroupTalk;
            CamGroupList = cam_group_list ?? new ObservableCollection<viewModels.VMTalkGroup>();
            CamGroupList.CollectionChanged += CamGroupList_CollectionChanged;
            UpdateTalkVisibility();
            for (int i = 0; i < CamGroupList.Count; i++)
            {
                CamGroupList[i].AllowSelecting = Visibility.Collapsed;
                for (int j = 0; j < CamGroupList[i].Cameras.Count; j++)
                    CamGroupList[i].Cameras[j].AllowSelecting = Visibility.Collapsed;
            }

            leftMenu = new LeftMenu(CamGroupList, heightZoneCameraList: 300,true,true,true,true);
            leftMenu.IsSettingButton_Visible = true;
            leftMenu.SetMenuCollapsed(collapseLeftMenu);
            frameLeftMenu.Content = leftMenu;

            _systemAndMap = new SystemAndMap(CamGroupList);

            leftMenu.frameBottom.Content = _systemAndMap;
            if (_talkOptionEnabled)
                leftMenu.Event_Selected_Voice_Group_Changed += Selectd_Voice_Group_Changed;

            leftMenu.Event_Nodes_Camera_Selected_Changed += Node_Selectd_Camera_Changed;
            leftMenu.Event_AIMode_Changed += LeftMenu_Event_AIMode_Changed;

            // Khởi tạo RightMenu với ucDocumentDashboard
            //var rightMenu = new UI.Pages.RightMenu(400);
            //rightMenu.AddContentToRightMenu(new ucs.ucDocumentDashboard());
            //frmRightSide.Content = rightMenu;

            //// Mặc định RightMenu được cấu hình ẩn nội dung, nên đặt cột và splitter là Auto/0.
            //colRightMenu.Width = GridLength.Auto;
            //colRightSplitter.Width = new GridLength(0);

            //rightMenu.MenuToggled += (s, e) =>
            //{
            //    if (rightMenu.gridContent.Visibility == Visibility.Collapsed)
            //    {
            //        if (colRightMenu.Width.IsAbsolute)
            //        {
            //            _lastRightMenuWidth = colRightMenu.Width.Value;
            //        }
            //        colRightSplitter.Width = new GridLength(0);
            //        colRightMenu.Width = GridLength.Auto;
            //    }
            //    else
            //    {
            //        colRightSplitter.Width = new GridLength(5);
            //        colRightMenu.Width = new GridLength(_lastRightMenuWidth);
            //    }
            //};

            Loaded += VLiveStream_Loaded;
            Unloaded += VLiveStream_Unloaded;

        }

        private void CamGroupList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateTalkVisibility();
        }

        private void UpdateTalkVisibility()
        {
            bool hasTalkGroup = CamGroupList != null && CamGroupList.Any(group =>
                group != null &&
                !string.IsNullOrWhiteSpace(group.groupID) &&
                !string.Equals(group.groupID, "None", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(group.name, "None", StringComparison.OrdinalIgnoreCase));

            if (TalkStatusBorder != null)
                TalkStatusBorder.Visibility = _talkOptionEnabled && hasTalkGroup
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void Node_Selectd_Camera_Changed(object sender, List<Camera> e)
        {
            if (e == null || e.Count == 0) return;

            UpdateSelectedCameras(e);
        }

        private void VLiveStream_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            gridCameraList.Dispatcher.Invoke(() =>
            {
                foreach (var cam in gridCameraList.Children)
                    Task.Run(() =>
                    {
                        ViewCamera viewcamera = cam as ViewCamera;
                        if (viewcamera != null)
                        {
                            SetCameraPlayingState(viewcamera.Camera.camID, false);
                            viewcamera.Player?.Dispose();
                        }
                    });
            });
            GlobalUserInfo.Instance.SetAllowSelectingForAllCams(Visibility.Visible);
        }

        private async void VLiveStream_Loaded(object sender, RoutedEventArgs e)
        {
            LoggerManager.LogInfo("Khởi tạo phiên xem trực tiếp (Live View).");
            Task task1 = Task.Run(() =>
            {
                CameraList.Clear();
                foreach (var cam_group in CamGroupList)
                    foreach (var cam in cam_group.Cameras)
                        CameraList.Add(cam);
            });

            Task task2 = Task.Run(() =>
            {
                int rows, cols;
                int num_cam = CamGroupList.Sum(x => x.Cameras.Count);
                libs.GlobalClass.FindRowsAndCols(num_cam, out rows, out cols);
                Rows = rows; Cols = cols;
            });

            await Task.WhenAll(task1, task2);
            //ShowCameras(rows: Rows, cols: Cols);
            var presetInfo = LoadLayoutPreset();
            LoggerManager.LogDebug($"Nạp layout preset: {presetInfo.preset}");
            if (presetInfo.preset == LayoutPreset.layout_custom && presetInfo.customCount > 0)
            {
                ShowCamerasCustom(presetInfo.customCount);
            }
            else
            {
                ShowCamerasPreset(presetInfo.preset);
            }

            if (_timer != null)
                _timer.Stop();

            _timer = new System.Timers.Timer(1000);

            _timer.Elapsed += (s, smg) =>
            {
                ForwardGPSBuffer?.Invoke(this, _gpsBuffer);
                _systemAndMap.UpdateGPS(_gpsBuffer);
            };
            _timer.Start();
            //30-06 Vr add Load AI meta
            LoadMetaAIResultsFromStorage();
            //Disable checkbox CamInfo
            GlobalUserInfo.Instance.SetAllowSelectingForAllCams(Visibility.Collapsed);
        }

        private void LoadMetaAIResultsFromStorage()
        {
            try
            {
                var storedResults = MetaAIResultStorage.Instance.GlobalResults;

                if (storedResults != null && storedResults.Count > 0)
                {
                    _systemAndMap.MetaAIResult.ShowAIResult(storedResults);
                }
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi khi nạp dữ liệu AI từ bộ nhớ tạm");
                MessageBox.Show("Có lỗi xảy ra khi tải danh sách nhận diện đã lưu. Dữ liệu mới vẫn sẽ được cập nhật bình thường.",
                                "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private void Selectd_Voice_Group_Changed(object sender, VMTalkGroup e)
        {
            if (!_talkOptionEnabled || TalkStatusBorder.Visibility != Visibility.Visible || e == null)
                return;

            Selected_Voice_Group = e;
            txtTalk2Group.Text = string.Format("{0} ({1})", Selected_Voice_Group.name,
                Selected_Voice_Group.Cameras.Count);

            // Cập nhật ID nhóm đang chọn toàn cục
            GlobalUserInfo.Instance.ActiveTalkGroupId = e.groupID;

            if (Selected_Voice_Group.name == "" || Selected_Voice_Group.name == "None")
            {
                LoggerManager.LogDebug("Hủy chọn nhóm đàm thoại (None).");
                ToastManager.ShowToast("Cảnh báo", "Nhóm None không sử dụng đàm thoại", ToastType.Warning);
                return;
            }

            // Update server (Now using REST API instead of Redis)
            LoggerManager.LogInfo($"Bắt đầu đàm thoại với nhóm: {Selected_Voice_Group.name} (ID: {e.groupID})");
            ToastManager.ShowToast("Thông báo", $"Đàm thoại với nhóm {Selected_Voice_Group.name}", ToastType.Info);

            // Cập nhật nhóm cho Commander (Bodycam/Radio của người dùng hiện tại)
            if (!string.IsNullOrEmpty(GlobalUserInfo.Instance.ActiveCommanderID))
            {
                Task.Run(async () =>
                {
                    bool success = await ApiManager.Instance.ChangeCameraTalkGroupAsync(GlobalUserInfo.Instance.ActiveCommanderID, e.groupID);
                    if (success)
                    {
                        LoggerManager.LogInfo($"Đã chuyển nhóm đàm thoại cho Commander {GlobalUserInfo.Instance.ActiveCommanderID} sang nhóm {e.groupID}");
                    }
                    else
                    {
                        LoggerManager.LogWarn($"Không thể chuyển nhóm đàm thoại cho Commander {GlobalUserInfo.Instance.ActiveCommanderID}");
                    }
                });
            }
        }

        public void SetCameraWarningById(string camId,int val)
        {
            try
            {
                foreach (var child in gridCameraList.Children)
                {
                    if (child is ViewCamera viewCam && viewCam.Camera.camID == camId)
                    {
                        viewCam.CameraWarning = val;
                        //Task.Delay(2000).ContinueWith(t =>
                        //{
                        //    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        //    {
                        //        try
                        //        {
                        //            if (viewCam.IsLoaded)
                        //                viewCam.CameraWarning = 0;
                        //        }
                        //        catch (Exception ex)
                        //        {
                        //            Debug.WriteLine($"[Warning reset failed] {ex.Message}");
                        //        }
                        //    }));
                        //});

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SetCameraWarningById - outer error] {ex.Message}");
            }
        }
        private void ShowCameras(int rows, int cols)
        {
            gridCameraList.RowDefinitions.Clear();
            gridCameraList.ColumnDefinitions.Clear();

            for (int i = 0; i < rows; i++)
            {
                RowDefinition row = new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star)
                };
                gridCameraList.RowDefinitions.Add(row);
            }

            for (int j = 0; j < cols; j++)
            {
                ColumnDefinition col = new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                };
                gridCameraList.ColumnDefinitions.Add(col);
            }


            int camIdx = 0;
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (camIdx >= CameraList.Count)
                        return;
                    ViewCamera viewCam = new ViewCamera(CameraList[camIdx]);
                    viewCam.IsAIViewMode = this.IsAIMode;
                   
                    Grid.SetColumn(viewCam, j);
                    Grid.SetRow(viewCam, i);
                    gridCameraList.Children.Add(viewCam);
                    camIdx++;
                    viewCam.SendGPS += GpsReceiver;
                    viewCam.SendMetaAIResult += ShowAIResult;
                    viewCam.StreamModeChanged += (s, mode) => SetCameraStreamMode(viewCam.Camera.camID, mode);
                }
            }


        }

        private void ShowAIResult(object sender, List<MetaAIResult> aiResults)
        {
            //logPage.vAIResultLog.ShowAIResult(aiResults);
            _systemAndMap.MetaAIResult.ShowAIResult(aiResults);
            MetaAIResultStorage.Instance.AddResults(aiResults);
        }

        private void GpsReceiver(object sender, PointLatLng gps)
        {
            string camid = sender as string;
            _gpsBuffer[camid] = gps;

        }

        private void InitialPipeline()
        {
            foreach (ViewCamera viewCamera in gridCameraList.Children)
            {
                Task.Run(() =>
                {
                    viewCamera.InitPipeline();
                });
            }
        }

        public void Dispose()
        {
            CamGroupList.CollectionChanged -= CamGroupList_CollectionChanged;
            if (_talkOptionEnabled && leftMenu != null)
                leftMenu.Event_Selected_Voice_Group_Changed -= Selectd_Voice_Group_Changed;

            foreach (var cam in gridCameraList.Children)
            {
                ViewCamera viewcamera = cam as ViewCamera;
                System.Threading.Tasks.Task.Run(() =>
                {
                    viewcamera?.Player.Dispose();
                });
            }
            //
        }


        #region Dev custom grid
        private void ClearGridAndDefs()
        {
            var existingVCs = gridCameraList.Children.OfType<ViewCamera>().ToList();
            foreach (var vc in existingVCs)
            {
                SetCameraPlayingState(vc.Camera.camID, false);
            }
            gridCameraList.Children.Clear();
            gridCameraList.RowDefinitions.Clear();
            gridCameraList.ColumnDefinitions.Clear();
        }
        public void ShowCamerasPreset(LayoutPreset preset)
        {
            LoggerManager.LogDebug($"Thay đổi layout sang: {preset}");
            // Ensure called on UI thread
            Dispatcher.Invoke(() =>
            {
                ClearGridAndDefs();

                // Determine grid size and placement strategy
                int rows = 1, cols = 1;
                List<(int row, int col, int rowSpan, int colSpan)> placements = new List<(int row, int col, int rowSpan, int colSpan)>();

                switch (preset)
                {
                    case LayoutPreset.layout_1x1:
                        rows = 1; cols = 1;
                        // single cell
                        placements.Add((0, 0, 1, 1));
                        break;

                    case LayoutPreset.layout_2x2:
                        rows = 2; cols = 2;
                        for (int r = 0; r < 2; r++)
                            for (int c = 0; c < 2; c++)
                                placements.Add((r, c, 1, 1));
                        break;

                    case LayoutPreset.layout_6_1big:
                        // Use 3x3 grid. Big occupies top-left 2x2, other five are small:
                        rows = 3; cols = 3;
                        placements.Add((0, 0, 2, 2)); // big (spans 2x2)
                                                      // small positions ordered: top-right, mid-right, bottom-left, bottom-mid, bottom-right
                        placements.Add((0, 2, 1, 1));
                        placements.Add((1, 2, 1, 1));
                        placements.Add((2, 0, 1, 1));
                        placements.Add((2, 1, 1, 1));
                        placements.Add((2, 2, 1, 1));
                        break;

                    case LayoutPreset.layout_3x3:
                        rows = 3; cols = 3;
                        for (int r = 0; r < 3; r++)
                            for (int c = 0; c < 3; c++)
                                placements.Add((r, c, 1, 1));
                        break;

                    case LayoutPreset.layout_15_1big:
                        // Grid 5x5: center big spans 3x3, pick 12 border slots for small cams
                        rows = 5; cols = 5;
                        // center big at (1,1) span 3x3
                        placements.Add((1, 1, 3, 3)); // big center
                                                      // border positions (clockwise from top-left) -> we'll collect them and pick up to 12
                        var border = new List<(int r, int c)>();
                        for (int c = 0; c < 5; c++) border.Add((0, c));           // top row
                        for (int r = 1; r < 5; r++) border.Add((r, 4));           // right col
                        for (int c = 3; c >= 0; c--) border.Add((4, c));         // bottom row
                        for (int r = 3; r >= 1; r--) border.Add((r, 0));         // left col
                                                                                 // remove any border positions that would fall inside the center 3x3 (shouldn't happen)
                                                                                 // choose first 12 border slots
                        int needSmall = 16;
                        foreach (var pos in border)
                        {
                            if (needSmall <= 0) break;
                            // skip any pos that is inside center 3x3 (rows 1..3, cols 1..3)
                            if (pos.r >= 1 && pos.r <= 3 && pos.c >= 1 && pos.c <= 3)
                                continue;
                            placements.Add((pos.r, pos.c, 1, 1));
                            needSmall--;
                        }
                        break;

                    case LayoutPreset.layout_6x6:
                        // Use rows x cols such that rows*cols >= 32, choose a balanced grid
                        // We'll use 4 rows x 8 cols = 32
                        rows = 6; cols = 6;
                        for (int r = 0; r < rows; r++)
                            for (int c = 0; c < cols; c++)
                                placements.Add((r, c, 1, 1));
                        break;

                    default:
                        // fallback to equal grid sized on camera count
                        int count = CameraList.Count;
                        libs.GlobalClass.FindRowsAndCols(count, out rows, out cols);
                        for (int r = 0; r < rows; r++)
                            for (int c = 0; c < cols; c++)
                                placements.Add((r, c, 1, 1));
                        break;
                }

                // Create RowDefinitions & ColumnDefinitions
                for (int r = 0; r < rows; r++)
                    gridCameraList.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                for (int c = 0; c < cols; c++)
                    gridCameraList.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });


                foreach (var p in placements)
                {
                    var border = new Border
                    {
                        Background = ((p.row + p.col) % 2 == 0)
                            ? new SolidColorBrush(Color.FromRgb(134, 132, 132)) // trắng nhạt
                            : new SolidColorBrush(Color.FromRgb(132, 132, 132)), // xám nhạt
                        BorderBrush = new SolidColorBrush(Color.FromRgb(30, 23, 19)),
                        BorderThickness = new Thickness(1),
                        Tag = (p.row, p.col)
                    };
                    border.MouseLeftButtonUp += Border_Click;
                    border.AllowDrop = true;
                    border.DragEnter += Border_DragEnter;
                    border.Drop += Border_Drop;


                    var img = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/base_camera.png")),
                        Stretch = Stretch.Uniform,
                        Opacity = 0.7, 
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Width = 32, 
                        Height = 32
                    };
                    border.Child = img;
                    Grid.SetRow(border, p.row);
                    Grid.SetColumn(border, p.col);
                    if (p.rowSpan > 1) Grid.SetRowSpan(border, p.rowSpan);
                    if (p.colSpan > 1) Grid.SetColumnSpan(border, p.colSpan);

                    gridCameraList.Children.Add(border);
                }
                UpdateCurrentIconLayout(preset);
            });
        }
        private void UpdateSelectedCameras(List<Camera> selectedCameras)
        {
            Dispatcher.Invoke(() =>
            {
                // 1. Lấy danh sách ViewCamera hiện có
                var existingVCs = gridCameraList.Children.OfType<ViewCamera>().ToList();

                // 2. Tạo map CameraID -> ViewCamera để dễ tìm và dispose
                var existingVCMap = existingVCs.ToDictionary(vc => vc.Camera.camID, vc => vc);

                // 3. Lấy danh sách các border trong grid (các vị trí layout)
                var placements = gridCameraList.Children.OfType<Border>()
                    .Select(b => new
                    {
                        Border = b,
                        Row = Grid.GetRow(b),
                        Col = Grid.GetColumn(b),
                        RowSpan = Grid.GetRowSpan(b),
                        ColSpan = Grid.GetColumnSpan(b)
                    })
                    .ToList();

                // 4. Nếu chỉ có 1 camera và có _selectedCell -> add đúng vào ô đó
                if (_selectedCell.HasValue && selectedCameras.Count == 1)
                {
                    var targetCell = _selectedCell.Value;
                    var cam = selectedCameras[0];

                    LoggerManager.LogDebug($"Thêm camera {cam.name} vào ô [{targetCell.row}, {targetCell.col}]");

                    // Nếu camera đang active ở ô khác thì remove nó
                    if (existingVCMap.TryGetValue(cam.camID, out var existingVC))
                    {
                        var p = existingVC.Player;
                        Task.Run(() => p?.Dispose());
                        gridCameraList.Children.Remove(existingVC);
                    }

                    // tìm Border tại cell đang chọn để lấy span
                    var targetBorder = placements.FirstOrDefault(p =>
                        p.Row == targetCell.row && p.Col == targetCell.col);

                    int rowSpan = targetBorder?.RowSpan ?? 1;
                    int colSpan = targetBorder?.ColSpan ?? 1;

                    // Xóa ViewCamera cũ trong cell (nếu có)
                    var oldVCInCell = existingVCs.FirstOrDefault(v =>
                        Grid.GetRow(v) == targetCell.row && Grid.GetColumn(v) == targetCell.col);
                    if (oldVCInCell != null)
                    {
                        var p = oldVCInCell.Player;
                        Task.Run(() => p?.Dispose());
                        gridCameraList.Children.Remove(oldVCInCell);
                        SetCameraPlayingState(oldVCInCell.Camera.camID, false);
                    }

                    // Add camera vào cell đang chọn
                    var vc = new ViewCamera(cam);
                    vc.IsAIViewMode = this.IsAIMode;
                    vc.OriginalRowSpan = rowSpan;
                    vc.OriginalColSpan = colSpan;

                    vc.SendGPS += GpsReceiver;
                    vc.SendMetaAIResult += ShowAIResult;
                    vc.StreamModeChanged += (s, mode) => SetCameraStreamMode(vc.Camera.camID, mode);
                    // Thêm EventClosed
                    vc.EventClosed += (s, e) =>
                    {
                        RemoveCameraFromCell(vc); // Dispose và remove khỏi Grid
                    };


                    Grid.SetRow(vc, targetCell.row);
                    Grid.SetColumn(vc, targetCell.col);
                    Grid.SetRowSpan(vc, rowSpan);
                    Grid.SetColumnSpan(vc, colSpan);

                    gridCameraList.Children.Add(vc);
                    SetCameraPlayingState(cam.camID, true);
                }
                else
                {
                    LoggerManager.LogDebug($"Thêm {selectedCameras.Count} camera vào lưới.");
                    // 5. Add nhiều camera -> bỏ qua _selectedCell, chỉ add vào ô trống
                    var usedCells = gridCameraList.Children.OfType<ViewCamera>()
                        .Select(v => (Grid.GetRow(v), Grid.GetColumn(v)))
                        .ToHashSet();

                    int camIdx = 0;
                    foreach (var p in placements)
                    {
                        if (camIdx >= selectedCameras.Count)
                            break;

                        // Nếu ô đang có camera rồi -> bỏ qua
                        if (usedCells.Contains((p.Row, p.Col)))
                            continue;

                        var cam = selectedCameras[camIdx];

                        // Nếu camera này đang active ở ô khác -> remove
                        if (existingVCMap.TryGetValue(cam.camID, out var existingVC))
                        {
                            var pl = existingVC.Player;
                            Task.Run(() => pl?.Dispose());
                            gridCameraList.Children.Remove(existingVC);
                        }

                        var vc = new ViewCamera(cam);
                        vc.IsAIViewMode = this.IsAIMode;
                        vc.OriginalRowSpan = p.RowSpan;
                        vc.OriginalColSpan = p.ColSpan;
                        vc.SendGPS += GpsReceiver;
                        vc.SendMetaAIResult += ShowAIResult;
                        vc.StreamModeChanged += (s, mode) => SetCameraStreamMode(vc.Camera.camID, mode);
                        // Thêm EventClosed
                        vc.EventClosed += (s, e) =>
                        {
                            RemoveCameraFromCell(vc); // Dispose và remove khỏi Grid
                        };
                
                        Grid.SetRow(vc, p.Row);
                        Grid.SetColumn(vc, p.Col);
                        if (p.RowSpan > 1) Grid.SetRowSpan(vc, p.RowSpan);
                        if (p.ColSpan > 1) Grid.SetColumnSpan(vc, p.ColSpan);

                        gridCameraList.Children.Add(vc);
                        SetCameraPlayingState(cam.camID, true);
                        usedCells.Add((p.Row, p.Col));
                        camIdx++;
                    }
                }

                // 6. Giữ lại highlight ô đang chọn
                if (_selectedCell.HasValue)
                {
                    var selectedBorder = gridCameraList.Children
                        .OfType<Border>()
                        .FirstOrDefault(b =>
                            Grid.GetRow(b) == _selectedCell.Value.row &&
                            Grid.GetColumn(b) == _selectedCell.Value.col);

                    if (selectedBorder != null)
                    {
                        selectedBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 23, 19));
                        selectedBorder.BorderThickness = new Thickness(2);
                    }
                }
            });
        }
        private void RemoveCameraFromCell(ViewCamera vc)
        {
            if (vc == null) return;

            // Lấy vị trí cell
            int row = Grid.GetRow(vc);
            int col = Grid.GetColumn(vc);

            // Dispose player
            var playerToDispose = vc.Player;
            Task.Run(() => playerToDispose?.Dispose());

            // Remove khỏi Grid
            gridCameraList.Children.Remove(vc);
            SetCameraPlayingState(vc.Camera.camID, false);

            // Hiển thị lại icon mặc định trong border nếu có
            var targetBorder = gridCameraList.Children
                .OfType<Border>()
                .FirstOrDefault(b => Grid.GetRow(b) == row && Grid.GetColumn(b) == col);

            if (targetBorder?.Child != null)
            {
                targetBorder.Child.Visibility = Visibility.Visible;
            }
        }

        private void MoveOrSwapCamera(ViewCamera vc, (int row, int col) targetCell)
        {
            var targetVC = gridCameraList.Children
                .OfType<ViewCamera>()
                .FirstOrDefault(v => Grid.GetRow(v) == targetCell.row && Grid.GetColumn(v) == targetCell.col);

            if (targetVC != null)
            {
                // swap vị trí
                int oldRow = Grid.GetRow(vc);
                int oldCol = Grid.GetColumn(vc);

                Grid.SetRow(vc, targetCell.row);
                Grid.SetColumn(vc, targetCell.col);

                Grid.SetRow(targetVC, oldRow);
                Grid.SetColumn(targetVC, oldCol);
            }
            else
            {
                // move sang ô trống
                Grid.SetRow(vc, targetCell.row);
                Grid.SetColumn(vc, targetCell.col);
            }
        }




        private void Border_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;

            // Reset màu tất cả ô
            foreach (var b in gridCameraList.Children.OfType<Border>())
            {
                b.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 23, 19));
                b.BorderThickness = new Thickness(1);
            }

            // Highlight ô đang chọn
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(177, 61, 3));
            border.BorderThickness = new Thickness(2);

            // Lưu vị trí
            _selectedCell = ((int row, int col))border.Tag;
        }

        private void Border_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("ViewCamera"))
                e.Effects = DragDropEffects.None;
        }

        private void Border_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("ViewCamera")) return;

            ViewCamera draggedVC = e.Data.GetData("ViewCamera") as ViewCamera;
            Border targetBorder = sender as Border;

            if (draggedVC == null || targetBorder == null) return;

            var targetCell = ((int row, int col))targetBorder.Tag;

            // Swap hoặc move
            MoveOrSwapCamera(draggedVC, targetCell);
        }

        private void ActionIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var image = sender as Image;
            if (image != null && image.ContextMenu != null)
            {
                image.ContextMenu.PlacementTarget = image;
                image.ContextMenu.IsOpen = true;
            }
        }

        private void MenuAction_ConnectAll_Click(object sender, RoutedEventArgs e)
        {
            var cameras = gridCameraList.Children.OfType<ViewCamera>().ToList();
            foreach (var vc in cameras)
            {
                // Sử dụng ConnectedCamera() để hệ thống tự xử lý logic RTSP url, InitPipeline và SetState(Playing)
                vc.ConnectedCamera();
                SetCameraPlayingState(vc.Camera.camID, true);
            }
        }

        private void MenuAction_DisconnectAll_Click(object sender, RoutedEventArgs e)
        {
            var cameras = gridCameraList.Children.OfType<ViewCamera>().ToList();
            foreach (var vc in cameras)
            {
                Task.Run(() => vc.Player?.Dispose());
                SetCameraPlayingState(vc.Camera.camID, false);
            }
        }

        private void MenuAction_ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var cameras = gridCameraList.Children.OfType<ViewCamera>().ToList();
            foreach (var vc in cameras)
            {
                RemoveCameraFromCell(vc);
            }
        }

        private void MenuLayout_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menu && menu.Tag is LayoutPreset preset)
            {
                if (preset == LayoutPreset.layout_custom)
                {
                    V3SClient.ucs.InputDialog dialog = new V3SClient.ucs.InputDialog("Nhập số lượng Camera", "4", V3SClient.ucs.InputType.Integer);
                    if (dialog.ShowDialog() == true)
                    {
                        if (int.TryParse(dialog.InputText, out int count) && count > 0)
                        {
                            if (count > 120)
                            {
                                MessageBox.Show("Số lượng ô tối đa cho phép là 120. Hệ thống sẽ giới hạn ở mức 120 ô.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                                count = 120;
                            }
                            _selectedCell = null;
                            ShowCamerasCustom(count);
                            SaveLayoutPreset(preset, count);
                        }
                    }
                }
                else
                {
                    _selectedCell = null;
                    ShowCamerasPreset(preset);
                    SaveLayoutPreset(preset);
                }
            }
        }
        private void UpdateCurrentIconLayout(LayoutPreset preset)
        {
            switch (preset)
            {
                case LayoutPreset.layout_1x1:
                    imgLayoutIcon.Source = new BitmapImage(new Uri("/images/layout/layout_1.png", UriKind.Relative));
                    break;
                case LayoutPreset.layout_2x2:
                    imgLayoutIcon.Source = new BitmapImage(new Uri("/images/layout/layout_2x2.png", UriKind.Relative));
                    break;
                case LayoutPreset.layout_6_1big:
                    imgLayoutIcon.Source = new BitmapImage(new Uri("/images/layout/layout_6_1big.png", UriKind.Relative));
                    break;
                case LayoutPreset.layout_3x3:
                    imgLayoutIcon.Source = new BitmapImage(new Uri("/images/layout/layout_3x3.png", UriKind.Relative));
                    break;
                case LayoutPreset.layout_15_1big:
                    imgLayoutIcon.Source = new BitmapImage(new Uri("/images/layout/layout_15_1big.png", UriKind.Relative));
                    break;
                case LayoutPreset.layout_6x6:
                    imgLayoutIcon.Source = new BitmapImage(new Uri("/images/layout/layout_6x6.png", UriKind.Relative));
                    break;
                case LayoutPreset.layout_custom:
                    imgLayoutIcon.Source = new BitmapImage(new Uri("/images/layout/layout_custom.png", UriKind.Relative));
                    break;
            }
        }

        public void ShowCamerasCustom(int count)
        {
            LoggerManager.LogDebug($"Thay đổi layout sang: Custom ({count} ô)");
            Dispatcher.Invoke(() =>
            {
                ClearGridAndDefs();

                int rows, cols;
                libs.GlobalClass.FindRowsAndCols(count, out rows, out cols);

                List<(int row, int col, int rowSpan, int colSpan)> placements = new List<(int row, int col, int rowSpan, int colSpan)>();

                int currentCount = 0;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (currentCount >= count) break;
                        placements.Add((r, c, 1, 1));
                        currentCount++;
                    }
                }

                // Create RowDefinitions & ColumnDefinitions
                for (int r = 0; r < rows; r++)
                    gridCameraList.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                for (int c = 0; c < cols; c++)
                    gridCameraList.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });


                foreach (var p in placements)
                {
                    var border = new Border
                    {
                        Background = ((p.row + p.col) % 2 == 0)
                            ? new SolidColorBrush(Color.FromRgb(134, 132, 132)) 
                            : new SolidColorBrush(Color.FromRgb(132, 132, 132)), 
                        BorderBrush = new SolidColorBrush(Color.FromRgb(30, 23, 19)),
                        BorderThickness = new Thickness(1),
                        Tag = (p.row, p.col)
                    };
                    border.MouseLeftButtonUp += Border_Click;
                    border.AllowDrop = true;
                    border.DragEnter += Border_DragEnter;
                    border.Drop += Border_Drop;

                    var img = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/base_camera.png")),
                        Stretch = Stretch.Uniform,
                        Opacity = 0.7, 
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Width = 32, 
                        Height = 32
                    };
                    border.Child = img;
                    Grid.SetRow(border, p.row);
                    Grid.SetColumn(border, p.col);
                    if (p.rowSpan > 1) Grid.SetRowSpan(border, p.rowSpan);
                    if (p.colSpan > 1) Grid.SetColumnSpan(border, p.colSpan);

                    gridCameraList.Children.Add(border);
                }
                UpdateCurrentIconLayout(LayoutPreset.layout_custom);
            });
        }
        private void SaveLayoutPreset(LayoutPreset preset, int customCount = 0)
        {
            string filePath = "layout.bin";
            if (preset == LayoutPreset.layout_custom && customCount > 0)
            {
                File.WriteAllText(filePath, $"{preset}:{customCount}");
            }
            else
            {
                File.WriteAllText(filePath, preset.ToString());
            }
        }
        private (LayoutPreset preset, int customCount) LoadLayoutPreset()
        {
            string filePath = "layout.bin";
            if (!File.Exists(filePath))
                return (LayoutPreset.layout_1x1, 0); // mặc định

            string text = File.ReadAllText(filePath).Trim();
            if (text.Contains(":"))
            {
                var parts = text.Split(':');
                if (Enum.TryParse<LayoutPreset>(parts[0], out var p) && int.TryParse(parts[1], out int count))
                {
                    return (p, count);
                }
            }
            else if (Enum.TryParse<LayoutPreset>(text, out var preset))
            {
                return (preset, 0);
            }

            return (LayoutPreset.layout_1x1, 0);
        }

        private void SetCameraPlayingState(string camId, bool isPlaying)
        {
            if (string.IsNullOrEmpty(camId)) return;

            if (!isPlaying) SetCameraStreamMode(camId, "");

            // Update models.Camera
            if (CamGroupList != null)
            {
                foreach (var group in CamGroupList)
                {
                    var cam = group.Cameras.FirstOrDefault(c => c.camID == camId);
                    if (cam != null) cam.IsPlaying = isPlaying;
                }
            }

            // Update libs.CamInfoNode
            var areaTree = GlobalUserInfo.Instance.AreaTree;
            if (areaTree != null)
            {
                foreach (var area in areaTree)
                {
                    UpdateCamInfoNodePlayingState(area, camId, isPlaying);
                }
            }
        }

        private void UpdateCamInfoNodePlayingState(AreaNode area, string camId, bool isPlaying)
        {
            if (area.Units == null) return;
            foreach (var unit in area.Units)
            {
                UpdateCamInfoNodePlayingState(unit, camId, isPlaying);
            }
        }

        private void UpdateCamInfoNodePlayingState(UnitNode unit, string camId, bool isPlaying)
        {
            if (unit.Cams != null)
            {
                var cam = unit.Cams.FirstOrDefault(c => c.CamData?.CamInfo_CamId == camId);
                if (cam != null) cam.IsPlaying = isPlaying;
            }
            if (unit.SubUnits != null)
            {
                foreach (var sub in unit.SubUnits)
                {
                    UpdateCamInfoNodePlayingState(sub, camId, isPlaying);
                }
            }
        }

        private void SetCameraStreamMode(string camId, string mode)
        {
            if (string.IsNullOrEmpty(camId)) return;

            // Update models.Camera (Group tree)
            if (CamGroupList != null)
                foreach (var group in CamGroupList)
                {
                    var cam = group.Cameras.FirstOrDefault(c => c.camID == camId);
                    if (cam != null) cam.ActiveStreamMode = mode;
                }

            // Update libs.CamInfoNode (Organization tree)
            var areaTree = GlobalUserInfo.Instance.AreaTree;
            if (areaTree != null)
                foreach (var area in areaTree)
                    UpdateCamInfoNodeStreamMode(area, camId, mode);
        }

        private void UpdateCamInfoNodeStreamMode(AreaNode area, string camId, string mode)
        {
            if (area.Units == null) return;
            foreach (var unit in area.Units)
            {
                UpdateCamInfoNodeStreamMode(unit, camId, mode);
            }
        }

        private void UpdateCamInfoNodeStreamMode(UnitNode unit, string camId, string mode)
        {
            if (unit.Cams != null)
            {
                var cam = unit.Cams.FirstOrDefault(c => c.CamData?.CamInfo_CamId == camId);
                if (cam != null) cam.ActiveStreamMode = mode;
            }
            if (unit.SubUnits != null)
            {
                foreach (var sub in unit.SubUnits)
                {
                    UpdateCamInfoNodeStreamMode(sub, camId, mode);
                }
            }
        }

        private void LeftMenu_Event_AIMode_Changed(object sender, bool isAIMode)
        {
            IsAIMode = isAIMode;

            // Copy the list to avoid collection modified exceptions if we remove items
            var camerasToProcess = gridCameraList.Children.OfType<ViewCamera>().ToList();

            foreach (var viewCam in camerasToProcess)
            {
                if (viewCam.Camera == null) continue;

                if (IsAIMode && !viewCam.Camera.HasAIStream)
                {
                    // If switching to AI mode and the camera doesn't support AI, close it
                    RemoveCameraFromCell(viewCam);
                }
                else
                {
                    // Otherwise, switch mode and restart stream to pick up correct AI/Raw RTSP link
                    viewCam.IsAIViewMode = IsAIMode;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                        viewCam.ConnectedCamera();
                    }));
                }
            }
        }

        #endregion
    }
}
