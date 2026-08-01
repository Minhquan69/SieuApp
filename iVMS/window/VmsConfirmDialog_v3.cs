using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace V3SClient.window
{
    public sealed class VmsConfirmDialog_v3 : Window
    {
        private VmsConfirmDialog_v3(Window owner, string title, string message)
        {
            Owner = owner;
            Title = title;
            Width = 430; Height = 214;
            MinWidth = Width; MinHeight = Height; MaxWidth = Width; MaxHeight = Height;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
            ShowInTaskbar = false;
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = Brushes.Transparent;

            var border = new Border { Background = Brush("#0B1E31"), BorderBrush = Brush("#1A4975"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(24) };
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); header.ColumnDefinitions.Add(new ColumnDefinition());
            var iconBorder = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Brush("#173B66"), Child = new TextBlock { Text = "?", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brush("#B8D5FF"), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Grid.SetColumn(iconBorder, 0); header.Children.Add(iconBorder);
            var titleText = new TextBlock { Text = title, Foreground = Brush("#F3F7FF"), FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(titleText, 1); header.Children.Add(titleText); root.Children.Add(header);

            var messageText = new TextBlock { Text = message, Foreground = Brush("#B7C7D9"), FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
            Grid.SetRow(messageText, 1); root.Children.Add(messageText);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = CreateButton("Hủy", "#122D48", "#28517A"); cancel.Margin = new Thickness(0, 0, 8, 0); cancel.Click += (s, e) => DialogResult = false;
            var confirm = CreateButton("Đăng xuất", "#2563EB", "#2563EB"); confirm.Click += (s, e) => DialogResult = true;
            actions.Children.Add(cancel); actions.Children.Add(confirm); Grid.SetRow(actions, 2); root.Children.Add(actions);
            border.Child = root; Content = border;
        }

        public static bool ConfirmLogout(Window owner) { return new VmsConfirmDialog_v3(owner, "Đăng xuất", "Bạn có muốn đăng xuất khỏi hệ thống không?").ShowDialog() == true; }

        private static Button CreateButton(string text, string background, string border)
        {
            return new Button { Content = text, MinWidth = 92, Height = 34, Padding = new Thickness(14, 0, 14, 0), Foreground = Brushes.White, Background = Brush(background), BorderBrush = Brush(border), BorderThickness = new Thickness(1), FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand };
        }

        private static SolidColorBrush Brush(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
    }
}
