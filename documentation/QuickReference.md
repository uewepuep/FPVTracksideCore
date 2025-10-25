# FPVTrackside Quick Reference Guide

## Build Commands

### Windows Development
```bash
# Build entire solution
dotnet build "FPVTrackside - Core.sln"

# Build specific project
dotnet build FPVTracksideCore/FPVTracksideCore.csproj

# Run application
cd FPVTracksideCore
dotnet run

# Clean build
dotnet clean
dotnet build
```

### macOS Development
```bash
# Build entire solution
dotnet build "FPVMacside - Core.sln"

# Build specific project
dotnet build FPVMacSideCore/FPVMacsideCore.csproj

# Run application
cd FPVMacSideCore
dotnet run --project FPVMacsideCore.csproj

# Clean build
dotnet clean
dotnet build
```

## Common Development Tasks

### Adding a New UI Component

1. **Create the Node Class**
```csharp
// UI/Nodes/MyCustomNode.cs
public class MyCustomNode : Node
{
    public MyCustomNode()
    {
        // Initialize your node
    }

    public override void Draw(Drawer id, float parentAlpha)
    {
        // Draw your content
        base.Draw(id, parentAlpha);
    }
}
```

2. **Add to Layer**
```csharp
// In your layer class
var myNode = new MyCustomNode();
AddChild(myNode);
```

### Adding a New Timing System

1. **Implement ITimingSystem**
```csharp
// Timing/MyTimingSystem.cs
public class MyTimingSystem : ITimingSystem
{
    public bool Connect() { /* implementation */ }
    public void Disconnect() { /* implementation */ }
    public void StartListening() { /* implementation */ }
    public void StopListening() { /* implementation */ }
    public event Action<Lap> OnLapDetected;
}
```

2. **Register in Timing System Factory**
```csharp
// Add to available timing systems
TimingSystemFactory.Available.Add(new MyTimingSystem());
```

### Adding a New Video Source

1. **Implement VideoFrameSource**
```csharp
// ffmpegMediaPlatform/MyVideoSource.cs
public class MyVideoSource : VideoFrameSource
{
    public override bool Connect() { /* implementation */ }
    public override void Disconnect() { /* implementation */ }
    public override Texture2D GetFrame() { /* implementation */ }
}
```

2. **Register in VideoFrameWorks**
```csharp
VideoFrameWorks.Available = new VideoFrameWork[]
{
    new MyVideoFrameWork()
};
```

## Debugging Commands

### Logging
```csharp
// UI logging
Tools.Logger.UI.Log(this, "Message");

// All logging
Tools.Logger.AllLog.LogCall(this, "Method called");

// Exception logging
Tools.Logger.UI.LogException(this, exception);
```

### Performance Monitoring
```csharp
using (var timer = new DebugTimer("Operation Name"))
{
    // Your code here
}
```

### Database Operations
```csharp
// Open database
using (var db = DatabaseFactory.Open(eventId))
{
    // Database operations
    var pilots = db.GetPilots();
    var races = db.GetRaces();
}
```

## File Locations

### Important Directories
- **Application Data**: `~/Documents/FPVTrackside/` (macOS) or `%USERPROFILE%\Documents\FPVTrackside\` (Windows)
- **Logs**: `/log/` directory in application data
- **Themes**: `/themes/` directory in application data
- **Pilots**: `/pilots/` directory in application data
- **Video**: `/video/` directory in application data

### Key Source Files
- **Main Entry (Windows)**: `FPVTracksideCore/Program.cs`
- **Main Entry (macOS)**: `FPVMacSideCore/Program.cs`
- **Base Game**: `UI/BaseGame.cs`
- **Game Engine**: `Compositor/LayerStackGame.cs`
- **Platform Tools**: `Compositor/PlatformTools.cs`
- **Race Logic**: `RaceLib/` directory
- **Timing Systems**: `Timing/` directory

## Common Patterns

### Event Handling
```csharp
// Subscribe to event
someObject.OnEvent += HandleEvent;

// Unsubscribe (important!)
someObject.OnEvent -= HandleEvent;

// Event handler
private void HandleEvent(object sender, EventArgs e)
{
    // Handle event
}
```

### Background Operations
```csharp
// Queue background work
Background.Enqueue("Operation Name", () =>
{
    // Long-running operation
});

// UI updates from background thread
PlatformTools.Invoke(() =>
{
    // Update UI
});
```

### Resource Management
```csharp
// Using statement for disposable resources
using (var resource = new DisposableResource())
{
    // Use resource
}

// Manual disposal
var resource = new DisposableResource();
try
{
    // Use resource
}
finally
{
    resource?.Dispose();
}
```

## Testing Commands

### Run Tests
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test TimingTesting/TimingTesting.csproj

# Run with verbose output
dotnet test --verbosity normal
```

### Performance Testing
```bash
# Profile application
dotnet run --configuration Release
# Use Visual Studio Profiler or dotnet-trace
```

## Deployment Commands

### Release Builds
```bash
# Windows Release
dotnet publish FPVTracksideCore/FPVTracksideCore.csproj -c Release -r win-x64 --self-contained

# macOS Release
dotnet publish FPVMacSideCore/FPVMacsideCore.csproj -c Release -r osx-x64 --self-contained
```

### Package Creation
```bash
# Create Windows installer (requires WiX)
# Create macOS DMG (requires create-dmg)
```

## Git Workflow

### Common Git Commands
```bash
# Create feature branch
git checkout -b feature/new-feature

# Commit changes
git add .
git commit -m "Add new feature"

# Push to remote
git push origin feature/new-feature

# Create pull request (via GitHub/GitLab web interface)
```

### Branch Naming Conventions
- `feature/feature-name` - New features
- `bugfix/bug-description` - Bug fixes
- `hotfix/urgent-fix` - Critical fixes
- `refactor/component-name` - Code refactoring

## Troubleshooting

### Common Issues

#### Build Errors
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

#### Runtime Errors
```bash
# Check log files in /log/ directory
# Enable verbose logging
# Check database integrity
```

#### Performance Issues
```bash
# Monitor memory usage
# Check background threads
# Profile application
```

### Debug Mode
```csharp
#if DEBUG
    // Debug-only code
    Console.WriteLine("Debug mode active");
#endif
```

## Platform-Specific Notes

### Windows
- Use DirectX for graphics
- Windows Forms integration available
- DirectShow for video capture
- File paths use backslashes (but use `Path.Combine()`)

### macOS
- Use OpenGL for graphics
- AVFoundation for video capture
- Native macOS APIs available
- File paths use forward slashes
- Black screen fixes implemented

## External Dependencies

### Required Software
- **FFmpeg**: Video processing (included)
- **.NET 6.0**: Runtime framework
- **MonoGame**: Graphics framework
- **LiteDB**: Database (embedded)

### Optional Software
- **Visual Studio 2022**: Windows development
- **Visual Studio for Mac**: macOS development
- **JetBrains Rider**: Cross-platform development
- **Git**: Version control

## Quick Tips

1. **Always dispose resources** using `using` statements
2. **Use background threads** for long-running operations
3. **Update UI on main thread** using `PlatformTools.Invoke()`
4. **Log exceptions** for debugging
5. **Test on both platforms** for cross-platform features
6. **Follow naming conventions** consistently
7. **Add error handling** to all external operations
8. **Use performance monitoring** for optimization
9. **Keep dependencies updated** regularly
10. **Document new features** thoroughly

---

For more detailed information, see the main [Developer Guide](DeveloperGuide.md). 