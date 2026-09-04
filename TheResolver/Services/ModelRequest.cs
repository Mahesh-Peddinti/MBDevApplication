namespace TheResolver.Services
{
    public enum ModelRequest
    {
        None,
        LoadModels,
        LoadCategories,
        RunClashDetection,
        PreviewRoute,
        DiscardPreview,
        ResolveSelected,
        ResolveAll,
        FinishSession,
        ExportReport,
        UpdateBypassPreview,
        RefreshFeasibility
    }
}
