using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;


namespace V3SClient.libs
{
    public class GlobalUserInfo
    {
        #region Instance
        private static readonly Lazy<GlobalUserInfo> _instance = new Lazy<GlobalUserInfo>(() => new GlobalUserInfo());
        public static GlobalUserInfo Instance => _instance.Value;
        #endregion Instance

        #region Field
        private bool _camInfoUpdated = false;
        private DateTime _startLoginTime;
        private string _userId;
        private Guid _activeClientId;
        private Dictionary<Guid, List<CamInfo>> _groupedClients = new Dictionary<Guid, List<CamInfo>>();
        #endregion Field

        #region Properties
        public string SelectedClientName { get;  set; }
        public string UserName { get; set; }
        public List<CamInfo> SelectedCamList { get;  set; }

        public string UserId { get=> _userId;  set => _userId = value; }
        public Guid ActiveClientId { get => _activeClientId; set => _activeClientId = value; }
        public Dictionary<Guid, List<CamInfo>> GroupClients { get => _groupedClients; set => _groupedClients = value; }
        public DateTime StartLoginTime=> _startLoginTime;
        public bool CamInfoUpdate { get => _camInfoUpdated;set=>_camInfoUpdated = value; }
        public ObservableCollection<CamInfo> Commanders { get; set; }
        public string ActiveCommanderID {  get; set; }
        public string ActiveTalkGroupId { get; set; }

        public int? TenantId { get; set; }
        public bool IsSuperAdmin { get; set; }
        public List<string> UserPermissions { get; set; } = new List<string>();
        public List<string> UserRoles { get; set; } = new List<string>();

        public string Redis_Server_IP { get; set; } = "localhost";
        public int Redis_Server_Port { get; set; } = 6379;
        public string Redis_Username { get; set; } = "";
        public string Redis_Password { get; set; } = "";

        public string Share_Data_Server_IP { get; set; } = "localhost";
        public int Share_Data_Server_Port { get; set; } = 21;

        public ObservableCollection<AreaNode> AreaTree { get; set; } = new ObservableCollection<AreaNode>();
        public List<ApiManager.ClientProfile> AuthorizedProfiles { get; set; } = new List<ApiManager.ClientProfile>();
        #endregion Properties

        #region Method
        public List<CamInfo> GetActiveClient()
        {
            try
            {
                if (_activeClientId != Guid.Empty)
                {
                    return _groupedClients?[_activeClientId];
                }
                return default(List<CamInfo>);
            }
            catch (Exception ex) {
                return default(List<CamInfo>);
            }
        }
        public void SetAllowSelectingForAllCams(Visibility visibility)
        {
            if (AreaTree == null) return;
            foreach (var area in AreaTree)
            {
                foreach (var unit in area.Units)
                {
                    SetAllowSelectingRecursive(unit, visibility);
                }
            }
        }

        private void SetAllowSelectingRecursive(UnitNode unit, Visibility visibility)
        {
            foreach (var cam in unit.Cams)
            {
                cam.AllowSelecting = visibility;
            }

            foreach (var sub in unit.SubUnits)
            {
                SetAllowSelectingRecursive(sub, visibility);
            }
        }

        public void BuildTreeViewWithOrganization()
        {
            AreaTree = new ObservableCollection<AreaNode>(Utils.BuildTree(GetActiveClient()));
        }

        public void UpdateCameraStatus(string camId, string newStatus)
        {
            if (AreaTree == null) return;
            foreach (var area in AreaTree)
            {
                foreach (var unit in area.Units)
                {
                    UpdateStatusRecursive(unit, camId, newStatus);
                }
            }
        }

        private void UpdateStatusRecursive(UnitNode unit, string camId, string newStatus)
        {
            // Tìm tất cả các camera khớp ID trong unit hiện tại
            var matchingCams = unit.Cams.Where(c => string.Equals(c.CamData?.CamInfo_CamId, camId, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var camNode in matchingCams)
            {
                if (camNode.CamData != null)
                {
                    camNode.CamData.Status = newStatus;
                }
            }

            // Tiếp tục đệ quy xuống tất cả các sub-units
            foreach (var sub in unit.SubUnits)
            {
                UpdateStatusRecursive(sub, camId, newStatus);
            }
        }
        public void SetLoginTime()
        {
            _startLoginTime = DateTime.Now;
        }

        public bool HasPermission(string permissionCode)
        {
            if (IsSuperAdmin) return true;
            if (UserPermissions == null) return false;
            return UserPermissions.Contains(permissionCode);
        }
        #endregion Method
    }
}















