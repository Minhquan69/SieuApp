using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.UI.Pages
{
    /// <summary>
    /// Interaction logic for ViewLogSearch.xaml
    /// </summary>
    public partial class ViewSearchCustom : Page
    {
       public VMViewSearchCustom VM {  get; set; }
        public event EventHandler<List<DateTime?>> EventSeachClick;
        public event EventHandler<string> CmbValueChange1;
        public event EventHandler<string> CmbValueChange2;
        public event EventHandler<string> TxtTextChange;
        public event EventHandler EventExportClick;
        public ViewSearchCustom()
        {
            InitializeComponent();
            // DataContext = this;
            VM=new VMViewSearchCustom();
            DataContext = VM;

            DateTime now = DateTime.Now;
            datetimeFrom.Value = now.AddHours(-6);
            datetimeTo.Value = now;
        }

        public void ConfigureComboBox(System.Windows.Controls.ComboBox comboBox, TextBlock label, bool isVisible, string title, List<string> items, SelectionChangedEventHandler handler, Action<bool> setVisibility)
        {
            setVisibility(isVisible);
            if (label != null)
                label.Text = title;
            comboBox.SelectionChanged -= handler;
            comboBox.ItemsSource = items;
            comboBox.SelectedIndex = items.Count > 0 ? 0 : -1;
            comboBox.SelectionChanged += handler;
        }
        public void Configure_ComboxContent_1(bool visible, string title, List<string> comboxItem)
        {
            ConfigureComboBox(
                comboBox: combox_Content_1,
                label: lablel_Content_1,
                isVisible: visible,
                title: title,
                items: comboxItem,
                handler: Comb_1_SelectionChanged,
                setVisibility: val => VM.IsPanelContent1_Visible = val
            );
        }

        public void Configure_ComboxContent_2(bool visible, string title, List<string> comboxItem)
        {
            ConfigureComboBox(
                comboBox: combox_Content_2,
                 label: lablel_Content_2,
                isVisible: visible,
                title: title,
                items: comboxItem,
                handler: Comb_2_SelectionChanged,
                setVisibility: val => VM.IsPanelContent2_Visible = val
            );
        }
        
        private void btnSearch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            List<DateTime?> list = new List<DateTime?> { datetimeFrom.Value, datetimeTo.Value };

            EventSeachClick?.Invoke(combox_Content_2.SelectedItem, list);
        }
        private void Comb_1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (combox_Content_1.SelectedItem is string selectedType)
            {
                CmbValueChange1?.Invoke(this, selectedType);
            }
        }
        private void Comb_2_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (combox_Content_2.SelectedItem is string selectedType)
            {
                CmbValueChange2?.Invoke(this, selectedType);
            }
        }
        private void btnExportFiles_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            EventExportClick?.Invoke(this,new EventArgs());
        }
        private void txtIDSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtTextChange?.Invoke(this, txtIDSearch.Text);
        }
    }
}
















