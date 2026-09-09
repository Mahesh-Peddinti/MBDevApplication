# TheResolver Documentation - Project Code: 0909206

## Project Overview

TheResolver is a Revit add-in designed to identify and resolve cable tray clashes with other MEP elements. It provides a comprehensive UI with 3D visualization, clash management, and automated route generation capabilities.

---

## 1. Pseudocode

### Core Resolution Algorithm
```plaintext
FUNCTION ResolveClash(app, clash, settings):
    IF app == NULL OR app.ActiveUIDocument == NULL:
        Log("Invalid application state")
        RETURN FALSE
    
    Document doc = app.ActiveUIDocument.Document
    IF clash?.Tray == NULL:
        Log("Invalid clash data")
        RETURN FALSE
    
    // Use default settings if none provided
    IF settings == NULL:
        settings = New RouteSettingDTOs()
        settings.BendAngle = 30.0
        settings.BendRadius = 100.0 * MM
        settings.MinimumClearance = 50.0 * MM
        settings.MinimumSideOffset = 250.0 * MM
        settings.BendSafetyfactor = 1.05
    
    // Collect obstructions for validation
    obstructions = CollectObstructions(doc, clash.Tray)
    
    // Attempt resolution with iteration
    (success, created) = TryResolveWithIterations(doc, clash, settings, obstructions)
    
    RETURN success
```

### Iterative Resolution Process
```plaintext
FUNCTION TryResolveWithIterations(doc, clash, settings, obstructions):
    maxIterations = 5
    currentSettings = CloneSettings(settings)
    
    FOR iteration = 0 TO maxIterations - 1:
        geometricUtilities = New GeometricUtilities()
        
        // Generate candidate route
        (routeSuccess, routePoints) = geometricUtilities.TryBuildBypassRoutingPoints(
            clash, currentSettings, out list)
        
        IF NOT routeSuccess OR routePoints == NULL OR routePoints.Count < 4:
            Log("Iteration {iteration}: Failed to compute bypass points")
            MutateSettingsForNextIteration(currentSettings, iteration)
            CONTINUE
        
        using (SubTransaction tx = New SubTransaction(doc)):
            tx.Start()
            
            // Create new bypass route
            created = CreateNewBypassRoute.CreateNewRoute(
                doc, clash.Tray, routePoints[0], routePoints[3],
                routePoints, currentSettings)
            
            IF created == NULL OR created.Count == 0:
                Log("Iteration {iteration}: Route creation failed, rolling back")
                tx.RollBack()
                MutateSettingsForNextIteration(currentSettings, iteration)
                CONTINUE
            
            doc.Regenerate()
            
            // Validate against existing obstructions
            IF created.Any(elem => ReClashesWithObstructions(elem, obstructions, clash.Tray)):
                Log("Iteration {iteration}: New route clashes with inline elements, rolling back")
                tx.RollBack()
                MutateSettingsForNextIteration(currentSettings, iteration)
                CONTINUE
            
            tx.Commit()
            Log("Clash resolved successfully for Tray {clash.Tray.Id} after {iteration + 1} iterations")
            RETURN (TRUE, created)
    
    Log("Failed to resolve clash for Tray {clash.Tray.Id} after {maxIterations} iterations")
    RETURN (FALSE, New List<CableTray>())
```

### Route Feasibility Check
```plaintext
FUNCTION IsRouteFeasible(ClashInfoDTOs clash, RouteSettingDTOs settings):
    IF clash?.Tray == NULL OR clash?.ClashElement == NULL OR settings == NULL:
        RETURN FALSE
    
    utils = New GeometricUtilities()
    RETURN utils.TryBuildBypassRoutingPoints(clash, settings, out var points)
```

### Grid Selection Management
```plaintext
FUNCTION UpdateSelectAllState():
    acceptCommand.RaiseCanExecuteChanged()
    
    IF _isUpdatingSelection:
        RETURN
    
    allSelected = Clashes.Count > 0 AND Clashes.All(c => c.IsSelected)
    
    IF _isAllSelected != allSelected:
        _isAllSelected = allSelected
        OnPropertyChanged(nameof(IsAllSelected))
```

### 3D Viewport Rendering
```plaintext
FUNCTION Render3DScene():
    IF SceneVisual == NULL:
        RETURN
    
    group = New Model3DGroup()
    data = ClashVm?.PreviewRouteData
    currentClash = ClashVm?.SelectedClash?.ClashInfo
    
    IF data?.RoutePoints == NULL OR data.RoutePoints.Count < 2 OR currentClash?.Tray == NULL:
        ViewportStatusText.Text = data?.Message ?? "Select a clash to view 3D route"
        SceneVisual.Content = NULL
        RETURN
    
    ViewportStatusText.Text = String.Empty
    
    // Calculate clash zone center
    clashCenter = GetClashCenter(currentClash)
    trayLine = GetTrayLine(currentClash.Tray)
    
    // Render host cable tray
    trayMesh = RevitMeshExtractor.ExtractElementMesh(currentClash.Tray, IdentityTransform, clashCenter)
    IF trayMesh.Positions.Count > 0:
        group.Children.Add(New GeometryModel3D(trayMesh, BaselineTrayMaterial))
    
    // Render primary clash element
    clashMesh = RevitMeshExtractor.ExtractElementMesh(currentClash.ClashElement, IdentityTransform, clashCenter)
    IF clashMesh.Positions.Count > 0:
        group.Children.Add(New GeometryModel3D(clashMesh, ClashMaterial))
    
    // Transform and render route points
    FOR i = 0 TO data.RoutePoints.Count - 2:
        p0 = data.RoutePoints[i]
        p1 = data.RoutePoints[i + 1]
        
        worldA = CalculateWorldCoordinates(trayLine, p0)
        worldB = CalculateWorldCoordinates(trayLine, p1)
        
        ptA = ConvertToViewportSpace(worldA, clashCenter)
        ptB = ConvertToViewportSpace(worldB, clashCenter)
        
        segment = CreateExtrudedSegment(ptA, ptB, trayWidth, trayHeight, SolvedRouteMaterial)
        group.Children.Add(segment)
        
        IF i > 0:
            sphere = CreateSphereModel(ptA, trayWidth * 0.40, FittingJointMaterial)
            group.Children.Add(sphere)
    
    SceneVisual.Content = group
    FrameCompoundBounds(group)
```

---

## 2. FlowChart

### TheResolver System Architecture Flowchart
```plaintext
┌─────────────────────────────────────────────────────────────────┐
│                         THE RESOLVER                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────┐    ┌──────────────────────────────┐    │
│  │    UI Layer         │    │       Business Logic          │    │
│  ├─────────────────────┤    ├──────────────────────────────┤    │
│  │                     │    │                              │    │
│  │  ┌─────────────────┐  │    │  ┌─────────────────────────┐  │    │
│  │  │ ResolverView    │  │    │  │ ClashResolver.cs         │  │    │
│  │  │ (XAML)         │  │    │  │ ClashResolveTool.cs      │  │    │
│  │  │                 │  │    │  │                         │  │    │
│  │  │ ┌─────────────┐  │  │    │  │ ┌─────────────────────┐  │    │
│  │  │ │ClashViewModel│  │  │    │  │ │ TryResolveWithIterations │  │    │
│  │  │ │              │  │    │  │ │                         │  │    │
│  │  │ │ ┌──────────┐  │  │    │  │ │ ┌──────────────────┐  │    │
│  │  │ │ │ Commands │  │  │    │  │ │ │ TryBuildBypassRoutingPoints│  │    │
│  │  │ │            │  │    │  │ │ │                         │  │    │
│  │  │ └──────────┬──┘  │  │    │  │ └─────────┬───────────┘  │    │
│  │  │            │    │  │    │  │            │               │    │
│  │  │ ┌─────────┐ │    │  │    │  │ ┌─────────┐ │    ┌─────────┐ │    │
│  │  │ │ Kinematic│ │    │  │    │  │ │ KinematicAStarSolver │  │    │
│  │  │ │ A*Solver │ │    │  │    │  │ │                         │  │    │
│  │  │ └─────────┘ │    │  │    │  │ └─────────┘               │    │
│  │  │             │    │  │    │  │                              │    │
│  │  └─────────────┘    │  │    │  │ ┌─────────────────────────┐  │    │
│  │                   │  │    │  │ │ GeometricUtilities.cs     │  │    │
│  │                   │  │    │  │ │                         │  │    │
│  │                   │  │    │  │ │ ┌─────────────────────┐  │    │
│  │                   │  │    │  │ │ │ ExtractSolidsRecursive │  │    │
│  │                   │  │    │  │ │ │                         │  │    │
│  │                   │  │    │  │ └─────────────────────┘  │    │
│  │                   │  │    │  │                              │    │
│  │                   │  │    │  │ ┌─────────────────────────┐  │    │
│  │                   │  │    │  │ │ DTOs                    │  │    │
│  │                   │  │    │  │ │                         │  │    │
│  │                   │  │    │  │ │ ┌─────────────────────┐  │    │
│  │                   │  │    │  │ │ │ RouteSettingDTOs      │  │    │
│  │                   │  │    │  │ │ │                         │  │    │
│  │                   │  │    │  │ └─────────────────────┘  │    │
│  │                   │  │    │  │                              │    │
│  │                   │  │    │  │ ┌─────────────────────────┐  │    │
│  │                   │  │    │  │ │ Services                │  │    │
│  │                   │  │    │  │ │                         │  │    │
│  │                   │  │    │  │ │ ┌─────────────────────┐  │    │
│  │                   │  │    │  │ │ │ ModelSelectionService  │  │    │
│  │                   │  │    │  │ │ │                         │  │    │
│  │                   │  │    │  │ └─────────────────────┘  │    │
│  │                   │  │    │  │                              │    │
│  │                   │  │    │  │ ┌─────────────────────────┐  │    │
│  │                   │  │    │  │ │ Utilities               │  │    │
│  │                   │  │    │  │ │                         │  │    │
│  │                   │  │    │  │ └─────────────────────────┘  │    │
└─────────┬────────────┘    └──────────────┬──────────────────────┘    │
          │                                │                          │
          ▼                                ▼                          ▼
┌─────────────────────────────────────────────────────────────────┐
│                        DATA LAYER                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────┐    ┌──────────────────────────────┐    │
│  │      DTOs           │    │      Services                │    │
│  ├─────────────────────┤    ├──────────────────────────────┤    │
│  │                     │    │                              │    │
│  │  ┌─────────────────┐  │    │  ┌─────────────────────────┐  │    │
│  │  │ RouteSettingDTOs│  │    │  │ IModelSelectionService   │  │    │
│  │  │                 │  │    │  │                         │  │    │
│  │  │ ┌─────────────┐  │  │    │  │ ┌─────────────────────┐  │    │
│  │  │ │ RouteDirec-  │  │    │  │ │ │ ModelSelectionService │  │    │
│  │  │ │ tion         │  │    │  │ │ │                      │  │    │
│  │  │ └─────────────┘  │  │    │  │ └─────────────────────┘  │    │
│  │  │                 │  │    │  │                              │    │
│  │  │ ┌─────────────┐  │  │    │  │ ┌─────────────────────┐  │    │
│  │  │ │ PreviewRoute │  │    │  │ │ │ LocalSpatialIndex    │  │    │
│  │  │ │ Data         │  │    │  │ │ │                      │  │    │
│  │  │ └─────────────┘  │  │    │  └─────────────────────┘  │    │
│  │                       │                                │
│  └─────────────────────┘                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. Workflow

### Main Application Workflow
```plaintext
STARTUP PROCESS:
1. Initialize Revit API
2. Register dockable pane (TheResolver)
3. Create ResolverView instance
4. Set up ViewModel binding
5. Display UI to user

USER INTERACTION FLOW:
┌───────────────────────────────────────────────────────────────┐
│  STEP 1: MODEL SELECTION                                     │
│  ├───────────────────────────────────────────────────────────┤
│  │ 1. User clicks "Load" button                             │
│  │ 2. RequestModelRefresh() is triggered                    │
│  │ 3. ExternalEvent raises ModelRequest.LoadModels          │
│  │ 4. ClashResolveTool.Initialize(app.Document)             │
│  │ 5. ModelSelectionService.Populate Host/Link Models        │
│  │ 6. Categories are loaded for selected models             │
│  │ 7. UI updates with available models                       │
└───────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────┐
│  STEP 2: CLASH DETECTION                                     │
│  ├───────────────────────────────────────────────────────────┤
│  │ 1. User clicks "Run Clash Detection" button              │
│  │ 2. RunClashDetectionCommand is triggered                 │
│  │ 3. ExternalEvent raises ModelRequest.RunClashDetection   │
│  │ 4. ClashResolveTool.RunClashDetection(app)               │
│  │   4.1. Collect selected categories and link instances     │
│  │   4.2. Harvest host obstructions                         │
│  │   4.3. Harvest linked obstructions                        │
│  │   4.4. Create spatial index                               │
│  │   4.5. Detect clashes between trays and obstructions       │
│  │   4.6. Load clashes into ViewModel                        │
│  │ 5. UI updates with clash list and status counts           │
└───────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────┐
│  STEP 3: ROUTE RESOLUTION                                    │
│  ├───────────────────────────────────────────────────────────┤
│  │ 1. User selects a clash from grid                         │
│  │ 2. SelectedClash property changes                        │
│  │ 3. PropertyChanged triggers PreviewRoute()               │
│  │ 4. ExternalEvent raises ModelRequest.PreviewRoute         │
│  │ 5. ClashResolveTool.UpdatePreview(app)                    │
│  │ 6. BuildPreviewData generates 3D route geometry          │
│  │ 7. RoutePreviewData is bound to 3D viewport               │
│  │ 8. User adjusts route parameters ( BendRadius, Clearance)│
│  │ 9. PropertyChanged triggers RefreshFeasibility()          │
│  │10. Route feasibility is recalculated                      │
│  │11. User clicks "Accept" to implement resolution            │
└───────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────┐
│  STEP 4: BATCH RESOLUTION                                    │
│  ├───────────────────────────────────────────────────────────┤
│  │ 1. User selects multiple clashes                           │
│  │ 2. User clicks "Accept" (batch mode)                      │
│  │ 3. RunClashResolution() is triggered                      │
│  │ 4. ExternalEvent raises ModelRequest.ResolveSelected      │
│  │ 5. ClashResolveTool.ResolveSelected(app)                   │
│  │ 6. Process selected clashes in optimized groups           │
│  │ 7. Use Kinematic A* for main routing                       │
│  │ 8. Create new cable tray segments                         │
│  │ 9. Delete original trays                                   │
│  │10. Update grid with resolution results                     │
└───────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────┐
│  STEP 5: SESSION MANAGEMENT                                  │
│  ├───────────────────────────────────────────────────────────┤
│  │ 1. User clicks "FINISH" button                            │
│  │ 2. Finish() is triggered                                   │
│  │ 3. ExternalEvent raises ModelRequest.FinishSession        │
│  │ 4. ClashResolveTool.FinishSession(app)                     │
│  │ 5. ResetViewModel clears all data                         │
│  │ 6. Hide dockable pane                                      │
│  │ 7. Release resources                                       │
└───────────────────────────────────────────────────────────────┘
```

---

## 4. Technical Logics and Implementation Files

### Core Algorithm Files

#### **TheResolver.BusinessLogics.ClashResolver.cs**
- **Purpose**: Main iterative clash resolution logic
- **Key Methods**:
  - `ResolveSelectedClash()`: Entry point for resolving single clashes
  - `TryResolveWithIterations()`: Iterative parameter adjustment
  - `MutateSettingsForNextIteration()`: Parameter mutation strategy
  - `CollectObstructions()`: Gather elements for validation

#### **TheResolver.BusinessLogics.ClashResolveTool.cs**
- **Purpose**: External event handler for Revit API integration
- **Key Methods**:
  - `Execute()`: Main event processing loop
  - `RunClashDetection()`: Clashes detection and spatial indexing
  - `ResolveSelected()`: Batch resolution with transactions
  - `UpdatePreview()`: Generate 3D route previews

#### **TheResolver.Services.GeometricUtilities.cs**
- **Purpose**: Core geometric calculations and collision detection
- **Key Methods**:
  - `FindClashes()`: Detect tray-obstruction collisions
  - `TryBuildBypassRoutingPoints()`: Generate route paths
  - `ExtractSolidsRecursive()`: Convert geometry to solid meshes

#### **TheResolver.Services.KinematicAStarSolver.cs**
- **Purpose**: Advanced pathfinding with kinematic constraints
- **Key Features**:
  - 3D grid-based navigation
  - Obstacle avoidance
  - Bender arc constraints
  - Multiple path optimizations

#### **TheResolver.Utilities.RevitMeshExtractor.cs**
- **Purpose**: Convert Revit elements to 3D meshes for visualization
- **Key Features**:
  - Element geometry extraction
  - Transform application
  - Solid mesh generation

#### **TheResolver.Utilities.LocalSpatialIndex.cs**
- **Purpose**: Spatial hashing for efficient collision detection
- **Key Features**:
  - Cell-based spatial indexing
- Query optimization
- Dynamic obstacle management

### ViewModel Implementation Files

#### **TheResolver.ViewModel.ClashViewModel.cs**
- **Purpose**: MVVM pattern implementation
- **Key Features**:
  - Command handling (Load, Detect, Resolve, Finish)
  - Clash grid data management
  - Route parameter validation
  - Preview synchronization

#### **TheResolver.ViewModel.ClashGridItemViewModel.cs**
- **Purpose**: Individual clash row data model
- **Key Properties**:
  - `IsSelected`: Selection state
  - `IsResolved`: Resolution status
  - `IsFeasible`: Route feasibility
  - `StatusColor`: Visual status indicator

### DTO and Data Transfer Files

#### **TheResolver.DTOs.RouteSettingDTOs.cs**
- **Purpose**: Route configuration data structure
- **Key Properties**:
  - `BendAngle`: Entry/exit angle (degrees)
  - `BendRadius`: Bend radius (feet)
  - `MinimumClearance`: Vertical clearance (feet)
  - `MinimumSideOffset`: Horizontal offset (feet)
  - `PreferredDirection`: Detour direction preference

#### **TheResolver.DTOs.PreviewRouteData.cs**
- **Purpose**: 3D route preview data
- **Key Properties**:
  - `RoutePoints`: List of 2D route coordinates
  - `TrayLengthMm`: Tray length in millimeters
  - `ClashStationMin/MaxMm`: Clash location range
  - `ClearanceMm`: Available clearance

---

## 5. Algorithms Used in This Project

### 5.1 Core Algorithms

#### **A. Kinematic A* Pathfinding**
- **File**: `TheResolver.Services.KinematicAStarSolver.cs`
- **Purpose**: Optimal path finding with kinematic constraints
- **Input**: Grid, start point, direction, target
- **Output**: Collision-free path with bending constraints
- **Features**:
  - Consideration of tray width and height
  - Bending radius constraints
  - Clearance requirements
  - Multiple path alternatives

#### **B. Iterative Parameter Adjustment**
- **File**: `TheResolver.BusinessLogics.ClashResolver.cs`
- **Purpose**: Escape local optima through parameter mutation
- **Strategy**:
  - Widening side offsets (iteration 0)
  - Direction reversal (iteration 1)
  - Increased bend radius (iteration 2)
  - Enhanced vertical clearance (iteration 3)
  - Auto-direction with maximum widening (iteration 4)

#### **C. Spatial Hashing Collision Detection**
- **File**: `TheResolver.Utilities.LocalSpatialIndex.cs`
- **Purpose**: Efficient spatial query processing
- **Method**: Cell-based spatial indexing
- **Complexity**: O(n) for insertion, O(k) for queries (k = nearby cells)

### 5.2 Geometric Algorithms

#### **A. Solid Intersection Testing**
- **File**: `TheResolver.BusinessLogics.ClashResolver.cs`
- **Purpose**: Precise collision detection using solid mathematics
- **Method**: Boolean operations on solid meshes
- **Implementation**: `ExecuteBooleanOperation()` with `Solid` objects

#### **B. Bounding Box Collision Detection**
- **File**: `TheResolver.BusinessLogics.ClashResolver.cs`
- **Purpose**: Fast pre-filter for solid intersection
- **Method**: Axis-Aligned Bounding Box (AABB) testing
- **Formula**: 
```
a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z
```

#### **C. Route Coordinate Transformation**
- **File**: `TheResolver.UI.ResolverView.xaml.cs`
- **Purpose**: Convert between 2D and 3D coordinate systems
- **Transformations**:
  - Station/Elevation → World XYZ
  - World XYZ → Viewport space
  - Real-world units (feet) → Viewport units

### 5.3 UI and Rendering Algorithms

#### **A. 3D Scene Management**
- **File**: `TheResolver.UI.ResolverView.xaml.cs`
- **Purpose**: 3D viewport rendering and interaction
- **Components**:
  - Camera control (orbit, pan, zoom)
  - Model rendering (materials, lighting)
  - Selection highlighting
  - Route visualization

#### **B. Data Grid Binding**
- **File**: `TheResolver.ViewModel.ClashViewModel.cs`
- **Purpose**: Efficient UI data binding
- **Method**: ObservableCollection with change notifications
- **Features**:
  - Automatic UI refresh on data changes
  - Selection synchronization
  - Summary calculations (counts, totals)

---

## 6. Key Calculation Methods and Working Principles

### 6.1 Unit Conversion System

#### **A. Millimeters to Feet Conversion**
```plaintext
CONSTANT: MM = 1.0 / 304.8  // Millimeters to Feet

FUNCTION ConvertMmToFeet(double mm):
    RETURN mm * MM

FUNCTION ConvertFeetToMm(double feet):
    RETURN feet / MM
```

**Files**: `TheResolver.BusinessLogics.ClashResolver.cs:19`, `TheResolver.ViewModel.ClashViewModel.cs:27`

#### **B. Coordinate System Transformation**
```plaintext
FUNCTION StationElevationToWorldTraysLine(start, end, p0):
    stationFt = p0.Station * MM
    elevationFt = p0.Elevation * MM
    RETURN start + (trayDir * stationFt) + new XYZ(0, 0, elevationFt)

FUNCTION WorldToViewport(worldPoint, center):
    RETURN new Point3D(
        (worldPoint.X - center.X) * 304.8,
        (worldPoint.Y - center.Y) * 304.8,
        (worldPoint.Z - center.Z) * 304.8
    )
```

**Files**: `TheResolver.UI.ResolverView.xaml.cs:154-165`

### 6.2 Route Geometry Calculations

#### **A. Bend Path Computation**
```plaintext
FUNCTION CreateBendPath(startPoint, endPoint, radius, angle):
    center = CalculateBendCenter(startPoint, endPoint, radius)
    arcAngle = angle * (PI / 180.0)
    
    points = []
    FOR t = 0 TO 1 STEP 0.1:
        theta = arcAngle * t
        point = center + new XYZ(
            radius * COS(theta),
            radius * SIN(theta),
            0
        )
        points.Add(TransformToWorldCoordinates(point))
    
    RETURN points
```

**Files**: `TheResolver.Services.GeometricUtilities.cs`

#### **B. Clearance Validation**
```plaintext
FUNCTION ValidateClearance(routePoints, obstacles, minClearance):
    clearanceMap = New SpatialHash()
    
    FOR each point in routePoints:
        nearbyObstacles = clearanceMap.Query(point, clearanceRange)
        
        FOR each obstacle in nearbyObstacles:
            distance = CalculateDistance(point, obstacle)
            IF distance < minClearance:
                RETURN FALSE, "Insufficient clearance"
    
    RETURN TRUE, "Clearance validated"
```

**Files**: `TheResolver.Services.GeometricUtilities.cs`

### 6.3 Spatial Index Calculations

#### **A. Cell Size Determination**
```plaintext
CONSTANT CELL_SIZE_MM = 1000.0  // 1 meter cells
CONSTANT CELL_SIZE_FT = CELL_SIZE_MM * MM

FUNCTION HashPosition(XYZ point):
    return new Tuple<int, int, int>(
        FLOOR(point.X / CELL_SIZE_FT),
        FLOOR(point.Y / CELL_SIZE_FT),
        FLOOR(point.Z / CELL_SIZE_FT)
    )
```

**Files**: `TheResolver.Utilities.LocalSpatialIndex.cs`

#### **B. ROI (Region of Interest) Calculation**
```plaintext
FUNCTION CalculateROI(trayLine, clashPoint, clearanceMeters):
    clearanceFt = clearanceMeters / 304.8
    spanFt = trayLine.Length + (2 * clearanceFt)
    
    center = GetLineCenter(trayLine)
    halfSpan = spanFt / 2.0
    
    RETURN new BoundingBoxXYZ(
        Min = center - new XYZ(halfSpan, halfSpan, halfSpan),
        Max = center + new XYZ(halfSpan, halfSpan, halfSpan)
    )
```

**Files**: `TheResolver.Commands.ClashResolveTool.cs:322-330`

### 6.4 Performance Optimization Calculations

#### **A. Grid Resolution Optimization**
```plaintext
CONSTANT MIN_CELL_SIZE_MM = 75.0  // ~2.5 inches
CONSTANT MAX_CELL_SIZE_MM = 2000.0  // 2 meters

FUNCTION OptimalCellSize(bbox, targetCellCount):
    volume = bbox.SizeX * bbox.SizeY * bbox.SizeZ
    idealCellSize = CUBE_ROOT(volume / targetCellCount)
    
    return Math.Max(
        Math.Min(idealCellSize, MAX_CELL_SIZE_MM),
        MIN_CELL_SIZE_MM
    ) * MM
```

**Files**: `TheResolver.Services.KinematicAStarSolver.cs:791`

#### **B. Bending Moment Calculation**
```plaintext
FUNCTION CalculateBendingMoment(force, radius, angle):
    // Force = weight of tray + connected components
    // Radius = bend radius in feet
    // Angle = bend angle in degrees
    
    moment = force * radius * (angle * PI / 180.0)
    safetyFactor = 1.5  // Conservative factor for MEP
    
    return moment * safetyFactor
```

**Files**: `TheResolver.BusinessLogics.ClashResolver.cs:357`

### 6.5 Status and Color Calculations

#### **A. Status Color Mapping**
```plaintext
FUNCTION GetStatusColor(IsResolved, ResolveFailed):
    IF IsResolved:
        RETURN "#2E7D32"  // Green
    ELSE IF ResolveFailed:
        RETURN "#B71C1C"  // Red
    ELSE:
        RETURN "#616161"  // Gray
```

**Files**: `TheResolver.ViewModel.ClashGridItemViewModel.cs:132-144`

#### **B. Progress Percentage Calculation**
```plaintext
FUNCTION CalculateResolutionProgress(TotalClashes, ResolvedClashes):
    IF TotalClashes == 0:
        RETURN 0
    
    return (ResolvedClashes * 100.0) / TotalClashes
```

**Files**: `TheResolver.ViewModel.ClashViewModel.cs:541-542`

---

## Project Summary

### Key Technical Components
1. **Core Resolution Engine**: Iterative parameter adjustment with geometric validation
2. **Advanced Pathfinding**: Kinematic A* algorithm for optimal routing
3. **Spatial Indexing**: Efficient collision detection and query processing
4. **3D Visualization**: Real-time route preview and tray representation
5. **Unit Conversion System**: Comprehensive scale management (mm, ft, viewport)

### Performance Characteristics
- **Clash Detection**: O(n) with spatial indexing
- **Path Finding**: O((N + E) log N) for A* algorithm
- **Memory Usage**: Dynamic spatial index with garbage collection
- **Update Frequency**: Real-time for UI, batch for resolution

### Scale and Units Management
- **Primary Unit**: Feet (Revit internal standard)
- **Input Unit**: Millimeters (user-friendly)
- **Viewport Unit**: Arbitrary (pixels)
- **Conversion Factors**:
  - 1 foot = 304.8 millimeters
  - 1 station unit = 1 millimeter
  - 1 elevation unit = 1 millimeter

### Error Handling and Recovery
- **Graceful Degradation**: Fall back to simpler routing on failure
- **Transaction Safety**: Rollback on any failure
- **User Feedback**: Clear status messages for all operations
- **Validation**: Comprehensive input validation and range checking

---

*This documentation covers the core technical architecture and algorithms of TheResolver project, with all calculations and implementations referenced to their source files for maintainability.*