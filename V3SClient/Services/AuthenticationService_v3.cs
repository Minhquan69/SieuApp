using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using V3SClient.libs;

namespace V3SClient.Services
{
    public sealed class AuthenticationService_v3
    {
        private readonly ClientSessionService _sessionService = new ClientSessionService();

        public async Task<AuthenticationResult_v3> SignInAsync(string username, string password, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var login = await ApiManager.Instance.LoginAsync(username, password, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!login.Success) return AuthenticationResult_v3.Failed(login.Message);

            var user = GlobalUserInfo.Instance;
            user.UserId = login.UserId;
            user.UserName = username;
            user.SetLoginTime();
            var meTask = ApiManager.Instance.GetMeAsync(cancellationToken);
            var profilesTask = _sessionService.LoadAuthorizedClientsAsync(cancellationToken);
            await Task.WhenAll(meTask, profilesTask);
            var me = await meTask;
            cancellationToken.ThrowIfCancellationRequested();
            if (me != null)
            {
                user.UserPermissions = me.Permissions ?? new List<string>();
                user.UserRoles = me.Roles ?? new List<string>();
                user.TenantId = me.TenantId;
                user.IsSuperAdmin = me.IsSuperAdmin;
            }
            var profiles = await profilesTask;
            LoggerManager.LogInfo("Login v3 completed in " + stopwatch.ElapsedMilliseconds + " ms.");
            return AuthenticationResult_v3.Succeeded(profiles);
        }

        public async Task<AuthenticationResult_v3> SelectProfileAsync(ApiManager.ClientProfile profile, CancellationToken cancellationToken)
        {
            if (profile == null) return AuthenticationResult_v3.Failed("Vui lòng chọn một profile.");
            try
            {
                await _sessionService.SwitchClientAsync(profile, cancellationToken);
                return AuthenticationResult_v3.Succeeded(GlobalUserInfo.Instance.AuthorizedProfiles);
            }
            catch (InvalidOperationException ex) { return AuthenticationResult_v3.Failed(ex.Message); }
        }
    }

    public sealed class AuthenticationResult_v3
    {
        private AuthenticationResult_v3(bool success, string message, IReadOnlyList<ApiManager.ClientProfile> profiles) { IsSuccess = success; Message = message; Profiles = profiles ?? new List<ApiManager.ClientProfile>(); }
        public bool IsSuccess { get; private set; }
        public string Message { get; private set; }
        public IReadOnlyList<ApiManager.ClientProfile> Profiles { get; private set; }
        public static AuthenticationResult_v3 Succeeded(IReadOnlyList<ApiManager.ClientProfile> profiles) { return new AuthenticationResult_v3(true, null, profiles); }
        public static AuthenticationResult_v3 Failed(string message) { return new AuthenticationResult_v3(false, string.IsNullOrWhiteSpace(message) ? "Đăng nhập không thành công." : message, null); }
    }
}
