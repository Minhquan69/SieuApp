using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace V3SClient.libs
{
    public abstract class BaseColumnDefinition
    {
        public string Header { get; set; }
        public string BindingPath { get; set; }
        public double Width { get; set; } = double.NaN;
        public bool FillRemainingSpace { get; set; } = false;
        public BaseColumnDefinition(string bindingPath, string header)
        {
            BindingPath = bindingPath;
            Header = header;
        }

        public abstract DataGridColumn GenerateColumn();
        protected DataGridLength ResolveWidth()
        {
            return FillRemainingSpace
                ? new DataGridLength(1, DataGridLengthUnitType.Star)
                : double.IsNaN(Width)
                    ? new DataGridLength(1, DataGridLengthUnitType.Auto)
                    : new DataGridLength(Width);
        }
    }

}















