using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using V3SClient.models;

namespace V3SClient.viewModels
{
    public sealed class PlaybackCameraListGroup_v3
    {
        public string Name { get; set; }
        public bool IsExpanded { get; set; }
        public ObservableCollection<PlaybackCameraListItem_v3> CameraItems { get; set; }
        public int Count { get { return CameraItems == null ? 0 : CameraItems.Count; } }
    }

    public sealed class PlaybackCameraListItem_v3 : INotifyPropertyChanged
    {
        private bool _isSelected;

        public PlaybackCameraListItem_v3(Camera camera) { Camera = camera; }
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
        public bool IsAiCamera { get { return Camera != null && (Camera.HasAIStream || string.Equals(Camera.type, "ai_processed", StringComparison.OrdinalIgnoreCase) || (Camera.Streams != null && Camera.Streams.Any(stream => stream != null && stream.IsAiMode == true))); } }
        public bool IsRecording { get { return Camera != null && Camera.is_recording; } }
        public bool IsSelected { get { return _isSelected; } set { if (_isSelected == value) return; _isSelected = value; OnChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string propertyName = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }
    }

    public sealed class PlaybackCameraListViewModel_v3 : INotifyPropertyChanged
    {
        private readonly List<VMTalkGroup> _sourceGroups;
        private string _searchText;
        private bool _aiOnly;
        private bool _recordingOnly;
        private readonly List<string> _selectedCameraIds = new List<string>();

        public PlaybackCameraListViewModel_v3(IEnumerable<VMTalkGroup> groups)
        {
            _sourceGroups = (groups ?? Enumerable.Empty<VMTalkGroup>()).Where(group => group != null).ToList();
            CameraGroups = new ObservableCollection<PlaybackCameraListGroup_v3>();
            SelectedCameraItems = new ObservableCollection<PlaybackCameraListItem_v3>();
            ApplyFilters();
        }

        public ObservableCollection<PlaybackCameraListGroup_v3> CameraGroups { get; private set; }
        public ObservableCollection<PlaybackCameraListItem_v3> SelectedCameraItems { get; private set; }
        public int SelectedCount { get { return SelectedCameraItems.Count; } }
        public int CameraCount { get { return AllCameras().Count(); } }
        public int AiCameraCount { get { return AllCameras().Count(IsAiCamera); } }
        public int RecordingCameraCount { get { return AllCameras().Count(camera => camera.is_recording); } }
        public int GroupCount { get { return _sourceGroups.Count; } }
        public string SearchText { get { return _searchText; } set { if (_searchText == value) return; _searchText = value; OnChanged(); ApplyFilters(); } }
        public bool AiOnly { get { return _aiOnly; } set { if (_aiOnly == value) return; _aiOnly = value; if (value) _recordingOnly = false; OnChanged(); OnChanged(nameof(RecordingOnly)); ApplyFilters(); } }
        public bool RecordingOnly { get { return _recordingOnly; } set { if (_recordingOnly == value) return; _recordingOnly = value; if (value) _aiOnly = false; OnChanged(); OnChanged(nameof(AiOnly)); ApplyFilters(); } }

        public void SetSelectedCameras(IEnumerable<Camera> selected)
        {
            var selectedCameras = (selected ?? Enumerable.Empty<Camera>())
                .Where(camera => camera != null && !string.IsNullOrWhiteSpace(camera.camID))
                .ToList();
            _selectedCameraIds.Clear();
            _selectedCameraIds.AddRange(selectedCameras.Select(camera => camera.camID.Trim()));
            SelectedCameraItems.Clear();
            foreach (var camera in selectedCameras)
                SelectedCameraItems.Add(new PlaybackCameraListItem_v3(camera) { IsSelected = true });
            OnChanged(nameof(SelectedCount));
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var query = (_searchText ?? string.Empty).Trim();
            var selectedIds = new HashSet<string>(_selectedCameraIds, StringComparer.OrdinalIgnoreCase);
            CameraGroups.Clear();
            foreach (var group in _sourceGroups.OrderBy(group => group.name))
            {
                var groupMatches = Contains(group.name, query);
                var cameras = (group.Cameras ?? new ObservableCollection<Camera>())
                    .Where(camera => camera != null)
                    .Where(camera => !_aiOnly || IsAiCamera(camera))
                    .Where(camera => !_recordingOnly || camera.is_recording)
                    .Where(camera => groupMatches || Matches(camera, query))
                    .Where(camera => string.IsNullOrWhiteSpace(camera.camID) || !selectedIds.Contains(camera.camID.Trim()))
                    .OrderBy(camera => camera.name)
                    .Select(camera => new PlaybackCameraListItem_v3(camera))
                    .ToList();
                if (cameras.Count == 0) continue;
                // Start compact on first entry. Once the user selects a
                // camera (or searches), keep matching groups open so the
                // remaining list does not disappear after synchronization.
                CameraGroups.Add(new PlaybackCameraListGroup_v3 { Name = string.IsNullOrWhiteSpace(group.name) ? "Chưa phân nhóm" : group.name, IsExpanded = _selectedCameraIds.Count > 0 || !string.IsNullOrWhiteSpace(query), CameraItems = new ObservableCollection<PlaybackCameraListItem_v3>(cameras) });
            }
            OnChanged(nameof(CameraGroups));
        }

        private IEnumerable<Camera> AllCameras() { return _sourceGroups.Where(group => group.Cameras != null).SelectMany(group => group.Cameras).Where(camera => camera != null).Distinct(); }
        private static bool IsAiCamera(Camera camera) { return camera != null && (camera.HasAIStream || string.Equals(camera.type, "ai_processed", StringComparison.OrdinalIgnoreCase) || (camera.Streams != null && camera.Streams.Any(stream => stream != null && stream.IsAiMode == true))); }
        private static bool Matches(Camera camera, string query) { return string.IsNullOrEmpty(query) || Contains(camera.camID, query) || Contains(camera.name, query) || Contains(camera.long_Name, query) || Contains(camera.description, query); }
        private static bool Contains(string value, string query) { return string.IsNullOrEmpty(query) || (!string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0); }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string propertyName = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }
    }
}
