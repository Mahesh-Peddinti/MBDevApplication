using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RevitMepAutomation.Models;

namespace RevitMepAutomation.Controls
{
    public partial class ClashPreviewControl : UserControl
    {
        public static readonly DependencyProperty BendRadiusProperty =
            DependencyProperty.Register(nameof(BendRadius), typeof(double), typeof(ClashPreviewControl),
                new PropertyMetadata(300.0, OnGeometryPropertyChanged));

        public static readonly DependencyProperty BendAngleProperty =
            DependencyProperty.Register(nameof(BendAngle), typeof(double), typeof(ClashPreviewControl),
                new PropertyMetadata(45.0, OnGeometryPropertyChanged));

        public static readonly DependencyProperty OffsetSpanProperty =
            DependencyProperty.Register(nameof(OffsetSpan), typeof(double), typeof(ClashPreviewControl),
                new PropertyMetadata(500.0, OnGeometryPropertyChanged));

        public static readonly DependencyProperty TopClearanceProperty =
            DependencyProperty.Register(nameof(TopClearance), typeof(double), typeof(ClashPreviewControl),
                new PropertyMetadata(50.0, OnGeometryPropertyChanged));

        public static readonly DependencyProperty DirectionProperty =
            DependencyProperty.Register(nameof(Direction), typeof(RoutingDirection), typeof(ClashPreviewControl),
                new PropertyMetadata(RoutingDirection.Up, OnGeometryPropertyChanged));

        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register(nameof(Status), typeof(ClashStatus), typeof(ClashPreviewControl),
                new PropertyMetadata(ClashStatus.NotResolved, OnStatusPropertyChanged));

        public static readonly DependencyProperty ElementNameProperty =
            DependencyProperty.Register(nameof(ElementName), typeof(string), typeof(ClashPreviewControl),
                new PropertyMetadata("Cable Tray", OnGeometryPropertyChanged));

        public static readonly DependencyProperty ObstacleNameProperty =
            DependencyProperty.Register(nameof(ObstacleName), typeof(string), typeof(ClashPreviewControl),
                new PropertyMetadata("HVAC Duct", OnGeometryPropertyChanged));

        public double BendRadius
        {
            get => (double)GetValue(BendRadiusProperty);
            set => SetValue(BendRadiusProperty, value);
        }

        public double BendAngle
        {
            get => (double)GetValue(BendAngleProperty);
            set => SetValue(BendAngleProperty, value);
        }

        public double OffsetSpan
        {
            get => (double)GetValue(OffsetSpanProperty);
            set => SetValue(OffsetSpanProperty, value);
        }

        public double TopClearance
        {
            get => (double)GetValue(TopClearanceProperty);
            set => SetValue(TopClearanceProperty, value);
        }

        public RoutingDirection Direction
        {
            get => (RoutingDirection)GetValue(DirectionProperty);
            set => SetValue(DirectionProperty, value);
        }

        public ClashStatus Status
        {
            get => (ClashStatus)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        public string ElementName
        {
            get => (string)GetValue(ElementNameProperty);
            set => SetValue(ElementNameProperty, value);
        }

        public string ObstacleName
        {
            get => (string)GetValue(ObstacleNameProperty);
            set => SetValue(ObstacleNameProperty, value);
        }

        private PreviewViewMode _currentViewMode = PreviewViewMode.Isometric3D;
        private double _zoomFactor = 1.0;
        private Point _panOffset = new Point(0, 0);
        private Point _lastMousePos;
        private bool _isPanning = false;

        public ClashPreviewControl()
        {
            InitializeComponent();
            Loaded += (s, e) => Redraw();
        }

        private static void OnGeometryPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ClashPreviewControl preview)
            {
                preview.Redraw();
            }
        }

        private static void OnStatusPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ClashPreviewControl preview)
            {
                preview.UpdateStatusBadge();
                preview.Redraw();
            }
        }

        private void UpdateStatusBadge()
        {
            if (Status == ClashStatus.Resolved)
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // #22c55e
                TxtStatusBadge.Text = "RESOLVED";
                TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(134, 239, 172));
            }
            else
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // #ef4444
                TxtStatusBadge.Text = "CLASH ACTIVE";
                TxtStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(252, 165, 165));
            }
        }

        private void DrawingCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Redraw();
        }

        private void ViewMode_Checked(object sender, RoutedEventArgs e)
        {
            if (RbIso?.IsChecked == true) _currentViewMode = PreviewViewMode.Isometric3D;
            else if (RbFront?.IsChecked == true) _currentViewMode = PreviewViewMode.FrontElevation;
            else if (RbTop?.IsChecked == true) _currentViewMode = PreviewViewMode.TopPlan;

            Redraw();
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _zoomFactor = Math.Min(3.0, _zoomFactor * 1.2);
            Redraw();
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _zoomFactor = Math.Max(0.4, _zoomFactor / 1.2);
            Redraw();
        }

        private void BtnResetView_Click(object sender, RoutedEventArgs e)
        {
            _zoomFactor = 1.0;
            _panOffset = new Point(0, 0);
            Redraw();
        }

        private void DrawingCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                _zoomFactor = Math.Min(3.0, _zoomFactor * 1.1);
            else
                _zoomFactor = Math.Max(0.4, _zoomFactor / 1.1);

            Redraw();
            e.Handled = true;
        }

        private void DrawingCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
            {
                _isPanning = true;
                _lastMousePos = e.GetPosition(DrawingCanvas);
                DrawingCanvas.CaptureMouse();
            }
        }

        private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                Point currentPos = e.GetPosition(DrawingCanvas);
                Vector delta = currentPos - _lastMousePos;
                _panOffset.X += delta.X;
                _panOffset.Y += delta.Y;
                _lastMousePos = currentPos;
                Redraw();
            }
        }

        private void DrawingCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                DrawingCanvas.ReleaseMouseCapture();
            }
        }

        public void Redraw()
        {
            if (DrawingCanvas == null || DrawingCanvas.ActualWidth < 10 || DrawingCanvas.ActualHeight < 10)
                return;

            DrawingCanvas.Children.Clear();

            double width = DrawingCanvas.ActualWidth;
            double height = DrawingCanvas.ActualHeight;
            Point center = new Point(width / 2.0 + _panOffset.X, height / 2.0 + _panOffset.Y + 20);

            // Draw Background Grid
            DrawGrid(width, height);

            // Calculate Model Dimensions in mm
            double obstacleWidth = 350.0;
            double obstacleHeight = 220.0;
            double obstacleLength = 700.0;
            double trayWidth = 120.0;
            double trayHeight = 40.0;

            double span = Math.Max(200.0, OffsetSpan);
            double clearance = Math.Max(10.0, TopClearance);
            double angleDeg = Math.Max(10.0, Math.Min(85.0, BendAngle));
            double angleRad = angleDeg * Math.PI / 180.0;

            // Delta Z based on direction and clearance
            double sign = (Direction == RoutingDirection.Up) ? 1.0 : -1.0;
            double deltaZ = sign * (obstacleHeight / 2.0 + clearance + trayHeight / 2.0);

            // Transition horizontal run calculated from deltaZ and angle
            double transitionRun = Math.Abs(deltaZ) / Math.Tan(angleRad);
            double bridgeHalfLength = Math.Max(obstacleWidth / 2.0 + 40.0, span - transitionRun);

            // Coordinate Points (X: along tray, Y: across tray/obstacle, Z: elevation)
            // Obstacle is centered at (0,0,0)
            Point3D pStart = new Point3D(-span - 250, 0, 0);
            Point3D p1 = new Point3D(-span, 0, 0);
            Point3D p2 = new Point3D(-bridgeHalfLength, 0, deltaZ);
            Point3D p3 = new Point3D(bridgeHalfLength, 0, deltaZ);
            Point3D p4 = new Point3D(span, 0, 0);
            Point3D pEnd = new Point3D(span + 250, 0, 0);

            // Scale factor to fit model to canvas
            double baseScale = Math.Min(width, height) / 1400.0 * _zoomFactor;

            // Draw Obstacle (Green Rectangular Duct)
            DrawObstacle(center, baseScale, obstacleWidth, obstacleLength, obstacleHeight);

            // Draw Original Clash Path (Ghosted Red Line)
            DrawOriginalClashLine(center, baseScale, pStart, pEnd);

            // Draw Rerouted MEP Route (Polished Red/Orange Pipe or Tray with elbows)
            DrawReroutedPath(center, baseScale, pStart, p1, p2, p3, p4, pEnd, trayWidth, trayHeight);

            // Draw Dimensions & Annotations
            DrawAnnotations(center, baseScale, p1, p2, p3, p4, deltaZ, clearance, span, angleDeg);
        }

        private void DrawGrid(double width, double height)
        {
            var gridBrush = new SolidColorBrush(Color.FromArgb(25, 148, 163, 184));
            double step = 30 * _zoomFactor;
            if (step < 15) step = 30;

            for (double x = 0; x < width; x += step)
            {
                var line = new Line { X1 = x, Y1 = 0, X2 = x, Y2 = height, Stroke = gridBrush, StrokeThickness = 1 };
                DrawingCanvas.Children.Add(line);
            }
            for (double y = 0; y < height; y += step)
            {
                var line = new Line { X1 = 0, Y1 = y, X2 = width, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 };
                DrawingCanvas.Children.Add(line);
            }
        }

        private Point Project(Point3D p, Point center, double scale)
        {
            switch (_currentViewMode)
            {
                case PreviewViewMode.Isometric3D:
                    // Standard 30-deg Isometric projection
                    double cos30 = Math.Cos(Math.PI / 6); // ~0.866
                    double sin30 = Math.Sin(Math.PI / 6); // 0.5
                    double isoX = (p.X - p.Y) * cos30 * scale;
                    double isoY = ((p.X + p.Y) * sin30 - p.Z) * scale;
                    return new Point(center.X + isoX, center.Y + isoY);

                case PreviewViewMode.FrontElevation:
                    // Side elevation (X vs Z)
                    return new Point(center.X + p.X * scale, center.Y - p.Z * scale);

                case PreviewViewMode.TopPlan:
                    // Top plan (X vs Y)
                    return new Point(center.X + p.X * scale, center.Y + p.Y * scale);
            }
            return center;
        }

        private void DrawObstacle(Point center, double scale, double w, double l, double h)
        {
            // 3D Box vertices around center (0,0,0)
            double xmin = -w / 2.0;
            double xmax = w / 2.0;
            double ymin = -l / 2.0;
            double ymax = l / 2.0;
            double zmin = -h / 2.0;
            double zmax = h / 2.0;

            Point3D c1 = new Point3D(xmin, ymin, zmin);
            Point3D c2 = new Point3D(xmax, ymin, zmin);
            Point3D c3 = new Point3D(xmax, ymax, zmin);
            Point3D c4 = new Point3D(xmin, ymax, zmin);
            Point3D c5 = new Point3D(xmin, ymin, zmax);
            Point3D c6 = new Point3D(xmax, ymin, zmax);
            Point3D c7 = new Point3D(xmax, ymax, zmax);
            Point3D c8 = new Point3D(xmin, ymax, zmax);

            var fillTop = new SolidColorBrush(Color.FromArgb(190, 74, 222, 128));     // Vivid Light Green
            var fillSide1 = new SolidColorBrush(Color.FromArgb(170, 34, 197, 94));   // Emerald Green
            var fillSide2 = new SolidColorBrush(Color.FromArgb(140, 22, 163, 74));   // Darker Green
            var stroke = new SolidColorBrush(Color.FromRgb(21, 128, 61));

            if (_currentViewMode == PreviewViewMode.Isometric3D)
            {
                // Top Face (c5, c6, c7, c8)
                DrawPolygon(new[] { Project(c5, center, scale), Project(c6, center, scale), Project(c7, center, scale), Project(c8, center, scale) }, fillTop, stroke, 1.5);
                // Front-Right Face (c6, c2, c3, c7)
                DrawPolygon(new[] { Project(c6, center, scale), Project(c2, center, scale), Project(c3, center, scale), Project(c7, center, scale) }, fillSide1, stroke, 1.5);
                // Front-Left Face (c5, c1, c2, c6)
                DrawPolygon(new[] { Project(c5, center, scale), Project(c1, center, scale), Project(c2, center, scale), Project(c6, center, scale) }, fillSide2, stroke, 1.5);
            }
            else if (_currentViewMode == PreviewViewMode.FrontElevation)
            {
                Point p1 = Project(new Point3D(xmin, 0, zmax), center, scale);
                Point p2 = Project(new Point3D(xmax, 0, zmax), center, scale);
                Point p3 = Project(new Point3D(xmax, 0, zmin), center, scale);
                Point p4 = Project(new Point3D(xmin, 0, zmin), center, scale);
                DrawPolygon(new[] { p1, p2, p3, p4 }, fillSide1, stroke, 2);
            }
            else if (_currentViewMode == PreviewViewMode.TopPlan)
            {
                Point p1 = Project(new Point3D(xmin, ymin, 0), center, scale);
                Point p2 = Project(new Point3D(xmax, ymin, 0), center, scale);
                Point p3 = Project(new Point3D(xmax, ymax, 0), center, scale);
                Point p4 = Project(new Point3D(xmin, ymax, 0), center, scale);
                DrawPolygon(new[] { p1, p2, p3, p4 }, fillTop, stroke, 2);
            }

            // Obstacle Text Tag
            Point tagPoint = Project(new Point3D(0, 0, zmax), center, scale);
            DrawTextTag(tagPoint.X, tagPoint.Y - 18, ObstacleName, "#15803D", "#DCFCE7");
        }

        private void DrawOriginalClashLine(Point center, double scale, Point3D pStart, Point3D pEnd)
        {
            Point s = Project(pStart, center, scale);
            Point e = Project(pEnd, center, scale);

            var dashedLine = new Line
            {
                X1 = s.X, Y1 = s.Y,
                X2 = e.X, Y2 = e.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 239, 68, 68)),
                StrokeThickness = 2.5,
                StrokeDashArray = new DoubleCollection { 4, 3 }
            };
            DrawingCanvas.Children.Add(dashedLine);

            // Clash marker X in center
            Point clashCenter = Project(new Point3D(0, 0, 0), center, scale);
            DrawClashMarker(clashCenter);
        }

        private void DrawClashMarker(Point pt)
        {
            var glow = new Ellipse
            {
                Width = 20, Height = 20,
                Fill = new SolidColorBrush(Color.FromArgb(90, 239, 68, 68)),
                Margin = new Thickness(pt.X - 10, pt.Y - 10, 0, 0)
            };
            DrawingCanvas.Children.Add(glow);

            var dot = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                Margin = new Thickness(pt.X - 4, pt.Y - 4, 0, 0)
            };
            DrawingCanvas.Children.Add(dot);
        }

        private void DrawReroutedPath(Point center, double scale, Point3D pStart, Point3D p1, Point3D p2, Point3D p3, Point3D p4, Point3D pEnd, double width, double height)
        {
            Point ptStart = Project(pStart, center, scale);
            Point pt1 = Project(p1, center, scale);
            Point pt2 = Project(p2, center, scale);
            Point pt3 = Project(p3, center, scale);
            Point pt4 = Project(p4, center, scale);
            Point ptEnd = Project(pEnd, center, scale);

            // Draw primary MEP route line with thick, glowing brush
            var routeBrush = (Status == ClashStatus.Resolved)
                ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) // Cyan for resolved/rerouted
                : new SolidColorBrush(Color.FromRgb(249, 115, 22)); // Vivid Orange/Red for active

            var pathGeometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = ptStart };

            figure.Segments.Add(new LineSegment(pt1, true));
            figure.Segments.Add(new LineSegment(pt2, true));
            figure.Segments.Add(new LineSegment(pt3, true));
            figure.Segments.Add(new LineSegment(pt4, true));
            figure.Segments.Add(new LineSegment(ptEnd, true));
            pathGeometry.Figures.Add(figure);

            // Thick base shadow
            var shadowPath = new Path
            {
                Data = pathGeometry,
                Stroke = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                StrokeThickness = 12 * _zoomFactor,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            DrawingCanvas.Children.Add(shadowPath);

            // Main body
            var routePath = new Path
            {
                Data = pathGeometry,
                Stroke = routeBrush,
                StrokeThickness = 7 * _zoomFactor,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            DrawingCanvas.Children.Add(routePath);

            // Center highlight core line
            var corePath = new Path
            {
                Data = pathGeometry,
                Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                StrokeThickness = 2 * _zoomFactor,
                StrokeLineJoin = PenLineJoin.Round
            };
            DrawingCanvas.Children.Add(corePath);

            // Draw P1, P2, P3, P4 Joint Nodes
            DrawVertexNode(pt1, "P1");
            DrawVertexNode(pt2, "P2");
            DrawVertexNode(pt3, "P3");
            DrawVertexNode(pt4, "P4");

            // Element category text
            Point labelPos = Project(new Point3D(p1.X - 120, 0, 0), center, scale);
            DrawTextTag(labelPos.X, labelPos.Y - 18, ElementName, "#0284C7", "#E0F2FE");
        }

        private void DrawVertexNode(Point pt, string label)
        {
            var nodeCircle = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                Stroke = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
                StrokeThickness = 2,
                Margin = new Thickness(pt.X - 5, pt.Y - 5, 0, 0)
            };
            DrawingCanvas.Children.Add(nodeCircle);

            var txt = new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                Margin = new Thickness(pt.X + 6, pt.Y - 14, 0, 0)
            };
            DrawingCanvas.Children.Add(txt);
        }

        private void DrawAnnotations(Point center, double scale, Point3D p1, Point3D p2, Point3D p3, Point3D p4, double deltaZ, double clearance, double span, double angleDeg)
        {
            // Clearance Dimension Annotation (Vertical from Obstacle Top to Bridge P2-P3)
            Point3D obstTopPoint = new Point3D(0, 0, (Direction == RoutingDirection.Up ? 110.0 : -110.0));
            Point3D bridgePoint = new Point3D(0, 0, deltaZ);

            Point ptClearStart = Project(obstTopPoint, center, scale);
            Point ptClearEnd = Project(bridgePoint, center, scale);

            DrawDimensionLine(ptClearStart, ptClearEnd, $"Clearance: {clearance:0} mm", "#F59E0B");

            // Span Dimension Annotation (Horizontal from clash origin 0 to P4)
            Point ptOrig = Project(new Point3D(0, 0, 0), center, scale);
            Point ptSpanEnd = Project(new Point3D(span, 0, 0), center, scale);
            DrawDimensionLine(new Point(ptOrig.X, ptOrig.Y + 25), new Point(ptSpanEnd.X, ptSpanEnd.Y + 25), $"Span: {span:0} mm", "#38BDF8");

            // Direction badge in upper canvas
            string dirText = (Direction == RoutingDirection.Up) ? "▲ ROUTING OVER (+Z)" : "▼ ROUTING UNDER (-Z)";
            string dirColor = (Direction == RoutingDirection.Up) ? "#38BDF8" : "#A855F7";
            Point dirPos = Project(new Point3D(0, 0, deltaZ + (Direction == RoutingDirection.Up ? 80 : -80)), center, scale);
            DrawTextTag(dirPos.X, dirPos.Y, $"{dirText} | {angleDeg:0.#}°", dirColor, "#0F172A");
        }

        private void DrawDimensionLine(Point p1, Point p2, string text, string hexColor)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hexColor);

            var line = new Line
            {
                X1 = p1.X, Y1 = p1.Y,
                X2 = p2.X, Y2 = p2.Y,
                Stroke = brush,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };
            DrawingCanvas.Children.Add(line);

            // Dimension Text
            Point mid = new Point((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);
            DrawTextTag(mid.X, mid.Y - 10, text, hexColor, "#1E293B");
        }

        private void DrawPolygon(Point[] points, Brush fill, Brush stroke, double thickness)
        {
            var poly = new Polygon
            {
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness
            };
            foreach (var pt in points)
            {
                poly.Points.Add(pt);
            }
            DrawingCanvas.Children.Add(poly);
        }

        private void DrawTextTag(double x, double y, string text, string fgHex, string bgHex)
        {
            var fg = (SolidColorBrush)new BrushConverter().ConvertFromString(fgHex);
            var bg = (SolidColorBrush)new BrushConverter().ConvertFromString(bgHex);

            var border = new Border
            {
                Background = bg,
                BorderBrush = fg,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(x - 30, y - 8, 0, 0),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = fg,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };
            DrawingCanvas.Children.Add(border);
        }
    }

    public struct Point3D
    {
        public double X, Y, Z;
        public Point3D(double x, double y, double z)
        {
            X = x; Y = y; Z = z;
        }
    }
}
