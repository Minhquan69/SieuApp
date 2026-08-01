using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using V3SClient.libs;
using V3SClient.libs.interfaces;

namespace V3SClient.ucs.Settings.viewmodels
{
    public class VMPageableDynamicGridMain<T> : VMPageableBase<T>, IDynamicGridViewModel
    {
        public ObservableCollection<BaseColumnDefinition> Columns { get; set; } = new ObservableCollection<BaseColumnDefinition>();

        public VMPageableDynamicGridMain()
        {
        }
        /// <summary>
        /// Dùng trong XAML Window_Loaded d? t?o c?t
        /// </summary>
        public void GenerateColumns(DataGrid dataGrid)
        {
            dataGrid.Columns.Clear();
            foreach (var columnDef in Columns)
            {
                var column = columnDef.GenerateColumn();
                dataGrid.Columns.Add(column);
            }
        }
        protected virtual void OnInitDataGrid() { }
        protected override IEnumerable<T> FilteredItems()
        {
            return AllItems;
        }
    }
}

















