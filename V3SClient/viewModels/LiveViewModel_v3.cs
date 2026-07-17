using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using V3SClient.libs;
using V3SClient.models;

namespace V3SClient.viewModels
{
    public enum LiveLayoutMode_v3 { Layout1x1, Layout2x2, Layout5Plus1, Layout3x3, Layout16Plus1, Layout6x6, Custom }
    public enum LiveConnectionState_v3 { Empty, Offline, Connecting, Disconnecting, Connected, Error, Retrying }

    public sealed class LiveSlotViewModel_v3 : INotifyPropertyChanged
    {
        private Camera _camera;
        private CameraStreamInfo _selectedStream;
        private LiveConnectionState_v3 _state = LiveConnectionState_v3.Empty;
        private string _errorMessage;
        private int _retryCount;
        private DateTime _connectedAtUtc = DateTime.MinValue;

        public int SlotId { get; internal set; }
        public Camera Camera { get { return _camera; } internal set { _camera = value; OnChanged(); OnChanged(nameof(HasCamera)); OnChanged(nameof(DisplayName)); } }
        public CameraStreamInfo SelectedStream { get { return _selectedStream; } set { _selectedStream = value; OnChanged(); OnChanged(nameof(StreamLabel)); } }
        public bool HasCamera { get { return Camera != null; } }
        // Camera IDs are the stable identity used by the live tile. Some API
        // responses contain an empty name, so null-coalescing alone can
        // produce a badge with only its status dot. Prefer camID and reject
        // whitespace values explicitly.
        public string DisplayName
        {
            get
            {
                if (Camera == null) return "Empty camera tile";
                if (!string.IsNullOrWhiteSpace(Camera.camID)) return Camera.camID.Trim();
                if (!string.IsNullOrWhiteSpace(Camera.name)) return Camera.name.Trim();
                return "Camera";
            }
        }
        public string StreamLabel { get { return SelectedStream == null ? "main" : (SelectedStream.StreamType ?? "main"); } }
        public LiveConnectionState_v3 State { get { return _state; } set { _state = value; OnChanged(); OnChanged(nameof(StatusText)); OnChanged(nameof(HasError)); OnChanged(nameof(IsConnected)); } }
        public string ErrorMessage { get { return _errorMessage; } set { _errorMessage = value; OnChanged(); } }
        public int RetryCount { get { return _retryCount; } set { _retryCount = value; OnChanged(); } }
        public bool HasError { get { return State == LiveConnectionState_v3.Error || State == LiveConnectionState_v3.Retrying; } }
        public bool IsConnected { get { return State == LiveConnectionState_v3.Connected; } }
        public DateTime ConnectedAtUtc { get { return _connectedAtUtc; } internal set { _connectedAtUtc = value; OnChanged(); } }
        public string StatusText
        {
            get
            {
                switch (State)
                {
                    case LiveConnectionState_v3.Connecting: return "Đang kết nối...";
                    case LiveConnectionState_v3.Disconnecting: return "Đang ngắt kết nối...";
                    case LiveConnectionState_v3.Connected: return "Đã kết nối";
                    case LiveConnectionState_v3.Error: return "Lỗi kết nối";
                    case LiveConnectionState_v3.Retrying: return "Đang thử lại " + RetryCount + "/3...";
                    case LiveConnectionState_v3.Offline: return "Ngoại tuyến";
                    default: return "Trống";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }

    public sealed class LiveCameraGroupViewModel_v3
    {
        public string Name { get; set; }
        public ObservableCollection<Camera> Cameras { get; set; }
        public ObservableCollection<LiveCameraItemViewModel_v3> CameraItems { get; set; }
        public bool IsExpanded { get; set; }
        public int Count { get { return Cameras == null ? 0 : Cameras.Count; } }
    }

    public sealed class LiveCameraItemViewModel_v3 : INotifyPropertyChanged
    {
        public Camera Camera { get; private set; }
        public string DisplayName
        {
            get
            {
                if (Camera == null) return "Camera";
                if (!string.IsNullOrWhiteSpace(Camera.camID)) return Camera.camID.Trim();
                if (!string.IsNullOrWhiteSpace(Camera.name)) return Camera.name.Trim();
                return "Camera";
            }
        }
        public bool IsAiCamera
        {
            get
            {
                return Camera != null &&
                    (Camera.HasAIStream ||
                     string.Equals(Camera.type, "ai_processed", StringComparison.OrdinalIgnoreCase) ||
                     (Camera.Streams != null && Camera.Streams.Any(stream => stream != null && stream.IsAiMode == true)));
            }
        }
        private bool _isSelected;
        private string _stateText;
        private bool _isConnecting;
        private bool _isConnected;
        private bool _hasConnectionError;
        public bool IsSelected { get { return _isSelected; } set { _isSelected = value; OnChanged(); } }
        public string StateText { get { return _stateText; } set { _stateText = value; OnChanged(); } }
        public bool IsConnecting { get { return _isConnecting; } set { _isConnecting = value; OnChanged(); } }
        public bool IsConnected { get { return _isConnected; } set { _isConnected = value; OnChanged(); } }
        public bool HasConnectionError { get { return _hasConnectionError; } set { _hasConnectionError = value; OnChanged(); } }
        public LiveCameraItemViewModel_v3(Camera camera) { Camera = camera; StateText = "Available"; }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }

    public sealed class LiveViewModel_v3 : VMBase
    {
        private readonly List<Camera> _allCameras;
        private readonly List<VMTalkGroup> _sourceGroups;
        private string _searchText;
        private bool _aiOnly;
        private LiveLayoutMode_v3 _layout = LiveLayoutMode_v3.Layout2x2;
        private int _customSlotCount = 10;

        public LiveViewModel_v3()
        {
            _sourceGroups = GlobalSystem.Instance.CameraGroups == null
                ? new List<VMTalkGroup>()
                : GlobalSystem.Instance.CameraGroups.CamGroupList.Where(group => group != null).ToList();
            _allCameras = _sourceGroups.Where(group => group.Cameras != null).SelectMany(group => group.Cameras)
                .Where(camera => camera != null).Distinct().ToList();
            CameraGroups = new ObservableCollection<LiveCameraGroupViewModel_v3>();
            Slots = new ObservableCollection<LiveSlotViewModel_v3>();
            ApplySearch();
            SetLayout(LiveLayoutMode_v3.Layout2x2);
            StatusMessage = CameraCount == 0 ? "No camera groups are available for the selected profile." : "Live monitoring is ready.";
        }

        public ObservableCollection<LiveCameraGroupViewModel_v3> CameraGroups { get; private set; }
        public ObservableCollection<LiveSlotViewModel_v3> Slots { get; private set; }
        public int CameraCount { get { return _allCameras.Count; } }
        public int AiCameraCount { get { return _allCameras.Count(camera => string.Equals(camera.type, "ai_processed", StringComparison.OrdinalIgnoreCase) || (camera.Streams != null && camera.Streams.Any(stream => stream.IsAiMode == true))); } }
        public int GroupCount { get { return _sourceGroups.Count; } }
        public int ActiveCameraCount { get { return Slots.Count(slot => slot.Camera != null); } }
        public string StatusMessage { get; private set; }
        public LiveLayoutMode_v3 Layout { get { return _layout; } }
        public int CustomSlotCount { get { return _customSlotCount; } set { _customSlotCount = Math.Max(1, Math.Min(100, value)); OnPropertyChanged(); } }
        public string SearchText { get { return _searchText; } set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); ApplySearch(); } }
        public bool AiOnly { get { return _aiOnly; } set { if (_aiOnly == value) return; _aiOnly = value; OnPropertyChanged(); ApplySearch(); } }

        public void SetLayout(LiveLayoutMode_v3 layout)
        {
            var previous = Slots.Select(slot => new
            {
                slot.Camera,
                slot.SelectedStream,
                slot.State,
                slot.ErrorMessage,
                slot.RetryCount,
                slot.ConnectedAtUtc
            }).ToList();
            _layout = layout;
            var count = GetSlotCount(layout);
            Slots.Clear();
            for (var index = 0; index < count; index++)
            {
                var slot = new LiveSlotViewModel_v3 { SlotId = index + 1 };
                if (index < previous.Count && previous[index].Camera != null)
                {
                    slot.Camera = previous[index].Camera;
                    slot.SelectedStream = previous[index].SelectedStream;
                    slot.State = previous[index].State;
                    slot.ErrorMessage = previous[index].ErrorMessage;
                    slot.RetryCount = previous[index].RetryCount;
                    slot.ConnectedAtUtc = previous[index].ConnectedAtUtc;
                }
                Slots.Add(slot);
            }
            OnPropertyChanged(nameof(Layout));
            OnPropertyChanged(nameof(ActiveCameraCount));
        }

        public void ApplyCustomLayout(int count)
        {
            CustomSlotCount = count;
            SetLayout(LiveLayoutMode_v3.Custom);
        }

        public LiveSlotViewModel_v3 ToggleCamera(Camera camera)
        {
            if (camera == null) return null;
            var existing = Slots.FirstOrDefault(slot => SameCamera(slot.Camera, camera));
            if (existing != null)
            {
                ClearSlot(existing);
                return existing;
            }
            var empty = Slots.FirstOrDefault(slot => slot.Camera == null);
            if (empty == null) return null;
            AssignCamera(empty, camera);
            return empty;
        }

        public IList<LiveSlotViewModel_v3> FillFromCamera(Camera camera)
        {
            var visible = CameraGroups.SelectMany(group => group.Cameras).ToList();
            var start = visible.FindIndex(item => SameCamera(item, camera));
            if (start < 0) return new List<LiveSlotViewModel_v3>();
            var assigned = new List<LiveSlotViewModel_v3>();
            // Double-click fills forward without rebuilding the grid. Keep
            // every existing camera/pipeline and use only currently empty
            // slots for the selected camera and its following cameras.
            var selectedSlot = Slots.FirstOrDefault(slot => SameCamera(slot.Camera, camera));
            if (selectedSlot != null)
                assigned.Add(selectedSlot);
            else
            {
                selectedSlot = Slots.FirstOrDefault(slot => slot.Camera == null);
                if (selectedSlot == null) return assigned;
                AssignCamera(selectedSlot, camera);
                assigned.Add(selectedSlot);
            }

            for (var sourceIndex = start + 1; sourceIndex < visible.Count; sourceIndex++)
            {
                var next = visible[sourceIndex];
                if (Slots.Any(slot => SameCamera(slot.Camera, next))) continue;
                var emptySlot = Slots.FirstOrDefault(slot => slot.Camera == null);
                if (emptySlot == null) break;
                AssignCamera(emptySlot, next);
                assigned.Add(emptySlot);
            }
            return assigned;
        }

        public void AssignCamera(LiveSlotViewModel_v3 slot, Camera camera)
        {
            slot.Camera = camera;
            slot.SelectedStream = SelectDefaultStream(camera);
            slot.ErrorMessage = null;
            slot.RetryCount = 0;
            slot.State = LiveConnectionState_v3.Offline;
            OnPropertyChanged(nameof(ActiveCameraCount));
        }

        public void ClearSlot(LiveSlotViewModel_v3 slot)
        {
            slot.Camera = null;
            slot.SelectedStream = null;
            slot.ErrorMessage = null;
            slot.RetryCount = 0;
            slot.State = LiveConnectionState_v3.Empty;
            OnPropertyChanged(nameof(ActiveCameraCount));
        }

        public void ClearAll()
        {
            foreach (var slot in Slots) ClearSlot(slot);
        }

        public void SwapSlots(LiveSlotViewModel_v3 first, LiveSlotViewModel_v3 second)
        {
            if (first == null || second == null || ReferenceEquals(first, second)) return;
            var camera = first.Camera;
            var stream = first.SelectedStream;
            first.Camera = second.Camera;
            first.SelectedStream = second.SelectedStream;
            second.Camera = camera;
            second.SelectedStream = stream;
            first.State = first.Camera == null ? LiveConnectionState_v3.Empty : LiveConnectionState_v3.Offline;
            second.State = second.Camera == null ? LiveConnectionState_v3.Empty : LiveConnectionState_v3.Offline;
            first.ErrorMessage = null;
            second.ErrorMessage = null;
            first.RetryCount = 0;
            second.RetryCount = 0;
            OnPropertyChanged(nameof(ActiveCameraCount));
        }

        private int GetSlotCount(LiveLayoutMode_v3 layout)
        {
            switch (layout)
            {
                case LiveLayoutMode_v3.Layout1x1: return 1;
                case LiveLayoutMode_v3.Layout2x2: return 4;
                case LiveLayoutMode_v3.Layout5Plus1: return 6;
                case LiveLayoutMode_v3.Layout3x3: return 9;
                case LiveLayoutMode_v3.Layout16Plus1: return 17;
                case LiveLayoutMode_v3.Layout6x6: return 36;
                default: return CustomSlotCount;
            }
        }

        private void ApplySearch()
        {
            var query = (_searchText ?? string.Empty).Trim();
            CameraGroups.Clear();
            foreach (var group in _sourceGroups.OrderBy(item => item.name))
            {
                var groupMatch = Contains(group.name, query);
                var cameras = (group.Cameras ?? new ObservableCollection<Camera>())
                    .Where(camera => !_aiOnly || IsAiCamera(camera))
                    .Where(camera => groupMatch || CameraMatches(camera, query))
                    .OrderBy(camera => camera.name)
                    .ToList();
                if (cameras.Count == 0) continue;
                CameraGroups.Add(new LiveCameraGroupViewModel_v3
                {
                    Name = group.name ?? "Ungrouped",
                    Cameras = new ObservableCollection<Camera>(cameras),
                    CameraItems = new ObservableCollection<LiveCameraItemViewModel_v3>(cameras.Select(camera => new LiveCameraItemViewModel_v3(camera))),
                    IsExpanded = !string.IsNullOrEmpty(query)
                });
            }
            OnPropertyChanged(nameof(CameraGroups));
        }

        public void RefreshCameraIndicators()
        {
            foreach (var group in CameraGroups)
            foreach (var item in group.CameraItems)
            {
                var slot = Slots.FirstOrDefault(candidate => SameCamera(candidate.Camera, item.Camera));
                item.IsSelected = slot != null;
                item.StateText = slot == null ? "Available" : slot.StatusText;
                item.IsConnected = slot != null && slot.State == LiveConnectionState_v3.Connected;
                item.IsConnecting = slot != null &&
                    (slot.State == LiveConnectionState_v3.Connecting ||
                     slot.State == LiveConnectionState_v3.Disconnecting);
                item.HasConnectionError = slot != null &&
                    (slot.State == LiveConnectionState_v3.Error ||
                     slot.State == LiveConnectionState_v3.Retrying);
            }
        }

        private static bool CameraMatches(Camera camera, string query)
        {
            return string.IsNullOrEmpty(query) || Contains(camera.name, query) || Contains(camera.camID, query) ||
                   Contains(camera.long_Name, query) || Contains(camera.description, query);
        }
        private static bool IsAiCamera(Camera camera)
        {
            return camera != null && (string.Equals(camera.type, "ai_processed", StringComparison.OrdinalIgnoreCase) ||
                (camera.Streams != null && camera.Streams.Any(stream => stream.IsAiMode == true)));
        }
        private static bool Contains(string value, string query) { return string.IsNullOrEmpty(query) || (!string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0); }
        private static bool SameCamera(Camera left, Camera right) { return left != null && right != null && string.Equals(left.camID, right.camID, StringComparison.OrdinalIgnoreCase); }
        private static CameraStreamInfo SelectDefaultStream(Camera camera)
        {
            return camera.Streams == null ? null : camera.Streams.FirstOrDefault(stream => string.Equals(stream.StreamType, "main", StringComparison.OrdinalIgnoreCase)) ?? camera.Streams.FirstOrDefault();
        }
    }
}
