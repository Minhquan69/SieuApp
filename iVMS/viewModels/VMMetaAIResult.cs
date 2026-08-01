using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using V3SClient.libs;
using V3SClient.models;


namespace V3SClient.viewModels
{
    
    public class VMMetaAIResult: VMBase 
    {
        public string logFileName { get; set; }
        public ObservableDictionary<string, MetaAIResult> logData { get;  } = new ObservableDictionary<string, MetaAIResult>();
     
        public VMMetaAIResult()
        {
            
        }
        public void Add(MetaAIResult result)
        {
            if (result == null) return;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                AddInternal(result);
                OnPropertyChanged(nameof(logData));
            });
        }
        
        public void Add(List<MetaAIResult> results)
        {
            if (results == null || results.Count == 0) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var result in results)
                {
                    AddInternal(result);
                }
                OnPropertyChanged(nameof(logData));
            });
        }

        private void AddInternal(MetaAIResult result)
        {
            string tracking_object_id = result.TrackingObjectIndex + "_" + result.MetaType;
            if (!logData.ContainsKey(tracking_object_id))
            {
                logData.Add(tracking_object_id, result);
                if (logData.Count > 1000)
                {
                    var firstKey = logData.Keys.ElementAt(0);
                    logData.Remove(firstKey);
                }
            }
            else if (logData[tracking_object_id].Confidence < result.Confidence)
            {
                logData[tracking_object_id].Confidence = result.Confidence;
                logData[tracking_object_id].IsBlackList = result.IsBlackList;
                logData[tracking_object_id].Caption = result.Caption;
                logData[tracking_object_id].ObjectID = result.ObjectID;
            }
        }

    }
}

















