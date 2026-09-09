using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using TheResolver.DTOs;
using TheResolver.Utilities;
using TheResolver.ViewModel;

// Type Aliases to permanently eliminate CS0104 collisions
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.SolidColorBrush;
using MediaTransform = System.Windows.Media.Media3D.Transform3D;
using RevitTransform = Autodesk.Revit.DB.Transform;
using RevitXYZ = Autodesk.Revit.DB.XYZ;
using WpfPoint = System.Windows.Point;
using WpfMaterial = System.Windows.Media.Media3D.Material;

namespace TheResolver.UI
{
    public partial class ResolverView : UserControl
    {
        public ClashViewModel ClashVm { get; }
        public ClashViewModel ViewModel => ClashVm;

        private WpfPoint _lastMousePos;
        private bool _isOrbiting;
        private bool _isPanning;
        private Point3D _cameraTarget = new Point3D(0, 0, 0);

        // Materials
        private static readonly WpfMaterial BaselineTrayMaterial = CreateLitMaterial(MediaColor.FromArgb(90, 80, 85, 95), 0);
        private static readonly WpfMaterial ClashMaterial = CreateLitMaterial(MediaColor.FromArgb(235, 255, 60, 60), 40);
        private static readonly WpfMaterial SecondaryMaterial = CreateLitMaterial(MediaColor.FromArgb(110, 110, 135, 160), 20);
        private static readonly WpfMaterial SolvedRouteMaterial = CreateLitMaterial(MediaColor.FromArgb(255, 0, 229, 255), 70);
        private static readonly WpfMaterial FittingJointMaterial = CreateLitMaterial(MediaColor.FromArgb(255, 255, 185, 0), 60);

        private static MaterialGroup CreateLitMaterial(MediaColor baseColor, double specularPower)
        {
            var grp = new MaterialGroup();
            grp.Children.Add(new DiffuseMaterial(new MediaBrush(baseColor)));
            if (specularPower > 0)
            {
                grp.Children.Add(new SpecularMaterial(new MediaBrush(MediaColor.FromArgb(160, 255, 255, 255)), specularPower));
            }
            return grp;
        }

        public ResolverView()
        {
            InitializeComponent();
            ClashVm = new ClashViewModel();
            DataContext = ClashVm;
            ClashVm.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void HeaderSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && ClashVm != null)
            {
                ClashVm.IsAllSelected = (chk.IsChecked == true);
            }
        }

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

            var currentClash = ClashVm.SelectedClash?.ClashInfo;
            if (currentClash == null) return;

            // Compute local reference offset (center of the active tray)
            RevitXYZ centerOffset = null;
            if (currentClash.Tray != null)
            {
                var bb = currentClash.Tray.get_BoundingBox(null);
                if (bb != null) centerOffset = (bb.Min + bb.Max) * 0.5;
            }
            if (centerOffset == null) centerOffset = RevitXYZ.Zero;

            // 1. Render Actual Host Cable Tray Solid
            var trayMesh = RevitMeshExtractor.ExtractElementMesh(currentClash.Tray, RevitTransform.Identity, centerOffset);
            if (trayMesh.Positions.Count > 0)
            {
                group.Children.Add(new GeometryModel3D(trayMesh, BaselineTrayMaterial));
            }

            // 2. Render Clashing Element Solid
            var clashMesh = RevitMeshExtractor.ExtractElementMesh(currentClash.ClashElement, RevitTransform.Identity, centerOffset);
            if (clashMesh.Positions.Count > 0)
            {
                group.Children.Add(new GeometryModel3D(clashMesh, ClashMaterial));
            }

            // 3. Render Proposed Kinematic Path
            double trayW = 300;
            double trayH = 100;

            for (int i = 0; i < data.RoutePoints.Count - 1; i++)
            {
                var p0 = data.RoutePoints[i];
                var p1 = data.RoutePoints[i + 1];

                Point3D ptA = new Point3D(p0.Station - (centerOffset.X * 304.8), 0, p0.Elevation - (centerOffset.Z * 304.8));
                Point3D ptB = new Point3D(p1.Station - (centerOffset.X * 304.8), 0, p1.Elevation - (centerOffset.Z * 304.8));

                if ((ptB - ptA).Length < 1.0) continue;

                group.Children.Add(CreateExtrudedSegment(ptA, ptB, trayW, trayH, SolvedRouteMaterial));

                if (i > 0)
                {
                    group.Children.Add(CreateSphereModel(ptA, Math.Min(trayH, trayW) * 0.40, FittingJointMaterial));
                }
            }

            SceneVisual.Content = group;
            AutoFitCamera(data);
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ClashViewModel.PreviewRouteData))
            {
                Render3DScene();
            }
        }

        private void AutoFitCamera(PreviewRouteData data)
        {
            _cameraTarget = new Point3D(0, 0, 0);
            double dist = 2200;
            ViewportCamera.Position = _cameraTarget + new Vector3D(dist * 0.75, -dist * 1.2, dist * 0.85);
            ViewportCamera.LookDirection = _cameraTarget - ViewportCamera.Position;
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        // --- 3D Mesh Helpers ---
        private static GeometryModel3D CreateExtrudedSegment(Point3D p0, Point3D p1, double width, double height, WpfMaterial mat)
        {
            var mesh = new MeshGeometry3D();
            Vector3D dir = p1 - p0;
            dir.Normalize();
            Vector3D lateral = new Vector3D(0, 1, 0);
            Vector3D normal = Vector3D.CrossProduct(dir, lateral);
            if (normal.LengthSquared < 1e-4)
                normal = new Vector3D(0, 0, 1);
            normal.Normalize();

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

            AddQuad(mesh, a0, b0, c0, d0);
            AddQuad(mesh, d1, c1, b1, a1);
            AddQuad(mesh, a0, a1, b1, b0);
            AddQuad(mesh, d0, c0, c1, d1);
            AddQuad(mesh, a0, d0, d1, a1);
            AddQuad(mesh, b0, b1, c1, c0);

            return new GeometryModel3D(mesh, mat);
        }

        private static GeometryModel3D CreateSphereModel(Point3D center, double radius, WpfMaterial mat)
        {
            var mesh = new MeshGeometry3D();
            int slices = 12;
            int stacks = 8;

            for (int i = 0; i <= stacks; i++)
            {
                double phi = Math.PI * i / stacks;
                for (int j = 0; j <= slices; j++)
                {
                    double theta = 2.0 * Math.PI * j / slices;
                    double x = radius * Math.Sin(phi) * Math.Cos(theta);
                    double y = radius * Math.Sin(phi) * Math.Sin(theta);
                    double z = radius * Math.Cos(phi);
                    mesh.Positions.Add(new Point3D(center.X + x, center.Y + y, center.Z + z));
                }
            }

            for (int i = 0; i < stacks; i++)
            {
                for (int j = 0; j < slices; j++)
                {
                    int p1 = (i * (slices + 1)) + j;
                    int p2 = p1 + slices + 1;

                    mesh.TriangleIndices.Add(p1);
                    mesh.TriangleIndices.Add(p2);
                    mesh.TriangleIndices.Add(p1 + 1);

                    mesh.TriangleIndices.Add(p1 + 1);
                    mesh.TriangleIndices.Add(p2);
                    mesh.TriangleIndices.Add(p2 + 1);
                }
            }

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

        // --- Mouse Interaction ---
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
            WpfPoint currentPos = e.GetPosition(View3D);
            double dx = currentPos.X - _lastMousePos.X;
            double dy = currentPos.Y - _lastMousePos.Y;

            if (_isOrbiting)
            {
                Vector3D pos = ViewportCamera.Position - _cameraTarget;
                double radius = pos.Length;
                double theta = Math.Atan2(pos.Y, pos.X) - dx * 0.008;
                double phi = Math.Asin(Math.Max(-0.95, Math.Min(0.95, pos.Z / radius))) + dy * 0.008;

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

                Vector3D pan = (-right * dx + up * dy) * 1.5;
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

        // --- Toolbar Actions ---
        private void Tool_ZoomIn(object sender, RoutedEventArgs e) => ApplyZoom(0.80);
        private void Tool_ZoomOut(object sender, RoutedEventArgs e) => ApplyZoom(1.25);

        private void ApplyZoom(double factor)
        {
            Vector3D look = ViewportCamera.Position - _cameraTarget;
            if (look.Length * factor < 80) return;
            ViewportCamera.Position = _cameraTarget + look * factor;
        }

        private void Tool_FitView(object sender, RoutedEventArgs e)
        {
            if (ClashVm?.PreviewRouteData != null)
                AutoFitCamera(ClashVm.PreviewRouteData);
        }

        private void Tool_OrbitHint(object sender, RoutedEventArgs e) =>
            ViewportStatusText.Text = "Left Click + Drag anywhere on canvas to Orbit.";

        private void Tool_PanHint(object sender, RoutedEventArgs e) =>
            ViewportStatusText.Text = "Right or Middle Click + Drag to Pan.";

        private void ResetView_Isometric(object sender, RoutedEventArgs e)
        {
            _cameraTarget = new Point3D(0, 0, 0);
            double dist = 2400;
            ViewportCamera.Position = _cameraTarget + new Vector3D(dist * 0.75, -dist * 1.2, dist * 0.85);
            ViewportCamera.LookDirection = _cameraTarget - ViewportCamera.Position;
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void ResetView_Front(object sender, RoutedEventArgs e)
        {
            _cameraTarget = new Point3D(0, 0, 0);
            double dist = 2400;
            ViewportCamera.Position = _cameraTarget + new Vector3D(0, -dist, 0);
            ViewportCamera.LookDirection = new Vector3D(0, 1, 0);
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void ResetView_Top(object sender, RoutedEventArgs e)
        {
            _cameraTarget = new Point3D(0, 0, 0);
            double dist = 2800;
            ViewportCamera.Position = _cameraTarget + new Vector3D(0, 0, dist);
            ViewportCamera.LookDirection = new Vector3D(0, 0, -1);
            ViewportCamera.UpDirection = new Vector3D(0, 1, 0);
        }
    }
}