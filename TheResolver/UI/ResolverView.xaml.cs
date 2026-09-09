using Autodesk.Revit.DB;
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

// Type Aliases to permanently eliminate CS0104 / CS1503 collisions
using MediaBrush = System.Windows.Media.SolidColorBrush;
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

        // --- Materials ---
        private static readonly WpfMaterial BaselineTrayMaterial = CreateLitMaterial(MediaColor.FromArgb(120, 80, 85, 95), 0);
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

            double trayWidthMm = currentClash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() * 304.8 ?? 300.0;
            double trayHeightMm = currentClash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() * 304.8 ?? 100.0;

            // Compute active bypass station window
            double minRouteStation = data.RoutePoints.Min(p => p.Station);
            double maxRouteStation = data.RoutePoints.Max(p => p.Station);
            double corridorMarginMm = 800.0; // Margin around the detour zone

            double clipStartStation = Math.Max(0, minRouteStation - corridorMarginMm);
            double clipEndStation = Math.Min(trayLine.Length * 304.8, maxRouteStation + corridorMarginMm);
            double activeBypassSpanMm = clipEndStation - clipStartStation;

            // 2. Render Host Cable Tray ONLY within the active clash window (Prevents 15m line squeeze)
            RevitXYZ clipStartWorld = trayStart + (trayDir * (clipStartStation / 304.8));
            RevitXYZ clipEndWorld = trayStart + (trayDir * (clipEndStation / 304.8));

            Point3D basePtA = new Point3D((clipStartWorld.X - clashCenter.X) * 304.8,
                                          (clipStartWorld.Y - clashCenter.Y) * 304.8,
                                          (clipStartWorld.Z - clashCenter.Z) * 304.8);
            Point3D basePtB = new Point3D((clipEndWorld.X - clashCenter.X) * 304.8,
                                          (clipEndWorld.Y - clashCenter.Y) * 304.8,
                                          (clipEndWorld.Z - clashCenter.Z) * 304.8);

            group.Children.Add(CreateExtrudedSegment(basePtA, basePtB, trayWidthMm, trayHeightMm, BaselineTrayMaterial));

            // 3. Render Primary Clashing Element Solid
            var clashMesh = RevitMeshExtractor.ExtractElementMesh(currentClash.ClashElement, RevitTransform.Identity, clashCenter);
            if (clashMesh.Positions.Count > 0)
            {
                group.Children.Add(new GeometryModel3D(clashMesh, ClashMaterial));
            }

            // 4. Render Relevant Crossing Obstacles Only (Tightly Bounded Section Box)
            var doc = currentClash.Tray.Document;
            double corridorHalfWidth = (trayWidthMm * 0.5 + 400.0) / 304.8;
            double corridorHalfHeight = (trayHeightMm + 600.0) / 304.8;

            RevitXYZ bbMin = new RevitXYZ(
                Math.Min(clipStartWorld.X, clipEndWorld.X) - corridorHalfWidth,
                Math.Min(clipStartWorld.Y, clipEndWorld.Y) - corridorHalfWidth,
                clashCenter.Z - corridorHalfHeight);

            RevitXYZ bbMax = new RevitXYZ(
                Math.Max(clipStartWorld.X, clipEndWorld.X) + corridorHalfWidth,
                Math.Max(clipStartWorld.Y, clipEndWorld.Y) + corridorHalfWidth,
                clashCenter.Z + corridorHalfHeight);

            var outline = new Outline(bbMin, bbMax);
            var bboxFilter = new BoundingBoxIntersectsFilter(outline);

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

                long catIdVal = elem.Category.Id.Value;
                BuiltInCategory bic = (BuiltInCategory)catIdVal;
                if (!allowedCategories.Contains(bic)) continue;

                var elemBb = elem.get_BoundingBox(null);
                if (elemBb == null) continue;

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

            // 6. Focus Camera Closely on Active Bypass
            FrameCompoundBounds(group, activeBypassSpanMm);
        }

        private void FrameCompoundBounds(Model3DGroup group, double bypassSpanMm)
        {
            _cameraTarget = new Point3D(0, 0, 0);

            // Frame based strictly on the bypass region rather than full room/run
            double effectiveSpan = Math.Max(1200.0, Math.Min(3500.0, bypassSpanMm));
            double dist = effectiveSpan * 1.15;

            ViewportCamera.Position = _cameraTarget + new Vector3D(dist * 0.75, -dist * 1.1, dist * 0.85);
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
            if (data?.RoutePoints != null && data.RoutePoints.Count >= 2)
            {
                double span = data.RoutePoints.Max(p => p.Station) - data.RoutePoints.Min(p => p.Station);
                FrameCompoundBounds(SceneVisual?.Content as Model3DGroup, span + 1600.0);
            }
            else
            {
                _cameraTarget = new Point3D(0, 0, 0);
                double dist = 1800;
                ViewportCamera.Position = _cameraTarget + new Vector3D(dist * 0.75, -dist * 1.1, dist * 0.85);
                ViewportCamera.LookDirection = _cameraTarget - ViewportCamera.Position;
                ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
            }
        }

        // --- 3D Mesh Helpers ---
        private static GeometryModel3D CreateExtrudedSegment(Point3D p0, Point3D p1, double width, double height, WpfMaterial mat)
        {
            var mesh = new MeshGeometry3D();
            Vector3D dir = p1 - p0;
            if (dir.LengthSquared < 1e-4) return new GeometryModel3D();
            dir.Normalize();

            // World Up is +Z
            Vector3D worldUp = new Vector3D(0, 0, 1);

            // Lateral is perpendicular to Tray Run in the horizontal plane (Width)
            Vector3D lateral = Vector3D.CrossProduct(dir, worldUp);
            if (lateral.LengthSquared < 1e-4)
            {
                lateral = new Vector3D(0, 1, 0);
            }
            lateral.Normalize();

            // Normal is perpendicular to both Run and Lateral (Height/Thickness)
            Vector3D normal = Vector3D.CrossProduct(lateral, dir);
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
            _lastMousePos = e.GetPosition(View3D);

            if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
            {
                _isPanning = true;
                _isOrbiting = false;
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isOrbiting = true;
                _isPanning = false;
            }

            View3D.CaptureMouse();
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isOrbiting && !_isPanning) return;

            WpfPoint currentPos = e.GetPosition(View3D);
            double dx = currentPos.X - _lastMousePos.X;
            double dy = currentPos.Y - _lastMousePos.Y;
            _lastMousePos = currentPos;

            if (_isPanning)
            {
                Vector3D look = ViewportCamera.LookDirection;
                look.Normalize();

                Vector3D right = Vector3D.CrossProduct(look, ViewportCamera.UpDirection);
                right.Normalize();

                Vector3D screenUp = Vector3D.CrossProduct(right, look);
                screenUp.Normalize();

                double panSpeed = (ViewportCamera.Position - _cameraTarget).Length * 0.0015;
                Vector3D panDelta = (-right * dx + screenUp * dy) * panSpeed;

                ViewportCamera.Position += panDelta;
                _cameraTarget += panDelta;
            }
            else if (_isOrbiting)
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
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isOrbiting = false;
            _isPanning = false;
            View3D.ReleaseMouseCapture();
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoomFactor = e.Delta > 0 ? 0.85 : 1.18;
            Vector3D offset = ViewportCamera.Position - _cameraTarget;
            double currentDist = offset.Length;

            if (currentDist * zoomFactor < 80.0 && e.Delta > 0) return;
            if (currentDist * zoomFactor > 25000.0 && e.Delta < 0) return;

            ViewportCamera.Position = _cameraTarget + offset * zoomFactor;
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
            double dist = Math.Min(2500.0, Math.Max(1200.0, (ViewportCamera.Position - _cameraTarget).Length));
            ViewportCamera.Position = _cameraTarget + new Vector3D(dist * 0.75, -dist * 1.1, dist * 0.85);
            ViewportCamera.LookDirection = _cameraTarget - ViewportCamera.Position;
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void ResetView_Front(object sender, RoutedEventArgs e)
        {
            _cameraTarget = new Point3D(0, 0, 0);
            double dist = Math.Min(2500.0, Math.Max(1200.0, (ViewportCamera.Position - _cameraTarget).Length));
            ViewportCamera.Position = _cameraTarget + new Vector3D(0, -dist, 0);
            ViewportCamera.LookDirection = new Vector3D(0, 1, 0);
            ViewportCamera.UpDirection = new Vector3D(0, 0, 1);
        }

        private void ResetView_Top(object sender, RoutedEventArgs e)
        {
            _cameraTarget = new Point3D(0, 0, 0);
            double dist = Math.Min(2800.0, Math.Max(1400.0, (ViewportCamera.Position - _cameraTarget).Length));
            ViewportCamera.Position = _cameraTarget + new Vector3D(0, 0, dist);
            ViewportCamera.LookDirection = new Vector3D(0, 0, -1);
            ViewportCamera.UpDirection = new Vector3D(0, 1, 0);
        }
    }
}