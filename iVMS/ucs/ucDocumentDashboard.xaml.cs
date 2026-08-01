using System.Windows.Controls;
using V3SClient.viewModels;

namespace V3SClient.ucs
{
    public partial class ucDocumentDashboard : UserControl
    {
        private VMDocumentDashboard _viewModel;

        public ucDocumentDashboard()
        {
            InitializeComponent();
            _viewModel = new VMDocumentDashboard();
            this.DataContext = _viewModel;
            this.Loaded += (s, e) => _viewModel.SubscribeEvents();
            this.Unloaded += (s, e) => _viewModel.UnsubscribeEvents();
        }
    }
}
