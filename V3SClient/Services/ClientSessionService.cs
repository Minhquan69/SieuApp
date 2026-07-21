using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using V3SClient.libs;

namespace V3SClient.Services
{
    public sealed class ClientSessionService
    {
        public async Task<List<ApiManager.ClientProfile>> LoadAuthorizedClientsAsync(CancellationToken cancellationToken)
        {
            var profiles = await ApiManager.Instance.GetWebProfilesAsync(cancellationToken);
            if (profiles == null || profiles.Count == 0)
                profiles = await ApiManager.Instance.GetMyAuthorizedProfilesAsync(cancellationToken);
            GlobalUserInfo.Instance.AuthorizedProfiles = profiles ?? new List<ApiManager.ClientProfile>();
            return GlobalUserInfo.Instance.AuthorizedProfiles;
        }

        public async Task SwitchClientAsync(ApiManager.ClientProfile profile, CancellationToken cancellationToken)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var cameras = await ApiManager.Instance.GetCamInfoAsync(cancellationToken, profile.Id.ToString());
            if (cameras == null || cameras.Count == 0)
                throw new InvalidOperationException("Client không có cấu hình thiết bị.");

            var info = GlobalUserInfo.Instance;
            info.ActiveClientId = profile.Id;
            info.SelectedClientName = profile.Name;
            info.GroupClients.Clear();
            info.GroupClients[profile.Id] = cameras;
            var commanders = cameras.Where(c => c.Device_Role != null && c.Device_Role != "client_device" && c.CamInfo_Type == "body_cam").ToList();
            info.Commanders = new System.Collections.ObjectModel.ObservableCollection<CamInfo>(commanders);
            info.ActiveCommanderID = commanders.FirstOrDefault()?.CamInfo_CamId;
            info.BuildTreeViewWithOrganization();

            // V3 pages read their camera source from GlobalSystem.CameraGroups,
            // while the session cache above is stored in GlobalUserInfo. Keep
            // both stores synchronized after every profile switch; otherwise
            // a newly created Live/Playback page would still display the
            // previous client's cameras.
            info.CamInfoUpdate = true;
            GlobalSystem.Instance.ReloadConfig();
        }

        public void ClearSession()
        {
            var info = GlobalUserInfo.Instance;
            info.ActiveClientId = Guid.Empty;
            info.SelectedClientName = null;
            info.GroupClients.Clear();
            info.AreaTree.Clear();
            info.Commanders = null;
            info.ActiveCommanderID = null;
            info.AuthorizedProfiles = new List<ApiManager.ClientProfile>();
            info.UserId = null;
            info.UserName = null;
        }
    }
}
