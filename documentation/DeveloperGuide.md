# FPVTrackside Developer Guide

## Table of Contents
1. [Project Overview](#project-overview)
2. [Architecture Overview](#architecture-overview)
3. [Project Structure](#project-structure)
4. [Development Setup](#development-setup)
5. [Core Components](#core-components)
6. [Platform Support](#platform-support)
7. [Development Workflow](#development-workflow)
8. [Common Issues and Solutions](#common-issues-and-solutions)
9. [Best Practices](#best-practices)
10. [Testing](#testing)
11. [Deployment](#deployment)
12. [Contributing](#contributing)

## Project Overview

FPVTrackside is a comprehensive drone racing timing and video software designed for the FPV (First Person View) drone racing community. The application provides real-time timing, video processing, race management, and streaming capabilities for drone racing events.

### Key Features
- **Real-time Timing**: Support for multiple timing systems (ImmersionRC, RotorHazard, Chorus)
- **Video Processing**: Multi-camera support with FFmpeg integration
- **Race Management**: Event creation, pilot management, heat scheduling
- **Live Streaming**: Integrated streaming capabilities
- **Web Interface**: Remote access and control via web browser
- **Cross-platform**: Windows and macOS support

### Technology Stack
- **Framework**: .NET 6.0 with MonoGame/XNA
- **Graphics**: DirectX (Windows) / OpenGL (macOS)
- **Database**: LiteDB (NoSQL document database)
- **Video Processing**: FFmpeg
- **UI Framework**: Custom layer-based compositor system
- **Platform Abstraction**: Custom platform tools for cross-platform support

## Architecture Overview

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                        │
├─────────────────────────────────────────────────────────────┤
│  FPVTracksideCore (Windows) │ FPVMacsideCore (macOS)       │
├─────────────────────────────────────────────────────────────┤
│                    UI Layer                                 │
├─────────────────────────────────────────────────────────────┤
│                Compositor System                            │
├─────────────────────────────────────────────────────────────┤
│              Platform Abstraction Layer                     │
├─────────────────────────────────────────────────────────────┤
│  WindowsPlatform │ MacPlatformTools │ PlatformTools        │
└─────────────────────────────────────────────────────────────┘
```

### Core Architecture Principles

1. **Layer-Based Compositor**: Custom UI system using layers for flexible rendering
2. **Platform Abstraction**: Cross-platform support through abstracted platform tools
3. **Event-Driven**: Asynchronous event handling for timing and UI updates
4. **Modular Design**: Separate projects for different functionality areas
5. **Background Processing**: Non-blocking operations for video and timing

## Project Structure

### Solution Organization

The project is organized into multiple .NET projects, each handling specific functionality:

```
FPVTracksideCore/
├── FPVTracksideCore/          # Windows main application
├── FPVMacSideCore/            # macOS main application
├── UI/                        # User interface components
├── Compositor/                # Graphics and rendering system
├── RaceLib/                   # Core racing logic and data models
├── Timing/                    # Timing system integrations
├── DB/                        # Database layer and data access
├── ImageServer/               # Image processing and serving
├── ffmpegMediaPlatform/       # Video processing with FFmpeg
├── Sound/                     # Audio system
├── Webb/                      # Web server and remote access
├── Tools/                     # Utility classes and helpers
├── ExternalData/              # External data integrations
├── Spreadsheets/              # Excel/CSV import/export
├── WindowsPlatform/           # Windows-specific platform code
├── WinFormsGraphicsDevice/    # Windows Forms integration
└── TimingTesting/             # Timing system testing utilities
```

### Key Directories

- **`/data/`**: Application data storage (events, settings, etc.)
- **`/themes/`**: UI theme definitions and assets
- **`/pilots/`**: Pilot images and data
- **`/sponsors/`**: Sponsor logos and information
- **`/video/`**: Video files and recordings
- **`/log/`**: Application logs
- **`/httpfiles/`**: Web server static files

## Development Setup

### Prerequisites

#### Windows Development
- Visual Studio 2022 (Community or higher)
- .NET 6.0 SDK
- Windows 10/11
- DirectX 11 compatible graphics card

#### macOS Development
- Visual Studio for Mac or JetBrains Rider
- .NET 6.0 SDK
- macOS 10.15 or higher
- Xcode Command Line Tools

#### Common Requirements
- FFmpeg (included in project)
- Git
- At least 8GB RAM (16GB recommended)
- SSD storage recommended

### Initial Setup

1. **Clone the Repository**
   ```bash
   git clone https://github.com/your-org/FPVTracksideCore.git
   cd FPVTracksideCore
   ```

2. **Restore Dependencies**
   ```bash
   dotnet restore
   ```

3. **Build the Solution**
   ```bash
   # Windows
   dotnet build "FPVTrackside - Core.sln"
   
   # macOS
   dotnet build "FPVMacside - Core.sln"
   ```

4. **Run the Application**
   ```bash
   # Windows
   cd FPVTracksideCore
   dotnet run
   
   # macOS
   cd FPVMacSideCore
   dotnet run --project FPVMacsideCore.csproj
   ```

### Development Environment Configuration

#### Visual Studio Configuration
1. Open the appropriate solution file
2. Set startup project to `FPVTracksideCore` (Windows) or `FPVMacsideCore` (macOS)
3. Configure debugging settings in project properties
4. Set working directory to project root for proper asset loading

#### Debugging Setup
- Enable "Just My Code" for better debugging experience
- Configure symbol loading for external libraries
- Set up conditional breakpoints for timing-sensitive code

## Core Components

### 1. Game Engine (LayerStackGame)

The application is built on a custom game engine extending MonoGame/XNA:

```csharp
public class LayerStackGame : Microsoft.Xna.Framework.Game
{
    public LayerStackScaled LayerStack { get; private set; }
    public GraphicsDeviceManager GraphicsDeviceManager { get; private set; }
    public PlatformTools PlatformTools { get; protected set; }
}
```

**Key Features:**
- Custom layer-based rendering system
- Platform abstraction through `PlatformTools`
- Background thread support for non-blocking operations
- Automatic window management and resizing

### 2. Compositor System

The UI is built using a custom compositor system with layers:

```csharp
public class LayerStack : IDisposable
{
    private List<Layer> layers;
    public void Add(Layer layer);
    public void Draw();
    public void Update(GameTime gameTime);
}
```

**Layer Types:**
- `BackgroundLayer`: Base background rendering
- `EventLayer`: Race event display
- `LoadingLayer`: Loading screens and progress
- `MenuLayer`: Menu system
- `PopupLayer`: Modal dialogs and popups
- `DragLayer`: Drag and drop functionality

### 3. Platform Abstraction

Cross-platform support is achieved through the `PlatformTools` abstraction:

```csharp
public abstract class PlatformTools
{
    public abstract ITextRenderer CreateTextRenderer();
    public abstract ISpeaker CreateSpeaker(string voice);
    public abstract DirectoryInfo WorkingDirectory { get; }
    public abstract IClipboard Clipboard { get; }
    public abstract bool Focused { get; }
    public abstract bool ThreadedDrawing { get; }
}
```

**Platform Implementations:**
- `WindowsPlatformTools`: Windows-specific functionality
- `MacPlatformTools`: macOS-specific functionality

### 4. Race Management System

Core racing logic is handled by the `RaceLib` project:

```csharp
public class EventManager
{
    public Event CurrentEvent { get; }
    public RaceManager RaceManager { get; }
    public PilotManager PilotManager { get; }
    public RoundManager RoundManager { get; }
}
```

**Key Components:**
- `Event`: Represents a racing event
- `Race`: Individual race with pilots and timing
- `Pilot`: Pilot information and statistics
- `Round`: Group of races
- `Lap`: Individual lap timing data

### 5. Timing System

Multiple timing system integrations are supported:

```csharp
public interface ITimingSystem
{
    bool Connect();
    void Disconnect();
    void StartListening();
    void StopListening();
    event Action<Lap> OnLapDetected;
}
```

**Supported Systems:**
- ImmersionRC LapRF
- RotorHazard
- Chorus
- Dummy timing system for testing

### 6. Video Processing

Video handling is managed through the `ImageServer` and `ffmpegMediaPlatform`:

```csharp
public class VideoFrameWork
{
    public bool Available { get; }
    public string Name { get; }
    public VideoFrameSource CreateFrameSource(string source);
}
```

**Features:**
- Multi-camera support
- Real-time video processing
- Chroma key support
- Video recording and playback

## Platform Support

### Windows Platform

**Entry Point:** `FPVTracksideCore/Program.cs`
**Main Class:** `FPVTracksideCoreGame`
**Platform Tools:** `WindowsPlatformTools`

**Key Features:**
- DirectX rendering
- Windows Forms integration
- Native Windows APIs
- DirectShow video capture

### macOS Platform

**Entry Point:** `FPVMacSideCore/Program.cs`
**Main Class:** `FPVMacsideCoreGame`
**Platform Tools:** `MacPlatformTools`

**Key Features:**
- OpenGL rendering
- Native macOS APIs
- AVFoundation video capture
- Cross-platform compatibility fixes

### Cross-Platform Considerations

1. **File Paths**: Use `Path.Combine()` and forward slashes
2. **Graphics**: Platform-specific rendering backends
3. **Video**: Different capture APIs per platform
4. **Threading**: Background thread handling differences
5. **UI**: Platform-specific UI behaviors

## Development Workflow

### 1. Feature Development

1. **Create Feature Branch**
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Implement Changes**
   - Follow the established architecture patterns
   - Add appropriate error handling
   - Include logging for debugging

3. **Test Your Changes**
   - Test on both Windows and macOS
   - Verify timing system integration
   - Check video processing functionality

4. **Submit Pull Request**
   - Include detailed description of changes
   - Reference any related issues
   - Ensure all tests pass

### 2. Debugging

#### Common Debugging Techniques

1. **Logging**
   ```csharp
   Tools.Logger.UI.Log(this, "Debug message");
   Tools.Logger.AllLog.LogCall(this, "Method called");
   ```

2. **Exception Handling**
   ```csharp
   try
   {
       // Your code
   }
   catch (Exception e)
   {
       Tools.Logger.UI.LogException(this, e);
   }
   ```

3. **Performance Monitoring**
   ```csharp
   using (var timer = new DebugTimer("Operation Name"))
   {
       // Your code
   }
   ```

#### Debugging Tools

- **Visual Studio Debugger**: Primary debugging tool
- **Log Files**: Located in `/log/` directory
- **Performance Profiler**: Built-in .NET profiling
- **Graphics Debugger**: DirectX/OpenGL debugging

### 3. Code Organization

#### Naming Conventions
- **Classes**: PascalCase (e.g., `EventManager`)
- **Methods**: PascalCase (e.g., `CreateEvent()`)
- **Properties**: PascalCase (e.g., `CurrentEvent`)
- **Fields**: camelCase with underscore prefix (e.g., `_eventManager`)
- **Constants**: UPPER_CASE (e.g., `MAX_PILOTS`)

#### File Organization
- **One class per file**: Each class in its own file
- **Namespace organization**: Follow project structure
- **Region usage**: Group related members with regions
- **Using statements**: Organize alphabetically

## Common Issues and Solutions

### 1. Black Screen Issues (macOS)

**Problem**: Application shows black screen on macOS
**Solution**: Implemented comprehensive fixes in `LayerStackGame.cs` and `FPVMacsideCoreGame.cs`

```csharp
// Force initial draw
protected override void Initialize()
{
    base.Initialize();
    if (GraphicsDevice != null)
    {
        this.GraphicsDevice.Clear(ClearColor);
        this.Draw(new GameTime());
        this.GraphicsDevice.Present();
    }
}
```

### 2. Video Capture Issues

**Problem**: Camera not detected or video not displaying
**Solution**: Check FFmpeg installation and camera permissions

```csharp
// Verify video source availability
var frameWorks = VideoFrameWorks.Available;
foreach (var frameWork in frameWorks)
{
    Console.WriteLine($"Available: {frameWork.Name}");
}
```

### 3. Timing System Connection Issues

**Problem**: Timing system not connecting
**Solution**: Check hardware connections and timing system settings

```csharp
// Test timing system connection
using (var timingSystem = new ImmersionRCTimingSystem())
{
    if (timingSystem.Connect())
    {
        Console.WriteLine("Timing system connected successfully");
    }
}
```

### 4. Database Issues

**Problem**: Data corruption or access issues
**Solution**: Check database file permissions and integrity

```csharp
// Verify database connection
using (var db = DatabaseFactory.Open(Guid.Empty))
{
    var version = db.Version;
    Console.WriteLine($"Database version: {version}");
}
```

### 5. Performance Issues

**Problem**: Application running slowly or freezing
**Solution**: Monitor background threads and memory usage

```csharp
// Monitor performance
using (var timer = new DebugTimer("Operation"))
{
    // Your operation
}
```

## Best Practices

### 1. Code Quality

- **SOLID Principles**: Follow SOLID design principles
- **DRY Principle**: Don't repeat yourself
- **Single Responsibility**: Each class should have one reason to change
- **Dependency Injection**: Use constructor injection for dependencies

### 2. Error Handling

```csharp
// Always wrap operations in try-catch
try
{
    // Risky operation
}
catch (SpecificException ex)
{
    // Handle specific exception
    Tools.Logger.UI.LogException(this, ex);
}
catch (Exception ex)
{
    // Handle general exception
    Tools.Logger.UI.LogException(this, ex);
}
```

### 3. Resource Management

```csharp
// Use using statements for disposable resources
using (var db = DatabaseFactory.Open(eventId))
{
    // Database operations
}

// Implement IDisposable for custom resources
public class CustomResource : IDisposable
{
    public void Dispose()
    {
        // Cleanup code
    }
}
```

### 4. Threading

```csharp
// Use background threads for long-running operations
Background.Enqueue("Operation Name", () =>
{
    // Long-running operation
});

// Use PlatformTools.Invoke for UI updates
PlatformTools.Invoke(() =>
{
    // UI update code
});
```

### 5. Memory Management

- **Dispose Resources**: Always dispose of disposable objects
- **Avoid Memory Leaks**: Be careful with event subscriptions
- **Monitor Memory Usage**: Use profiling tools to identify leaks
- **Optimize Collections**: Use appropriate collection types

## Testing

### 1. Unit Testing

```csharp
[Test]
public void TestEventCreation()
{
    var eventManager = new EventManager();
    var newEvent = eventManager.CreateEvent("Test Event");
    
    Assert.IsNotNull(newEvent);
    Assert.AreEqual("Test Event", newEvent.Name);
}
```

### 2. Integration Testing

- **Timing System Tests**: Test timing system integrations
- **Video System Tests**: Test video capture and processing
- **Database Tests**: Test data persistence and retrieval
- **UI Tests**: Test user interface functionality

### 3. Performance Testing

- **Load Testing**: Test with multiple pilots and races
- **Memory Testing**: Monitor memory usage over time
- **Video Performance**: Test video processing performance
- **Timing Accuracy**: Verify timing system accuracy

### 4. Platform Testing

- **Windows Testing**: Test on different Windows versions
- **macOS Testing**: Test on different macOS versions
- **Cross-Platform**: Ensure consistent behavior across platforms

## Deployment

### 1. Build Configuration

```xml
<!-- Project file configuration -->
<PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <OutputType>WinExe</OutputType>
    <Platforms>x64</Platforms>
</PropertyGroup>
```

### 2. Release Build

```bash
# Windows Release Build
dotnet publish FPVTracksideCore/FPVTracksideCore.csproj -c Release -r win-x64 --self-contained

# macOS Release Build
dotnet publish FPVMacSideCore/FPVMacsideCore.csproj -c Release -r osx-x64 --self-contained
```

### 3. Distribution

- **Windows**: Create installer using WiX or similar
- **macOS**: Create DMG package
- **Updates**: Implement auto-update mechanism
- **Documentation**: Include user documentation

## Contributing

### 1. Development Guidelines

- **Code Style**: Follow established coding standards
- **Documentation**: Document new features and APIs
- **Testing**: Include tests for new functionality
- **Review**: All changes require code review

### 2. Pull Request Process

1. **Fork Repository**: Create your own fork
2. **Create Branch**: Create feature branch from main
3. **Implement Changes**: Follow development guidelines
4. **Test Changes**: Ensure all tests pass
5. **Submit PR**: Create pull request with description
6. **Code Review**: Address review comments
7. **Merge**: Changes merged after approval

### 3. Issue Reporting

When reporting issues, include:
- **Platform**: Windows/macOS version
- **Steps to Reproduce**: Detailed reproduction steps
- **Expected Behavior**: What should happen
- **Actual Behavior**: What actually happens
- **Logs**: Relevant log files
- **Screenshots**: Visual evidence if applicable

### 4. Community

- **Discord**: Join the community Discord server
- **Documentation**: Contribute to documentation
- **Examples**: Share code examples and tutorials
- **Feedback**: Provide feedback on features and improvements

---

## Additional Resources

- **API Documentation**: Generated from code comments
- **Video System Documentation**: See `VideoSystemDocumentation.md`
- **Timing System Guide**: Platform-specific timing setup
- **Troubleshooting Guide**: Common problems and solutions
- **Performance Guide**: Optimization techniques and best practices

For more information, visit the project website or join the community Discord server. 