using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using TheResolver.DTOs;

namespace TheResolver.Services
{
    public interface IModelSelectionService
    {
        IList<ClashModelItem> GetAvailableModels(Document doc);

        IList<CategoryItem> GetAvailableCategories(Document doc);
    }

    public class ModelSelectionService : IModelSelectionService
    {
        private static readonly BuiltInCategory[] TrackedCategories =
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_DetailComponents,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_ConduitFitting
        };

        public IList<ClashModelItem> GetAvailableModels(
            Document hostDoc)
        {
            var result = new List<ClashModelItem>();

            if (hostDoc == null)
                return result;

            // Host model
            result.Add(
                new ClashModelItem
                {
                    Name = hostDoc.Title,
                    Document = hostDoc,
                    IsHost = true,
                    IsSelected = true
                });

            // Revit links
            var links =
                new FilteredElementCollector(hostDoc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>();

            foreach (var link in links)
            {
                Document linkDoc = link.GetLinkDocument();

                if (linkDoc == null)
                    continue;

                result.Add(
                    new ClashModelItem
                    {
                        Name = linkDoc.Title,
                        Document = linkDoc,
                        IsHost = false,
                        LinkInstanceId = link.Id,
                        IsSelected = true
                    });
            }

            return result;
        }

        /// <summary>
        /// Returns the MEP categories that exist in the given document,
        /// each as a checkable <see cref="CategoryItem"/>.
        /// </summary>
        public IList<CategoryItem> GetAvailableCategories(Document doc)
        {
            var result = new List<CategoryItem>();

            if (doc == null)
                return result;

            foreach (var cat in TrackedCategories)
            {
                var collector =
                    new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType();

                Element first = collector.FirstOrDefault();

                if (first != null)
                {
                    result.Add(
                        new CategoryItem
                        {
                            Name = first.Category?.Name ?? cat.ToString(),
                            Category = cat,
                            IsSelected = true
                        });
                }
            }

            return result;
        }
    }
}
