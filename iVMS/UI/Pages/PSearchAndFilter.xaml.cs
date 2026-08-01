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
using V3SClient.viewModels;

namespace V3SClient.UI.Pages
{
    /// <summary>
    /// Interaction logic for PSearchAndFilter.xaml
    /// </summary>
    public partial class PSearchAndFilter : Page
    {
        public event EventHandler<string> CmbValueChange1;
        public event EventHandler<string> CmbValueChange2;
        public VMPSearchAndFilter VM { get; set; }
        public bool UseNameSearch { get; private set; }
        public string NameSearchText { get; private set; }

        public bool UseDeviceIDSearch { get; private set; }
        public string DeviceIDSearchText { get; private set; }

        public bool UseDateSearch { get; private set; }
        public DateTime? StartDate { get; private set; }
        public DateTime? EndDate { get; private set; }

        public string SelectedFilter { get; private set; }
        public PSearchAndFilter()
        {
            InitializeComponent();
            VM = new VMPSearchAndFilter();
            DataContext = VM;
            InitEvents();
        }
        private void InitEvents()
        {
            // Gán event cho CheckBox
            chkUseNameSearch.Checked += (s, e) => UseNameSearch = true;
            chkUseNameSearch.Unchecked += (s, e) => UseNameSearch = false;

            chkUseDeviceIDSearch.Checked += (s, e) => UseDeviceIDSearch = true;
            chkUseDeviceIDSearch.Unchecked += (s, e) => UseDeviceIDSearch = false;

            chkUseDateSearch.Checked += (s, e) => UseDateSearch = true;
            chkUseDateSearch.Unchecked += (s, e) => UseDateSearch = false;

            // Gán event cho TextBox
            txtNameSearch.TextChanged += (s, e) => NameSearchText = txtNameSearch.Text;
            txtDeviceIDSearch.TextChanged += (s, e) => DeviceIDSearchText = txtDeviceIDSearch.Text;

            // Gán event cho DateTimePicker
            dateStart.ValueChanged += (s, e) => StartDate = dateStart.Value;
            dateEnd.ValueChanged += (s, e) => EndDate = dateEnd.Value;

            // Gán event cho ComboBox
            cbFilter.SelectionChanged += (s, e) =>
            {
                if (cbFilter.SelectedItem is ComboBoxItem selectedItem)
                {
                    SelectedFilter = selectedItem.Content?.ToString();
                }
                else
                {
                    SelectedFilter = cbFilter.SelectedItem?.ToString();
                }
            };
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
    }
    public class VMPSearchAndFilter : VMBase
    {
        private bool _isPanelContent1_isible = false;
        public bool IsPanelContent1_Visible
        {
            get => _isPanelContent1_isible;
            set
            {
                _isPanelContent1_isible = value;
                OnPropertyChanged(nameof(IsPanelContent1_Visible));
            }
        }
        private bool _isPanelContent2_isible = false;
        public bool IsPanelContent2_Visible
        {
            get => _isPanelContent2_isible;
            set
            {
                _isPanelContent2_isible = value;
                OnPropertyChanged(nameof(IsPanelContent2_Visible));
            }
        }
    }
}
















