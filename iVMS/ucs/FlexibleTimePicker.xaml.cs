using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace V3SClient.ucs
{
    public partial class FlexibleTimePicker : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private System.DateTime _currentMonthLeft;
        private System.DateTime _startDate;
        private System.DateTime _endDate;

        public System.DateTime StartDate
        {
            get => _startDate;
            set { _startDate = value; OnPropertyChanged(nameof(StartDate)); UpdateUI(); }
        }

        public System.DateTime EndDate
        {
            get => _endDate;
            set { _endDate = value; OnPropertyChanged(nameof(EndDate)); UpdateUI(); }
        }

        // Keep 'Value' for backward compatibility or simple usage (returns StartDate)
        public System.DateTime Value
        {
            get => _startDate;
            set { StartDate = value; }
        }

        public FlexibleTimePicker()
        {
            InitializeComponent();
            _currentMonthLeft = new System.DateTime(System.DateTime.Now.Year, System.DateTime.Now.Month, 1);
            _startDate = System.DateTime.Now.AddDays(-7).Date;
            _endDate = System.DateTime.Now.Date;
            UpdateUI();
        }

        private void UpdateUI()
        {
            RenderCalendar(gridLeft, _currentMonthLeft, true);
            RenderCalendar(gridRight, _currentMonthLeft.AddMonths(1), false);

            txtMonthLeft.Text = _currentMonthLeft.ToString("'Tháng' MM yyyy");
            txtMonthRight.Text = _currentMonthLeft.AddMonths(1).ToString("'Tháng' MM yyyy");

            txtStartDisplay.Text = _startDate.ToString("dd/MM/yyyy");
            txtEndDisplay.Text = _endDate.ToString("dd/MM/yyyy");
        }

        private void RenderCalendar(ItemsControl grid, System.DateTime month, bool isLeft)
        {
            grid.Items.Clear();
            System.DateTime firstDay = new System.DateTime(month.Year, month.Month, 1);
            int offset = (int)firstDay.DayOfWeek;
            System.DateTime start = firstDay.AddDays(-offset);

            for (int i = 0; i < 42; i++)
            {
                System.DateTime d = start.AddDays(i);
                Button btn = new Button
                {
                    Content = d.Day.ToString(),
                    Style = (Style)this.Resources["DayButton"],
                    Tag = d
                };

                // Styling based on state
                bool isSelected = (d.Date == _startDate.Date || d.Date == _endDate.Date);
                bool isInRange = (d.Date > _startDate.Date && d.Date < _endDate.Date);
                bool isOuter = d.Month != month.Month;

                if (isSelected) { btn.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#3D85C6")); btn.Foreground = Brushes.White; }
                else if (isInRange) { btn.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#1A3D5E")); }
                
                if (isOuter) btn.Foreground = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#333"));
                else if (!isSelected) btn.Foreground = Brushes.Silver;

                btn.Click += Day_Click;
                grid.Items.Add(btn);
            }
        }

        private void Day_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is System.DateTime d)
            {
                // Simple logic: first click sets Start, second sets End (if after Start), else Reset Start
                if (_startDate == _endDate || d < _startDate)
                {
                    _startDate = d.Date;
                    _endDate = d.Date;
                }
                else
                {
                    _endDate = d.Date;
                }
                UpdateUI();
            }
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentMonthLeft = _currentMonthLeft.AddMonths(-1);
            UpdateUI();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentMonthLeft = _currentMonthLeft.AddMonths(1);
            UpdateUI();
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                System.DateTime now = System.DateTime.Now.Date;
                switch (tag)
                {
                    case "7d": _startDate = now.AddDays(-7); _endDate = now; break;
                    case "14d": _startDate = now.AddDays(-14); _endDate = now; break;
                    case "30d": _startDate = now.AddDays(-30); _endDate = now; break;
                    case "last_month": 
                        var prev = now.AddMonths(-1);
                        _startDate = new System.DateTime(prev.Year, prev.Month, 1);
                        _endDate = _startDate.AddMonths(1).AddDays(-1);
                        break;
                    case "last_year":
                        _startDate = new System.DateTime(now.Year - 1, 1, 1);
                        _endDate = new System.DateTime(now.Year - 1, 12, 31);
                        break;
                }
                _currentMonthLeft = new System.DateTime(_startDate.Year, _startDate.Month, 1);
                UpdateUI();
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _startDate = System.DateTime.Now.Date;
            _endDate = System.DateTime.Now.Date;
            UpdateUI();
        }

        // Get final range with time
        public (System.DateTime start, System.DateTime end) GetRange()
        {
            int h = 0, m = 0;
            int.TryParse(txtHour.Text, out h);
            int.TryParse(txtMinute.Text, out m);
            
            // For playback, usually search starts from 00:00 of start date to 23:59 of end date
            // or specific time if user provided.
            // Let's use the provided time for BOTH for now, or just start=00:00, end=current/23:59
            return (new System.DateTime(_startDate.Year, _startDate.Month, _startDate.Day, h, m, 0),
                    new System.DateTime(_endDate.Year, _endDate.Month, _endDate.Day, 23, 59, 59));
        }
    }
}
