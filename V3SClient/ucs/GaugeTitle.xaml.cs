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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for GaugeTitle.xaml
    /// </summary>
    public partial class GaugeTitle : UserControl
    {
        public GaugeTitle()
        {
            InitializeComponent();
        }
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(GaugeTitle), new PropertyMetadata(nameof(Title), OnTitleChanged));

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GaugeTitle control)
            {
                control.TitleTextBlock.Text = e.NewValue as string;
            }
        }
        public UIElement GaugeContent
        {
            get => (UIElement)GetValue(GaugeContentProperty);
            set => SetValue(GaugeContentProperty, value);
        }

        public static readonly DependencyProperty GaugeContentProperty =
            DependencyProperty.Register(nameof(GaugeContent), typeof(UIElement), typeof(GaugeTitle), new PropertyMetadata(null, OnGaugeContentChanged));

        private static void OnGaugeContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GaugeTitle control)
            {
                control.GaugeContainer.Content = e.NewValue;
            }
        }
    }
}

















