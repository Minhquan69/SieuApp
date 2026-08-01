using System.Windows.Controls;
using V3SClient.viewModels;

namespace V3SClient.ucs
{
    public partial class ucDocumentConfig : UserControl
    {
        public ucDocumentConfig()
        {
            InitializeComponent();
            this.DataContext = new VMDocumentConfig();
        }
    }
}
