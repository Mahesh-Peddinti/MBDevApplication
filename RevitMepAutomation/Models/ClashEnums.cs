namespace RevitMepAutomation.Models
{
    public enum ClashStatus
    {
        NotResolved = 0,
        Resolved = 1,
        Ignored = 2,
        Pending = 3
    }

    public enum RoutingDirection
    {
        Up = 0,
        Down = 1
    }

    public enum AppThemeMode
    {
        Dark = 0,
        Light = 1,
        BlueAccent = 2
    }

    public enum PreviewViewMode
    {
        Isometric3D = 0,
        FrontElevation = 1,
        TopPlan = 2
    }
}
