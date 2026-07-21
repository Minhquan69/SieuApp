using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        private bool _editingFromDate;
        private DateTime _displayedCalendarMonth;
        private Window _ownerWindow;
        private int? _selectedQuickRangeHours;
        private VMVideoStorage VideoStorage { get; set; } = new VMVideoStorage();
        public event EventHandler<List<DateTime?>> EventSeachClick;
        public event EventHandler<object> EventBtn2Click;
        public ViewSearch( string txtBnt1_Text= "Tìm kiếm", string txtBnt2_Text = "Export", bool btn1Visible = true,bool btn2Visible=false)
        {
            InitializeComponent();
            Loaded += ViewSearch_Loaded;
            Unloaded += ViewSearch_Unloaded;

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
            RefreshDateFields();
        }

        private void ViewSearch_Loaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (ReferenceEquals(_ownerWindow, window)) return;

            if (_ownerWindow != null)
            {
                _ownerWindow.PreviewMouseDown -= OwnerWindow_PreviewMouseDown;
            }

            _ownerWindow = window;
            if (_ownerWindow != null)
            {
                _ownerWindow.PreviewMouseDown += OwnerWindow_PreviewMouseDown;
            }
        }

        private void ViewSearch_Unloaded(object sender, RoutedEventArgs e)
        {
            datePopup.IsOpen = false;
            if (_ownerWindow != null)
            {
                _ownerWindow.PreviewMouseDown -= OwnerWindow_PreviewMouseDown;
                _ownerWindow = null;
            }
        }

        private void OwnerWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ClosePopupForExternalClick();
        }

        private void DateField_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _editingFromDate = ReferenceEquals(sender, fromDateField);
            DateTime value = (_editingFromDate ? datetimeFrom.Value : datetimeTo.Value) ?? DateTime.Now;
            datePopup.PlacementTarget = (UIElement)sender;
            _displayedCalendarMonth = new DateTime(value.Year, value.Month, 1);
            popupDateInput.Text = value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            popupTimeInput.Text = value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            RenderCalendar();
            datePopup.IsOpen = true;
            e.Handled = true;
        }

        private void CloseDatePopup_Click(object sender, RoutedEventArgs e)
        {
            datePopup.IsOpen = false;
        }

        private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ClosePopupForExternalClick();
        }

        private void ClosePopupForExternalClick()
        {
            if (!datePopup.IsOpen)
            {
                return;
            }

            // Popup is rendered in its own visual tree. IsMouseOver remains
            // reliable for its child, while this preview event catches every
            // click elsewhere in the Playback toolbar/page.
            var popupChild = datePopup.Child as UIElement;
            if ((popupChild != null && popupChild.IsMouseOver) ||
                fromDateField.IsMouseOver ||
                toDateField.IsMouseOver)
            {
                return;
            }

            datePopup.IsOpen = false;
        }

        private void QuickRange_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            int hours;
            if (button == null || !int.TryParse(button.Tag as string, out hours)) return;

            DateTime end = DateTime.Now;
            datetimeFrom.Value = end.AddHours(-hours);
            datetimeTo.Value = end;
            _selectedQuickRangeHours = hours;
            RefreshDateFields();
            UpdateQuickRangeSelection();
            datePopup.IsOpen = false;
        }

        private void UpdateQuickRangeSelection()
        {
            var activeBackground = FindResource("VmsPrimarySoftBrush_v3") as Brush;
            var activeBorder = FindResource("VmsPrimaryBrush_v3") as Brush;
            var activeForeground = FindResource("VmsPrimaryBrush_v3") as Brush;
            var normalForeground = FindResource("VmsTextSecondaryBrush_v3") as Brush;
            foreach (var button in new[] { quickRange1h, quickRange6h, quickRange12h, quickRange24h })
            {
                if (button == null) continue;
                int value;
                bool selected = int.TryParse(button.Tag as string, out value) && _selectedQuickRangeHours == value;
                button.Background = selected ? activeBackground : Brushes.Transparent;
                button.BorderBrush = selected ? activeBorder : Brushes.Transparent;
                button.Foreground = selected ? activeForeground : normalForeground;
            }
        }

        private void PreviousMonth_Click(object sender, RoutedEventArgs e)
        {
            _displayedCalendarMonth = _displayedCalendarMonth.AddMonths(-1);
            RenderCalendar();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _displayedCalendarMonth = _displayedCalendarMonth.AddMonths(1);
            RenderCalendar();
        }

        private void CalendarDay_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button == null || !(button.Tag is DateTime)) return;
            DateTime current = (_editingFromDate ? datetimeFrom.Value : datetimeTo.Value) ?? DateTime.Now;
            SetActiveDate(((DateTime)button.Tag).Date.Add(current.TimeOfDay));
            RenderCalendar();
        }

        private void RenderCalendar()
        {
            if (calendarDaysPanel == null) return;
            calendarMonthText.Text = _displayedCalendarMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            calendarDaysPanel.Children.Clear();

            DateTime first = new DateTime(_displayedCalendarMonth.Year, _displayedCalendarMonth.Month, 1);
            DateTime visibleStart = first.AddDays(-(int)first.DayOfWeek);
            DateTime selected = ((_editingFromDate ? datetimeFrom.Value : datetimeTo.Value) ?? DateTime.Now).Date;

            for (var i = 0; i < 42; i++)
            {
                DateTime day = visibleStart.AddDays(i);
                var button = new System.Windows.Controls.Button
                {
                    Content = day.Day.ToString(CultureInfo.InvariantCulture),
                    Tag = day,
                    Style = FindResource("PlaybackCalendarDayButton_v3") as Style,
                    Foreground = day.Month == _displayedCalendarMonth.Month
                        ? FindResource("VmsTextPrimaryBrush_v3") as Brush
                        : FindResource("VmsTextTertiaryBrush_v3") as Brush,
                    Opacity = day.Month == _displayedCalendarMonth.Month ? 1.0 : 0.45
                };
                if (day.Date == selected)
                {
                    button.Background = FindResource("VmsPrimarySoftBrush_v3") as Brush;
                    button.BorderBrush = FindResource("VmsPrimaryBrush_v3") as Brush;
                }
                button.Click += CalendarDay_Click;
                calendarDaysPanel.Children.Add(button);
            }
        }

        private void PopupDateInput_LostFocus(object sender, RoutedEventArgs e)
        {
            DateTime date;
            if (DateTime.TryParseExact(popupDateInput.Text, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                DateTime current = (_editingFromDate ? datetimeFrom.Value : datetimeTo.Value) ?? DateTime.Now;
                SetActiveDate(date.Date.Add(current.TimeOfDay));
                _displayedCalendarMonth = new DateTime(date.Year, date.Month, 1);
                RenderCalendar();
            }
        }

        private void PopupTimeInput_LostFocus(object sender, RoutedEventArgs e)
        {
            TimeSpan time;
            if (TimeSpan.TryParseExact(popupTimeInput.Text, "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out time))
            {
                DateTime current = (_editingFromDate ? datetimeFrom.Value : datetimeTo.Value) ?? DateTime.Now;
                SetActiveDate(current.Date.Add(time));
            }
        }

        private void SetActiveDate(DateTime value)
        {
            _selectedQuickRangeHours = null;
            if (_editingFromDate) datetimeFrom.Value = value;
            else datetimeTo.Value = value;
            popupDateInput.Text = value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            popupTimeInput.Text = value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            RefreshDateFields();
            UpdateQuickRangeSelection();
        }

        private void RefreshDateFields()
        {
            fromDateText.Text = (datetimeFrom.Value ?? DateTime.Now).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            toDateText.Text = (datetimeTo.Value ?? DateTime.Now).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
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
