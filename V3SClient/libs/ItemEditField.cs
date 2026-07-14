using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using V3SClient.enums;
using V3SClient.viewModels;

namespace V3SClient.libs
{
    public class ItemEditField : VMBase
    {
        public IMultiValueConverter Converter { get; set; }
        public string Label { get; set; }
        public string BindingPath { get; set; }
        public EditControlType ControlType { get; set; }
        public string ButtonLabel { get; set; }           // Nhãn nút, ví dụ: "Kiểm tra"
        public ICommand ButtonCommand { get; set; }       // Lệnh thực thi khi click
        public bool ShowButton { get; set; } = false;     // Có hiển thị nút không?

        public IEnumerable<ComboBoxItemModel> ComboItems { get; set; }
        private IEnumerable<object> _selectedItems;
        public IEnumerable<object> SelectedItems
        {
            get => _selectedItems;
            set
            {
                _selectedItems = value;
                OnPropertyChanged();
            }
        }
       
        public MultiBinding CreateMultiBinding()
        {
            var mb = new MultiBinding
            {
                Mode = BindingMode.TwoWay,
                Converter = Converter,
                ConverterParameter = BindingPath
            };

            mb.Bindings.Add(new Binding("DataContext.EditItem")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(FrameworkElement), 1)
            });
            mb.Bindings.Add(new Binding("BindingPath"));

            return mb;
        }
    }
}
