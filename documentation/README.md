# FPVTrackside Documentation

Welcome to the FPVTrackside documentation! This folder contains comprehensive technical documentation for developers working on the FPVTrackside project.

## Documentation Structure

### 📚 Core Documentation

- **[Developer Guide](DeveloperGuide.md)** - Comprehensive guide for new developers
  - Project overview and architecture
  - Development setup and workflow
  - Core components and platform support
  - Best practices and testing guidelines
  - Deployment and contributing guidelines

- **[Quick Reference](QuickReference.md)** - Fast reference for common tasks
  - Build commands for Windows and macOS
  - Common development patterns
  - Debugging commands and techniques
  - File locations and troubleshooting

- **[Architecture Diagrams](ArchitectureDiagrams.md)** - Visual system overview
  - System architecture diagrams
  - Component relationships
  - Class hierarchies
  - Data flow and platform differences

### 🎥 Specialized Documentation

- **[Video System Documentation](VideoSystemDocumentation.md)** - Video processing system
  - FFmpeg integration details
  - Camera capture and processing
  - Video pipeline architecture
  - Platform-specific video handling

## Getting Started

### For New Developers

1. **Start with the [Developer Guide](DeveloperGuide.md)**
   - Read the project overview and architecture sections
   - Follow the development setup instructions
   - Understand the core components

2. **Use the [Quick Reference](QuickReference.md)**
   - Keep this open while developing
   - Reference build commands and common patterns
   - Use for troubleshooting

3. **Review [Architecture Diagrams](ArchitectureDiagrams.md)**
   - Understand the system structure visually
   - Reference component relationships
   - Learn about data flow

### For Specific Tasks

- **Setting up development environment**: See [Developer Guide - Development Setup](DeveloperGuide.md#development-setup)
- **Adding new UI components**: See [Quick Reference - Common Development Tasks](QuickReference.md#common-development-tasks)
- **Debugging issues**: See [Developer Guide - Common Issues and Solutions](DeveloperGuide.md#common-issues-and-solutions)
- **Video system work**: See [Video System Documentation](VideoSystemDocumentation.md)
- **Platform-specific development**: See [Developer Guide - Platform Support](DeveloperGuide.md#platform-support)

## Documentation Standards

### Writing New Documentation

When adding new documentation:

1. **Follow the existing structure** and formatting
2. **Include code examples** where appropriate
3. **Use clear, concise language**
4. **Add diagrams** for complex concepts
5. **Update this README** when adding new files

### Documentation Maintenance

- Keep documentation up to date with code changes
- Review and update annually
- Add examples for new features
- Remove outdated information

## Quick Links

### Essential Commands

```bash
# Windows Development
dotnet build "FPVTrackside - Core.sln"
cd FPVTracksideCore && dotnet run

# macOS Development
dotnet build "FPVMacside - Core.sln"
cd FPVMacSideCore && dotnet run --project FPVMacsideCore.csproj
```

### Key Files

- **Main Entry (Windows)**: `FPVTracksideCore/Program.cs`
- **Main Entry (macOS)**: `FPVMacSideCore/Program.cs`
- **Base Game**: `UI/BaseGame.cs`
- **Game Engine**: `Compositor/LayerStackGame.cs`
- **Platform Tools**: `Compositor/PlatformTools.cs`

### Important Directories

- **Application Data**: `~/Documents/FPVTrackside/` (macOS) or `%USERPROFILE%\Documents\FPVTrackside\` (Windows)
- **Logs**: `/log/` directory in application data
- **Themes**: `/themes/` directory in application data
- **Pilots**: `/pilots/` directory in application data

## Contributing to Documentation

### How to Contribute

1. **Identify gaps** in existing documentation
2. **Create or update** documentation files
3. **Follow the established format** and style
4. **Include practical examples** and code snippets
5. **Test your documentation** by following the instructions

### Documentation Review Process

1. **Self-review** your changes
2. **Test the instructions** you've written
3. **Get feedback** from other developers
4. **Update based on feedback**
5. **Submit for review**

## Support and Resources

### Community Resources

- **Discord Server**: [FPVTrackside Discord](https://discord.com/invite/V2x6dCs)
- **Project Website**: [FPVTrackside.com](https://fpvtrackside.com/)
- **GitHub Repository**: Project source code and issues

### Getting Help

1. **Check the documentation** first
2. **Search existing issues** on GitHub
3. **Ask in Discord** for community help
4. **Create an issue** for bugs or feature requests

### Reporting Documentation Issues

When reporting documentation issues:

- **Specify the file** and section
- **Describe the problem** clearly
- **Suggest improvements** if possible
- **Include screenshots** for visual issues

---

## Documentation Version

This documentation is maintained for FPVTrackside Core project and is updated regularly to reflect the current state of the codebase.

**Last Updated**: August 2025  
**Version**: 1.0  
**Compatible with**: .NET 6.0, MonoGame/XNA

For questions about this documentation, please contact the development team or create an issue on GitHub. 