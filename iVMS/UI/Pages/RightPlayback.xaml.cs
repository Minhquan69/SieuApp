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
using V3SClient.UI.Views;

namespace V3SClient.UI.Pages
{
    /// <summary>
    /// Interaction logic for Left_Playback.xaml
    /// </summary>
    public partial class RightPlayback : Page
    {

        public VMetaAIResult vAIResultLog { get; set; }
        public RightPlayback(int expandContentWith=220)
        {
            InitializeComponent();
            this.Loaded += (s, e) => SetWithOfContent(expandContentWith);
            vAIResultLog = new VMetaAIResult(true);
            this.frmAIResultLog.Navigate(vAIResultLog);
        }

        public void SetWithOfContent(int expandWidth)
        {
            gridContent.Width = expandWidth;
        }
        public void ShowContent(object sender, RoutedEventArgs e)
        {
            gridContent.Visibility = gridContent.Visibility == Visibility.Visible? Visibility.Collapsed:
                Visibility.Visible;
            System.Diagnostics.Debug.WriteLine(gridContent.Visibility);
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            gridContent.Visibility = Visibility.Collapsed;
           


        }
    }

}
















