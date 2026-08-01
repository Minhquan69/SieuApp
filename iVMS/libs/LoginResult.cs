using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.libs
{
    public class LoginResult
    {
        public bool Success { get; set; }
        public string UserId { get; set; }
        public string Message { get; set; }
        public LoginResult(bool success, string userId, string message)
        {
            Success = success;
            UserId = userId;
            Message = message;
        }
    }
}















