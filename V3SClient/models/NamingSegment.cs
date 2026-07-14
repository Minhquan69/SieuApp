using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using V3SClient.viewModels;

namespace VehicleDocumentProcessing.WPF.Models
{
    public class NamingSegment : VMBase
    {
        private string _segmentType;
        public string SegmentType
        {
            get => _segmentType;
            set
            {
                _segmentType = value;
                OnPropertyChanged();
            }
        }

        private string _value;
        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged();
            }
        }

        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set
            {
                _displayName = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<CharReplacementRule> _replacementRules = new ObservableCollection<CharReplacementRule>();
        public ObservableCollection<CharReplacementRule> ReplacementRules
        {
            get => _replacementRules;
            set
            {
                _replacementRules = value;
                OnPropertyChanged();
            }
        }
    }

    public class CharReplacementRule : VMBase
    {
        private string _sourceChars;
        public string SourceChars
        {
            get => _sourceChars;
            set
            {
                _sourceChars = value;
                OnPropertyChanged();
            }
        }

        private string _targetChar;
        public string TargetChar
        {
            get => _targetChar;
            set
            {
                _targetChar = value;
                OnPropertyChanged();
            }
        }
    }

    public class NamingFieldDefinition
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string SampleValue { get; set; }
    }

    public class NamingRule : VMBase
    {
        private string _separator = "_";
        public string Separator
        {
            get => _separator;
            set
            {
                _separator = value;
                OnPropertyChanged();
            }
        }

        private System.Collections.ObjectModel.ObservableCollection<NamingSegment> _segments = new System.Collections.ObjectModel.ObservableCollection<NamingSegment>();
        public System.Collections.ObjectModel.ObservableCollection<NamingSegment> Segments
        {
            get => _segments;
            set
            {
                _segments = value;
                OnPropertyChanged();
            }
        }
    }
}
