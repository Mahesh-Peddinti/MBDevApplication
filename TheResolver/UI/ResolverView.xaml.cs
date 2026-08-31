using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TheResolver.ViewModel;

namespace TheResolver.UI
{
    /// <summary>
    /// Interaction logic for ResolverView.xaml
    /// </summary>
    public partial class ResolverView : UserControl
    {
        public ResolverView()
        {
            InitializeComponent();

            // The property used to stay null, so callers that wanted to drive
            // the pane from a command had nothing to talk to.
            ViewModel = new ClashViewModel();

            DataContext = ViewModel;

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        public ClashViewModel ViewModel { get; }

        private void OnViewModelPropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ClashViewModel.PreviewRouteData))
                RenderPreview();
        }

        private void PreviewCanvas_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            RenderPreview();
        }

        // ------------------------------------------------------------
        // 2D SIDE-VIEW PREVIEW RENDERING
        // ------------------------------------------------------------

        private static readonly Brush TrayBrush =
            new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));

        private static readonly Brush ClashFill =
            new SolidColorBrush(Color.FromArgb(0x40, 0xE5, 0x39, 0x35));

        private static readonly Brush ClashStroke =
            new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

        private static readonly Brush RouteFill =
            new SolidColorBrush(Color.FromArgb(0x22, 0x4C, 0xAF, 0x50));

        private static readonly Brush RouteStroke =
            new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

        private static readonly Brush DimBrush =
            new SolidColorBrush(Color.FromRgb(0x90, 0xA4, 0xAE));

        private static readonly Brush InfoBrush =
            new SolidColorBrush(Color.FromRgb(0xBD, 0xD3, 0xF2));

        private static readonly Brush LabelBrush =
            new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xED));

        private void RenderPreview()
        {
            var canvas = PreviewCanvas;

            if (canvas == null)
                return;

            canvas.Children.Clear();

            var data = ViewModel?.PreviewRouteData;

            if (data == null
                || data.RoutePoints == null
                || data.RoutePoints.Count < 4)
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

            if (cw < 10 || ch < 10)
                return;

            const double m = 26;

            // World bounds (mm): station = along tray, elevation = Z.
            double sMin = data.RoutePoints[0].Station;
            double sMax = data.RoutePoints[0].Station;
            double eMin = data.RoutePoints[0].Elevation;
            double eMax = data.RoutePoints[0].Elevation;

            foreach (var p in data.RoutePoints)
            {
                sMin = System.Math.Min(sMin, p.Station);
                sMax = System.Math.Max(sMax, p.Station);
                eMin = System.Math.Min(eMin, p.Elevation);
                eMax = System.Math.Max(eMax, p.Elevation);
            }

            if (data.ClashStationMaxMm > data.ClashStationMinMm)
            {
                sMin = System.Math.Min(sMin, data.ClashStationMinMm);
                sMax = System.Math.Max(sMax, data.ClashStationMaxMm);
            }

            eMin = System.Math.Min(eMin, data.ClashMinElevationMm);
            eMax = System.Math.Max(eMax, data.ClashMaxElevationMm);

            double sPad = (sMax - sMin) * 0.12;
            double ePad = (eMax - eMin) * 0.20;

            if (sPad < 30) sPad = 30;
            if (ePad < 25) ePad = 25;

            sMin -= sPad;
            sMax += sPad;
            eMin -= ePad;
            eMax += ePad;

            double rangeX = System.Math.Max(1.0, sMax - sMin);
            double rangeY = System.Math.Max(1.0, eMax - eMin);

            double scaleX = (cw - 2 * m) / rangeX;
            double scaleY = (ch - 2 * m) / rangeY;

            System.Func<double, double, Point> map = (s, e) =>
                new Point(
                    m + (s - sMin) * scaleX,
                    m + (eMax - e) * scaleY);

            // ---- Tray line ----
            double trayVisStart = System.Math.Max(0, sMin);
            double trayVisEnd = System.Math.Min(data.TrayLengthMm, sMax);

            if (trayVisStart < trayVisEnd)
            {
                var ts = map(trayVisStart, 0);
                var te = map(trayVisEnd, 0);

                canvas.Children.Add(
                    new Line
                    {
                        X1 = ts.X, Y1 = ts.Y,
                        X2 = te.X, Y2 = te.Y,
                        Stroke = TrayBrush,
                        StrokeThickness = 3
                    });

                AddLabel(canvas, "Tray", TrayBrush, 9, ts.X + 2, ts.Y + 6);
            }

            // ---- Clash element ----
            if (data.ClashStationMaxMm > data.ClashStationMinMm)
            {
                var tl = map(data.ClashStationMinMm, data.ClashMaxElevationMm);
                var br = map(data.ClashStationMaxMm, data.ClashMinElevationMm);

                var rect = new Rectangle
                {
                    Width = System.Math.Max(1, br.X - tl.X),
                    Height = System.Math.Max(1, br.Y - tl.Y),
                    Fill = ClashFill,
                    Stroke = ClashStroke,
                    StrokeThickness = 1.5
                };

                Canvas.SetLeft(rect, tl.X);
                Canvas.SetTop(rect, tl.Y);
                canvas.Children.Add(rect);

                AddLabel(
                    canvas,
                    "Clash",
                    ClashStroke,
                    9,
                    (tl.X + br.X) / 2 - 14,
                    tl.Y - 13);
            }

            // ---- Bypass route ----
            var pts = data.RoutePoints;
            var poly = new Polygon
            {
                Stroke = RouteStroke,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = RouteFill
            };

            poly.Points.Add(map(pts[0].Station, pts[0].Elevation));
            poly.Points.Add(map(pts[1].Station, pts[1].Elevation));
            poly.Points.Add(map(pts[2].Station, pts[2].Elevation));
            poly.Points.Add(map(pts[3].Station, pts[3].Elevation));
            canvas.Children.Add(poly);

            // ---- P1..P4 markers + labels ----
            string[] pLabels = { "P1", "P2", "P3", "P4" };

            for (int i = 0; i < 4 && i < pts.Count; i++)
            {
                var p = map(pts[i].Station, pts[i].Elevation);

                canvas.Children.Add(
                    new Ellipse
                    {
                        Width = 8, Height = 8,
                        Fill = Brushes.White,
                        Stroke = RouteStroke,
                        StrokeThickness = 1.5
                    });

                Canvas.SetLeft(canvas.Children[canvas.Children.Count - 1], p.X - 4);
                Canvas.SetTop(canvas.Children[canvas.Children.Count - 1], p.Y - 4);

                double ly = (i == 1 || i == 2) ? p.Y - 18 : p.Y + 10;
                AddLabel(canvas, pLabels[i], LabelBrush, 9, p.X - 8, ly);
            }

            // ---- Rise dimension ----
            if (data.RiseMm > 1)
            {
                double midS = (pts[1].Station + pts[2].Station) / 2;
                var rb = map(midS, 0);
                var rt = map(midS, pts[1].Elevation);

                canvas.Children.Add(
                    new Line
                    {
                        X1 = rb.X, Y1 = rb.Y,
                        X2 = rt.X, Y2 = rt.Y,
                        Stroke = DimBrush,
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 4, 2 }
                    });

                AddLabel(
                    canvas,
                    $"Rise: {data.RiseMm:F0} mm",
                    InfoBrush,
                    9,
                    rb.X + 4,
                    (rb.Y + rt.Y) / 2 - 6);
            }

            // ---- Direction + clearance info (top-right) ----
            var info = new StackPanel { Orientation = Orientation.Vertical };

            info.Children.Add(
                new TextBlock
                {
                    Text = data.IsDetourUp
                        ? "▲ Detour Up"
                        : "▼ Detour Down",
                    Foreground = InfoBrush,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                });

            if (data.ClearanceMm > 0)
                info.Children.Add(
                    new TextBlock
                    {
                        Text = $"Clearance: {data.ClearanceMm:F0} mm",
                        Foreground = DimBrush,
                        FontSize = 8
                    });

            canvas.Children.Add(info);
            info.Measure(
                new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(info, cw - info.DesiredSize.Width - 8);
            Canvas.SetTop(info, 4);
        }

        private static void AddLabel(
            Canvas canvas,
            string text,
            Brush brush,
            double size,
            double x,
            double y)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontSize = size,
                FontWeight = FontWeights.Bold
            };

            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            canvas.Children.Add(tb);
        }
    }
}
