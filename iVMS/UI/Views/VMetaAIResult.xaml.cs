using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using OpenCvSharp.Flann;
using V3SClient.models;
using V3SClient.viewModels;

namespace V3SClient.UI.Views
{
    /// <summary>
    /// Interaction logic for VMetaAIResult.xaml
    /// </summary>
    public partial class VMetaAIResult : Page
    {
        public VMMetaAIResult logAIResult = new VMMetaAIResult();
        Int32 index = 100;
        public VMetaAIResult( bool lbVisible=false)
        {
            InitializeComponent();
            this.DataContext = logAIResult;
            this.txtAIResult.Visibility= lbVisible?Visibility.Visible:Visibility.Hidden;
            this.HeaderRow.Height = lbVisible ?new GridLength(30) :new GridLength(0);

            logAIResult.logData.CollectionChanged += LogData_CollectionChanged;
        }

        private void LogData_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count > 0)
            {
                var added = (KeyValuePair<string, MetaAIResult>)e.NewItems[0];

                // Scroll vào item vừa thêm
                Dispatcher.Invoke(() =>
                {
                    LogDataGrid.ScrollIntoView(added);
                });
            }
        }

        public void ShowAIResult(List<MetaAIResult> aiResult)
        {
            logAIResult.Add(aiResult);
        }
        public void ClearAIResult()
        {
            Application.Current.Dispatcher.Invoke(() =>
             {
                 logAIResult.logData.Clear();
             });
        }
    }
}
