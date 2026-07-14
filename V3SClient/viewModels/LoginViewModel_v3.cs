using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using V3SClient.Services;
using V3SClient.libs;

namespace V3SClient.viewModels
{
    public sealed class LoginViewModel_v3 : VMBase, IDisposable
    {
        private readonly AuthenticationService_v3 _authenticationService;
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private string _username;
        private string _password;
        private string _errorMessage;
        private string _statusMessage;
        private bool _isBusy;
        private bool _isProfileSelectionVisible;
        private ApiManager.ClientProfile _selectedProfile;

        public LoginViewModel_v3() : this(new AuthenticationService_v3())
        {
        }

        internal LoginViewModel_v3(AuthenticationService_v3 authenticationService)
        {
            _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
            Profiles = new ObservableCollection<ApiManager.ClientProfile>();
            LoginCommand = new AsyncRelayCommand(LoginAsync, _ => !IsBusy && !IsProfileSelectionVisible);
            ContinueCommand = new AsyncRelayCommand(ContinueAsync, _ => !IsBusy && IsProfileSelectionVisible && SelectedProfile != null);
        }

        public event EventHandler AuthenticationCompleted;

        public ObservableCollection<ApiManager.ClientProfile> Profiles { get; private set; }
        public AsyncRelayCommand LoginCommand { get; private set; }
        public AsyncRelayCommand ContinueCommand { get; private set; }

        public string Username
        {
            get { return _username; }
            set { _username = value; OnPropertyChanged(); }
        }

        // Kept only for the duration of a login attempt; it is never logged or persisted.
        public string Password
        {
            get { return _password; }
            set { _password = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set
            {
                _errorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError { get { return !string.IsNullOrWhiteSpace(ErrorMessage); } }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                _isBusy = value;
                OnPropertyChanged();
                LoginCommand.RaiseCanExecuteChanged();
                ContinueCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsProfileSelectionVisible
        {
            get { return _isProfileSelectionVisible; }
            private set
            {
                _isProfileSelectionVisible = value;
                OnPropertyChanged();
                LoginCommand.RaiseCanExecuteChanged();
                ContinueCommand.RaiseCanExecuteChanged();
            }
        }

        public ApiManager.ClientProfile SelectedProfile
        {
            get { return _selectedProfile; }
            set
            {
                _selectedProfile = value;
                OnPropertyChanged();
                ContinueCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task LoginAsync(object parameter)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Vui lòng nhập tài khoản và mật khẩu.";
                return;
            }

            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = "Đang xác thực…";
            try
            {
                var result = await _authenticationService.SignInAsync(Username.Trim(), Password, _lifetimeCancellation.Token);
                if (!result.IsSuccess)
                {
                    ErrorMessage = result.Message;
                    StatusMessage = null;
                    return;
                }

                Profiles.Clear();
                foreach (var profile in result.Profiles)
                {
                    Profiles.Add(profile);
                }

                if (Profiles.Count == 0)
                {
                    ErrorMessage = "Tài khoản này chưa được gán profile nào.";
                    StatusMessage = null;
                    return;
                }

                SelectedProfile = Profiles[0];
                IsProfileSelectionVisible = true;
                StatusMessage = "Chọn profile để tiếp tục.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = null;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Login v3 failed unexpectedly.");
                ErrorMessage = "Không thể kết nối tới máy chủ. Vui lòng kiểm tra lại cấu hình và kết nối.";
                StatusMessage = null;
            }
            finally
            {
                Password = string.Empty;
                IsBusy = false;
            }
        }

        private async Task ContinueAsync(object parameter)
        {
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = "Đang tải thiết bị…";
            try
            {
                var result = await _authenticationService.SelectProfileAsync(SelectedProfile, _lifetimeCancellation.Token);
                if (!result.IsSuccess)
                {
                    ErrorMessage = result.Message;
                    StatusMessage = null;
                    return;
                }

                StatusMessage = "Đăng nhập thành công.";
                AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = null;
            }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Login v3 profile selection failed unexpectedly.");
                ErrorMessage = "Không thể tải thiết bị cho profile được chọn.";
                StatusMessage = null;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Dispose()
        {
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
        }
    }
}
