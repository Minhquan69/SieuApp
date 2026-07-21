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
using V3SClient.libs;
using V3SClient.ucs.Settings.viewmodels;
using V3SClient.viewModels;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for ViewCamGroupWithOrganization.xaml
    /// </summary>
    public partial class ViewCamGroupWithOrganization : UserControl
    {
        public event EventHandler<CamInfoNode> Event_Camera_Selection_Changed;
        public event EventHandler<List<CamInfoNode>> Event_Node_Selected;
        public ObservableCollection<AreaNode> AreaTree => GlobalUserInfo.Instance.AreaTree;
        public ViewCamGroupWithOrganization()
        {
            InitializeComponent();
            this.DataContext = this;
            MyTreeView.AddHandler(TreeViewItem.MouseDoubleClickEvent,
            new RoutedEventHandler(TreeViewItem_DoubleClick));
        }

        private void TreeViewItem_DoubleClick(object sender, RoutedEventArgs e)
        {
            // Lấy item được double click
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext != null)
            {
                var item = fe.DataContext;
                if (item is CamInfoNode cam)
                {
                    Event_Node_Selected?.Invoke(this, new List<CamInfoNode> { cam });
                    return;

                }
                else if (item is UnitNode unit)
                {
                    var visibleCams = GetVisibleCamsFromUnit(unit);
                    if (visibleCams.Any())
                    {
                        Event_Node_Selected?.Invoke(this, visibleCams);
                    }
                }
                else if (item is AreaNode area)
                {
                    var visibleCams = GetVisibleCamsFromArea(area);
                    if (visibleCams.Any())
                    {
                        Event_Node_Selected?.Invoke(this, visibleCams);
                    }
                }
            }

            e.Handled = true; // tránh bubble event
        }
        private void Camera_CheckedChanged(object sender, RoutedEventArgs e)
        {
            var checkedCams = new List<CamInfoNode>();
            if (AreaTree != null)
            {
                foreach (var area in AreaTree)
                {
                    GetCheckedCamsRecursive(area, checkedCams);
                }
            }
            Event_Node_Selected?.Invoke(this, checkedCams);
        }

        private void GetCheckedCamsRecursive(AreaNode area, List<CamInfoNode> result)
        {
            if (area.Units != null)
            {
                foreach (var unit in area.Units)
                {
                    GetCheckedCamsRecursive(unit, result);
                }
            }
        }

        private void GetCheckedCamsRecursive(UnitNode unit, List<CamInfoNode> result)
        {
            if (unit.Cams != null)
            {
                foreach (var cam in unit.Cams)
                {
                    if (cam.IsChecked) result.Add(cam);
                }
            }
            if (unit.SubUnits != null)
            {
                foreach (var sub in unit.SubUnits)
                {
                    GetCheckedCamsRecursive(sub, result);
                }
            }
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            //if (e.NewValue == null) return;

            //var result = new List<CamInfoNode>();

            //switch (e.NewValue)
            //{
            //    case CamInfoNode camNode:
            //        // Node lá (camera)
            //        result.Add(camNode);
            //        break;
            //    case BaseUnitNode baseUnit:
            //        // Node BaseUnit → lấy toàn bộ Camera con
            //        result.AddRange(GetAllCamsFromBaseUnit(baseUnit));
            //        break;

            //    case ProvinceNode province:
            //        // Node Province → lấy toàn bộ Camera con 
            //        result.AddRange(GetAllCamsFromProvince(province));
            //        break;
            //}

            //if (result.Count > 0)
            //    Event_Node_Selected?.Invoke(this, result);
        }
        private List<CamInfoNode> GetAllCamsFromArea(AreaNode area)
        {
            var cams = new List<CamInfoNode>();
            foreach (var unit in area.Units)
            {
                cams.AddRange(GetAllCamsFromUnit(unit));
            }
            return cams;
        }

        private List<CamInfoNode> GetAllCamsFromUnit(UnitNode unit)
        {
            var cams = new List<CamInfoNode>();
            foreach (var cam in unit.Cams)
            {
                cams.Add(cam);
            }

            foreach (var sub in unit.SubUnits)
            {
                cams.AddRange(GetAllCamsFromUnit(sub));
            }
            return cams;
        }

        private List<CamInfoNode> GetVisibleCamsFromArea(AreaNode area)
        {
            var cams = new List<CamInfoNode>();
            foreach (var unit in area.Units)
            {
                cams.AddRange(GetVisibleCamsFromUnit(unit));
            }
            return cams;
        }

        private List<CamInfoNode> GetVisibleCamsFromUnit(UnitNode unit)
        {
            var cams = new List<CamInfoNode>();
            foreach (var cam in unit.Cams)
            {
                if (cam.NodeVisibility == Visibility.Visible)
                {
                    cams.Add(cam);
                }
            }

            foreach (var sub in unit.SubUnits)
            {
                cams.AddRange(GetVisibleCamsFromUnit(sub));
            }
            return cams;
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
        public void FilterTree(string keyword, bool isAIMode = false, bool isRecordingMode = false)
        {
            if (AreaTree == null) return;
            foreach (var area in AreaTree)
            {
                FilterArea(area, keyword, isAIMode, isRecordingMode);
            }
        }

        private bool FilterArea(AreaNode area, string keyword, bool isAIMode, bool isRecordingMode)
        {
            bool anyChildVisible = false;

            foreach (var unit in area.Units)
            {
                bool match = FilterUnit(unit, keyword, isAIMode, isRecordingMode);
                if (match) anyChildVisible = true;
            }

            bool groupMatch = string.IsNullOrEmpty(keyword) || (area.Name != null && area.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

            bool isVisible = groupMatch || anyChildVisible;
            area.NodeVisibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!string.IsNullOrEmpty(keyword) && anyChildVisible)
                area.IsExpanded = true;
            else if (string.IsNullOrEmpty(keyword))
                area.IsExpanded = false;

            return isVisible;
        }

        private bool FilterUnit(UnitNode unit, string keyword, bool isAIMode, bool isRecordingMode)
        {
            bool anyChildVisible = false;

            foreach (var cam in unit.Cams)
            {
                bool matchKeyword = string.IsNullOrEmpty(keyword) ||
                             (cam.Name != null && cam.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                             (cam.LongName != null && cam.LongName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                             
                bool matchAI = !isAIMode || (cam.CamData != null && cam.CamData.HasAIStream);
                bool matchRecording = !isRecordingMode || (cam.CamData != null && cam.CamData.is_recording);
                bool match = matchKeyword && matchAI && matchRecording;
                
                cam.NodeVisibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match) anyChildVisible = true;
            }

            foreach (var sub in unit.SubUnits)
            {
                bool match = FilterUnit(sub, keyword, isAIMode, isRecordingMode);
                if (match) anyChildVisible = true;
            }

            bool groupMatch = string.IsNullOrEmpty(keyword) || (unit.Name != null && unit.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

            bool isVisible = groupMatch || anyChildVisible;
            unit.NodeVisibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!string.IsNullOrEmpty(keyword) && anyChildVisible)
                unit.IsExpanded = true;
            else if (string.IsNullOrEmpty(keyword))
                unit.IsExpanded = false;

            return isVisible;
        }
    }
}
