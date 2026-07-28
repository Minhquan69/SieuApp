using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace V3SClient.ucs
{
    public sealed class CustomLayoutCell_v3
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public int RowSpan { get; set; } = 1;
        public int ColumnSpan { get; set; } = 1;

        public CustomLayoutCell_v3 Clone()
        {
            return new CustomLayoutCell_v3 { Row = Row, Column = Column, RowSpan = RowSpan, ColumnSpan = ColumnSpan };
        }
    }

    public partial class CustomLayoutDialog_v3 : Window
    {
        private readonly List<CustomLayoutCell_v3> _cells = new List<CustomLayoutCell_v3>();
        private Tuple<int, int> _selectionStart;
        private Tuple<int, int> _selectionEnd;
        private bool _isSelecting;

        public int Rows { get; private set; }
        public int Columns { get; private set; }
        public IReadOnlyList<CustomLayoutCell_v3> LayoutCells
        {
            get { return _cells.Select(cell => cell.Clone()).ToList(); }
        }

        public CustomLayoutDialog_v3(int defaultRows, int defaultColumns, IEnumerable<CustomLayoutCell_v3> savedCells = null)
        {
            InitializeComponent();
            Rows = Math.Max(1, defaultRows);
            Columns = Math.Max(1, defaultColumns);
            RowsInput.Text = Rows.ToString();
            ColumnsInput.Text = Columns.ToString();
            if (savedCells != null)
                _cells.AddRange(savedCells.Where(IsValidCell).Select(cell => cell.Clone()));
            if (_cells.Count == 0) ResetToSingleCells();
            Loaded += delegate { RenderPreview(); };
        }

        private bool IsValidCell(CustomLayoutCell_v3 cell)
        {
            return cell != null && cell.Row >= 0 && cell.Column >= 0 && cell.RowSpan > 0 && cell.ColumnSpan > 0 &&
                   cell.Row + cell.RowSpan <= Rows && cell.Column + cell.ColumnSpan <= Columns;
        }

        private void IntegerInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is Button || e.LeftButton != MouseButtonState.Pressed) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; }

        private bool TryReadGridSize(out int rows, out int columns)
        {
            rows = columns = 0;
            if (!int.TryParse(RowsInput.Text, out rows) || !int.TryParse(ColumnsInput.Text, out columns) || rows < 1 || columns < 1)
            {
                ShowError("S\u1ed1 h\u00e0ng v\u00e0 s\u1ed1 c\u1ed9t ph\u1ea3i l\u00e0 s\u1ed1 nguy\u00ean l\u1edbn h\u01a1n 0.");
                return false;
            }
            if ((long)rows * columns > 120)
            {
                ShowError("T\u1ed5ng s\u1ed1 \u00f4 kh\u00f4ng \u0111\u01b0\u1ee3c v\u01b0\u1ee3t qu\u00e1 120.");
                return false;
            }
            return true;
        }

        private void CreateGridButton_Click(object sender, RoutedEventArgs e)
        {
            int rows, columns;
            if (!TryReadGridSize(out rows, out columns)) return;
            Rows = rows;
            Columns = columns;
            ResetToSingleCells();
            ClearSelection();
            HideError();
            RenderPreview();
        }

        private void ResetToSingleCells()
        {
            _cells.Clear();
            for (var row = 0; row < Rows; row++)
                for (var column = 0; column < Columns; column++)
                    _cells.Add(new CustomLayoutCell_v3 { Row = row, Column = column });
        }

        private void RenderPreview()
        {
            PreviewGrid.Children.Clear();
            PreviewGrid.RowDefinitions.Clear();
            PreviewGrid.ColumnDefinitions.Clear();
            for (var row = 0; row < Rows; row++) PreviewGrid.RowDefinitions.Add(new RowDefinition());
            for (var column = 0; column < Columns; column++) PreviewGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var bounds = GetSelectionBounds();
            foreach (var cell in _cells.OrderBy(item => item.Row).ThenBy(item => item.Column))
            {
                var selected = bounds != null && IsCellInside(cell, bounds);
                var border = new Border
                {
                    Tag = cell,
                    Margin = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selected ? "#2864C4" : "#10243A")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selected ? "#70A5FF" : "#285276")),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    Child = new TextBlock
                    {
                        Text = cell.RowSpan > 1 || cell.ColumnSpan > 1 ? cell.RowSpan + "\u00d7" + cell.ColumnSpan : string.Empty,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeights.SemiBold
                    }
                };
                border.MouseLeftButtonDown += Cell_MouseLeftButtonDown;
                border.MouseEnter += Cell_MouseEnter;
                border.MouseLeftButtonUp += Cell_MouseLeftButtonUp;
                Grid.SetRow(border, cell.Row);
                Grid.SetColumn(border, cell.Column);
                Grid.SetRowSpan(border, cell.RowSpan);
                Grid.SetColumnSpan(border, cell.ColumnSpan);
                PreviewGrid.Children.Add(border);
            }
            LayoutSummaryText.Text = string.Format("{0} h\u00e0ng \u00d7 {1} c\u1ed9t \u2022 {2} khung hi\u1ec3n th\u1ecb", Rows, Columns, _cells.Count);
        }

        private void Cell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var cell = ((FrameworkElement)sender).Tag as CustomLayoutCell_v3;
            if (cell == null) return;
            _selectionStart = Tuple.Create(cell.Row, cell.Column);
            _selectionEnd = Tuple.Create(cell.Row + cell.RowSpan - 1, cell.Column + cell.ColumnSpan - 1);
            _isSelecting = true;
            ((UIElement)sender).CaptureMouse();
            RenderPreview();
        }

        private void Cell_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isSelecting || e.LeftButton != MouseButtonState.Pressed) return;
            var cell = ((FrameworkElement)sender).Tag as CustomLayoutCell_v3;
            if (cell == null) return;
            _selectionEnd = Tuple.Create(cell.Row + cell.RowSpan - 1, cell.Column + cell.ColumnSpan - 1);
            RenderPreview();
        }

        private void Cell_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isSelecting = false;
            ((UIElement)sender).ReleaseMouseCapture();
        }

        private int[] GetSelectionBounds()
        {
            if (_selectionStart == null || _selectionEnd == null) return null;
            return new[]
            {
                Math.Min(_selectionStart.Item1, _selectionEnd.Item1), Math.Min(_selectionStart.Item2, _selectionEnd.Item2),
                Math.Max(_selectionStart.Item1, _selectionEnd.Item1), Math.Max(_selectionStart.Item2, _selectionEnd.Item2)
            };
        }

        private static bool IsCellInside(CustomLayoutCell_v3 cell, int[] bounds)
        {
            return cell.Row >= bounds[0] && cell.Column >= bounds[1] &&
                   cell.Row + cell.RowSpan - 1 <= bounds[2] && cell.Column + cell.ColumnSpan - 1 <= bounds[3];
        }

        private void MergeButton_Click(object sender, RoutedEventArgs e)
        {
            var bounds = GetSelectionBounds();
            if (bounds == null) { ShowError("H\u00e3y k\u00e9o ch\u1ecdn m\u1ed9t v\u00f9ng h\u00ecnh ch\u1eef nh\u1eadt c\u1ea7n g\u1ed9p."); return; }
            var area = (bounds[2] - bounds[0] + 1) * (bounds[3] - bounds[1] + 1);
            var selected = _cells.Where(cell => IsCellInside(cell, bounds)).ToList();
            if (area <= 1 || selected.Sum(cell => cell.RowSpan * cell.ColumnSpan) != area)
            {
                ShowError("V\u00f9ng g\u1ed9p ph\u1ea3i l\u00e0 h\u00ecnh ch\u1eef nh\u1eadt li\u00ean t\u1ee5c, kh\u00f4ng \u0111\u01b0\u1ee3c ch\u00e9o ho\u1eb7c khuy\u1ebft \u00f4.");
                return;
            }
            _cells.RemoveAll(cell => selected.Contains(cell));
            _cells.Add(new CustomLayoutCell_v3 { Row = bounds[0], Column = bounds[1], RowSpan = bounds[2] - bounds[0] + 1, ColumnSpan = bounds[3] - bounds[1] + 1 });
            ClearSelection();
            HideError();
            RenderPreview();
        }

        private void UnmergeButton_Click(object sender, RoutedEventArgs e)
        {
            var bounds = GetSelectionBounds();
            if (bounds == null) { ShowError("H\u00e3y ch\u1ecdn \u00f4 \u0111\u00e3 g\u1ed9p c\u1ea7n t\u00e1ch."); return; }
            var cell = _cells.FirstOrDefault(item => IsCellInside(item, bounds) && (item.RowSpan > 1 || item.ColumnSpan > 1));
            if (cell == null) { ShowError("H\u00e3y ch\u1ecdn \u00f4 \u0111\u00e3 g\u1ed9p c\u1ea7n t\u00e1ch."); return; }
            _cells.Remove(cell);
            for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                    _cells.Add(new CustomLayoutCell_v3 { Row = row, Column = column });
            ClearSelection();
            HideError();
            RenderPreview();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            int rows, columns;
            if (!TryReadGridSize(out rows, out columns)) return;
            if (rows != Rows || columns != Columns)
            {
                ShowError("S\u1ed1 h\u00e0ng ho\u1eb7c s\u1ed1 c\u1ed9t \u0111\u00e3 \u0111\u1ed5i. H\u00e3y b\u1ea5m \u201cT\u1ea1o l\u1ea1i l\u01b0\u1edb;i\u201d tr\u01b0\u1edbc khi \u00e1p d\u1ee5ng.");
                return;
            }
            DialogResult = true;
        }

        private void ClearSelection() { _selectionStart = _selectionEnd = null; }
        private void ShowError(string message) { ErrorText.Text = message; ErrorText.Visibility = Visibility.Visible; }
        private void HideError() { ErrorText.Visibility = Visibility.Collapsed; }
    }
}
