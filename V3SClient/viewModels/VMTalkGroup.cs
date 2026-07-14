using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using V3SClient.libs;

namespace V3SClient.viewModels
{
    public class VMTalkGroup : INotifyPropertyChanged
    {
        public string name { get; set; }
        public string groupID { get; set; }
        public Visibility AllowSelecting { get; set; } = Visibility.Collapsed;

        private Visibility _nodeVisibility = Visibility.Visible;
        public Visibility NodeVisibility
        {
            get => _nodeVisibility;
            set
            {
                if (_nodeVisibility != value)
                {
                    _nodeVisibility = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NodeVisibility)));
                }
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                }
            }
        }

        public ObservableCollection<VMTalkGroup> SubGroups { get; set; } = new ObservableCollection<VMTalkGroup>();
        public ObservableCollection<models.Camera> Cameras { get; set; } = new ObservableCollection<models.Camera>();

        public IEnumerable<object> Items => SubGroups.Cast<object>().Concat(Cameras.Cast<object>());

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyItemsChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));

        public VMTalkGroup() { }

        public VMTalkGroup(models.TalkGroup group, List<models.Camera> allcameras)
        {
            if (group != null)
            {
                name = group.name;
                groupID = group.groupID;
                var cams = allcameras.Where(x => x.groupID == groupID && x.is_Live == true).OrderBy(x => x.name, new NaturalStringComparer()).ToList();
                foreach (var cam in cams)
                    Cameras.Add(cam);
            }
            else
            {
                name = "None";
                groupID = "None";
                var cams = allcameras.Where(x => (x.groupID == "None" || x.groupID == "Uknow") && x.is_Live == true).OrderBy(x => x.name, new NaturalStringComparer()).ToList();
                foreach (var cam in cams)
                    Cameras.Add(cam);
            }
        }
    }
}















