using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using TheResolver.DTOs;
using TheResolver.ViewModel;

namespace TheResolver.UI
{
    public partial class ResolverView : UserControl
    {
        // 1. Strongly-typed property avoiding namespace collision
        public ClashViewModel ClashVm { get; }

        private Point _lastMousePos;
        private bool _isOrbiting;
        private bool _isPanning;
        private Point3D _cameraTarget = new Point3D(0, 0, 0);

        // Materials
        private static readonly Material TrayMaterial 
            = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(180, 100, 149, 237)));
        private static readonly Material DetourMaterial 
            = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(220, 76, 175, 80)));
        private static readonly Material ClashMaterial 
            = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(200, 229, 57, 53)));
        private static readonly Material SecondaryMaterial 
            = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(120, 158, 158, 158)));


        // 2. Compatibility accessor for ResolverDockablePane.cs
        public ClashViewModel ViewModel => ClashVm;

        public ResolverView()
        {
            InitializeComponent();
            ClashVm = new ClashViewModel();
            DataContext = ClashVm;
            ClashVm.PropertyChanged += OnViewModelPropertyChanged;
        }

        // 3. Missing Click event handler for the DataGrid Header CheckBox
        private void HeaderSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && ClashVm != null)
            {
                ClashVm.IsAllSelected = (chk.IsChecked == true);
            }
        }
       

        // --- 3D Scene Rendering ---
        private void Render3DScene()
        {
            if (SceneVisual == null) return;
            var group = new Model3DGroup();           
            var data = ClashVm?.PreviewRouteData;
            if (data?.RoutePoints == null || data.RoutePoints.Count < 2)
            {
                ViewportStatusText.Text = data?.Message ?? "Select a clash to view 3D route";
                SceneVisual.Content = null;
                return;
            }
            ViewportStatusText.Text = string.Empty;

            double origTrayLength = data.TrayLengthMm;
            double trayW = 300; // mm
            double trayH = 100; // mm

            // 1. Render Baseline Tray (Reference)
            group.Children.Add(CreateBoxModel(new Point3D(0, -trayW * 0.5, -trayH * 0.5), new Size3D(origTrayLength, trayW, trayH), TrayMaterial));

            // 2. Render Clash Obstacle
            if (data.ClashStationMaxMm > data.ClashStationMinMm)
            {
                double cLen = Math.Max(20, data.ClashStationMaxMm - data.ClashStationMinMm);
                double cHeight = Math.Max(20, data.ClashMaxElevationMm - data.ClashMinElevationMm);
                group.Children.Add(CreateBoxModel(
                    new Point3D(data.ClashStationMinMm, -trayW * 0.75, data.ClashMinElevationMm),
                    new Size3D(cLen, trayW * 1.5, cHeight),
                    ClashMaterial));
            }

            // 3. Render Secondary Surrounding Obstacles
            foreach (var sec in data.SecondaryObstacles)
            {
                double sLen = Math.Max(20, sec.StationMaxMm - sec.StationMinMm);
                double sHeight = Math.Max(20, sec.ElevationMaxMm - sec.ElevationMinMm);
                group.Children.Add(CreateBoxModel(
                    new Point3D(sec.StationMinMm, -trayW * 0.6, sec.ElevationMinMm),
                    new Size3D(sLen, trayW * 1.2, sHeight),
                    SecondaryMaterial));
            }

            // 4. Render N-Point Detour Segments (Connected 3D Trays)
            for (int i = 0; i < data.RoutePoints.Count - 1; i++)
            {
                var p0 = data.RoutePoints[i];
                var p1 = data.RoutePoints[i + 1];

                Vector3D dir = new Vector3D(p1.Station - p0.Station, 0, p1.Elevation - p0.Elevation);
                double segLength = dir.Length;
                if (segLength < 1) continue;

                var segModel = CreateExtrudedSegment(
                    new Point3D(p0.Station, 0, p0.Elevation),
                    new Point3D(p1.Station, 0, p1.Elevation),
                    trayW, trayH, DetourMaterial);

                group.Children.Add(segModel);
            }

            SceneVisual.Content = group;

            // Center camera target on the clash station
            double clashCenter = (data.ClashStationMinMm + data.ClashStationMaxMm) * 0.5;
            _cameraTarget = new Point3D(clashCenter, 0, data.RiseMm * 0.5);
        }
        // --- Trigger the  3D Render on Selection ---
        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ClashViewModel.PreviewRouteData))
            {
                Render3DScene();
            }
        }

        // --- 3D Mesh Helpers ---
        private static GeometryModel3D CreateBoxModel(Point3D origin, Size3D size, Material material)
        {
            var mesh = new MeshGeometry3D();
            Point3D p0 = origin;
            Point3D p1 = new Point3D(origin.X + size.X, origin.Y, origin.Z);
            Point3D p2 = new Point3D(origin.X + size.X, origin.Y + size.Y, origin.Z);
            Point3D p3 = new Point3D(origin.X, origin.Y + size.Y, origin.Z);
            Point3D p4 = new Point3D(origin.X, origin.Y, origin.Z + size.Z);
            Point3D p5 = new Point3D(origin.X + size.X, origin.Y, origin.Z + size.Z);
            Point3D p6 = new Point3D(origin.X + size.X, origin.Y + size.Y, origin.Z + size.Z);
            Point3D p7 = new Point3D(origin.X, origin.Y + size.Y, origin.Z + size.Z);

            AddQuad(mesh, p0, p1, p2, p3); // Bottom
            AddQuad(mesh, p7, p6, p5, p4); // Top
            AddQuad(mesh, p0, p4, p5, p1); // Front
            AddQuad(mesh, p2, p6, p7, p3); // Back
            AddQuad(mesh, p0, p3, p7, p4); // Left
            AddQuad(mesh, p1, p5, p6, p2); // Right

            return new GeometryModel3D(mesh, material);
        }

        private static GeometryModel3D CreateExtrudedSegment(Point3D p0, Point3D p1, double width, double height, Material mat)
        {
            var mesh = new MeshGeometry3D();
            Vector3D dir = (p1 - p0);
            dir.Normalize();
            Vector3D lateral = new Vector3D(0, 1, 0); // Along Y
            Vector3D normal = Vector3D.CrossProduct(dir, lateral);

            Vector3D wOffset = lateral * (width * 0.5);
            Vector3D hOffset = normal * (height * 0.5);

            Point3D a0 = p0 - wOffset - hOffset;
            Point3D b0 = p0 + wOffset - hOffset;
            Point3D c0 = p0 + wOffset + hOffset;
            Point3D d0 = p0 - wOffset + hOffset;

            Point3D a1 = p1 - wOffset - hOffset;
            Point3D b1 = p1 + wOffset - hOffset;
            Point3D c1 = p1 + wOffset + hOffset;
            Point3D d1 = p1 - wOffset + hOffset;

            AddQuad(mesh, a0, b0, c0, d0); // Start cap
            AddQuad(mesh, d1, c1, b1, a1); // End cap
            AddQuad(mesh, a0, a1, b1, b0); // Bottom
            AddQuad(mesh, d0, c0, c1, d1); // Top
            AddQuad(mesh, a0, d0, d1, a1); // Left
            AddQuad(mesh, b0, b1, c1, c0); // Right

            return new GeometryModel3D(mesh, mat);
        }

        private static void AddQuad(MeshGeometry3D mesh, Point3D p0, Point3D p1, Point3D p2, Point3D p3)
        {
            int idx = mesh.Positions.Count;
            mesh.Positions.Add(p0);
            mesh.Positions.Add(p1);
            mesh.Positions.Add(p2);
            mesh.Positions.Add(p3);

            mesh.TriangleIndices.Add(idx);
            mesh.TriangleIndices.Add(idx + 1);
            mesh.TriangleIndices.Add(idx + 2);
            mesh.TriangleIndices.Add(idx);
            mesh.TriangleIndices.Add(idx + 2);
            mesh.TriangleIndices.Add(idx + 3);
        }

        // --- Mouse Orbit, Pan, and Zoom Navigation ---
        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) _isOrbiting = true;
            if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed) _isPanning = true;
            _lastMousePos = e.GetPosition(View3D);
            View3D.CaptureMouse();
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isOrbiting && !_isPanning) return;
            Point currentPos = e.GetPosition(View3D);
            double dx = currentPos.X - _lastMousePos.X;
            double dy = currentPos.Y - _lastMousePos.Y;

            if (_isOrbiting)
            {
                // Orbit camera around _cameraTarget
                Vector3D pos = ViewportCamera.Position - _cameraTarget;
                double radius = pos.Length;
                double theta = Math.Atan2(pos.Y, pos.X) - dx * 0.01;
                double phi = Math.Asin(Math.Max(-0.95, Math.Min(0.95, pos.Z / radius))) + dy * 0.01;

                double x = radius * Math.Cos(phi) * Math.Cos(theta);
                double y = radius * Math.Cos(phi) * Math.Sin(theta);
                double z = radius * Math.Sin(phi);

                ViewportCamera.Position = _cameraTarget + new Vector3D(x, y, z);
                ViewportCamera.LookDirection = _cameraTarget - ViewportCamera.Position;
            }
            else if (_isPanning)
            {
                Vector3D look = ViewportCamera.LookDirection;
                look.Normalize();
                Vector3D right = Vector3D.CrossProduct(look, ViewportCamera.UpDirection);
                right.Normalize();
                Vector3D up = Vector3D.CrossProduct(right, look);

                Vector3D pan = (-right * dx + up * dy) * 2.0;
                ViewportCamera.Position += pan;
                _cameraTarget += pan;
            }

            _lastMousePos = currentPos;
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isOrbiting = false;
            _isPanning = false;
            View3D.ReleaseMouseCapture();
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 0.85 : 1.15;
            Vector3D look = ViewportCamera.Position - _cameraTarget;
            ViewportCamera.Position = _cameraTarget + look * factor;
        }           

        
        // --- Navigation Toolbar Actions ---

        private void Tool_ZoomIn(object sender, RoutedEventArgs e)
        {
            ApplyZoom(0.80);
        }

        private void Tool_ZoomOut(object sender, RoutedEventArgs e)
        {
            ApplyZoom(1.25);
        }

        private void ApplyZoom(double factor)
        {
            Vector3D look = ViewportCamera.Position - _cameraTarget;
            if (look.Length * factor < 50) return; // Prevent zooming through target
            ViewportCamera.Position = _cameraTarget + look * factor;
        }

        private void Tool_FitView(object sender, RoutedEventArgs e)
        {
            var data = ClashVm?.PreviewRouteData;
            if (data == null || data.TrayLengthMm <= 0) return;

            double clashCenter = (data.ClashStationMinMm + data.ClashStationMaxMm) * 0.5;
            _cameraTarget = new Point3D(clashCenter, 0, data.RiseMm * 0.5);

            double span = Math.Max(data.TrayLengthMm * 0.6, 2000);
            ViewportCamera.Position = _cameraTarget + new Vector3D(span * 0.8, -span * 1.3, span * 0.9);
            ViewportCamera.LookDirection = _cameraTarget - ViewportCamera.Position;
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void Tool_OrbitHint(object sender, RoutedEventArgs e)
        {
            ViewportStatusText.Text = "Orbit Mode: Click & drag Left Mouse Button on 3D viewport.";
        }

        private void Tool_PanHint(object sender, RoutedEventArgs e)
        {
            ViewportStatusText.Text = "Pan Mode: Click & drag Right or Middle Mouse Button.";
        }

        // --- Camera Preset Projections ---

        private void ResetView_Isometric(object sender, RoutedEventArgs e)
        {
            var data = ClashVm?.PreviewRouteData;
            double center = data != null ? (data.ClashStationMinMm + data.ClashStationMaxMm) * 0.5 : 0;
            _cameraTarget = new Point3D(center, 0, data != null ? data.RiseMm * 0.5 : 0);

            double dist = 2200;
            ViewportCamera.Position = _cameraTarget + new Vector3D(dist * 0.8, -dist * 1.2, dist * 0.9);
            ViewportCamera.LookDirection = _cameraTarget - ViewportCamera.Position;
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void ResetView_Front(object sender, RoutedEventArgs e)
        {
            var data = ClashVm?.PreviewRouteData;
            double center = data != null ? (data.ClashStationMinMm + data.ClashStationMaxMm) * 0.5 : 0;
            _cameraTarget = new Point3D(center, 0, data != null ? data.RiseMm * 0.5 : 0);

            double dist = 2800;
            ViewportCamera.Position = _cameraTarget + new Vector3D(0, -dist, 0);
            ViewportCamera.LookDirection = new Vector3D(0, 1, 0);
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void ResetView_Top(object sender, RoutedEventArgs e)
        {
            var data = ClashVm?.PreviewRouteData;
            double center = data != null ? (data.ClashStationMinMm + data.ClashStationMaxMm) * 0.5 : 0;
            _cameraTarget = new Point3D(center, 0, data != null ? data.RiseMm * 0.5 : 0);

            double dist = 3200;
            ViewportCamera.Position = _cameraTarget + new Vector3D(0, 0, dist);
            ViewportCamera.LookDirection = new Vector3D(0, 0, -1);
            ViewportCamera.UpDirection = new Vector3D(0, 1, 0);
        }
    }
}