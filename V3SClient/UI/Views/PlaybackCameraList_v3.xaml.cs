using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using V3SClient.models;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class PlaybackCameraList_v3 : UserControl
    {
        private PlaybackCameraListViewModel_v3 _viewModel;
        public event EventHandler<Camera> CameraSelectionRequested;

        public PlaybackCameraList_v3()
        {
            InitializeComponent();
        }

        public void SetCameraGroups(ObservableCollection<VMTalkGroup> groups)
        {
            _viewModel = new PlaybackCameraListViewModel_v3(groups);
            DataContext = _viewModel;
        }

        public void SetSelectedCameras(IEnumerable<Camera> cameras)
        {
            if (_viewModel != null) _viewModel.SetSelectedCameras(cameras);
        }

        private void Camera_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var camera = border == null ? null : border.Tag as Camera;
            if (camera == null) return;
            CameraSelectionRequested?.Invoke(this, camera);
            e.Handled = true;
        }

        private void AllFilter_Click(object sender, RoutedEventArgs e) { if (_viewModel == null) return; _viewModel.AiOnly = false; _viewModel.RecordingOnly = false; }
        private void AiFilter_Click(object sender, RoutedEventArgs e) { if (_viewModel != null) _viewModel.AiOnly = !_viewModel.AiOnly; }
        private void RecordingFilter_Click(object sender, RoutedEventArgs e) { if (_viewModel != null) _viewModel.RecordingOnly = !_viewModel.RecordingOnly; }
    }
}
