using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using V3SClient.enums;
using V3SClient.libs;
using V3SClient.ucs.Settings.models;
using V3SClient.viewModels;

namespace V3SClient.ucs.Settings.viewmodels
{
    public class VMItemEditWindow : VMBase
    {
        public ObservableCollection<ItemEditField> Fields { get; set; } = new ObservableCollection<ItemEditField>();
        public Dictionary<string, MultiSelectFieldMapping> MultiSelectMappings { get; set; } =new Dictionary<string, MultiSelectFieldMapping>();
        public string WindowTitle { get; set; } = "Ca`i da?t";
        public object EditItem { get; set; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<bool> CloseAction;


        public VMItemEditWindow()
        {
            SaveCommand = new RelayCommand(o =>
            {
                ApplyMultiSelections();
                CloseAction?.Invoke(true);
            });
            CancelCommand = new RelayCommand(o => CloseAction?.Invoke(false));
        }
        public void ApplyMultiSelections()
        {
            foreach (var field in Fields)
            {
                if (field.ControlType == EditControlType.ListBoxMultiSelect &&
                    MultiSelectMappings.TryGetValue(field.BindingPath, out var mapping))
                {
                    var selectedKeys = field.SelectedItems?.OfType<ComboBoxItemModel>()?.Select(x => x.Key).ToList();
                    if (selectedKeys != null)
                    {
                        var resultObjects = mapping.ConvertKeysToObjects(selectedKeys);

                        var prop = EditItem.GetType().GetProperty(field.BindingPath);
                        if (prop != null)
                        {
                            // Ép ki?u resultObjects v? dúng ki?u danh sách
                            var targetListType = prop.PropertyType.GetGenericArguments().FirstOrDefault();
                            var typedList = typeof(Enumerable)
                                .GetMethod(nameof(Enumerable.Cast))
                                .MakeGenericMethod(targetListType)
                                .Invoke(null, new object[] { resultObjects });

                            var finalList = typeof(Enumerable)
                                .GetMethod(nameof(Enumerable.ToList))
                                .MakeGenericMethod(targetListType)
                                .Invoke(null, new object[] { typedList });

                            prop.SetValue(EditItem, finalList);
                        }
                    }
                }
            }
        }


        public void LoadMultiSelections()
        {
            foreach (var field in Fields)
            {
                if (field.ControlType == EditControlType.ListBoxMultiSelect && MultiSelectMappings.TryGetValue(field.BindingPath, out var mapping))
                {
                    // L?y danh sách key t? object ( string)
                    var selectedKeys = mapping.ExtractKeysFromModel(EditItem);

                    // Gán l?i SelectedItems d? hi?n th? trong ListBox
                    field.SelectedItems = field.ComboItems
                        .Where(item => selectedKeys.Contains(item.Key))
                        .Cast<object>()
                        .ToList();
                }
            }
        }

    }
}

















