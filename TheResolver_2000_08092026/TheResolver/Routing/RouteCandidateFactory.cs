using Autodesk.Revit.DB;

namespace TheResolver.Routing
{
    public static class RouteCandidateFactory
    {
        public static RouteCandidate Create(
            RouteType routeType,
            XYZ breakStart,
            XYZ breakEnd)
        {
            return new RouteCandidate
            {
                RouteType = routeType,
                BreakStart = breakStart,
                BreakEnd = breakEnd,
                Name = routeType.ToString(),
                IsValid = false,
                IsCollisionFree = false
            };
        }
    }
}