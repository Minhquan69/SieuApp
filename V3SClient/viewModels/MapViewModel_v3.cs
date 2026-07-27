using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using V3SClient.models;

namespace V3SClient.viewModels
{
    public sealed class MapCameraItem_v3 : VMBase
    {
        private readonly Camera _camera;

        public MapCameraItem_v3(Camera camera, string groupName)
        {
            _camera = camera;
            GroupName = groupName;
        }

        public Camera Camera => _camera;
        public string Id => _camera.camID;
        public string DisplayName => string.IsNullOrWhiteSpace(_camera.name) ? _camera.camID : _camera.name;
        public string GroupName { get; }
        public bool IsAiCamera => _camera.HasAIStream;
        public bool HasLocation => _camera.Latitude.HasValue && _camera.Longitude.HasValue;
        public bool IsOnline => string.Equals(_camera.Status, "online", StringComparison.OrdinalIgnoreCase) || _camera.is_online == true;
        public bool IsAlert => string.Equals(_camera.Status, "alert", StringComparison.OrdinalIgnoreCase);
        public string Status => IsAlert ? "alert" : (IsOnline ? "online" : "offline");

        public void Refresh()
        {
            OnPropertyChanged(nameof(IsAiCamera));
            OnPropertyChanged(nameof(IsOnline));
            OnPropertyChanged(nameof(IsAlert));
            OnPropertyChanged(nameof(Status));
        }
    }

    public sealed class MapCameraGroup_v3 : VMBase
    {
        private bool _isExpanded;

        public MapCameraGroup_v3(string id, string name, IEnumerable<MapCameraItem_v3> cameras)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? "Không phân nhóm" : name;
            Cameras = new ObservableCollection<MapCameraItem_v3>(cameras);
        }

        public string Id { get; }
        public string Name { get; }
        public ObservableCollection<MapCameraItem_v3> Cameras { get; }
        public int CameraCount => Cameras.Count;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public sealed class MapViewModel_v3 : VMBase, IDisposable
    {
        private readonly ObservableCollection<MapCameraItem_v3> _allItems = new ObservableCollection<MapCameraItem_v3>();
        private readonly List<Camera> _subscribedCameras = new List<Camera>();
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>();
        private ObservableCollection<VMTalkGroup> _groups;
        private string _searchText = string.Empty;
        private bool _aiOnly;

        public MapViewModel_v3() { }

        public MapViewModel_v3(ObservableCollection<VMTalkGroup> groups, IEnumerable<Camera> cameras)
        {
            Refresh(groups, cameras);
        }

        public ObservableCollection<MapCameraGroup_v3> CameraGroups { get; } = new ObservableCollection<MapCameraGroup_v3>();
        public event EventHandler CameraStatesChanged;

        public string SearchText
        {
            get => _searchText;
            set
            {
                var normalized = value ?? string.Empty;
                if (_searchText == normalized) return;
                _searchText = normalized;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public bool AiOnly
        {
            get => _aiOnly;
            set
            {
                if (_aiOnly == value) return;
                _aiOnly = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public int CameraCount { get; private set; }
        public int OnlineCameraCount { get; private set; }
        public int GroupCount { get; private set; }
        public int AiCameraCount => _allItems.Count(item => item.IsAiCamera);
        public bool HasCameras => CameraCount > 0;

        public void Refresh(ObservableCollection<VMTalkGroup> groups, IEnumerable<Camera> cameras)
        {
            foreach (var camera in _subscribedCameras)
                camera.PropertyChanged -= Camera_PropertyChanged;
            _subscribedCameras.Clear();
            _allItems.Clear();
            _groups = groups;

            var selected = cameras == null
                ? new List<Camera>()
                : cameras.Where(camera => camera != null).GroupBy(camera => camera.camID).Select(group => group.First()).ToList();
            var selectedIds = new HashSet<string>(selected.Select(camera => camera.camID), StringComparer.OrdinalIgnoreCase);
            var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (groups != null)
            {
                foreach (var group in groups)
                {
                    if (group == null) continue;
                    foreach (var camera in group.Cameras.Where(camera => camera != null && selectedIds.Contains(camera.camID)))
                    {
                        if (knownIds.Add(camera.camID)) AddCamera(camera, group.name);
                    }
                }
            }

            foreach (var camera in selected.Where(camera => knownIds.Add(camera.camID)))
                AddCamera(camera, "Không phân nhóm");

            ApplyFilter();
        }

        public Camera FindCamera(string id)
        {
            return _allItems.Select(item => item.Camera).FirstOrDefault(camera =>
                string.Equals(camera.camID, id, StringComparison.OrdinalIgnoreCase));
        }

        public void ExpandAll()
        {
            foreach (var group in CameraGroups) { group.IsExpanded = true; _expanded[group.Id] = true; }
        }

        public void CollapseAll()
        {
            foreach (var group in CameraGroups) { group.IsExpanded = false; _expanded[group.Id] = false; }
        }

        private void AddCamera(Camera camera, string groupName)
        {
            var item = new MapCameraItem_v3(camera, groupName);
            _allItems.Add(item);
            camera.PropertyChanged += Camera_PropertyChanged;
            _subscribedCameras.Add(camera);
        }

        private void Camera_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Camera.Status)) return;
            var camera = sender as Camera;
            foreach (var item in _allItems.Where(item => ReferenceEquals(item.Camera, camera))) item.Refresh();
            ApplyFilter();
            CameraStatesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyFilter()
        {
            foreach (var group in CameraGroups) _expanded[group.Id] = group.IsExpanded;
            var query = _searchText.Trim();
            var items = _allItems.Where(item => !_aiOnly || item.IsAiCamera)
                .Where(item => query.Length == 0 || item.DisplayName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || item.GroupName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .ToList();

            CameraGroups.Clear();
            foreach (var group in items.GroupBy(item => item.GroupName))
            {
                bool expanded;
                if (!_expanded.TryGetValue(group.Key ?? string.Empty, out expanded)) expanded = false;
                CameraGroups.Add(new MapCameraGroup_v3(group.Key, group.Key, group) { IsExpanded = expanded });
            }

            CameraCount = items.Count;
            OnlineCameraCount = items.Count(item => item.IsOnline);
            GroupCount = CameraGroups.Count;
            OnPropertyChanged(nameof(CameraCount));
            OnPropertyChanged(nameof(OnlineCameraCount));
            OnPropertyChanged(nameof(GroupCount));
            OnPropertyChanged(nameof(AiCameraCount));
            OnPropertyChanged(nameof(HasCameras));
        }

        public void Dispose()
        {
            foreach (var camera in _subscribedCameras) camera.PropertyChanged -= Camera_PropertyChanged;
            _subscribedCameras.Clear();
        }
    }
}
