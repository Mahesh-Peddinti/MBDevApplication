using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using TheResolver.DTOs;
using TheResolver.Utilities;
using TheResolver.ViewModel;
using MediaBrush = System.Windows.Media.SolidColorBrush;
// Type Aliases to permanently eliminate CS0104 collisions
using MediaColor = System.Windows.Media.Color;
using RevitTransform = Autodesk.Revit.DB.Transform;
using RevitXYZ = Autodesk.Revit.DB.XYZ;
using WpfMaterial = System.Windows.Media.Media3D.Material;
using WpfPoint = System.Windows.Point;

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
            var currentClash = ClashVm?.SelectedClash?.ClashInfo;
            if (data?.RoutePoints == null || data.RoutePoints.Count < 2 || currentClash?.Tray == null)
            {
                ViewportStatusText.Text = data?.Message ?? "Select a clash to view 3D route";
                SceneVisual.Content = null;
                return;
            }
            ViewportStatusText.Text = string.Empty;

            // 1. Establish the Real World Center of the Clash Zone
            if (!(currentClash.Tray.Location is LocationCurve lc) || !(lc.Curve is Autodesk.Revit.DB.Line trayLine))
                return;

            RevitXYZ trayStart = trayLine.GetEndPoint(0);
            RevitXYZ trayEnd = trayLine.GetEndPoint(1);
            RevitXYZ trayDir = (trayEnd - trayStart).Normalize();
            RevitXYZ trayLateral = trayDir.CrossProduct(RevitXYZ.BasisZ).Normalize();

            // Center offset based on the clash intersection point
            RevitXYZ clashCenter = currentClash.Intersection?.ComputeCentroid();
            if (clashCenter == null)
            {
                var bb = currentClash.ClashElement.get_BoundingBox(null);
                clashCenter = bb != null ? (bb.Min + bb.Max) * 0.5 : (trayStart + trayEnd) * 0.5;
            }

            // 2. Render Host Cable Tray Solid
            var trayMesh = RevitMeshExtractor.ExtractElementMesh(currentClash.Tray, RevitTransform.Identity, clashCenter);
            if (trayMesh.Positions.Count > 0)
            {
                group.Children.Add(new GeometryModel3D(trayMesh, BaselineTrayMaterial));
            }

            // 3. Render Primary Clashing Element Solid
            var clashMesh = RevitMeshExtractor.ExtractElementMesh(currentClash.ClashElement, RevitTransform.Identity, clashCenter);
            if (clashMesh.Positions.Count > 0)
            {
                group.Children.Add(new GeometryModel3D(clashMesh, ClashMaterial));
            }

            // 4. Render Relevant Crossing Obstacles Only (Tight Oriented Corridor Filter)
            var doc = currentClash.Tray.Document;
            double trayWidthMm = currentClash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() * 304.8 ?? 300.0;
            double trayHeightMm = currentClash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() * 304.8 ?? 100.0;

            double corridorHalfWidth = (trayWidthMm * 0.5 + 400.0) / 304.8;
            double corridorHalfHeight = (trayHeightMm + 500.0) / 304.8;

            RevitXYZ bbMin = new RevitXYZ(
                Math.Min(trayStart.X, trayEnd.X) - corridorHalfWidth,
                Math.Min(trayStart.Y, trayEnd.Y) - corridorHalfWidth,
                clashCenter.Z - corridorHalfHeight);

            RevitXYZ bbMax = new RevitXYZ(
                Math.Max(trayStart.X, trayEnd.X) + corridorHalfWidth,
                Math.Max(trayStart.Y, trayEnd.Y) + corridorHalfWidth,
                clashCenter.Z + corridorHalfHeight);

            var outline = new Outline(bbMin, bbMax);
            var bboxFilter = new BoundingBoxIntersectsFilter(outline);

            // Whitelist only physical MEP and crossing structural framing elements
            var allowedCategories = new HashSet<BuiltInCategory>
            {
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_StructuralFraming
            };

            var nearbyElements = new FilteredElementCollector(doc)
                .WherePasses(bboxFilter)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var elem in nearbyElements)
            {
                if (elem.Id == currentClash.Tray.Id || elem.Id == currentClash.ClashElement.Id)
                    continue;

                if (elem.Category == null) continue;
                BuiltInCategory bic = (BuiltInCategory)elem.Category.Id.Value;
                if (!allowedCategories.Contains(bic)) continue;

                var elemBb = elem.get_BoundingBox(null);
                if (elemBb == null) continue;

                // Eliminate objects far away laterally
                RevitXYZ elemCenter = (elemBb.Min + elemBb.Max) * 0.5;
                double lateralDist = Math.Abs((elemCenter - trayStart).DotProduct(trayLateral));
                if (lateralDist > corridorHalfWidth) continue;

                var secMesh = RevitMeshExtractor.ExtractElementMesh(elem, RevitTransform.Identity, clashCenter);
                if (secMesh.Positions.Count > 0)
                {
                    group.Children.Add(new GeometryModel3D(secMesh, SecondaryMaterial));
                }
            }

            // 5. Convert Local Route (Station, Elevation) into Real 3D World Geometry
            for (int i = 0; i < data.RoutePoints.Count - 1; i++)
            {
                var p0 = data.RoutePoints[i];
                var p1 = data.RoutePoints[i + 1];

                RevitXYZ worldA = trayStart + (trayDir * (p0.Station / 304.8)) + new RevitXYZ(0, 0, p0.Elevation / 304.8);
                RevitXYZ worldB = trayStart + (trayDir * (p1.Station / 304.8)) + new RevitXYZ(0, 0, p1.Elevation / 304.8);

                Point3D ptA = new Point3D(
                    (worldA.X - clashCenter.X) * 304.8,
                    (worldA.Y - clashCenter.Y) * 304.8,
                    (worldA.Z - clashCenter.Z) * 304.8);

                Point3D ptB = new Point3D(
                    (worldB.X - clashCenter.X) * 304.8,
                    (worldB.Y - clashCenter.Y) * 304.8,
                    (worldB.Z - clashCenter.Z) * 304.8);

                if ((ptB - ptA).Length < 1.0) continue;

                group.Children.Add(CreateExtrudedSegment(ptA, ptB, trayWidthMm, trayHeightMm, SolvedRouteMaterial));

                if (i > 0)
                {
                    group.Children.Add(CreateSphereModel(ptA, Math.Min(trayWidthMm, trayHeightMm) * 0.40, FittingJointMaterial));
                }
            }

            SceneVisual.Content = group;

            // 6. Automatically Center and Frame the Bounding Box
            FrameCompoundBounds(group);
        }

        private void FrameCompoundBounds(Model3DGroup group)
        {
            Rect3D bounds = group.Bounds;
            if (bounds.IsEmpty) return;

            _cameraTarget = new Point3D(
                bounds.X + (bounds.SizeX * 0.5),
                bounds.Y + (bounds.SizeY * 0.5),
                bounds.Z + (bounds.SizeZ * 0.5));

            double diagonal = Math.Sqrt(
                Math.Pow(bounds.SizeX, 2) +
                Math.Pow(bounds.SizeY, 2) +
                Math.Pow(bounds.SizeZ, 2));

            double dist = Math.Max(1800.0, diagonal * 1.35);

            ViewportCamera.Position = _cameraTarget + new Vector3D(dist * 0.75, -dist * 1.2, dist * 0.85);
            ViewportCamera.LookDirection = _cameraTarget - ViewportCamera.Position;
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
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
            if (SceneVisual?.Content is Model3DGroup group)
            {
                FrameCompoundBounds(group);
            }
            else
            {
                _cameraTarget = new Point3D(0, 0, 0);
                double dist = 2200;
                ViewportCamera.Position = _cameraTarget + new Vector3D(dist * 0.75, -dist * 1.2, dist * 0.85);
                ViewportCamera.LookDirection = _cameraTarget - ViewportCamera.Position;
                ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
            }
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
            if (SceneVisual?.Content is Model3DGroup group)
            {
                Rect3D bounds = group.Bounds;
                _cameraTarget = new Point3D(
                    bounds.X + (bounds.SizeX * 0.5),
                    bounds.Y + (bounds.SizeY * 0.5),
                    bounds.Z + (bounds.SizeZ * 0.5));
            }
            else
            {
                _cameraTarget = new Point3D(0, 0, 0);
            }

            double dist = 2400;
            ViewportCamera.Position = _cameraTarget + new Vector3D(dist * 0.75, -dist * 1.2, dist * 0.85);
            ViewportCamera.LookDirection = _cameraTarget - ViewportCamera.Position;
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void ResetView_Front(object sender, RoutedEventArgs e)
        {
            if (SceneVisual?.Content is Model3DGroup group)
            {
                Rect3D bounds = group.Bounds;
                _cameraTarget = new Point3D(
                    bounds.X + (bounds.SizeX * 0.5),
                    bounds.Y + (bounds.SizeY * 0.5),
                    bounds.Z + (bounds.SizeZ * 0.5));
            }
            else
            {
                _cameraTarget = new Point3D(0, 0, 0);
            }

            double dist = 2400;
            ViewportCamera.Position = _cameraTarget + new Vector3D(0, -dist, 0);
            ViewportCamera.LookDirection = new Vector3D(0, 1, 0);
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void ResetView_Top(object sender, RoutedEventArgs e)
        {
            if (SceneVisual?.Content is Model3DGroup group)
            {
                Rect3D bounds = group.Bounds;
                _cameraTarget = new Point3D(
                    bounds.X + (bounds.SizeX * 0.5),
                    bounds.Y + (bounds.SizeY * 0.5),
                    bounds.Z + (bounds.SizeZ * 0.5));
            }
            else
            {
                _cameraTarget = new Point3D(0, 0, 0);
            }

            double dist = 2800;
            ViewportCamera.Position = _cameraTarget + new Vector3D(0, 0, dist);
            ViewportCamera.LookDirection = new Vector3D(0, 0, -1);
            ViewportCamera.UpDirection = new Vector3D(0, 1, 0);
        }
    }
}