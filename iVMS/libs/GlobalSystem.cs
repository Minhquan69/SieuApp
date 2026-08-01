using System;
using System.Collections.Generic;
using System.Linq;
using V3SClient.viewModels;

namespace V3SClient.libs
{
    public class GlobalSystem
    {
        #region Instance
        private static readonly Lazy<GlobalSystem> _instance = new Lazy<GlobalSystem>(() => new GlobalSystem());
        public static GlobalSystem Instance => _instance.Value;

        #endregion Instance

        #region Delegate & Events
        public event EventHandler EventConfigChange;
        #endregion Delegate & Events

        #region Field
        string[] allCocoClasses = new string[]
        {
            "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
            "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat",
            "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack",
            "umbrella", "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball",
            "kite", "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket",
            "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
            "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair",
            "couch", "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse",
            "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink",
            "refrigerator", "book", "clock", "vase", "scissors", "teddy bear", "hair drier",
            "toothbrush"
        };
        string[] allowedCocoClasses = new string[]
        {
            "person", "car", "bus", "truck", "motorcycle", "bicycle"
        };
 
        #endregion Field

        #region Properties
        public HashSet<string> CocoBlacklists => new HashSet<string>(
                allCocoClasses.Except(AllowCocoClass, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase
            );
        public HashSet<string> AllCocoClass => new HashSet<string>(
                allCocoClasses
            );
        public HashSet<string> AllowCocoClass => new HashSet<string>(
                MetaAIResultStorage.Instance.AllowedClasses,
                StringComparer.OrdinalIgnoreCase
            );
        
        public HashSet<string> AllowImageClasses => new HashSet<string>(
                MetaAIResultStorage.Instance.AllowedImageClasses,
                StringComparer.OrdinalIgnoreCase
            );
        
        public HashSet<string> AllowEvents => new HashSet<string>(
                MetaAIResultStorage.Instance.AllowedEvents,
                StringComparer.OrdinalIgnoreCase
            );
        
        public float MinConfidence => MetaAIResultStorage.Instance.MinConfidence;
        public VMTalkGroups CameraGroups { get; set; }
        public List<models.Camera> CameraList { get; set; }
        public HashSet<string> Devices { get; set; }

        #endregion Properties

        #region Method
        public void Init()
        {
            CameraGroups = new VMTalkGroups();
            
            // Lấy tất cả camera từ mọi cấp độ nhóm (đệ quy)
            var allCameras = new List<models.Camera>();
            if (CameraGroups.CamGroupList != null)
            {
                foreach (var group in CameraGroups.CamGroupList)
                {
                    allCameras.AddRange(GetAllCamerasRecursive(group));
                }
            }

            CameraList = allCameras
                .GroupBy(c => c.camID)
                .Select(g => g.First())
                .OrderBy(cam => cam.name, new NaturalStringComparer())
                .ToList();

            // Tăng tốc tìm kiếm
            Devices = new HashSet<string>(CameraList.Select(cam => cam.camID), StringComparer.OrdinalIgnoreCase);
        }

        private List<models.Camera> GetAllCamerasRecursive(VMTalkGroup group)
        {
            var cameras = new List<models.Camera>();
            if (group.Cameras != null)
                cameras.AddRange(group.Cameras);

            if (group.SubGroups != null)
            {
                foreach (var subGroup in group.SubGroups)
                {
                    cameras.AddRange(GetAllCamerasRecursive(subGroup));
                }
            }
            return cameras;
        }
        public void ReloadConfig()
        {
            Init();
            EventConfigChange?.Invoke(null,null);
        }
        #endregion Method
    }
}
