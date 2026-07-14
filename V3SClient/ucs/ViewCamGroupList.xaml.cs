using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using V3SClient.models;
using V3SClient.viewModels;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for VoiceGroups.xaml
    /// </summary>
    public partial class ViewCamGroupList : UserControl
    {
        public ObservableCollection<viewModels.VMTalkGroup> CameraGroupList { get; set; }
            = new ObservableCollection<viewModels.VMTalkGroup>();


        // Notify cho Parent chứa user control

        public event EventHandler<Camera> Event_Camera_Seleced_Changed;
       
        public event EventHandler<VMTalkGroup> Event_Selected_Voice_GroupTalk_Changed;
        public event EventHandler<List<Camera>> Event_Node_Selected_Cameras;
        public event EventHandler<List<Camera>> Event_Selected_Voice_OneTalk_Changed;
        public ViewCamGroupList()
        {

            InitializeComponent();
            DataContext = this;

            MyTreeView.AddHandler(TreeViewItem.MouseDoubleClickEvent,
           new RoutedEventHandler(TreeViewItem_DoubleClick));
        }
        private void TreeViewItem_DoubleClick(object sender, RoutedEventArgs e)
        {
            // Lấy item được double click
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext != null)
            {
                var item = fe.DataContext;
                if (item is Camera cam)
                {
                    Event_Node_Selected_Cameras?.Invoke(this, new List<Camera> { cam });
                    return;

                }
                else if (item is VMTalkGroup group)
                {
                    if (group.Cameras != null && group.Cameras.Any())
                    {
                        // Chỉ lấy các camera đang hiển thị (đã lọc), không lấy toàn bộ
                        var visibleCameras = GetVisibleCameras(group);
                        if (visibleCameras.Any())
                        {
                            Event_Node_Selected_Cameras?.Invoke(this, visibleCameras);
                        }
                    }
                }
            }

            e.Handled = true; // tránh bubble event
        }
        private void MyTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            //if (e.NewValue == null)
            //    return;

            //// Trường hợp chọn 1 group
            //if (e.NewValue is VMTalkGroup selectedVoiceGroup)
            //{
            //    if (selectedVoiceGroup.Cameras != null && selectedVoiceGroup.Cameras.Any())
            //    {
            //        Event_Node_Selected_Cameras?.Invoke(this, selectedVoiceGroup.Cameras.ToList());
            //    }
            //    return;
            //}

            //// Trường hợp chọn 1 camera
            //if (e.NewValue is Camera selectedCamera)
            //{
            //    Event_Node_Selected_Cameras?.Invoke(this, new List<Camera> { selectedCamera });
            //    return;
            //}
        }


        private void TalkToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (MyTreeView.SelectedItem is VMTalkGroup group)
            {
                if (group.name.StartsWith("None"))
                    return;
                // Bắn event cho group talk
                Event_Selected_Voice_GroupTalk_Changed?.Invoke(this, group);
            }
        }
        private void TalkToCamera_Click(object sender, RoutedEventArgs e)
        {
            if (MyTreeView.SelectedItem is Camera cam)
            {
                // Bắn event cho camera talk
                Event_Selected_Voice_OneTalk_Changed?.Invoke(this,new List<Camera> { cam });
            }
        }
        public void Camera_CheckedChanged(object sender, RoutedEventArgs e)
        {
            var checkedCameras = new List<Camera>();
            foreach (var group in CameraGroupList)
            {
                GetCheckedCamerasRecursive(group, checkedCameras);
            }
            Event_Node_Selected_Cameras?.Invoke(this, checkedCameras);
        }

        private void GetCheckedCamerasRecursive(VMTalkGroup group, List<Camera> result)
        {
            if (group.Cameras != null)
            {
                foreach (var cam in group.Cameras)
                {
                    if (cam.IsChecked) result.Add(cam);
                }
            }
            if (group.SubGroups != null)
            {
                foreach (var sub in group.SubGroups)
                {
                    GetCheckedCamerasRecursive(sub, result);
                }
            }
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindParent<T>(parentObject);
        }
        private List<Camera> GetVisibleCameras(VMTalkGroup group)
        {
            var result = new List<Camera>();
            if (group.Cameras != null)
            {
                foreach (var cam in group.Cameras)
                {
                    if (cam.NodeVisibility == Visibility.Visible)
                    {
                        result.Add(cam);
                    }
                }
            }
            if (group.SubGroups != null)
            {
                foreach (var sub in group.SubGroups)
                {
                    result.AddRange(GetVisibleCameras(sub));
                }
            }
            return result;
        }

        public void FilterTree(string keyword, bool isAIMode = false)
        {
            if (CameraGroupList == null) return;
            foreach (var group in CameraGroupList)
            {
                FilterTalkGroup(group, keyword, isAIMode);
            }
        }

        private bool FilterTalkGroup(VMTalkGroup group, string keyword, bool isAIMode)
        {
            bool anyChildVisible = false;

            // Filter cameras
            foreach (var cam in group.Cameras)
            {
                bool matchKeyword = string.IsNullOrEmpty(keyword) ||
                             (cam.name != null && cam.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                             (cam.long_Name != null && cam.long_Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                             
                bool matchAI = !isAIMode || cam.HasAIStream;
                bool match = matchKeyword && matchAI;
                
                cam.NodeVisibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) anyChildVisible = true;
            }

            // Filter subgroups
            foreach (var sub in group.SubGroups)
            {
                bool match = FilterTalkGroup(sub, keyword, isAIMode);
                if (match) anyChildVisible = true;
            }

            bool groupMatch = string.IsNullOrEmpty(keyword) || (group.name != null && group.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

            bool isVisible = groupMatch || anyChildVisible;
            group.NodeVisibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!string.IsNullOrEmpty(keyword) && anyChildVisible)
                group.IsExpanded = true;
            else if (string.IsNullOrEmpty(keyword))
                group.IsExpanded = false;

            return isVisible;
        }
    }
}
