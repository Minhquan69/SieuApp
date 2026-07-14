using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using V3SClient.libs;

namespace V3SClient.Services
{
    /// <summary>
    /// Coordinates the existing V3 authentication APIs for the isolated login flow.
    /// It deliberately owns no HTTP client and changes no backend contract.
    /// </summary>
    public sealed class AuthenticationService_v3
    {
        public async Task<AuthenticationResult_v3> SignInAsync(string username, string password, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loginResult = await ApiManager.Instance.LoginAsync(username, password);
            cancellationToken.ThrowIfCancellationRequested();

            if (!loginResult.Success)
            {
                return AuthenticationResult_v3.Failed(loginResult.Message);
            }

            GlobalUserInfo.Instance.UserId = loginResult.UserId;
            GlobalUserInfo.Instance.UserName = username;
            GlobalUserInfo.Instance.SetLoginTime();

            var currentUser = await ApiManager.Instance.GetMeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (currentUser != null)
            {
                GlobalUserInfo.Instance.UserPermissions = currentUser.Permissions ?? new List<string>();
                GlobalUserInfo.Instance.UserRoles = currentUser.Roles ?? new List<string>();
                GlobalUserInfo.Instance.TenantId = currentUser.TenantId;
                GlobalUserInfo.Instance.IsSuperAdmin = currentUser.IsSuperAdmin;
            }
            else
            {
                LoggerManager.LogWarn("Login v3 could not load the current user's permission profile.");
            }

            var profiles = await ApiManager.Instance.GetWebProfilesAsync(cancellationToken);
            if (profiles == null || profiles.Count == 0)
                profiles = await ApiManager.Instance.GetMyAuthorizedProfilesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            GlobalUserInfo.Instance.AuthorizedProfiles = profiles ?? new List<ApiManager.ClientProfile>();

            return AuthenticationResult_v3.Succeeded(GlobalUserInfo.Instance.AuthorizedProfiles);
        }

        public async Task<AuthenticationResult_v3> SelectProfileAsync(ApiManager.ClientProfile profile, CancellationToken cancellationToken)
        {
            if (profile == null)
            {
                return AuthenticationResult_v3.Failed("Vui lòng chọn một profile.");
            }

            var cameras = await ApiManager.Instance.GetCamInfoAsync(cancellationToken, profile.Id.ToString());
            cancellationToken.ThrowIfCancellationRequested();
            if (cameras == null || cameras.Count == 0)
            {
                return AuthenticationResult_v3.Failed("Profile được chọn không có thiết bị được cấu hình.");
            }

            var user = GlobalUserInfo.Instance;
            user.ActiveClientId = profile.Id;
            user.SelectedClientName = profile.Name;
            user.GroupClients.Clear();
            user.GroupClients[profile.Id] = cameras;

            var commanders = cameras
                .Where(camera => camera.Device_Role != null && camera.Device_Role != "client_device" && camera.CamInfo_Type == "body_cam")
                .ToList();
            user.Commanders = new ObservableCollection<CamInfo>(commanders);
            user.ActiveCommanderID = commanders.Count > 0 ? commanders[0].CamInfo_CamId : null;
            user.BuildTreeViewWithOrganization();

            LoggerManager.LogInfo($"Login v3 selected profile: {profile.Name} ({cameras.Count} devices).");
            return AuthenticationResult_v3.Succeeded(user.AuthorizedProfiles);
        }
    }

    public sealed class AuthenticationResult_v3
    {
        private AuthenticationResult_v3(bool isSuccess, string message, IReadOnlyList<ApiManager.ClientProfile> profiles)
        {
            IsSuccess = isSuccess;
            Message = message;
            Profiles = profiles ?? new List<ApiManager.ClientProfile>();
        }

        public bool IsSuccess { get; private set; }
        public string Message { get; private set; }
        public IReadOnlyList<ApiManager.ClientProfile> Profiles { get; private set; }

        public static AuthenticationResult_v3 Succeeded(IReadOnlyList<ApiManager.ClientProfile> profiles)
        {
            return new AuthenticationResult_v3(true, null, profiles);
        }

        public static AuthenticationResult_v3 Failed(string message)
        {
            return new AuthenticationResult_v3(false, string.IsNullOrWhiteSpace(message) ? "Đăng nhập không thành công." : message, null);
        }
    }
}
