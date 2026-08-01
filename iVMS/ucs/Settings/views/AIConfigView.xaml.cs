using System.Windows.Controls;
using V3SClient.libs.interfaces;
using V3SClient.ucs.Settings.viewmodels;

namespace V3SClient.ucs.Settings.views
{
    public partial class AIConfigView : UserControl, IClosableView
    {
        VMCamInfo VM;
        public AIConfigView()
        {
            InitializeComponent();
            VM = new VMCamInfo();
            VM.WindowTitle = "Cấu hình AI Node Camera";
            this.DataContext = VM;
        }

        public void Cleanup()
        {
        }
    }
}

















