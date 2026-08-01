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
using V3SClient.libs.interfaces;
using V3SClient.ucs.Settings.viewmodels;

namespace V3SClient.ucs.Settings.views
{
    /// <summary>
    /// Interaction logic for GroupTalkView.xaml
    /// </summary>
    public partial class GroupTalkView : UserControl, IClosableView
    {
        VMGroupTalkInfo VM;
        public GroupTalkView()
        {
            InitializeComponent();
            VM = new VMGroupTalkInfo();
            DataContext = VM;
        }

        public void Cleanup()
        {
           
        }
    }
}

















