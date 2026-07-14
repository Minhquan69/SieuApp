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
    /// Interaction logic for Gauge.xaml
    /// </summary>
    public partial class Gauge : UserControl
    {
        public Gauge()
        {
            InitializeComponent();
        }
        public double GaugeValue
        {
            get { return (double)GetValue(GaugeValueProperty); }
            set { SetValue(GaugeValueProperty, value); }
        }

        public static readonly DependencyProperty GaugeValueProperty = DependencyProperty.Register("GaugeValue", typeof(double), typeof(Gauge),new PropertyMetadata(0.0));




        public SolidColorBrush Foreground1
        {
            get { return (SolidColorBrush)GetValue(Foreground1Property); }
            set { SetValue(Foreground1Property, value); }
        }
        public static readonly DependencyProperty Foreground1Property =
            DependencyProperty.Register("Foreground1", typeof(SolidColorBrush), typeof(Gauge),
                new PropertyMetadata(new SolidColorBrush( Colors.Blue)));

  
        public SolidColorBrush Foreground2
        {
            get { return (SolidColorBrush)GetValue(Foreground2Property); }
            set { SetValue(Foreground2Property, value); }
        }

        public static readonly DependencyProperty Foreground2Property =
            DependencyProperty.Register("Foreground2", typeof(SolidColorBrush), typeof(Gauge),
                new PropertyMetadata(new SolidColorBrush(Colors.Red)));

        

        public double Radius
        {
            get { return (double)GetValue(RadiusProperty); }
            set { SetValue(RadiusProperty, value); }
        }

        public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register("Radius", typeof(double), typeof(Gauge),new PropertyMetadata(10.0));

    }
}

















