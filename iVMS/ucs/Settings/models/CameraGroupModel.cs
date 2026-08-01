using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using V3SClient.viewModels;

namespace V3SClient.ucs.Settings.models
{
    public class CameraGroupModel
    {
        public int Index { get; set; }
        public Guid CameraGroup_Id { get; set; }
        public Guid? CameraGroup_Parent_Id { get; set; }
        public string CameraGroup_Code { get; set; } = "100";
        public string CameraGroup_Name { get; set; } = "Unit name";
        public string CameraGroup_Type { get; set; } = "geographical";
        public string Description { get; set; }
        public int? TalkId { get; set; }
        public bool Active { get; set; } = false;
    }
    

}

















