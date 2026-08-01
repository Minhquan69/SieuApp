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
using System.Windows.Shapes;
using V3SClient.libs;
using V3SClient.ucs.Settings.viewmodels;

namespace V3SClient.ucs.Settings.views
{
    /// <summary>
    /// Interaction logic for ItemEditWindow.xaml
    /// </summary>
    public partial class ItemEditWindow : Window
    {
        public ItemEditWindow(VMItemEditWindow viewModel)
        {
            InitializeComponent();
            this.DataContext = viewModel;
            viewModel.CloseAction += result => { DialogResult = result; Close(); };
        }
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
        private void DynamicTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox?.DataContext is ItemEditField field)
            {
                var binding = new Binding($"EditItem.{field.BindingPath}")
                {
                    Source = this.DataContext,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                };
                textBox.SetBinding(TextBox.TextProperty, binding);
            }
        }

        private void DynamicCheckBox_Loaded(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox?.DataContext is ItemEditField field)
            {
                var binding = new Binding($"EditItem.{field.BindingPath}")
                {
                    Source = this.DataContext,
                    Mode = BindingMode.TwoWay
                };
                checkBox.SetBinding(CheckBox.IsCheckedProperty, binding);
            }
        }

        private void DynamicComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            if (comboBox?.DataContext is ItemEditField field)
            {
                var binding = new Binding($"EditItem.{field.BindingPath}")
                {
                    Source = this.DataContext,
                    Mode = BindingMode.TwoWay
                };
                comboBox.SetBinding(ComboBox.SelectedValueProperty, binding);
            }
        }
        private void DynamicListBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ListBox listBox && listBox.DataContext is ItemEditField field)
            {
                listBox.SelectionChanged -= OnListBoxSelectionChanged;
                listBox.SelectionChanged += OnListBoxSelectionChanged;

                listBox.SelectedItems.Clear();

                if (field.SelectedItems != null)
                {
                    foreach (var selected in field.SelectedItems.OfType<ComboBoxItemModel>())
                    {
                        var match = listBox.Items
                            .OfType<ComboBoxItemModel>()
                            .FirstOrDefault(i => i.Key == selected.Key);

                        if (match != null)
                            listBox.SelectedItems.Add(match);
                    }
                }

                void OnListBoxSelectionChanged(object s, SelectionChangedEventArgs args)
                {
                    field.SelectedItems = listBox.SelectedItems.Cast<object>().ToList();
                }
            }
        }




    }
}

















