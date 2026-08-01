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

namespace V3SClient.UI.Pages
{
    /// <summary>
    /// Interaction logic for RightMenu.xaml
    /// </summary>
    public partial class RightMenu : Page
    {

        public RightMenu(int expandContentWith = 300)
        {
            InitializeComponent();
            this.Loaded += (s, e) => SetWithOfContent(expandContentWith);
        }

        public event EventHandler MenuToggled;

        public void SetWithOfContent(int expandWidth)
        {
            gridContent.Width = double.NaN;
        }
        public void AddContentToRightMenu(UIElement content)
        {
            gridContent.Child = content;
        }
        public void AddPageToRightMenu(Page page)
        {
            var frame = new Frame();
            frame.NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden;
            frame.Content = page;
            gridContent.Child = frame;
        }

        public void ShowContent(object sender, RoutedEventArgs e)
        {
            gridContent.Visibility = gridContent.Visibility == Visibility.Visible ? Visibility.Collapsed :
                Visibility.Visible;
            System.Diagnostics.Debug.WriteLine(gridContent.Visibility);
            MenuToggled?.Invoke(this, EventArgs.Empty);
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            gridContent.Visibility = Visibility.Collapsed;
        }
    }

}
















