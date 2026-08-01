using System.Windows.Controls;
using V3SClient.libs.interfaces;
using V3SClient.ucs.Settings.viewmodels;

namespace V3SClient.ucs.Settings.views
{
    public partial class ROIManagementView : UserControl, IClosableView
    {
        VMCamInfo VM;
        public ROIManagementView()
        {
            InitializeComponent();
            VM = new VMCamInfo();
            VM.WindowTitle = "Quáº£n lÃ½ VÃ¹ng ROI Camera";
            this.DataContext = VM;
        }

        public void Cleanup()
        {
        }
    }
}

















