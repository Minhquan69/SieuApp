using System.Windows.Controls;
using V3SClient.libs;
using V3SClient.Services;
using V3SClient.viewModels;
using V3SClient.models;

namespace V3SClient.UI.Views
{
    /// <summary>
    /// Interaction logic for DOC_SCAN.xaml
    /// </summary>
    public partial class DOC_SCAN : Page
    {
        private readonly VMQLBSXDK _viewModel;

        public DOC_SCAN()
        {
            InitializeComponent();

            // Initialize ViewModel for Document Processing mode
            var processingConfig = new VMDocumentConfig();
            string outputDir = processingConfig.OutputDirectoryDocument;
            var dbHelper = new DatabaseHelper();
            _viewModel = new VMQLBSXDK(dbHelper, outputDir, processingConfig.InputDirectoryDocument, processingConfig.PlateNamingRule, sourceTypeFilter: "Giấy tờ xe");
            this.DataContext = _viewModel;

            this.Unloaded += DOC_SCAN_Unloaded;
        }

        private void DOC_SCAN_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _viewModel?.Dispose();
        }

        private void OutputFileTree_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
        {
            _viewModel.SelectedOutputFile = e.NewValue as PlateImageItem;
            if (e.NewValue is PlateFolderItem folder)
            {
                _viewModel.SelectedFolder = folder;
            }
        }

        private void FolderSortList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FolderSortPopup.IsOpen = false;
            FolderSortButton.IsChecked = false;
        }
    }
}
