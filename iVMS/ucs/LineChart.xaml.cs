using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
using LiveCharts.Configurations;
using LiveCharts;
using System.Windows.Threading;
using SharpDX;

namespace V3SClient.ucs
{
    /// <summary>
    /// Interaction logic for LineChart.xaml
    /// </summary>
    public partial class LineChart : UserControl, INotifyPropertyChanged
    {
        private double _axisXMax;
        private double _axisXMin;
        private double _axisYMax=100;
        private double _axisYMin=0;
        private double _trend;
        public LineChart()
        {
            InitializeComponent();

            var mapper = Mappers.Xy<MeasureModel>()
               .X(model => model.DateTime.Ticks)   //use DateTime.Ticks as X
               .Y(model => model.Value);

            Charting.For<MeasureModel>(mapper);

            //the values property will store our values array
            ChartValues = new ChartValues<MeasureModel>();

            //lets set how to display the X Labels
            DateTimeFormatter = value => new DateTime((long)value).ToString("mm:ss");

            //AxisStep forces the distance between each separator in the X axis
            AxisXStep = TimeSpan.FromSeconds(1).Ticks;
            //AxisUnit forces lets the axis know that we are plotting seconds
            //this is not always necessary, but it can prevent wrong labeling
            AxisXUnit = TimeSpan.TicksPerSecond;

            SetAxisXLimits(DateTime.Now);

            //The next code simulates data changes every 300 ms

            IsReading = false;

            //DispatcherTimer timer = new DispatcherTimer();
            //timer.Interval = TimeSpan.FromSeconds(1);
            //timer.Tick += timer_Tick;
            //timer.Start();

            DataContext = this;
        }

        public double AxisXMax
        {
            get { return _axisXMax; }
            set
            {
                _axisXMax = value;
                OnPropertyChanged(nameof(AxisXMax));
            }
        }
        public double AxisXMin
        {
            get { return _axisXMin; }
            set
            {
                _axisXMin = value;
                OnPropertyChanged(nameof(AxisXMin));
            }
        }
        public double AxisXStep { get; set; }
        public double AxisXUnit { get; set; }

        public double AxisYMax
        {
            get { return _axisYMax; }
            set
            {
                _axisYMax = value;
                OnPropertyChanged(nameof(AxisYMax));
            }
        }
        public double AxisYMin
        {
            get { return _axisYMin; }
            set
            {
                _axisYMin = value;
                OnPropertyChanged(nameof(AxisYMin));
            }
        }

        public bool IsReading { get; set; }

        PerformanceCounter performanceCPU = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");

        public ChartValues<MeasureModel> ChartValues { get; set; }
        public Func<double, string> DateTimeFormatter { get; set; }
       


        public void SetAxisXLimits(DateTime now)
        {
            AxisXMax = now.Ticks + TimeSpan.FromSeconds(1).Ticks; // lets force the axis to be 1 second ahead
            AxisXMin = now.Ticks - TimeSpan.FromSeconds(30).Ticks; // and 8 seconds behind
        }
        public void SetAxisYLimits(double yAxisMin,double yAxisMax)
        {
            AxisYMax = yAxisMax;
            AxisYMin = yAxisMin;
        }
        void timer_Tick(object sender, EventArgs e)
        {
            var now = DateTime.Now;

            ChartValues.Add(new MeasureModel
            {
                DateTime = now,
                Value = (double)performanceCPU.NextValue()
            });

            SetAxisXLimits(now);

            //lets only use the last 150 values
            if (ChartValues.Count > 150) ChartValues.RemoveAt(0);

        }
        public void SetVal(double val)
        {
            var now = DateTime.Now;
            ChartValues.Add(new MeasureModel
            {
                DateTime = now,
                Value = val
            });
            SetAxisXLimits(now);
            if (ChartValues.Count > 150) ChartValues.RemoveAt(0);
        }

        #region INotifyPropertyChanged implementation

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
        public class MeasureModel
        {
            public DateTime DateTime { get; set; }
            public double Value { get; set; }
        }
    }
}

















