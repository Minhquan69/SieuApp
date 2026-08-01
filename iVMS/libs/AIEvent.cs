using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.libs
{
    public class AIEvent
    {
        public Guid Server_id { get; set; }                // GUID
        public string CamID { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Detect_Object_id { get; set; }
        public string Is_BlackList { get; set; }
        public string Location { get; set; }
        public string Type { get; set; }
        public string Image_Path { get; set; }
        public string Placehold { get; set; }
    }
}















