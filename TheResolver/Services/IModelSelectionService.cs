using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using TheResolver.DTOs;

namespace TheResolver.Services
{
    public interface IModelSelectionService
    {
        IList<ClashModelItem> GetAvailableModels(Document doc);
    }

    public class ModelSelectionService : IModelSelectionService
    {
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
    }
}
