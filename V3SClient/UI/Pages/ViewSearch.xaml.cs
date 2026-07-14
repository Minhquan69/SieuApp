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
using Microsoft.VisualBasic;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.UI.Pages
{
    /// <summary>
    /// Interaction logic for ViewSearch.xaml
    /// </summary>
    public partial class ViewSearch : Page
    {
        private VMVideoStorage VideoStorage { get; set; } = new VMVideoStorage();
        public event EventHandler<List<DateTime?>> EventSeachClick;
        public event EventHandler<object> EventBtn2Click;
        public ViewSearch( string txtBnt1_Text= "Search / Playback", string txtBnt2_Text = "Export", bool btn1Visible = true,bool btn2Visible=false)
        {
            InitializeComponent();

            DataContext = VideoStorage;
            
            this.btn_01.Visibility = btn1Visible ? Visibility.Visible : Visibility.Collapsed;
            this.btn_02.Visibility = btn2Visible ? Visibility.Visible : Visibility.Collapsed;
            this.txtTitle_bnt1.Text = txtBnt1_Text;
            this.txtTitle_bnt2.Text = txtBnt2_Text;
            if(btn1Visible && btn2Visible)
            {
                this.btnSize.Height = new GridLength(this.btnSize.Height.Value*2);
            }     
            UpdateButtonLayout();
            //DataContext = this;
            //combVideoStorage.DataContext = VideoStorage;

            DateTime now = DateTime.Now;
            datetimeFrom.Value = now.AddHours(-6);
            datetimeTo.Value = now;
        }

        

        private void btnSearch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Border border = (Border)sender;
            switch (border.Tag)
            {
                case "btn1":
                    List<DateTime?> list = new List<DateTime?> { datetimeFrom.Value, datetimeTo.Value };
                    EventSeachClick?.Invoke(combVideoStorage.SelectedItem, list);
                    break;
                case "btn2":
                    EventBtn2Click?.Invoke(null, null);
                    break;
            }
            
        }
        private void UpdateButtonLayout()
        {
            bool isBtn1Visible = btn_01.Visibility == Visibility.Visible;
            bool isBtn2Visible = btn_02.Visibility == Visibility.Visible;

            if (isBtn1Visible && isBtn2Visible)
            {
                // Cả hai hiện → chia đều
                ButtonGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                ButtonGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            }
            else if (isBtn1Visible)
            {
                // Chỉ btn_01 hiện → chiếm hết
                ButtonGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                ButtonGrid.RowDefinitions[1].Height = new GridLength(0);
            }
            else if (isBtn2Visible)
            {
                // Chỉ btn_02 hiện → chiếm hết
                ButtonGrid.RowDefinitions[0].Height = new GridLength(0);
                ButtonGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                // Cả hai ẩn → co lại
                ButtonGrid.RowDefinitions[0].Height = new GridLength(0);
                ButtonGrid.RowDefinitions[1].Height = new GridLength(0);
            }
        }

        private void btnExportFiles_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var tmpFiles = GlobalClass.GetAllKeyPairsMp4Tmp();
            if (tmpFiles.Count == 0)
            {
                System.Windows.MessageBox.Show("không có file video để Export",
                    caption: "video export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Chọn thư mục lưu trữ";
                    dialog.ShowNewFolderButton = true;

                    DialogResult result = dialog.ShowDialog();
                    if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    {
                        string targetFolder= dialog.SelectedPath;
                    foreach (var file in tmpFiles)
                    {
                            foreach (var f in file.Value)
                            {
                                if (!File.Exists(f))
                                {
                                    Debug.WriteLine("File nguồn không tồn tại!");
                                    continue;
                                }
                                string srcPath=System.IO. Path.GetFullPath(f);
                                string targetSubFolder = System.IO.Path.Combine(targetFolder, GetTargetFolder(srcPath));
                                Directory.CreateDirectory(targetSubFolder);
                                string newFilePath = System.IO.Path.Combine(targetSubFolder, System.IO.Path.GetFileName(f));
                                File.Copy(f, newFilePath, true);
                            }
                           
                        }
                        System.Windows.MessageBox.Show("Xuất file thành công");
                }
            }
        }
         string GetTargetFolder(string filePath)
        {
            try
            {
                string[] parts = filePath.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

                for (int i = 0; i < parts.Length; i++)
                {
                  
                    if (parts[i].Length == 4 && int.TryParse(parts[i], out int year) && year >= 2000 && year <= 2100)
                    {
                     
                        if (i + 3 < parts.Length)
                        {
                            string newFolder = System.IO.Path.Combine( parts[i], parts[i + 1], parts[i + 2], parts[i + 3]);
                            return newFolder;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lỗi khi lấy thư mục đích: {ex.Message}");
            }

            return null;
        }
    }
}
