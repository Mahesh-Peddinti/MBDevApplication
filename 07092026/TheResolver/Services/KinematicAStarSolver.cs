using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TheResolver.Services
{
    public class KinematicAStarSolver
    {
        private class Node : IComparable<Node>
        {
            public int X, Y, Z;
            public int DirX, DirY, DirZ; // Direction vector indices
            public int StraightRunCells; // Run counter for minimum tangent verification
            public double G, H;
            public double F => G + H;
            public Node Parent;

            public int CompareTo(Node other) => F.CompareTo(other.F);

            public long StateKey =>
                ((long)X & 0xFFF) |
                (((long)Y & 0xFFF) << 12) |
                (((long)Z & 0xFFF) << 24) |
                (((long)(DirX + 1) & 0x3) << 36) |
                (((long)(DirY + 1) & 0x3) << 38) |
                (((long)(DirZ + 1) & 0x3) << 40);
        }

        public static List<XYZ> SolvePath(
            VoxelGrid3D grid,
            XYZ startPt,
            XYZ startDir,
            XYZ goalPt,
            double minTangentDistance,
            double turnPenaltyWeight = 5.0)
        {
            grid.WorldToGrid(startPt, out int sx, out int sy, out int sz);
            grid.WorldToGrid(goalPt, out int gx, out int gy, out int gz);

            int dx = (int)Math.Round(startDir.X);
            int dy = (int)Math.Round(startDir.Y);
            int dz = (int)Math.Round(startDir.Z);

            int minTangentCells = (int)Math.Ceiling(minTangentDistance / grid.CellSize);

            var openList = new List<Node>();
            var closedMap = new HashSet<long>();

            var startNode = new Node
            {
                X = sx,
                Y = sy,
                Z = sz,
                DirX = dx,
                DirY = dy,
                DirZ = dz,
                StraightRunCells = minTangentCells,
                G = 0,
                H = Math.Sqrt(Math.Pow(gx - sx, 2) + Math.Pow(gy - sy, 2) + Math.Pow(gz - sz, 2))
            };

            openList.Add(startNode);

            // Allowed direction transitions (Orthogonal and 45° single-plane bends)
            var allowedTransitions = new List<(int x, int y, int z)>
            {
                (1, 0, 0), (-1, 0, 0),
                (0, 1, 0), (0, -1, 0),
                (0, 0, 1), (0, 0, -1),
                (1, 1, 0), (-1, 1, 0), (1, -1, 0), (-1, -1, 0),
                (1, 0, 1), (-1, 0, 1), (1, 0, -1), (-1, 0, -1),
                (0, 1, 1), (0, -1, 1), (0, 1, -1), (0, -1, -1)
            };

            Node bestGoal = null;
            int iteration = 0;
            const int maxIterations = 75000;

            while (openList.Count > 0 && iteration++ < maxIterations)
            {
                // Retrieve lowest F-cost node
                openList.Sort();
                Node current = openList[0];
                openList.RemoveAt(0);

                if (Math.Abs(current.X - gx) <= 1 && Math.Abs(current.Y - gy) <= 1 && Math.Abs(current.Z - gz) <= 1)
                {
                    bestGoal = current;
                    break;
                }

                if (!closedMap.Add(current.StateKey))
                    continue;

                foreach (var t in allowedTransitions)
                {
                    int nx = current.X + t.x;
                    int ny = current.Y + t.y;
                    int nz = current.Z + t.z;

                    if (grid.IsBlocked(nx, ny, nz))
                        continue;

                    bool isTurn = (t.x != current.DirX || t.y != current.DirY || t.z != current.DirZ);

                    // Prune turns that violate fitting tangent allowance
                    if (isTurn && current.StraightRunCells < minTangentCells)
                        continue;

                    double stepDist = Math.Sqrt(t.x * t.x + t.y * t.y + t.z * t.z);
                    double turnPenalty = isTurn ? (turnPenaltyWeight * grid.CellSize) : 0.0;

                    var neighbor = new Node
                    {
                        X = nx,
                        Y = ny,
                        Z = nz,
                        DirX = t.x,
                        DirY = t.y,
                        DirZ = t.z,
                        StraightRunCells = isTurn ? 1 : (current.StraightRunCells + 1),
                        G = current.G + stepDist + turnPenalty,
                        H = Math.Sqrt(Math.Pow(gx - nx, 2) + Math.Pow(gy - ny, 2) + Math.Pow(gz - nz, 2)),
                        Parent = current
                    };

                    openList.Add(neighbor);
                }
            }

            if (bestGoal == null) return null;

            // Reconstruct path
            var rawPoints = new List<XYZ>();
            Node tracer = bestGoal;
            while (tracer != null)
            {
                rawPoints.Add(grid.GridToWorld(tracer.X, tracer.Y, tracer.Z));
                tracer = tracer.Parent;
            }
            rawPoints.Reverse();

            // Decimate intermediate collinear points into clean vertices
            return DecimatePath(rawPoints, minTangentDistance);
        }

        private static List<XYZ> DecimatePath(List<XYZ> pts, double minSegmentLength)
        {
            if (pts.Count <= 2) return pts;

            var simplified = new List<XYZ> { pts[0] };
            XYZ currentDir = (pts[1] - pts[0]).Normalize();

            for (int i = 2; i < pts.Count; i++)
            {
                XYZ segDir = (pts[i] - pts[i - 1]).Normalize();
                if (segDir.DistanceTo(currentDir) > 1e-3)
                {
                    simplified.Add(pts[i - 1]);
                    currentDir = segDir;
                }
            }
            simplified.Add(pts.Last());

            // Merge micro-segments shorter than standard fitting tangent allowance
            var result = new List<XYZ> { simplified[0] };
            for (int i = 1; i < simplified.Count; i++)
            {
                if (simplified[i].DistanceTo(result.Last()) >= minSegmentLength * 0.75 || i == simplified.Count - 1)
                {
                    result.Add(simplified[i]);
                }
            }

            return result;
        }
    }
}