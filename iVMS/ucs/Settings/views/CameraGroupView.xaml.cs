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
using V3SClient.viewModels;

namespace V3SClient.ucs.Settings.views
{
    /// <summary>
    /// Interaction logic for CameraGroupView.xaml
    /// </summary>
    public partial class CameraGroupView : UserControl,IClosableView
    {
        VMCameraGroup VM;
        public CameraGroupView()
        {
            InitializeComponent();
            VM = new VMCameraGroup();
            this.DataContext = VM;
            
        }

        public void Cleanup()
        {
            
        }

       
    }
}

















