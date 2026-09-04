using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TheResolver.ViewModel;

namespace TheResolver.UI
{
    public partial class ResolverView : UserControl
    {
        public ResolverView()
        {
            InitializeComponent();
            ViewModel = new ClashViewModel();
            DataContext = ViewModel;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        public ClashViewModel ViewModel { get; }

        private void HeaderSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && ViewModel != null)
            {
                ViewModel.IsAllSelected = chk.IsChecked == true;
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ClashViewModel.PreviewRouteData))
                RenderPreview();
        }

        private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RenderPreview();
        }

        private static readonly Brush TrayBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x95, 0xED));
        private static readonly Brush ClashFill = new SolidColorBrush(Color.FromArgb(0x40, 0xE5, 0x39, 0x35));
        private static readonly Brush ClashStroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        private static readonly Brush RouteFill = new SolidColorBrush(Color.FromArgb(0x22, 0x4C, 0xAF, 0x50));
        private static readonly Brush RouteStroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly Brush DimBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x95, 0xED));
        private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x95, 0xED));

        private static readonly Brush SecObstacleFill = new SolidColorBrush(Color.FromArgb(0x25, 0x80, 0x80, 0x80));
        private static readonly Brush SecObstacleStroke = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

        private void RenderPreview()
        {
            var canvas = PreviewCanvas;
            if (canvas == null) return;
            canvas.Children.Clear();

            var data = ViewModel?.PreviewRouteData;
            if (data?.RoutePoints == null || data.RoutePoints.Count < 2)
            {
                var tb = new TextBlock
                {
                    Text = data?.Message ?? "Select a clash to preview",
                    Foreground = DimBrush,
                    FontSize = 11
                };
                canvas.Children.Add(tb);
                Canvas.SetLeft(tb, 8);
                Canvas.SetTop(tb, 8);
                return;
            }

            double cw = canvas.ActualWidth;
            double ch = canvas.ActualHeight;
            if (cw < 10 || ch < 10) return;

            const double m = 24;
            double sMin = double.MaxValue, sMax = double.MinValue;
            double eMin = double.MaxValue, eMax = double.MinValue;

            foreach (var p in data.RoutePoints)
            {
                sMin = Math.Min(sMin, p.Station);
                sMax = Math.Max(sMax, p.Station);
                eMin = Math.Min(eMin, p.Elevation);
                eMax = Math.Max(eMax, p.Elevation);
            }

            if (data.ClashStationMaxMm > data.ClashStationMinMm)
            {
                sMin = Math.Min(sMin, data.ClashStationMinMm);
                sMax = Math.Max(sMax, data.ClashStationMaxMm);
                eMin = Math.Min(eMin, data.ClashMinElevationMm);
                eMax = Math.Max(eMax, data.ClashMaxElevationMm);
            }

            foreach (var sec in data.SecondaryObstacles)
            {
                sMin = Math.Min(sMin, sec.StationMinMm);
                sMax = Math.Max(sMax, sec.StationMaxMm);
                eMin = Math.Min(eMin, sec.ElevationMinMm);
                eMax = Math.Max(eMax, sec.ElevationMaxMm);
            }

            double sPad = Math.Max(30, (sMax - sMin) * 0.15);
            double ePad = Math.Max(25, (eMax - eMin) * 0.20);
            sMin -= sPad; sMax += sPad;
            eMin -= ePad; eMax += ePad;

            double scaleX = (cw - 2 * m) / Math.Max(1.0, sMax - sMin);
            double scaleY = (ch - 2 * m) / Math.Max(1.0, eMax - eMin);

            Point Map(double s, double e) => new Point(m + (s - sMin) * scaleX, m + (eMax - e) * scaleY);

            // Baseline Tray Line
            var ts = Map(Math.Max(0, sMin), 0);
            var te = Map(Math.Min(data.TrayLengthMm, sMax), 0);
            canvas.Children.Add(new Line { X1 = ts.X, Y1 = ts.Y, X2 = te.X, Y2 = te.Y, Stroke = TrayBrush, StrokeThickness = 3 });

            // Secondary Environmental Obstacles (Surrounding Geometry)
            foreach (var sec in data.SecondaryObstacles)
            {
                var pTopLeft = Map(sec.StationMinMm, sec.ElevationMaxMm);
                var pBottomRight = Map(sec.StationMaxMm, sec.ElevationMinMm);
                var sRect = new Rectangle
                {
                    Width = Math.Max(2, pBottomRight.X - pTopLeft.X),
                    Height = Math.Max(2, pBottomRight.Y - pTopLeft.Y),
                    Fill = SecObstacleFill,
                    Stroke = SecObstacleStroke,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(sRect, pTopLeft.X);
                Canvas.SetTop(sRect, pTopLeft.Y);
                canvas.Children.Add(sRect);
            }

            // Primary Clash Obstacle (Red highlight)
            if (data.ClashStationMaxMm > data.ClashStationMinMm)
            {
                var tl = Map(data.ClashStationMinMm, data.ClashMaxElevationMm);
                var br = Map(data.ClashStationMaxMm, data.ClashMinElevationMm);
                var rect = new Rectangle
                {
                    Width = Math.Max(2, br.X - tl.X),
                    Height = Math.Max(2, br.Y - tl.Y),
                    Fill = ClashFill,
                    Stroke = ClashStroke,
                    StrokeThickness = 1.5
                };
                Canvas.SetLeft(rect, tl.X);
                Canvas.SetTop(rect, tl.Y);
                canvas.Children.Add(rect);
                AddLabel(canvas, "Clash", ClashStroke, 9, (tl.X + br.X) * 0.5 - 14, tl.Y - 13);
            }

            // Dynamic Bypass Polyline
            var poly = new Polyline
            {
                Stroke = RouteStroke,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round
            };

            for (int i = 0; i < data.RoutePoints.Count; i++)
            {
                var rp = data.RoutePoints[i];
                Point pt = Map(rp.Station, rp.Elevation);
                poly.Points.Add(pt);

                var dot = new Ellipse { Width = 6, Height = 6, Fill = Brushes.White, Stroke = RouteStroke, StrokeThickness = 1.5 };
                Canvas.SetLeft(dot, pt.X - 3);
                Canvas.SetTop(dot, pt.Y - 3);
                canvas.Children.Add(dot);

                double labelY = (i > 0 && i < data.RoutePoints.Count - 1) ? pt.Y - 16 : pt.Y + 8;
                AddLabel(canvas, $"P{i + 1}", LabelBrush, 9, pt.X - 6, labelY);
            }
            canvas.Children.Add(poly);
        }

        private static void AddLabel(Canvas canvas, string text, Brush brush, double size, double x, double y)
        {
            var tb = new TextBlock { Text = text, Foreground = brush, FontSize = size, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
        }
    }
}