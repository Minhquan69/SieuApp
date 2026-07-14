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

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for InputDialog.xaml
    /// </summary>
    public partial class InputDialog : Window
    {

        public string InputText { get; private set; }
        private InputType expectedType; 

        public InputDialog(string title, string defaultText, InputType type)
        {
            InitializeComponent();
            TitleText.Text = title;
            InputBox.Text = defaultText;
            expectedType = type;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string input = InputBox.Text.Trim();

            switch (expectedType)
            {
                case InputType.Integer:
                    if (!int.TryParse(input, out _))
                    {
                        ShowError("Giá tr? ph?i là s? nguyên!");
                        return;
                    }
                    break;

                case InputType.Double:
                    if (!double.TryParse(input, out _))
                    {
                        ShowError("Giá tr? ph?i là s? th?c!");
                        return;
                    }
                    break;

                case InputType.String:
                    if (string.IsNullOrWhiteSpace(input))
                    {
                        ShowError("Tên nhóm không du?c d? tr?ng!");
                        return;
                    }
                    break;
            }

            InputText = input;
            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
    public enum InputType
    {
        Integer,
        String,
        Double
    }

}

















