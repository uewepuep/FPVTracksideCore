# FPVTrackside Architecture Diagrams

## System Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           FPVTrackside Application                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐        │
│  │   Windows App   │    │   macOS App     │    │   Web Server    │        │
│  │ FPVTracksideCore│    │ FPVMacsideCore  │    │      Webb       │        │
│  └─────────────────┘    └─────────────────┘    └─────────────────┘        │
│           │                       │                       │                │
│           └───────────────────────┼───────────────────────┘                │
│                                   │                                        │
│  ┌─────────────────────────────────┼─────────────────────────────────────┐  │
│  │                    Core Application Layer                             │  │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐     │  │
│  │  │     UI      │ │  Compositor │ │   RaceLib   │ │   Timing    │     │  │
│  │  └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘     │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                   │                                        │
│  ┌─────────────────────────────────┼─────────────────────────────────────┐  │
│  │                    Platform Abstraction Layer                         │  │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐     │  │
│  │  │   Windows   │ │   macOS     │ │   Image     │ │   Sound     │     │  │
│  │  │  Platform   │ │  Platform   │ │   Server    │ │   System    │     │  │
│  │  └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘     │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                   │                                        │
│  ┌─────────────────────────────────┼─────────────────────────────────────┐  │
│  │                    External Dependencies                               │  │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐     │  │
│  │  │   MonoGame  │ │    FFmpeg   │ │   LiteDB    │ │   .NET 6    │     │  │
│  │  │    /XNA     │ │             │ │             │ │             │     │  │
│  │  └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘     │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Component Relationships

### Core Application Flow

```
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│   Program   │───▶│  BaseGame   │───▶│LayerStackGame│───▶│  MonoGame   │
│   Entry     │    │             │    │             │    │   Engine    │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
                          │
                          ▼
                   ┌─────────────┐
                   │ EventManager│
                   └─────────────┘
                          │
                    ┌─────┴─────┐
                    ▼           ▼
              ┌─────────┐ ┌─────────┐
              │RaceMgr  │ │PilotMgr │
              └─────────┘ └─────────┘
```

### UI Layer Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Layer Stack                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │  Background     │  │    Event        │  │     Menu        │             │
│  │     Layer       │  │     Layer       │  │     Layer       │             │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘             │
│           │                       │                       │                 │
│           ▼                       ▼                       ▼                 │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │   Background    │  │   Race Display  │  │   Menu Items    │             │
│  │     Nodes       │  │     Nodes       │  │     Nodes       │             │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘             │
│           │                       │                       │                 │
│           └───────────────────────┼───────────────────────┘                 │
│                                   │                                         │
│  ┌─────────────────────────────────┼─────────────────────────────────────┐  │
│  │                        Popup Layer                                    │  │
│  │  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐       │  │
│  │  │   Dialog        │  │   Notification  │  │   Loading       │       │  │
│  │  │   Nodes         │  │     Nodes       │  │     Nodes       │       │  │
│  │  └─────────────────┘  └─────────────────┘  └─────────────────┘       │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Data Flow Architecture

```
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│  Timing     │───▶│  RaceLib    │───▶│     DB      │───▶│   LiteDB    │
│  Systems    │    │             │    │             │    │   Files     │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
       │                    │                    │                    │
       ▼                    ▼                    ▼                    ▼
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│   Lap       │    │   Event     │    │  Database   │    │   JSON      │
│   Events    │    │   Manager   │    │   Factory   │    │   Storage   │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
       │                    │                    │                    │
       └────────────────────┼────────────────────┘                    │
                            │                                           │
                            ▼                                           │
                   ┌─────────────┐                                     │
                   │     UI      │◀────────────────────────────────────┘
                   │   Updates   │
                   └─────────────┘
```

### Video Processing Pipeline

```
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│   Camera    │───▶│   FFmpeg    │───▶│  Image      │───▶│   Compositor│
│   Input     │    │  Platform   │    │  Server     │    │   System    │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
       │                    │                    │                    │
       ▼                    ▼                    ▼                    ▼
┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│  Video      │    │  Frame      │    │  Texture    │    │   UI        │
│  Sources    │    │  Sources    │    │  Processing │    │   Display   │
└─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘
```

## Class Hierarchy

### Game Engine Hierarchy

```
Microsoft.Xna.Framework.Game
           │
           ▼
    LayerStackGame
           │
           ▼
LayerStackGameBackgroundThread
           │
           ▼
       BaseGame
           │
    ┌──────┴──────┐
    ▼             ▼
FPVTracksideCoreGame  FPVMacsideCoreGame
```

### UI Component Hierarchy

```
Node (Base)
  │
  ├── TextNode
  │   ├── TextNode
  │   ├── ButtonNode
  │   └── MenuButton
  │
  ├── ImageNode
  │   ├── ImageNode
  │   ├── VideoNode
  │   └── TextureNode
  │
  ├── ContainerNode
  │   ├── ContainerNode
  │   ├── GridNode
  │   └── FlowNode
  │
  └── SpecializedNodes
      ├── DragNode
      ├── ColorNode
      └── CustomNodes
```

### Layer Hierarchy

```
Layer (Base)
  │
  ├── BackgroundLayer
  │   └── Background rendering
  │
  ├── EventLayer
  │   ├── Race display
  │   ├── Timing display
  │   └── Results display
  │
  ├── MenuLayer
  │   ├── Main menu
  │   ├── Settings menu
  │   └── Navigation
  │
  ├── PopupLayer
  │   ├── Dialogs
  │   ├── Notifications
  │   └── Loading screens
  │
  └── SpecializedLayers
      ├── DragLayer
      ├── LoadingLayer
      └── SponsorLayer
```

## Data Models

### Core Data Entities

```
Event
  │
  ├── EventId (Guid)
  ├── Name (string)
  ├── Date (DateTime)
  ├── Location (string)
  ├── Pilots (List<Pilot>)
  ├── Rounds (List<Round>)
  └── Settings (EventSettings)

Pilot
  │
  ├── PilotId (Guid)
  ├── Name (string)
  ├── Callsign (string)
  ├── Image (string)
  ├── Statistics (PilotStatistics)
  └── Races (List<Race>)

Race
  │
  ├── RaceId (Guid)
  ├── Name (string)
  ├── Type (RaceType)
  ├── Pilots (List<Pilot>)
  ├── Laps (List<Lap>)
  ├── StartTime (DateTime)
  └── EndTime (DateTime)

Lap
  │
  ├── LapId (Guid)
  ├── PilotId (Guid)
  ├── RaceId (Guid)
  ├── LapNumber (int)
  ├── Time (TimeSpan)
  ├── DetectionTime (DateTime)
  └── Valid (bool)
```

### Database Schema

```
LiteDB Collections:
  │
  ├── Events
  │   ├── _id (ObjectId)
  │   ├── EventId (Guid)
  │   ├── Name (string)
  │   ├── Date (DateTime)
  │   └── Data (BsonDocument)
  │
  ├── Pilots
  │   ├── _id (ObjectId)
  │   ├── PilotId (Guid)
  │   ├── Name (string)
  │   ├── Callsign (string)
  │   └── Data (BsonDocument)
  │
  ├── Races
  │   ├── _id (ObjectId)
  │   ├── RaceId (Guid)
  │   ├── EventId (Guid)
  │   ├── Name (string)
  │   └── Data (BsonDocument)
  │
  └── Laps
      ├── _id (ObjectId)
      ├── LapId (Guid)
      ├── RaceId (Guid)
      ├── PilotId (Guid)
      ├── LapNumber (int)
      ├── Time (TimeSpan)
      └── DetectionTime (DateTime)
```

## Platform Differences

### Windows vs macOS

```
Windows Platform:
  │
  ├── Graphics: DirectX
  ├── Video: DirectShow
  ├── UI: Windows Forms
  ├── File System: NTFS
  └── Threading: Windows Threads

macOS Platform:
  │
  ├── Graphics: OpenGL
  ├── Video: AVFoundation
  ├── UI: Native macOS
  ├── File System: APFS/HFS+
  └── Threading: POSIX Threads
```

### Cross-Platform Abstraction

```
PlatformTools (Abstract)
  │
  ├── CreateTextRenderer()
  ├── CreateSpeaker()
  ├── WorkingDirectory
  ├── Clipboard
  ├── Focused
  ├── ThreadedDrawing
  └── Invoke()

WindowsPlatformTools
  │
  ├── DirectX rendering
  ├── Windows Forms integration
  ├── DirectShow video
  └── Windows-specific APIs

MacPlatformTools
  │
  ├── OpenGL rendering
  ├── Native macOS APIs
  ├── AVFoundation video
  └── macOS-specific APIs
```

## Performance Considerations

### Memory Management

```
Resource Lifecycle:
  │
  ├── Creation
  │   ├── Allocate memory
  │   ├── Initialize resources
  │   └── Register for disposal
  │
  ├── Usage
  │   ├── Access resources
  │   ├── Monitor performance
  │   └── Handle errors
  │
  └── Disposal
      ├── Release memory
      ├── Unregister events
      └── Clean up references
```

### Threading Model

```
Main Thread:
  │
  ├── UI Updates
  ├── User Input
  ├── Game Loop
  └── Graphics Rendering

Background Threads:
  │
  ├── Video Processing
  ├── Database Operations
  ├── Network Communication
  └── File I/O Operations

Thread Communication:
  │
  ├── PlatformTools.Invoke()
  ├── Background.Enqueue()
  ├── Event System
  └── Thread-Safe Collections
```

## Security Considerations

### Data Protection

```
Security Layers:
  │
  ├── Input Validation
  │   ├── Sanitize user input
  │   ├── Validate file paths
  │   └── Check data types
  │
  ├── Access Control
  │   ├── File permissions
  │   ├── Database access
  │   └── Network security
  │
  ├── Error Handling
  │   ├── Exception logging
  │   ├── Graceful degradation
  │   └── User feedback
  │
  └── Data Integrity
      ├── Database validation
      ├── File integrity checks
      └── Backup strategies
```

---

These diagrams provide a high-level overview of the FPVTrackside architecture. For detailed implementation information, refer to the source code and the main [Developer Guide](DeveloperGuide.md). 