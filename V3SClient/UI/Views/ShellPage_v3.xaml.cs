using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    public partial class ShellPage_v3 : UserControl
    {
        private ShellViewModel_v3 _viewModel;

        public ShellPage_v3()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _viewModel = e.NewValue as ShellViewModel_v3;
            if (_viewModel == null)
                return;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            NavigateToSelectedModule();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ShellViewModel_v3.SelectedNavigationItem))
                NavigateToSelectedModule();
        }

        private void NavigateToSelectedModule()
        {
            if (_viewModel == null || _viewModel.SelectedNavigationItem == null)
                return;

            if (_viewModel.SelectedNavigationItem.Title == "Live View")
            {
                ContentFrame.Content = new LivePage_v3();
                return;
            }
            if (_viewModel.SelectedNavigationItem.Title == "Playback")
            {
                ContentFrame.Content = new PlaybackPage_v3();
                return;
            }
            if (_viewModel.SelectedNavigationItem.Title == "E-Map")
            {
                ContentFrame.Content = new MapPage_v3();
                return;
            }

            ContentFrame.Content = new TextBlock
            {
                Text = _viewModel.SelectedNavigationItem.Title + " is not yet available in the _v3 shell.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)FindResource("VmsBodyText_v3")
            };
        }
    }
}
