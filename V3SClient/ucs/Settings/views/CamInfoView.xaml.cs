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
    /// Interaction logic for CamInfoView.xaml
    /// </summary>
    public partial class CamInfoView : UserControl, IClosableView
    {
        VMCamInfo VM;
        public CamInfoView()
        {
            InitializeComponent();
            VM = new VMCamInfo();
            this.DataContext = VM;
        }

        public void Cleanup()
        {
         
        }
        //private void OnPageInputLostFocus(object sender, RoutedEventArgs e)
        //{
        //    if (DataContext is VMCamInfo vm)
        //    {
        //        if (int.TryParse(vm.PageInput, out int page))
        //        {
        //            vm.GoToPage(page);
        //        }
        //        else
        //        {
        //            // Không h?p l? -> reset v? trang hi?n t?i
        //            vm.PageInput = vm.CurrentPage.ToString();
        //        }
        //    }
        //}
    }
}

















