using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MahApps.Metro.IconPacks;

namespace V3SClient.ucs
{
    public partial class SidebarControl_v3 : UserControl
    {
        public static readonly DependencyProperty IsCollapsedProperty = DependencyProperty.Register(nameof(IsCollapsed), typeof(bool), typeof(SidebarControl_v3), new PropertyMetadata(false, OnCollapsedChanged));
        public bool IsCollapsed { get { return (bool)GetValue(IsCollapsedProperty); } set { SetValue(IsCollapsedProperty, value); } }

        public SidebarControl_v3() { InitializeComponent(); }

        private static void OnCollapsedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SidebarControl_v3)d).ApplyCollapsedState((bool)e.NewValue, true);
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e) { IsCollapsed = !IsCollapsed; }

        private void ApplyCollapsedState(bool collapsed, bool animate)
        {
            var target = collapsed ? 64d : 176d;
            if (animate)
            {
                var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase() };
                BeginAnimation(WidthProperty, animation);
            }
            else Width = target;
            LogoImage.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            LogoSubtext.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            CollapsedLogo.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            CollapsedLogoSubtext.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            ExpandedFooter.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            CollapsedFooter.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            ToggleText.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            ToggleIcon.Kind = collapsed ? PackIconMaterialKind.ChevronRight : PackIconMaterialKind.ChevronLeft;
            ToggleButton.Width = collapsed ? 34 : double.NaN;
            ToggleButton.ToolTip = collapsed ? "Mở rộng" : "Thu gọn";
        }
    }
}
