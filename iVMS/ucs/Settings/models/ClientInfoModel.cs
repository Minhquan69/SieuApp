using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.ucs.Settings.models
{
    public class ClientInfoModel
    {
        public int Index { get; set; }
        public Guid Id { get; set; }
        public string ClientInfo_Name { get; set; }
        public string ClientInfo_Code { get; set; }
        public string ClientInfo_Description { get; set; }

        public List<string> CameraIds { get; set; } = new List<string>();
        public List<int> AccountIds { get; set; } = new List<int>();
    }
}

















