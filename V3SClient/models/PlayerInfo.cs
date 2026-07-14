using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.models
{
    public struct PlayerInfo
    {
        public PlayerStatus Key;
        public string Value;
    }

    public enum PlayerStatus
    {
      Log, 
        Name, 
        Position,
        Duration, 
        Stop,
        GPS,
        Eof,
    }
}















