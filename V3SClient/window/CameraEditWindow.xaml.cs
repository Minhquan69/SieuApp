using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using V3SClient.libs;
using static V3SClient.libs.ApiManager;
using V3SClient.ucs.Settings.models;

namespace V3SClient.window
{
    public partial class CameraEditWindow : Window
    {
        private CamInfoModel _editingCamera;
        private bool _isEditMode;
        private List<MediaServerInfo> _mediaServers = new List<MediaServerInfo>();
        private List<GroupSelection> _groupSelections = new List<GroupSelection>();

        public class GroupSelection
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public bool IsSelected { get; set; }
        }

        public CameraEditWindow(CamInfoModel camera)
        {
            InitializeComponent();
            _editingCamera = camera;
            _isEditMode = camera != null;

            if (_isEditMode)
            {
                txtTitle.Text = "Chỉnh sửa Camera";
                btnSave.Content = "Cập nhật";
            }

            LoadMediaServers();
            LoadGroups();
            if (_isEditMode) PopulateFields();
        }

        private async void LoadGroups()
        {
            try
            {
                var groups = await ApiManager.Instance.GetCameraGroupsAsync(CancellationToken.None);
                _groupSelections = groups.Select(g => new GroupSelection
                {
                    Id = g.Id,
                    Name = g.Name,
                    IsSelected = _editingCamera?.GroupIds != null && _editingCamera.GroupIds.Contains(g.Id.ToString())
                }).ToList();
                lbGroups.ItemsSource = _groupSelections;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error loading groups: " + ex.Message);
            }
        }

        private async void LoadMediaServers()
        {
            try
            {
                _mediaServers = await ApiManager.Instance.GetMediaServersAsync(CancellationToken.None);
                cboMediaServer.ItemsSource = _mediaServers;

                if (_isEditMode && !string.IsNullOrEmpty(_editingCamera.MediaServerId))
                {
                    cboMediaServer.SelectedValue = _editingCamera.MediaServerId;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error loading media servers: " + ex.Message);
            }
        }

        private void PopulateFields()
        {
            if (_editingCamera == null) return;

            txtCameraCode.Text = _editingCamera.CameraCode;
            txtDisplayName1.Text = _editingCamera.DisplayName1;
            txtDisplayName2.Text = _editingCamera.DisplayName2;
            txtSourceIp.Text = _editingCamera.SourceIp;
            txtSourcePort.Text = _editingCamera.SourcePort?.ToString() ?? "";
            txtStreamUrl.Text = _editingCamera.SourceStreamUrl;
            txtLocationName.Text = _editingCamera.LocationName;
            txtLatitude.Text = _editingCamera.Latitude?.ToString() ?? "";
            txtLongitude.Text = _editingCamera.Longitude?.ToString() ?? "";
            txtDescription.Text = _editingCamera.Description;
            chkActive.IsChecked = _editingCamera.IsActive;

            // Select camera type
            foreach (ComboBoxItem item in cboCameraType.Items)
            {
                if ((string)item.Tag == _editingCamera.CameraType)
                {
                    cboCameraType.SelectedItem = item;
                    break;
                }
            }

            // Select codec
            foreach (ComboBoxItem item in cboCodec.Items)
            {
                if ((string)item.Tag == _editingCamera.Codec)
                {
                    cboCodec.SelectedItem = item;
                    break;
                }
            }

            // Select operation mode
            foreach (ComboBoxItem item in cboOperationMode.Items)
            {
                if ((string)item.Tag == _editingCamera.OperationMode)
                {
                    cboOperationMode.SelectedItem = item;
                    break;
                }
            }

            // Update visibility
            string camType = (_editingCamera.CameraType ?? "ip_cam");
            UpdateFieldVisibility(camType);
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDisplayName1.Text))
            {
                if ((cboCameraType.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "ip_cam" && !string.IsNullOrWhiteSpace(txtStreamUrl.Text))
                {
                    // Auto-generate name from RTSP URL
                    string url = txtStreamUrl.Text;
                    string name = "Camera_" + DateTime.Now.ToString("HHmmss");
                    try {
                        var uri = new Uri(url.Replace("rtsp://", "http://")); // Simple trick to parse IP
                        name = "Cam_" + uri.Host;
                    } catch {}
                    txtDisplayName1.Text = name;
                }
                else
                {
                    MessageBox.Show("Vui lòng nhập tên camera!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtDisplayName1.Focus();
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(txtCameraCode.Text))
            {
                if ((cboCameraType.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "ip_cam")
                {
                    txtCameraCode.Text = "IPCAM_" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
                }
                else
                {
                    MessageBox.Show("Vui lòng nhập mã định danh camera!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtCameraCode.Focus();
                    return;
                }
            }

            // Auto-parse RTSP for IP/Port and Credentials for IP Cameras
            string cameraType = (cboCameraType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ip_cam";
            if (cameraType == "ip_cam" && !string.IsNullOrWhiteSpace(txtStreamUrl.Text))
            {
                try
                {
                    string url = txtStreamUrl.Text.Trim();
                    // Uri parsing trick
                    var uri = new Uri(url.Replace("rtsp://", "http://")); 

                    // 1. Extract credentials if they exist in URL
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        var parts = uri.UserInfo.Split(':');
                        if (parts.Length > 0 && string.IsNullOrWhiteSpace(txtRtspUser.Text))
                            txtRtspUser.Text = parts[0];
                        if (parts.Length > 1 && string.IsNullOrWhiteSpace(txtRtspPass.Password))
                            txtRtspPass.Password = parts[1];
                    }

                    // 2. Extract IP and Port
                    if (string.IsNullOrWhiteSpace(txtSourceIp.Text))
                        txtSourceIp.Text = uri.Host;
                    
                    if (string.IsNullOrWhiteSpace(txtSourcePort.Text) || txtSourcePort.Text == "554")
                        txtSourcePort.Text = uri.Port > 0 ? uri.Port.ToString() : "554";

                    // 3. SECURE: Reconstruct the URL without credentials to avoid plain text password storage
                    string cleanUrl = "rtsp://" + uri.Host;
                    if (uri.Port > 0 && uri.Port != 554)
                        cleanUrl += ":" + uri.Port;
                    cleanUrl += uri.PathAndQuery;
                    
                    txtStreamUrl.Text = cleanUrl;
                }
                catch { }
            }
            if (cboCameraType.SelectedItem == null)
            {
                MessageBox.Show("Vui lòng chọn loại camera!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (cboCodec.SelectedItem == null)
            {
                MessageBox.Show("Vui lòng chọn Codec!", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string codec = (cboCodec.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "h264";
            string operationMode = (cboOperationMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "raw_data";
            string mediaServerId = cboMediaServer.SelectedValue?.ToString();

            int? sourcePort = null;
            if (int.TryParse(txtSourcePort.Text, out int port))
                sourcePort = port;

            double? latitude = null;
            if (double.TryParse(txtLatitude.Text, out double lat))
                latitude = lat;

            double? longitude = null;
            if (double.TryParse(txtLongitude.Text, out double lng))
                longitude = lng;

            bool success;
            if (_isEditMode)
            {
                // Update camera
                var updateData = new
                {
                    camera_code = txtCameraCode.Text.Trim(),
                    display_name_1 = txtDisplayName1.Text.Trim(),
                    display_name_2 = string.IsNullOrWhiteSpace(txtDisplayName2.Text) ? null : txtDisplayName2.Text.Trim(),
                    camera_type = cameraType,
                    codec = codec,
                    operation_mode = operationMode,
                    source_ip = string.IsNullOrWhiteSpace(txtSourceIp.Text) ? null : txtSourceIp.Text.Trim(),
                    source_port = sourcePort,
                    source_stream_url = string.IsNullOrWhiteSpace(txtStreamUrl.Text) ? null : txtStreamUrl.Text.Trim(),
                    location_name = string.IsNullOrWhiteSpace(txtLocationName.Text) ? null : txtLocationName.Text.Trim(),
                    latitude = latitude,
                    longitude = longitude,
                    description = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim(),
                    is_active = chkActive.IsChecked ?? true,
                    media_server_id = mediaServerId,
                    rtsp_username = string.IsNullOrWhiteSpace(txtRtspUser.Text) ? null : txtRtspUser.Text.Trim(),
                    rtsp_password = string.IsNullOrWhiteSpace(txtRtspPass.Password) ? null : txtRtspPass.Password,
                    group_ids = _groupSelections.Where(gs => gs.IsSelected).Select(gs => gs.Id).ToList()
                };
                success = await ApiManager.Instance.UpdateCameraAsync(_editingCamera.Id, updateData, CancellationToken.None);
            }
            else
            {
                // Create camera
                var createData = new CameraCreateRequest
                {
                    CameraCode = txtCameraCode.Text.Trim(),
                    DisplayName1 = txtDisplayName1.Text.Trim(),
                    DisplayName2 = string.IsNullOrWhiteSpace(txtDisplayName2.Text) ? null : txtDisplayName2.Text.Trim(),
                    CameraType = cameraType,
                    Codec = codec,
                    OperationMode = operationMode,
                    SourceIp = string.IsNullOrWhiteSpace(txtSourceIp.Text) ? null : txtSourceIp.Text.Trim(),
                    SourcePort = sourcePort,
                    SourceStreamUrl = string.IsNullOrWhiteSpace(txtStreamUrl.Text) ? null : txtStreamUrl.Text.Trim(),
                    LocationName = string.IsNullOrWhiteSpace(txtLocationName.Text) ? null : txtLocationName.Text.Trim(),
                    Latitude = latitude,
                    Longitude = longitude,
                    Description = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim(),
                    IsActive = chkActive.IsChecked ?? true,
                    MediaServerId = mediaServerId,
                    RtspUsername = string.IsNullOrWhiteSpace(txtRtspUser.Text) ? null : txtRtspUser.Text.Trim(),
                    RtspPassword = string.IsNullOrWhiteSpace(txtRtspPass.Password) ? null : txtRtspPass.Password,
                    GroupIds = _groupSelections.Where(gs => gs.IsSelected).Select(gs => gs.Id.ToString()).ToList()
                };
                success = await ApiManager.Instance.CreateCameraAsync(createData, CancellationToken.None);
            }

            if (success)
            {
                MessageBox.Show(_isEditMode ? "Cập nhật camera thành công!" : "Đăng ký camera thành công!",
                    "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(_isEditMode ? "Cập nhật camera thất bại!" : "Đăng ký camera thất bại!",
                    "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void cboCameraType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboCameraType.SelectedItem is ComboBoxItem selectedItem)
            {
                string type = selectedItem.Tag?.ToString();
                UpdateFieldVisibility(type);
            }
        }

        private void UpdateFieldVisibility(string cameraType)
        {
            if (basicInfoSection == null || streamUrlSection == null || technicalFieldsSection == null) return;

            if (cameraType == "ip_cam")
            {
                // IP Camera: Show Basic + RTSP URL, Hide Technical
                basicInfoSection.Visibility = Visibility.Visible;
                streamUrlSection.Visibility = Visibility.Visible;
                technicalFieldsSection.Visibility = Visibility.Collapsed;
            }
            else if (cameraType == "body_cam")
            {
                // Body Camera: Show Basic + Technical, Hide RTSP URL
                basicInfoSection.Visibility = Visibility.Visible;
                streamUrlSection.Visibility = Visibility.Collapsed;
                technicalFieldsSection.Visibility = Visibility.Visible;
                
                // Keep IP/Auth sections collapsed as per earlier user feedback for bodycam
                if (ipPortSection != null) ipPortSection.Visibility = Visibility.Collapsed;
                if (authSection != null) authSection.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Default fallback
                basicInfoSection.Visibility = Visibility.Visible;
                streamUrlSection.Visibility = Visibility.Visible;
                technicalFieldsSection.Visibility = Visibility.Visible;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}



















