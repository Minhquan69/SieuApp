using System.Collections.ObjectModel;
using System.Windows.Controls;
using V3SClient.libs;
using V3SClient.Services;
using V3SClient.viewModels;
using V3SClient.models;

namespace V3SClient.UI.Views
{
    public partial class QL_BSX_DK : Page
    {
        private VMQLBSXDK _viewModel;
        private VLiveStream _vLiveStream;

        public QL_BSX_DK(ObservableCollection<VMTalkGroup> camGroupList)
        {
            InitializeComponent();

            // Initialize ViewModel
            var processingConfig = new VMDocumentConfig();
            string outputDir = processingConfig.OutputDirectoryPlate;
            var dbHelper = new DatabaseHelper();
            _viewModel = new VMQLBSXDK(dbHelper, outputDir, processingConfig.InputDirectoryPlate, processingConfig.PlateNamingRule, sourceTypeFilter: "Biển số xe");
            this.DataContext = _viewModel;

            // Initialize and embed VLiveStream
            _vLiveStream = new VLiveStream(camGroupList, collapseLeftMenu: true);
            
            // Set 2x2 layout initially for QL_BSX_DK
            _vLiveStream.Loaded += (s, e) =>
            {
                _vLiveStream.ShowCamerasPreset(LayoutPreset.layout_2x2);
                
                // Hide Left/Right menu of VLiveStream if we only want the grid. 
                // But user wants to keep code as is. The left menu might be needed to pick cameras.
                // We'll let VLiveStream handle its own layout internally.
            };

            frameLiveStream.Content = _vLiveStream;

            this.Unloaded += QL_BSX_DK_Unloaded;
        }

        private void QL_BSX_DK_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _viewModel?.Dispose();
            
            // Allow VLiveStream to clean up when we leave
            if (frameLiveStream.Content is System.IDisposable disposableLiveStream)
            {
                disposableLiveStream.Dispose();
            }
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
