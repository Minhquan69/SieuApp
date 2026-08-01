using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using V3SClient.libs.interfaces;

namespace V3SClient.ucs.Settings.views
{
    /// <summary>
    /// Interaction logic for DynamicDataGridMainControl.xaml
    /// </summary>
    public partial class DynamicDataGridMainControl : UserControl
    {
        public DynamicDataGridMainControl()
        {
            InitializeComponent();
            this.DataContextChanged += DynamicDataGridMainControl_DataContextChanged;
        }

        private void DynamicDataGridMainControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            TryGenerateColumns();
        }
        private void TryGenerateColumns()
        {
            if (this.DataContext is IDynamicGridViewModel vm)
            {
                vm.GenerateColumns(DynamicGrid);
            }
        }
    }
}

















