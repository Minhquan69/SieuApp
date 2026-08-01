using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class DisplayInfoAttribute : Attribute
    {
        public string DisplayName { get; set; }
        public int Order { get; set; } = int.MaxValue; 
        public bool IsVisible { get; set; } = true;

        public DisplayInfoAttribute(string displayName, int order = int.MaxValue, bool isVisible = true)
        {
            DisplayName = displayName;
            Order = order;
            IsVisible = isVisible;
        }
    }
}















