using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace TheResolver.Utilities
{
    public static class RevitMeshExtractor
    {
        public static MeshGeometry3D ExtractElementMesh(Element element, Transform linkTransform, XYZ centerOffset)
        {
            var mesh = new MeshGeometry3D();
            if (element == null) return mesh;

            var opt = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = false
            };

            GeometryElement geomElem = element.get_Geometry(opt);
            if (geomElem == null) return mesh;

            ExtractSolids(geomElem, linkTransform ?? Transform.Identity, centerOffset, mesh);
            return mesh;
        }

        private static void ExtractSolids(GeometryElement geomElem, Transform transform, XYZ offset, MeshGeometry3D mesh)
        {
            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 1e-6)
                {
                    AddSolidToMesh(solid, transform, offset, mesh);
                }
                else if (obj is GeometryInstance inst)
                {
                    Transform nestedTransform = transform.Multiply(inst.Transform);
                    GeometryElement instGeom = inst.GetInstanceGeometry();
                    if (instGeom != null)
                    {
                        ExtractSolids(instGeom, nestedTransform, offset, mesh);
                    }
                }
            }
        }

        private static void AddSolidToMesh(Solid solid, Transform transform, XYZ offset, MeshGeometry3D mesh)
        {
            foreach (Face face in solid.Faces)
            {
                Mesh triangulated = face.Triangulate();
                if (triangulated == null) continue;

                int baseIdx = mesh.Positions.Count;

                for (int i = 0; i < triangulated.Vertices.Count; i++)
                {
                    XYZ v = transform.OfPoint(triangulated.Vertices[i]);
                    mesh.Positions.Add(new Point3D(
                        (v.X - offset.X) * 304.8,
                        (v.Y - offset.Y) * 304.8,
                        (v.Z - offset.Z) * 304.8
                    ));
                }

                for (int i = 0; i < triangulated.NumTriangles; i++)
                {
                    MeshTriangle tri = triangulated.get_Triangle(i);
                    mesh.TriangleIndices.Add(baseIdx + (int)tri.get_Index(0));
                    mesh.TriangleIndices.Add(baseIdx + (int)tri.get_Index(1));
                    mesh.TriangleIndices.Add(baseIdx + (int)tri.get_Index(2));
                }
            }
        }
    }
}