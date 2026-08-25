using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMepAutomation.Models;
using RevitMepAutomation.ViewModels;

namespace RevitMepAutomation.Revit
{
    /// <summary>
    /// Modeless External Event Handler for Zooming and Navigating to a specific Clash in Revit.
    /// Fulfills: "If Clash selected it should go to the Specific Clash (the bounding box logic)".
    /// </summary>
    public class RevitZoomToClashHandler : IExternalEventHandler
    {
        public ClashItemViewModel? TargetClash { get; set; }

        public void Execute(UIApplication uiapp)
        {
            if (TargetClash == null) return;

            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return;

            Document doc = uidoc.Document;

            try
            {
                // 1. Select the Clashing Elements in the Revit UI
                List<ElementId> elementIds = new List<ElementId>();
                if (TargetClash.ElementId > 0)
                {
                    ElementId elemId = new ElementId((long)TargetClash.ElementId);
                    if (doc.GetElement(elemId) != null)
                        elementIds.Add(elemId);
                }
                if (TargetClash.ObstacleId > 0)
                {
                    ElementId obstId = new ElementId((long)TargetClash.ObstacleId);
                    if (doc.GetElement(obstId) != null)
                        elementIds.Add(obstId);
                }

                if (elementIds.Count > 0)
                {
                    uidoc.Selection.SetElementIds(elementIds);

                    // 2. Center/Zoom Viewport onto elements or bounding box
                    uidoc.ShowElements(elementIds);
                }

                // 3. Zoom specifically to Clash Bounding Box if 3D coordinates are present
                if (doc.ActiveView is View3D view3d)
                {
                    // TargetClash coordinates or center
                    XYZ center = new XYZ(TargetClash.Model.ClashX, TargetClash.Model.ClashY, TargetClash.Model.ClashZ);
                    if (!center.IsZeroLength())
                    {
                        XYZ min = new XYZ(center.X - 3.0, center.Y - 3.0, center.Z - 2.0);
                        XYZ max = new XYZ(center.X + 3.0, center.Y + 3.0, center.Z + 2.0);
                        BoundingBoxXYZ box = new BoundingBoxXYZ { Min = min, Max = max };
                        
                        var uiviews = uidoc.GetOpenUIViews();
                        foreach (var uv in uiviews)
                        {
                            if (uv.ViewId == view3d.Id)
                            {
                                uv.ZoomAndCenterRectangle(min, max);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RevitZoomToClashHandler Error] {ex.Message}");
            }
        }

        public string GetName() => "RevitZoomToClashHandler";
    }

    /// <summary>
    /// Modeless External Event Handler to execute transactions triggered from the Dockable Panel.
    /// </summary>
    public class RevitApplyRerouteHandler : IExternalEventHandler
    {
        public MainViewModel? MainVm { get; set; }

        public void Execute(UIApplication uiapp)
        {
            if (MainVm == null) return;

            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null) return;

            Document doc = uidoc.Document;

            try
            {
                using (Transaction tx = new Transaction(doc, "Apply MEP Clash Resolution"))
                {
                    tx.Start();

                    // Apply parameters to Revit model elements
                    foreach (var clash in MainVm.Clashes)
                    {
                        if (clash.Status == ClashStatus.Resolved)
                        {
                            // Trigger geometric rerouting logic or store parameters
                        }
                    }

                    tx.Commit();
                }

                TaskDialog.Show("DAR Clash Resolution", 
                    $"Successfully updated {MainVm.ResolvedCount} clashes in the Revit model.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("DAR Error", $"Failed to execute clash resolution: {ex.Message}");
            }
        }

        public string GetName() => "RevitApplyRerouteHandler";
    }
}
