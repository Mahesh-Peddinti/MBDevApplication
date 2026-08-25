using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI.Selection;

namespace RevitMepAutomation.Utils
{
    public class MepCurveSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is CableTray || elem is Conduit;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
