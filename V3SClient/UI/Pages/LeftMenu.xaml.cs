using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Newtonsoft.Json;
using V3SClient.libs;
using V3SClient.models;
using V3SClient.ucs;
using V3SClient.viewModels;

namespace V3SClient.UI.Pages
{
    /// <summary>
    /// Interaction logic for Left_Menu.xaml
    /// </summary>
    public partial class LeftMenu : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public ObservableCollection<CamInfo> Commanders => GlobalUserInfo.Instance.Commanders;
        public string ActiveCommanderID
        {
            get => GlobalUserInfo.Instance.ActiveCommanderID;
            set
            {
                if (GlobalUserInfo.Instance.ActiveCommanderID != value)
                {
                    GlobalUserInfo.Instance.ActiveCommanderID = value;
                    OnPropertyChanged(nameof(ActiveCommanderID));
                }
            }
        }


        private bool _isSettingButton_Visible = false;
        public bool IsSettingButton_Visible
        {
            get => _isSettingButton_Visible;
            set
            {
                _isSettingButton_Visible = value;
                OnPropertyChanged(nameof(IsSettingButton_Visible));
            }
        }

        private bool _isOrganizationView;
        public bool IsOrganizationView
        {
            get => _isOrganizationView;
            set
            {
                _isOrganizationView = value;
                OnPropertyChanged(nameof(IsOrganizationView));
                UpdateCommanderVisibility();
            }
        }

        private bool _commandVisible = false;
        private void UpdateCommanderVisibility()
        {
            if (panelcomboBoxContent != null)
            {
                if (_commandVisible && !_isOrganizationView)
                    panelcomboBoxContent.Visibility = Visibility.Visible;
                else
                    panelcomboBoxContent.Visibility = Visibility.Collapsed;
            }
        }

        public event EventHandler EventSettingClick;
        public ObservableCollection<viewModels.VMTalkGroup> CameraGroupList { get; set; }
        public int HeightZoneCameraList { get; set; } = 330;
  

        // Thông báo Live Page rằng Người dùng đã Thay đổi Group để talk
        public event EventHandler<VMTalkGroup> Event_Selected_Voice_Group_Changed;
        public event EventHandler<Camera> Event_Camera_Selected_Changed;
        public event EventHandler<List<Camera>> Event_Nodes_Camera_Selected_Changed;
        public event EventHandler<CamInfoNode> Event_Org_Camera_Selection_Changed;
        public event EventHandler<bool> Event_AIMode_Changed;
        public bool IsAIMode { get; private set; }
        public bool IsRecordingMode { get; private set; }
        public LeftMenu(ObservableCollection<viewModels.VMTalkGroup> camera_group_list, 
            int heightZoneCameraList=300, 
            bool commandVisible=false,bool switchviewVisible=false,bool showCamGroupList=false,bool showOrgFrist=false)
        {
            InitializeComponent();
            DataContext = this;
            _commandVisible = commandVisible;
            HeightZoneCameraList = heightZoneCameraList;
            btnSwapView.Visibility= switchviewVisible ? Visibility.Visible : Visibility.Collapsed;
            CameraGroupList = camera_group_list;
            IsOrganizationView = !switchviewVisible && !showCamGroupList;
            if (showOrgFrist) IsOrganizationView = !IsOrganizationView;
            UpdateCommanderVisibility();
            View_Cam_Group_List.CameraGroupList = CameraGroupList;

            View_Cam_Group_List.Event_Selected_Voice_GroupTalk_Changed += Selected_Group_Changed;

            View_Cam_Group_List.Event_Camera_Seleced_Changed += Forward_Camera_Selected_Changed;
            View_Cam_Group_List.Event_Node_Selected_Cameras +=GroupTalk_Nodes_Camera_Selected_Changed;

            View_Cam_Group_Org.Event_Camera_Selection_Changed += Org_Forward_Camera_Selected_Changed;
            View_Cam_Group_Org.Event_Node_Selected += Org_Nodes_Camera_Selected_Changed;
        }

        private void Org_Nodes_Camera_Selected_Changed(object sender, List<CamInfoNode> e)
        {
            var selectedCameras = new List<Camera>();
            if (e != null)
            {
                foreach (var node in e)
                {
                    var camId = node?.CamData?.CamInfo_CamId;
                    if (string.IsNullOrEmpty(camId))
                        continue;

                    foreach (var group in CameraGroupList)
                    {
                        var matchedCamera = FindCameraInGroupRecursive(group, camId);
                        if (matchedCamera != null)
                        {
                            selectedCameras.Add(matchedCamera);
                            break;
                        }
                    }
                }
            }

            Event_Nodes_Camera_Selected_Changed?.Invoke(this, selectedCameras);
        }

        private void GroupTalk_Nodes_Camera_Selected_Changed(object sender, List<Camera> e)
        {
            Event_Nodes_Camera_Selected_Changed?.Invoke(this, e ?? new List<Camera>());
        }

        private void Org_Forward_Camera_Selected_Changed(object sender, CamInfoNode e)
        {
           
            // Bước 1: Lấy ID từ CamInfoNode
            var camId = e?.CamData?.CamInfo_CamId;
            if (camId == null)
                return;

            // Bước 2: tìm camera tương ứng trong CameraGroupList
            foreach (var group in CameraGroupList)
            {
                var matchedCamera = FindCameraInGroupRecursive(group, camId);
                if (matchedCamera != null)
                {
                    Forward_Camera_Selected_Changed(this, matchedCamera);
                    return;
                }
            }
        }

        private Camera FindCameraInGroupRecursive(VMTalkGroup group, string camId)
        {
            var cam = group.Cameras.FirstOrDefault(c => c.camID == camId);
            if (cam != null) return cam;

            foreach (var sub in group.SubGroups)
            {
                var result = FindCameraInGroupRecursive(sub, camId);
                if (result != null) return result;
            }
            return null;
        }

        private void Forward_Camera_Selected_Changed(object sender, Camera e)
        {
            Event_Camera_Selected_Changed?.Invoke(this, e);
        }

        private void Selected_Group_Changed(object sender, VMTalkGroup e)
        {
            Event_Selected_Voice_Group_Changed?.Invoke(this, e);  
        }

        private void btn_hide_left_menu_Click(object sender, RoutedEventArgs e)
        {

            leftMenu.Visibility = leftMenu.Visibility== Visibility.Visible? 
                Visibility.Collapsed: Visibility.Visible;
        }

        public void SetMenuCollapsed(bool isCollapsed)
        {
            leftMenu.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        }
        private void btnSetting_Click(object sender, RoutedEventArgs e)
        {
            EventSettingClick?.Invoke(this, new EventArgs());
        }
        private void btnToggleView_Click(object sender, RoutedEventArgs e)
        {
            IsOrganizationView = !IsOrganizationView;
        }

        public void SetBottomContent(object content)
        {
            frameBottom.Content = content;
        }

        private void txtSearchTree_TextChanged(object sender, TextChangedEventArgs e)
        {
            string keyword = txtSearchTree.Text.Trim();
            
            // Toggle placeholder visibility
            if (txtSearchHint != null)
            {
                txtSearchHint.Visibility = string.IsNullOrEmpty(txtSearchTree.Text) ? Visibility.Visible : Visibility.Collapsed;
            }

            ApplyCameraFilter(keyword);
        }

        private void btnToggleAIMode_Click(object sender, RoutedEventArgs e)
        {
            IsAIMode = btnToggleAIMode.IsChecked == true;
            
            Event_AIMode_Changed?.Invoke(this, IsAIMode);
            
            UpdateFilterVisuals();
            ApplyCameraFilter();
        }

        private void btnToggleRecordingMode_Click(object sender, RoutedEventArgs e)
        {
            IsRecordingMode = btnToggleRecordingMode.IsChecked == true;
            UpdateFilterVisuals();
            ApplyCameraFilter();
        }

        private void btnShowAll_Click(object sender, RoutedEventArgs e)
        {
            btnToggleAIMode.IsChecked = false;
            btnToggleRecordingMode.IsChecked = false;
            IsAIMode = false;
            IsRecordingMode = false;
            Event_AIMode_Changed?.Invoke(this, false);
            UpdateFilterVisuals();
            ApplyCameraFilter();
        }

        private void UpdateFilterVisuals()
        {
            var primary = FindResource("VmsPrimaryBrush_v3") as Brush;
            var surface = FindResource("VmsSurfaceBrush_v3") as Brush;
            if (primary == null || surface == null) return;

            btnShowAll.Background = !IsAIMode && !IsRecordingMode ? primary : surface;
            btnToggleAIMode.Background = IsAIMode ? primary : surface;
            btnToggleRecordingMode.Background = IsRecordingMode ? primary : surface;
        }

        private void ApplyCameraFilter(string keyword = null)
        {
            keyword = keyword ?? (txtSearchTree?.Text?.Trim() ?? string.Empty);
            if (IsOrganizationView)
            {
                View_Cam_Group_Org.FilterTree(keyword, IsAIMode, IsRecordingMode);
            }
            else
            {
                View_Cam_Group_List.FilterTree(keyword, IsAIMode, IsRecordingMode);
            }
        }
    }
}
