using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace TheResolver.Services
{
    public class VoxelGrid3D
    {
        public XYZ Origin { get; }
        public double CellSize { get; }
        public int DimX { get; }
        public int DimY { get; }
        public int DimZ { get; }

        private readonly bool[] _blocked;

        public VoxelGrid3D(BoundingBoxXYZ roi, double cellSize)
        {
            CellSize = cellSize;
            Origin = roi.Min;

            DimX = (int)Math.Ceiling((roi.Max.X - roi.Min.X) / cellSize) + 1;
            DimY = (int)Math.Ceiling((roi.Max.Y - roi.Min.Y) / cellSize) + 1;
            DimZ = (int)Math.Ceiling((roi.Max.Z - roi.Min.Z) / cellSize) + 1;

            _blocked = new bool[DimX * DimY * DimZ];
        }

        public bool IsInBounds(int x, int y, int z) =>
            x >= 0 && x < DimX && y >= 0 && y < DimY && z >= 0 && z < DimZ;

        public bool IsBlocked(int x, int y, int z)
        {
            if (!IsInBounds(x, y, z)) return true;
            return _blocked[x + y * DimX + z * DimX * DimY];
        }

        public void RasterizeObstacle(BoundingBoxXYZ box, double inflation)
        {
            XYZ min = box.Min - new XYZ(inflation, inflation, inflation);
            XYZ max = box.Max + new XYZ(inflation, inflation, inflation);

            WorldToGrid(min, out int x0, out int y0, out int z0);
            WorldToGrid(max, out int x1, out int y1, out int z1);

            x0 = Math.Max(0, x0);
            y0 = Math.Max(0, y0);
            z0 = Math.Max(0, z0);

            x1 = Math.Min(DimX - 1, x1);
            y1 = Math.Min(DimY - 1, y1);
            z1 = Math.Min(DimZ - 1, z1);

            for (int z = z0; z <= z1; z++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    int baseIdx = y * DimX + z * DimX * DimY;
                    for (int x = x0; x <= x1; x++)
                    {
                        _blocked[x + baseIdx] = true;
                    }
                }
            }
        }

        public void WorldToGrid(XYZ pt, out int x, out int y, out int z)
        {
            x = (int)Math.Round((pt.X - Origin.X) / CellSize);
            y = (int)Math.Round((pt.Y - Origin.Y) / CellSize);
            z = (int)Math.Round((pt.Z - Origin.Z) / CellSize);
        }

        public XYZ GridToWorld(int x, int y, int z)
        {
            return new XYZ(
                Origin.X + x * CellSize,
                Origin.Y + y * CellSize,
                Origin.Z + z * CellSize
            );
        }
    }
}