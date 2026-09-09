# TheResolver Technical Specification

## 1. Project Overview
TheResolver is a Revit add-in designed to automatically detect clashes between Cable Tray systems and other MEP elements, then generate optimized bypass routing solutions with full 2D/3D visualization and user approval workflow.

## 2. Key Features

### 1. Automatic Clash Detection and Route Validation
- Automatically collects Cable Tray and MEP elements from active Revit model
- Generates proposed bypass route using `CreateNewBypassRoute.CreateNewRoute`
- Validates new route geometry against surrounding model elements
- Performs second-stage clash check on new Cable Tray segments and fittings

### 2. Adjacent Element Search Optimization
- Creates localized search region around original clash point
- Considers vertical range (above/below) and lateral clearance
- Uses Revit API geometry methods (BoundingBoxXYZ, Solid, GeometryElement) for efficient checking

### 3. Grouping of Blocking Elements
- Groups adjacent elements (conduit, pipe, duct) into logical clusters
- Treats clustered elements as single obstruction for routing decisions
- Reduces unnecessary route iterations

### 4. Iterative Route Generation
- Standard 45° transition as initial candidate
- Progressive parameter adjustment: vertical offset, transition length, entry/exit points, routing direction
- Route scoring based on length, segment count, fittings, clearance, and clash risk
- Maximum of 5 iterative attempts to find valid solution

### 5. Adaptive 45-Degree Routing
- Initial 45-degree transition as preferred method
- Parameter adjustments for alternative candidates:
  - Vertical offset (rise)
  - Transition length
  - Entry/exit point positioning
  - Routing direction (up/down)
- Prioritizes shortest, simplest valid route with required clearances

### 6. Route Candidate Evaluation
- Total route length
- Vertical displacement
- Number of Cable Tray segments
- Number of fittings
- Transition angle
- Required clearance
- Connector connectivity
- Clash with surrounding elements

### 18. Final Workflow
1. Detect Cable Tray clashes automatically
2. Identify local clash region
3. Find nearby blocking elements
4. Generate 45° bypass candidate
5. Temporarily create route in Revit
6. Validate complete route for clashes
7. Group blocking elements into clusters
8. Generate improved route candidate
9. Repeat validation until clash-free solution found
10. Generate 3D preview with section box
11. Highlight original clash in red, proposed route in green
12. Provide Isometric/Plan/Elevation views
14. User reviews and approves
15. Commit validated routing solution to model

### 20. Development Constraint
- All changes must be made within existing TheResolver codebase
- Preserve existing functionality while extending with new classes/modules
- Maintain clean C# architecture with dedicated classes:
  - RouteCandidate
  - AdaptiveRouteSolver
  - RouteValidator
  - BlockingElementCluster
  - SpatialSearchEngine
  - PreviewManager