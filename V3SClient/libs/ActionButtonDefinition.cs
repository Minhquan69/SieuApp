using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace V3SClient.libs
{
    public class ActionButtonDefinition
    {
        public string ImagePath { get; set; }
        public ICommand Command { get; set; }
        public string ToolTip { get; set; }
        public double ButtonSize { get; set; } = 24;

        public ActionButtonDefinition(string imagePath, ICommand command, string toolTip = null)
        {
            ImagePath = imagePath;
            Command = command;
            ToolTip = toolTip;
        }
    }
}















