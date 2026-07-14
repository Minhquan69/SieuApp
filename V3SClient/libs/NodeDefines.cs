using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;using System.Windows;

namespace V3SClient.libs
{
    public class CamInfoNode:INotifyPropertyChanged
    {
        public string Name => CamData?.CamInfo_Name;
        public string LongName => CamData?.CamInfo_LongName;
        public string Status => CamData?.Status;

        private string _activeStreamMode = "";
        public string ActiveStreamMode
        {
            get => _activeStreamMode;
            set
            {
                if (_activeStreamMode != value)
                {
                    _activeStreamMode = value;
                    OnPropertyChanged(nameof(ActiveStreamMode));
                }
            }
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                }
            }
        }
        private Visibility _allowSelecting = Visibility.Collapsed;
        public Visibility AllowSelecting
        {
            get => _allowSelecting;
            set
            {
                if (_allowSelecting != value)
                {
                    _allowSelecting = value;
                    OnPropertyChanged(nameof(AllowSelecting));
                }
            }
        }

        private Visibility _nodeVisibility = Visibility.Visible;
        public Visibility NodeVisibility
        {
            get => _nodeVisibility;
            set
            {
                if (_nodeVisibility != value)
                {
                    _nodeVisibility = value;
                    OnPropertyChanged(nameof(NodeVisibility));
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
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        private CamInfo _camData;
        public CamInfo CamData
        {
            get => _camData;
            set
            {
                if (_camData != value)
                {
                    if (_camData is INotifyPropertyChanged oldNotify)
                        oldNotify.PropertyChanged -= CamData_PropertyChanged;

                    _camData = value;

                    if (_camData is INotifyPropertyChanged newNotify)
                        newNotify.PropertyChanged += CamData_PropertyChanged;

                    OnPropertyChanged(nameof(CamData));
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(LongName));
                    OnPropertyChanged(nameof(Status));
                }
            }
        }
        private void CamData_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CamInfo.Status))
                OnPropertyChanged(nameof(Status));

            if (e.PropertyName == nameof(CamInfo.CamInfo_Name))
                OnPropertyChanged(nameof(Name));

            if (e.PropertyName == nameof(CamInfo.CamInfo_LongName))
                OnPropertyChanged(nameof(LongName));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class UnitNode : INotifyPropertyChanged
    {
        public string Name { get; set; }

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

        public ObservableCollection<UnitNode> SubUnits { get; set; } = new ObservableCollection<UnitNode>();
        public ObservableCollection<CamInfoNode> Cams { get; set; } = new ObservableCollection<CamInfoNode>();

        // Combined property for recursive tree display
        public IEnumerable<object> Items => SubUnits.Cast<object>().Concat(Cams.Cast<object>());

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyItemsChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
    }

    public class AreaNode : INotifyPropertyChanged
    {
        public string Name { get; set; }

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

        public ObservableCollection<UnitNode> Units { get; set; } = new ObservableCollection<UnitNode>();

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyItemsChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Units)));
    }
}















