using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.models
{
    public class VideoStorage
    {
        public string Name { get; set; }
        public string Location { get; set; }
        public int Port { get; set; } = 21;
        public VideoStorage() 
        {
            Name = "Local";
            Location = "C:\tmp\videos";
        
        }
        public VideoStorage(string name, string location,int port=21)
        {
            Name = name;    
            Location = location;
            Port = port;
        }
        public override string ToString()
        {
            return Name;
        }
        public VideoStorage Clone()
        {
            return (VideoStorage)this.MemberwiseClone();
        }
    }
}















