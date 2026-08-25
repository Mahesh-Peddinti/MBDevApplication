using Autodesk.Revit.DB;

namespace RevitMepAutomation.Utils
{
    /// <summary>
    /// Suppresses non-fatal warnings (e.g. transient connector disconnects) during element re-segmentation.
    /// </summary>
    public class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            failuresAccessor.DeleteAllWarnings();
            return FailureProcessingResult.Continue;
        }
    }
}
