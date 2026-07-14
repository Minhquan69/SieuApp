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
    /// Interaction logic for ChangeNameWindow.xaml
    /// </summary>
    public partial class ChangeNameWindow : Window
    {
        public string NewName { get; private set; }
        public bool UseOriginal { get; private set; }

        public ChangeNameWindow(string originalName)
        {
            InitializeComponent();
            txtNewName.Text = originalName;
        }

        private void chkUseOriginal_Checked(object sender, RoutedEventArgs e)
        {
            txtNewName.Visibility = Visibility.Collapsed;
        }

        private void chkUseOriginal_Unchecked(object sender, RoutedEventArgs e)
        {
            txtNewName.Visibility = Visibility.Visible;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            UseOriginal = chkUseOriginal.IsChecked == true;

            if (!UseOriginal)
            {
                NewName = txtNewName.Text.Trim();

                if (string.IsNullOrEmpty(NewName))
                {
                    MessageBox.Show("Vui lòng nh?p tên m?i!", "Thi?u thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtNewName.Focus();
                    return;
                }
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

















