using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using V3SClient.libs;
using V3SClient.enums;

namespace V3SClient.UI.Selectors
{
    public class EditFieldTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TextBoxTemplate { get; set; }
        public DataTemplate CheckBoxTemplate { get; set; }
        public DataTemplate ComboBoxTemplate { get; set; }
        public DataTemplate ListBoxMultiSelectTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var field = item as ItemEditField;
            if (field == null)
                return base.SelectTemplate(item, container);

            if (field.ControlType == EditControlType.TextBox)
                return TextBoxTemplate;
            if (field.ControlType == EditControlType.CheckBox)
                return CheckBoxTemplate;
            if (field.ControlType == EditControlType.ComboBox)
                return ComboBoxTemplate;
            if (field.ControlType == EditControlType.ListBoxMultiSelect)
                return ListBoxMultiSelectTemplate;

            return base.SelectTemplate(item, container);
        }
    }

}
















