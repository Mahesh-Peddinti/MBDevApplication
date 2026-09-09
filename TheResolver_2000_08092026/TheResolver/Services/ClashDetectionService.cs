using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheResolver.Services
{
    public class ClashDetectionService
    {        
        private readonly Document _doc;
        public ClashDetectionService(Document doc) => _doc = doc;

        public List<ClashResult> DetectClashes()
        {
            var results = new List<ClashResult>();            

            var collector = new FilteredElementCollector(_doc)
                                .WhereElementIsNotElementType()
                                .ToElements();           
            /*
            var linkInstances = new FilteredElementCollector(_doc)
                                .OfClass(typeof(RevitLinkInstance))
                                .Cast<RevitLinkInstance>();
            IList<Element> linkElements = new List<Element>();
            foreach ( RevitLinkInstance linkInstance in linkInstances )
            {
                Document document = linkInstance.GetLinkDocument();
                var linkInstanceElements = new FilteredElementCollector(document)
                                                .WhereElementIsNotElementType()
                                                .ToElements() ;
                foreach (Element elem in linkInstanceElements)
                {
                    linkElements.Add(elem);
                }
            }
            */
                                

            foreach (var e1 in collector)
            {
                Solid s1 = GetSolid(e1);
                if (s1 == null) continue;

                foreach (var e2 in collector)
                {
                    if (e1.Id.ToString() == e2.Id.ToString()) continue;
                    Solid s2 = GetSolid(e2);
                    if (s2 == null) continue;

                    // Quick bounding box check
                    BoundingBoxXYZ bb1 = e1.get_BoundingBox(null);
                    BoundingBoxXYZ bb2 = e2.get_BoundingBox(null);
                    if (!BoundingBoxesIntersect(bb1, bb2)) continue;

                    // Precise solid intersection
                    Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(s1, s2, BooleanOperationsType.Intersect);
                    if (intersection != null && intersection.Volume > 1e-6)
                    {
                        results.Add(new ClashResult
                        {
                            Element1 = e1.Id,
                            Element2 = e2.Id,
                            Description = $"{e1.Name}-{e1.Id} clashes with {e2.Name}-{e2.Id}"
                        });
                    }
                }
            }

            return results;
        }

        private Solid GetSolid(Element e)
        {
            Options opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geo = e.get_Geometry(opt);
            if (geo == null) return null;

            foreach (GeometryObject obj in geo)
            {
                if (obj is Solid solid && solid.Volume > 0) return solid;
            }
            return null;
        }

        private bool BoundingBoxesIntersect(BoundingBoxXYZ bb1, BoundingBoxXYZ bb2)
        {
            if (bb1 == null || bb2 == null) return false;
            return !(bb1.Max.X < bb2.Min.X || bb1.Min.X > bb2.Max.X ||
                     bb1.Max.Y < bb2.Min.Y || bb1.Min.Y > bb2.Max.Y ||
                     bb1.Max.Z < bb2.Min.Z || bb1.Min.Z > bb2.Max.Z);
        }
    }

    public class ClashResult
    {
        public ElementId Element1 { get; set; }
        public ElementId Element2 { get; set; }
        public string Description { get; set; }      
    }
}
