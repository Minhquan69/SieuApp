using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using V3SClient.Services;
using V3SClient.libs;

namespace V3SClient.viewModels
{
    public sealed class LoginViewModel_v3 : VMBase, IDisposable
    {
        private readonly AuthenticationService_v3 _authentication = new AuthenticationService_v3();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private string _username, _password, _errorMessage, _statusMessage;
        private bool _isBusy, _isProfileSelectionVisible, _isRememberMe;
        private ApiManager.ClientProfile _selectedProfile;

        public LoginViewModel_v3()
        {
            Profiles = new ObservableCollection<ApiManager.ClientProfile>();
            LoginCommand = new AsyncRelayCommand(LoginAsync, _ => !IsBusy && !IsProfileSelectionVisible);
            ContinueCommand = new AsyncRelayCommand(ContinueAsync, _ => !IsBusy && IsProfileSelectionVisible && SelectedProfile != null);
            LogoutCommand = new AsyncRelayCommand(_ => { ResetLogin(); return Task.CompletedTask; }, _ => !IsBusy && IsProfileSelectionVisible);
            LoadLoginCache();
        }
        public event EventHandler AuthenticationCompleted;
        public ObservableCollection<ApiManager.ClientProfile> Profiles { get; private set; }
        public AsyncRelayCommand LoginCommand { get; private set; }
        public AsyncRelayCommand ContinueCommand { get; private set; }
        public AsyncRelayCommand LogoutCommand { get; private set; }
        public event EventHandler LoginResetRequested;
        public string Username { get { return _username; } set { _username = value; OnPropertyChanged(); } }
        public string Password { get { return _password; } set { _password = value; OnPropertyChanged(); } }
        public bool IsRememberMe { get { return _isRememberMe; } set { _isRememberMe = value; OnPropertyChanged(); } }
        public string ErrorMessage { get { return _errorMessage; } private set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); } }
        public bool HasError { get { return !string.IsNullOrWhiteSpace(ErrorMessage); } }
        public string StatusMessage { get { return _statusMessage; } private set { _statusMessage = value; OnPropertyChanged(); } }
        public bool IsBusy { get { return _isBusy; } private set { _isBusy = value; OnPropertyChanged(); LoginCommand.RaiseCanExecuteChanged(); ContinueCommand.RaiseCanExecuteChanged(); } }
        public bool IsProfileSelectionVisible { get { return _isProfileSelectionVisible; } private set { _isProfileSelectionVisible = value; OnPropertyChanged(); LoginCommand.RaiseCanExecuteChanged(); ContinueCommand.RaiseCanExecuteChanged(); } }
        public ApiManager.ClientProfile SelectedProfile { get { return _selectedProfile; } set { _selectedProfile = value; OnPropertyChanged(); ContinueCommand.RaiseCanExecuteChanged(); } }

        private async Task LoginAsync(object parameter)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)) { ErrorMessage = "Vui lòng nhập tài khoản và mật khẩu."; return; }
            PersistLoginCache();
            IsBusy = true; ErrorMessage = null; StatusMessage = "Đang xác thực…";
            try
            {
                var result = await _authentication.SignInAsync(Username.Trim(), Password, _lifetime.Token);
                if (!result.IsSuccess) { ErrorMessage = result.Message; StatusMessage = null; return; }
                Profiles.Clear(); foreach (var profile in result.Profiles) Profiles.Add(profile);
                if (Profiles.Count == 0) { ErrorMessage = "Tài khoản này chưa được gán profile nào."; StatusMessage = null; return; }
                SelectedProfile = Profiles[0]; IsProfileSelectionVisible = true; StatusMessage = "Chọn profile để tiếp tục.";
            }
            catch (OperationCanceledException) { StatusMessage = null; }
            catch (Exception ex) { LoggerManager.LogException(ex, "Login v3 failed."); ErrorMessage = "Không thể kết nối tới máy chủ."; StatusMessage = null; }
            // Keep the credentials in the view-model after a transient DNS/
            // network failure. The PasswordBox still contains the text, and
            // clearing only this backing value made the next click fail local
            // validation with “Vui lòng nhập tài khoản và mật khẩu.”
            finally { IsBusy = false; }
        }

        private async Task ContinueAsync(object parameter)
        {
            IsBusy = true; ErrorMessage = null; StatusMessage = "Đang tải thiết bị…";
            try
            {
                var result = await _authentication.SelectProfileAsync(SelectedProfile, _lifetime.Token);
                if (!result.IsSuccess) { ErrorMessage = result.Message; StatusMessage = null; return; }
                AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) { StatusMessage = null; }
            catch (Exception ex) { LoggerManager.LogException(ex, "Login v3 profile selection failed."); ErrorMessage = "Không thể tải thiết bị cho profile được chọn."; StatusMessage = null; }
            finally { IsBusy = false; }
        }
        public void ResetLogin()
        {
            Username = string.Empty;
            Password = string.Empty;
            ErrorMessage = null;
            StatusMessage = null;
            SelectedProfile = null;
            Profiles.Clear();
            IsProfileSelectionVisible = false;
            LoginResetRequested?.Invoke(this, EventArgs.Empty);
        }
        private void LoadLoginCache()
        {
            try
            {
                var path = GetLoginCachePath();
                if (!File.Exists(path)) return;
                var values = File.ReadAllText(path).Split('|');
                if (values.Length != 2) return;
                Username = Encoding.UTF8.GetString(Convert.FromBase64String(values[0]));
                Password = Encoding.UTF8.GetString(Convert.FromBase64String(values[1]));
                IsRememberMe = true;
            }
            catch { }
        }
        private void PersistLoginCache()
        {
            try
            {
                var path = GetLoginCachePath();
                if (!IsRememberMe) { if (File.Exists(path)) File.Delete(path); return; }
                File.WriteAllText(path, Convert.ToBase64String(Encoding.UTF8.GetBytes(Username ?? string.Empty)) + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Password ?? string.Empty)));
            }
            catch { }
        }
        private static string GetLoginCachePath()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "iVista VMS");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "login.tmp");
        }
        public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
    }
}
