namespace TheResolver.Services
{
    public enum ModelRequest
    {
        None,
        LoadModels,
        RunClashDetection,
        PreviewRoute,
        DiscardPreview,
        ResolveSelected,
        ResolveAll,
        FinishSession,
        ExportReport,
        UpdateBypassPreview
    }
}
