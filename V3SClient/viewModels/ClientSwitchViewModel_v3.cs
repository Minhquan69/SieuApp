using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using V3SClient.libs;
using V3SClient.Services;

namespace V3SClient.viewModels
{
    public sealed class ClientSwitchViewModel_v3 : INotifyPropertyChanged, IDisposable
    {
        private readonly ClientSessionService _sessionService = new ClientSessionService();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private ApiManager.ClientProfile _selectedProfile;
        private string _errorMessage;
        private bool _isLoading;

        public ObservableCollection<ApiManager.ClientProfile> Profiles { get; } = new ObservableCollection<ApiManager.ClientProfile>();
        public ApiManager.ClientProfile SelectedProfile
        {
            get { return _selectedProfile; }
            set
            {
                if (ReferenceEquals(_selectedProfile, value)) return;
                _selectedProfile = value;
                OnPropertyChanged(nameof(SelectedProfile));
                OnPropertyChanged(nameof(SelectedProfileName));
                if (ConfirmCommand != null) ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
        public string SelectedProfileName { get { return SelectedProfile == null ? "Chưa chọn client" : SelectedProfile.Name; } }
        public string ErrorMessage { get { return _errorMessage; } private set { _errorMessage = value; OnPropertyChanged(nameof(ErrorMessage)); } }
        public bool IsLoading { get { return _isLoading; } private set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); if (ReloadCommand != null) ReloadCommand.RaiseCanExecuteChanged(); if (ConfirmCommand != null) ConfirmCommand.RaiseCanExecuteChanged(); } }
        public AsyncRelayCommand ReloadCommand { get; }
        public AsyncRelayCommand ConfirmCommand { get; }
        public event EventHandler Confirmed;
        public event PropertyChangedEventHandler PropertyChanged;

        public ClientSwitchViewModel_v3(System.Collections.Generic.IEnumerable<ApiManager.ClientProfile> profiles)
        {
            if (profiles != null)
                foreach (var profile in profiles) Profiles.Add(profile);
            ReloadCommand = new AsyncRelayCommand(_ => ReloadAsync(), _ => !IsLoading);
            ConfirmCommand = new AsyncRelayCommand(_ => ConfirmAsync(), _ => !IsLoading && SelectedProfile != null);
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == GlobalUserInfo.Instance.ActiveClientId) ?? Profiles.FirstOrDefault();
        }

        private async Task ReloadAsync()
        {
            IsLoading = true;
            ErrorMessage = null;
            Guid previousId = SelectedProfile == null ? Guid.Empty : SelectedProfile.Id;
            try
            {
                var profiles = await _sessionService.LoadAuthorizedClientsAsync(_lifetime.Token).ConfigureAwait(true);
                Profiles.Clear();
                foreach (var profile in profiles ?? new System.Collections.Generic.List<ApiManager.ClientProfile>()) Profiles.Add(profile);
                SelectedProfile = Profiles.FirstOrDefault(p => p.Id == previousId) ?? Profiles.FirstOrDefault();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LoggerManager.LogException(ex, "Lỗi tải lại danh sách client");
                ErrorMessage = "Không thể tải danh sách client.";
            }
            finally { IsLoading = false; }
        }

        private Task ConfirmAsync()
        {
            if (SelectedProfile != null) Confirmed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private void OnPropertyChanged(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
        public void Dispose() { if (!_lifetime.IsCancellationRequested) _lifetime.Cancel(); _lifetime.Dispose(); }
    }
}
