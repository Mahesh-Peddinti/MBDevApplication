using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace TheResolver.Utilities
{
    public class ObstacleBounds
    {
        public string Id { get; set; }
        public BoundingBoxXYZ Box { get; set; } // Always in host project coordinates
        public Element Element { get; set; }
    }

    public class LocalSpatialIndex
    {
        private readonly double _cellSize;
        private readonly Dictionary<string, List<ObstacleBounds>> _grid = new Dictionary<string, List<ObstacleBounds>>();

        public LocalSpatialIndex(double cellSizeMm = 1000.0)
        {
            _cellSize = cellSizeMm / 304.8; // convert mm to internal feet
        }

        public void IngestElements(Document hostDoc, IEnumerable<Element> hostElements, IEnumerable<(RevitLinkInstance Instance, Element Element)> linkedElements)
        {
            if (hostElements != null)
            {
                foreach (Element e in hostElements)
                {
                    BoundingBoxXYZ bb = e.get_BoundingBox(null);
                    if (bb == null) continue;
                    Insert(new ObstacleBounds { Id = e.UniqueId, Box = bb, Element = e });
                }
            }

            if (linkedElements != null)
            {
                foreach (var item in linkedElements)
                {
                    BoundingBoxXYZ bb = item.Element.get_BoundingBox(null);
                    if (bb == null) continue;

                    Transform transform = item.Instance.GetTotalTransform();
                    BoundingBoxXYZ hostBb = TransformBoundingBox(bb, transform);

                    Insert(new ObstacleBounds { Id = item.Element.UniqueId, Box = hostBb, Element = item.Element });
                }
            }
        }

        public List<ObstacleBounds> QueryRoi(BoundingBoxXYZ roiBox)
        {
            var found = new Dictionary<string, ObstacleBounds>();
            int minX = (int)Math.Floor(roiBox.Min.X / _cellSize);
            int maxX = (int)Math.Floor(roiBox.Max.X / _cellSize);
            int minY = (int)Math.Floor(roiBox.Min.Y / _cellSize);
            int maxY = (int)Math.Floor(roiBox.Max.Y / _cellSize);
            int minZ = (int)Math.Floor(roiBox.Min.Z / _cellSize);
            int maxZ = (int)Math.Floor(roiBox.Max.Z / _cellSize);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        string key = $"{x}:{y}:{z}";
                        if (_grid.TryGetValue(key, out var list))
                        {
                            foreach (var obs in list)
                            {
                                if (!found.ContainsKey(obs.Id) && Intersects(roiBox, obs.Box))
                                {
                                    found.Add(obs.Id, obs);
                                }
                            }
                        }
                    }
                }
            }
            return new List<ObstacleBounds>(found.Values);
        }

        private void Insert(ObstacleBounds obs)
        {
            int minX = (int)Math.Floor(obs.Box.Min.X / _cellSize);
            int maxX = (int)Math.Floor(obs.Box.Max.X / _cellSize);
            int minY = (int)Math.Floor(obs.Box.Min.Y / _cellSize);
            int maxY = (int)Math.Floor(obs.Box.Max.Y / _cellSize);
            int minZ = (int)Math.Floor(obs.Box.Min.Z / _cellSize);
            int maxZ = (int)Math.Floor(obs.Box.Max.Z / _cellSize);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        string key = $"{x}:{y}:{z}";
                        if (!_grid.TryGetValue(key, out var list))
                        {
                            list = new List<ObstacleBounds>();
                            _grid[key] = list;
                        }
                        list.Add(obs);
                    }
                }
            }
        }

        private static BoundingBoxXYZ TransformBoundingBox(BoundingBoxXYZ box, Transform transform)
        {
            XYZ[] corners = new XYZ[]
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            };

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (var pt in corners)
            {
                XYZ tPt = transform.OfPoint(pt);
                minX = Math.Min(minX, tPt.X);
                minY = Math.Min(minY, tPt.Y);
                minZ = Math.Min(minZ, tPt.Z);
                maxX = Math.Max(maxX, tPt.X);
                maxY = Math.Max(maxY, tPt.Y);
                maxZ = Math.Max(maxZ, tPt.Z);
            }

            return new BoundingBoxXYZ { Min = new XYZ(minX, minY, minZ), Max = new XYZ(maxX, maxY, maxZ) };
        }

        private static bool Intersects(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
                   a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
                   a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
        }
    }
}