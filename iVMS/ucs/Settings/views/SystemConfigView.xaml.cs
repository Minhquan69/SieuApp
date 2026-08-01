using System.Windows.Controls;
using V3SClient.libs.interfaces;
using V3SClient.ucs.Settings.viewmodels;

namespace V3SClient.ucs.Settings.views
{
    public partial class SystemConfigView : UserControl, IClosableView
    {
        public SystemConfigView()
        {
            InitializeComponent();
            this.DataContext = new VMSystemConfig();
        }

        public void Cleanup()
        {
        }
    }
}

















