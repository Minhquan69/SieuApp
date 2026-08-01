using MahApps.Metro.IconPacks;
using Org.BouncyCastle.Asn1.Crmf;
using System;
using System.Collections;
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


namespace V3SClient.libs
{
    public class TextColumnDefinition : BaseColumnDefinition
    {
        public TextColumnDefinition(string bindingPath, string header)
            : base(bindingPath, header)
        {
            
        }

        public override DataGridColumn GenerateColumn()
        {
            return new DataGridTextColumn
            {
                Header = Header,
                Binding = new Binding(BindingPath),
                Width = double.IsNaN(Width) ? new DataGridLength(1, DataGridLengthUnitType.Auto) : Width
            };
            
        }
    }
    public class CheckBoxColumnDefinition : BaseColumnDefinition
    {
        public CheckBoxColumnDefinition(string bindingPath, string header)
            : base(bindingPath, header)
        {
        }

        public override DataGridColumn GenerateColumn()
        {
          
            return new DataGridCheckBoxColumn
            {
                Header = Header,
                Binding = new Binding(BindingPath),
                Width = ResolveWidth()
            };

        }
    }
    public class ComboBoxColumnDefinition : BaseColumnDefinition
    {
        public IEnumerable ItemsSource { get; set; }
        public string DisplayMemberPath { get; set; }
        public string SelectedValuePath { get; set; }

        public ComboBoxColumnDefinition(string bindingPath, string header, IEnumerable itemsSource,
                                        string displayMemberPath, string selectedValuePath)
            : base(bindingPath, header)
        {
            ItemsSource = itemsSource;
            DisplayMemberPath = displayMemberPath;
            SelectedValuePath = selectedValuePath;
        }

        public override DataGridColumn GenerateColumn()
        {
            return new DataGridComboBoxColumn
            {
                Header = Header,
                SelectedValueBinding = new Binding(BindingPath),
                ItemsSource = ItemsSource,
                DisplayMemberPath = DisplayMemberPath,
                SelectedValuePath = SelectedValuePath,
                Width = ResolveWidth()
            };
        }
    }

    public class DateColumnDefinition : BaseColumnDefinition
    {
        public DateColumnDefinition(string bindingPath, string header)
            : base(bindingPath, header) { }

        public override DataGridColumn GenerateColumn()
        {
            var datePickerFactory = new FrameworkElementFactory(typeof(DatePicker));
            datePickerFactory.SetBinding(DatePicker.SelectedDateProperty, new Binding(BindingPath));

            var dataTemplate = new DataTemplate
            {
                VisualTree = datePickerFactory
            };

            return new DataGridTemplateColumn
            {
                Header = Header,
                CellTemplate = dataTemplate,
                Width = ResolveWidth()
            };
        }
    }
    public class ButtonColumnDefinition : BaseColumnDefinition
    {
        public string ButtonContent { get; set; }
        public ICommand Command { get; set; }

        public ButtonColumnDefinition(string header, string buttonContent, ICommand command)
            : base(null, header)
        {
            ButtonContent = buttonContent;
            Command = command;
        }

        public override DataGridColumn GenerateColumn()
        {
            var template = new DataTemplate();

            var factory = new FrameworkElementFactory(typeof(Button));
            factory.SetValue(Button.ContentProperty, ButtonContent);
            factory.SetBinding(Button.CommandProperty, new Binding { Source = Command });
            factory.SetBinding(Button.CommandParameterProperty, new Binding());

            template.VisualTree = factory;
            return new DataGridTemplateColumn
            {
                Header = Header,
                CellTemplate = template,
                Width = ResolveWidth()
            };
           
        }
    }
    public class ImageColumnDefinition : BaseColumnDefinition
    {
        public ImageColumnDefinition(string bindingPath, string header)
            : base(bindingPath, header)
        {
        }

        public override DataGridColumn GenerateColumn()
        {
            var template = new DataTemplate();

            var factory = new FrameworkElementFactory(typeof(Image));
            factory.SetBinding(Image.SourceProperty, new Binding(BindingPath));
            factory.SetValue(Image.WidthProperty, 24.0);
            factory.SetValue(Image.HeightProperty, 24.0);
            factory.SetValue(Image.MarginProperty, new Thickness(4));
            factory.SetValue(Image.StretchProperty, Stretch.Uniform);

            template.VisualTree = factory;

            return new DataGridTemplateColumn
            {
                Header = Header,
                CellTemplate = template,
                Width = ResolveWidth()
            };
          
        }
    }
    
    public class MultiButtonColumnDefinition : BaseColumnDefinition
    {
        public string EditButtonContent { get; set; } = "Sửa";
        public string DeleteButtonContent { get; set; } = "Xóa";
        public ICommand EditCommand { get; set; }
        public ICommand DeleteCommand { get; set; }

        public MultiButtonColumnDefinition(string header, ICommand editCommand, ICommand deleteCommand)
            : base(null, header)
        {
            EditCommand = editCommand;
            DeleteCommand = deleteCommand;
        }

        public override DataGridColumn GenerateColumn()
        {
            var template = new DataTemplate();

            var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
            panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panelFactory.SetValue(StackPanel.HorizontalAlignmentProperty, HorizontalAlignment.Center);

            // Edit button
            var editButton = new FrameworkElementFactory(typeof(Button));
            editButton.SetValue(Button.ContentProperty, EditButtonContent);
            editButton.SetValue(Button.MarginProperty, new Thickness(2, 0, 2, 0));
            editButton.SetBinding(Button.CommandProperty, new Binding { Source = EditCommand });
            editButton.SetBinding(Button.CommandParameterProperty, new Binding()); // pass row item
            panelFactory.AppendChild(editButton);

            // Delete button
            var deleteButton = new FrameworkElementFactory(typeof(Button));
            deleteButton.SetValue(Button.ContentProperty, DeleteButtonContent);
            deleteButton.SetValue(Button.MarginProperty, new Thickness(2, 0, 2, 0));
            deleteButton.SetBinding(Button.CommandProperty, new Binding { Source = DeleteCommand });
            deleteButton.SetBinding(Button.CommandParameterProperty, new Binding()); // pass row item
            panelFactory.AppendChild(deleteButton);

            template.VisualTree = panelFactory;

           return new DataGridTemplateColumn
            {
                Header = Header,
                CellTemplate = template,
                Width = ResolveWidth()
           };
           
        }
    }
    public class TwoImageButtonColumnDefinition : BaseColumnDefinition
    {
        public string EditIconKind { get; set; } = "PencilOutline";
        public string DeleteIconKind { get; set; } = "DeleteOutline";

        public string EditCommandName { get; set; } = "EditCommand";
        public string DeleteCommandName { get; set; } = "DeleteCommand";

  
        public TwoImageButtonColumnDefinition(string header)
            : base(null, header)
        {
       
        }

        public override DataGridColumn GenerateColumn()
        {
            var template = new DataTemplate();

            var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
            panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            // --- EDIT BUTTON ---
            var editButtonFactory = new FrameworkElementFactory(typeof(Button));
            var editstyte = Application.Current.TryFindResource("gridEditButton") as Style;
            editButtonFactory.SetValue(Button.StyleProperty, editstyte);
            editButtonFactory.SetBinding(Button.CommandProperty, new Binding($"DataContext.{EditCommandName}")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
            });
            // editButtonFactory.SetValue(Button.CommandProperty, EditCommand);
            editButtonFactory.SetBinding(Button.CommandParameterProperty, new Binding()); // whole row


            // Icon inside button
            var editIconFactory = new FrameworkElementFactory(typeof(PackIconMaterial));
            if (Enum.TryParse(EditIconKind, out PackIconMaterialKind editKind))
            {
                editIconFactory.SetValue(PackIconMaterial.KindProperty, editKind);
            }
           
            editIconFactory.SetValue(FrameworkElement.StyleProperty, Application.Current.TryFindResource("gridButtonIcon"));
            editButtonFactory.AppendChild(editIconFactory);

            panelFactory.AppendChild(editButtonFactory);

            // --- DELETE BUTTON ---
            var deleteButtonFactory = new FrameworkElementFactory(typeof(Button));
            deleteButtonFactory.SetValue(Button.StyleProperty, Application.Current.TryFindResource("gridRemoveButton"));
            deleteButtonFactory.SetValue(Button.MarginProperty, new Thickness(5, 0, 0, 0));
            deleteButtonFactory.SetBinding(Button.CommandProperty, new Binding($"DataContext.{DeleteCommandName}")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
            });

            deleteButtonFactory.SetBinding(Button.CommandParameterProperty, new Binding());

            var deleteIconFactory = new FrameworkElementFactory(typeof(PackIconMaterial));
            if (Enum.TryParse(DeleteIconKind, out PackIconMaterialKind delKind))
            {
                deleteIconFactory.SetValue(PackIconMaterial.KindProperty, delKind);
            }
           
            deleteIconFactory.SetValue(FrameworkElement.StyleProperty, Application.Current.TryFindResource("gridButtonIcon"));
            deleteButtonFactory.AppendChild(deleteIconFactory);

            panelFactory.AppendChild(deleteButtonFactory);

            template.VisualTree = panelFactory;

            return new DataGridTemplateColumn
            {
                Header = Header,
                CellTemplate = template,
                Width = ResolveWidth()
            };
        }
    }
    public class MultiActionButtonColumnDefinition : BaseColumnDefinition
    {
        public List<ActionButtonDefinition> Buttons { get; set; } = new List<ActionButtonDefinition>();

        public MultiActionButtonColumnDefinition(string header, IEnumerable<ActionButtonDefinition> buttons)
            : base(null, header)
        {
            Buttons.AddRange(buttons);
        }

        public override DataGridColumn GenerateColumn()
        {
            var template = new DataTemplate();
            var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
            panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panelFactory.SetValue(StackPanel.HorizontalAlignmentProperty, HorizontalAlignment.Center);

            foreach (var btnDef in Buttons)
            {
                var buttonFactory = new FrameworkElementFactory(typeof(Button));
                buttonFactory.SetValue(Button.WidthProperty, btnDef.ButtonSize);
                buttonFactory.SetValue(Button.HeightProperty, btnDef.ButtonSize);
                buttonFactory.SetValue(Button.MarginProperty, new Thickness(2, 0, 2, 0));
                buttonFactory.SetBinding(Button.CommandProperty, new Binding { Source = btnDef.Command });
                buttonFactory.SetBinding(Button.CommandParameterProperty, new Binding());

                if (!string.IsNullOrEmpty(btnDef.ToolTip))
                    buttonFactory.SetValue(Button.ToolTipProperty, btnDef.ToolTip);

                var imageFactory = new FrameworkElementFactory(typeof(Image));
                imageFactory.SetValue(Image.SourceProperty, new BitmapImage(new Uri(btnDef.ImagePath, UriKind.RelativeOrAbsolute)));
                imageFactory.SetValue(Image.WidthProperty, 16.0);
                imageFactory.SetValue(Image.HeightProperty, 16.0);
                imageFactory.SetValue(Image.StretchProperty, Stretch.Uniform);
                buttonFactory.AppendChild(imageFactory);

                panelFactory.AppendChild(buttonFactory);
            }
            template.VisualTree = panelFactory;
            return new DataGridTemplateColumn
            {
                Header = Header,
                CellTemplate = template,
                Width = ResolveWidth()
            };

        }
    }
    public class ClientListColumnDefinition : BaseColumnDefinition
    {
        public string ItemsSourcePath { get; set; }
        public ICommand ClientCommand { get; set; }

        public ClientListColumnDefinition(string header, string itemsSourcePath, ICommand clientCommand)
            : base(null, header)
        {
            ItemsSourcePath = itemsSourcePath;
            ClientCommand = clientCommand;
        }

        public override DataGridColumn GenerateColumn()
        {
            // Define template for the buttons
            var buttonTemplate = new DataTemplate();
            var spFactory = new FrameworkElementFactory(typeof(StackPanel));
            spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetValue(Button.MarginProperty, new Thickness(2, 2, 2, 2));
            buttonFactory.SetValue(Button.PaddingProperty, new Thickness(8, 4, 8, 4));
            buttonFactory.SetValue(Button.BackgroundProperty, new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#28a745")));
            buttonFactory.SetValue(Button.ForegroundProperty, Brushes.White);
            buttonFactory.SetValue(Button.BorderBrushProperty, Brushes.Transparent);
            buttonFactory.SetValue(Button.FontWeightProperty, FontWeights.Bold);
            buttonFactory.SetValue(Button.CursorProperty, Cursors.Hand);

            // Rounded corners
            var style = new Style(typeof(Button));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "border";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));

            // ContentPresenter cho nút
            var contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenterFactory.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));

            // Gắn ContentPresenter vào Border
            borderFactory.AppendChild(contentPresenterFactory);

            // Gắn template vào style
            var template = new ControlTemplate(typeof(Button));
            template.VisualTree = borderFactory;

            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            buttonFactory.SetValue(Button.StyleProperty, style);

            buttonFactory.SetBinding(Button.ContentProperty, new Binding("."));
            buttonFactory.SetBinding(Button.CommandProperty, new Binding { Source = ClientCommand });
            buttonFactory.SetBinding(Button.CommandParameterProperty, new Binding("."));

            spFactory.AppendChild(buttonFactory);
            buttonTemplate.VisualTree = spFactory;

            // Wrap buttons in ItemsControl
            var itemsControlFactory = new FrameworkElementFactory(typeof(ItemsControl));
            itemsControlFactory.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(ItemsSourcePath));
            itemsControlFactory.SetValue(ItemsControl.ItemTemplateProperty, buttonTemplate);

            var cellTemplate = new DataTemplate { VisualTree = itemsControlFactory };

            
            return new DataGridTemplateColumn
            {
                Header = Header,
                CellTemplate = cellTemplate,
                Width = ResolveWidth()
            };
            
        }
    }
    public class ClientListWrapPanelColumnDefinition : BaseColumnDefinition
    {
        public string ItemsSourcePath { get; set; }
        public ICommand ClientCommand { get; set; }

        public ClientListWrapPanelColumnDefinition(string header, string itemsSourcePath, ICommand clientCommand)
            : base(null, header)
        {
            ItemsSourcePath = itemsSourcePath;
            ClientCommand = clientCommand;
        }

        public override DataGridColumn GenerateColumn()
        {
            // Define button style with rounded corners
            var style = new Style(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "border";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));

            var contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenterFactory.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));

            borderFactory.AppendChild(contentPresenterFactory);

            var controlTemplate = new ControlTemplate(typeof(Button))
            {
                VisualTree = borderFactory
            };
            style.Setters.Add(new Setter(Control.TemplateProperty, controlTemplate));

            // Template for each client button
            var buttonTemplate = new DataTemplate();
            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetValue(Button.MarginProperty, new Thickness(2));
            buttonFactory.SetValue(Button.PaddingProperty, new Thickness(8, 4, 8, 4));
            buttonFactory.SetValue(Button.BackgroundProperty, new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#28a745")));
            buttonFactory.SetValue(Button.ForegroundProperty, Brushes.White);
            buttonFactory.SetValue(Button.BorderBrushProperty, Brushes.Transparent);
            buttonFactory.SetValue(Button.FontWeightProperty, FontWeights.SemiBold);
            buttonFactory.SetValue(Button.CursorProperty, Cursors.Hand);
            buttonFactory.SetValue(Button.StyleProperty, style);

            buttonFactory.SetBinding(Button.ContentProperty, new Binding("."));
            buttonFactory.SetBinding(Button.CommandProperty, new Binding { Source = ClientCommand });
            buttonFactory.SetBinding(Button.CommandParameterProperty, new Binding("."));
            buttonTemplate.VisualTree = buttonFactory;

            // WrapPanel for horizontal layout + automatic wrap
            var wrapPanelFactory = new FrameworkElementFactory(typeof(WrapPanel));
            wrapPanelFactory.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);

            var itemsPanelTemplate = new ItemsPanelTemplate
            {
                VisualTree = wrapPanelFactory
            };

            // ItemsControl for rendering client buttons
            var itemsControlFactory = new FrameworkElementFactory(typeof(ItemsControl));
            itemsControlFactory.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(ItemsSourcePath));
            itemsControlFactory.SetValue(ItemsControl.ItemTemplateProperty, buttonTemplate);
            itemsControlFactory.SetValue(ItemsControl.ItemsPanelProperty, itemsPanelTemplate);

            var cellTemplate = new DataTemplate
            {
                VisualTree = itemsControlFactory
            };

            return new DataGridTemplateColumn
            {
                Header = Header,
                CellTemplate = cellTemplate,
                Width = ResolveWidth()
            };
            
        }
    }


    //EXAMPLE
    /*
     * Columns.Add(new ImageColumnDefinition(nameof(CameraItem.IconPath), "Biểu tượng"));
     * Columns.Add(new ButtonColumnDefinition("Thao tác", "Chi tiết", new RelayCommand(item => ShowDetails((CameraItem)item))));
     * Columns.Add(new MultiButtonColumnDefinition(
            "Thao tác",
            new RelayCommand(item => EditCamera(item)),
            new RelayCommand(item => DeleteCamera(item))
        ));
    Columns.Add(new TextColumnDefinition(nameof(CameraPermissionItem.Name), "Tên Camera", 200));
        Columns.Add(new CheckBoxColumnDefinition(nameof(CameraPermissionItem.CanAdd), "Thêm"));
        Columns.Add(new CheckBoxColumnDefinition(nameof(CameraPermissionItem.CanDelete), "Xóa"));

        Columns.Add(new MultiActionButtonColumnDefinition("Thao tác", new[]
        {
            new ActionButtonDefinition("/images/edit.png", EditCommand, "Sửa"),
            new ActionButtonDefinition("/images/delete.png", DeleteCommand, "Xóa")
        }));
     */

}
