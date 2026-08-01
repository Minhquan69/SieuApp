using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.libs
{
    public class FileStorageInfo
    {
        public bool IsSearchDB { get; set; } = true;
        public string ParentPath { get; set; }
        public string ServerId { get; set; }
        public string CamId { get; set; }
        public DateTime TimeStart { get; set; }
        public DateTime TimeEnd { get; set; }
        public List<LocationData> Locations { get; set; }
        public string RemotePath { get; set; }
        public string LocalPath { get; set; } = "";
    }
}















