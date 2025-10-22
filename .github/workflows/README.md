# FPV Trackside Core - GitHub Actions Pipeline

This directory contains the GitHub Actions workflows for the FPV Trackside Core project. The pipeline is designed to provide comprehensive CI/CD coverage for all branches with different levels of testing and building.

## Workflow Overview

### 🔄 Continuous Integration (`ci.yml`)
**Triggers:** All pushes and pull requests on all branches
**Purpose:** Fast feedback for developers

- **Quick Build Check**: Verifies both macOS and Windows projects compile successfully
- **Code Quality**: Checks formatting, common issues, and code analysis
- **Dependency Check**: Scans for outdated and vulnerable packages
- **Branch Info**: Provides context about the current branch and changes

### 🧪 Build Test (`build-test.yml`)
**Triggers:** All pushes and pull requests on all branches
**Purpose:** Comprehensive testing and validation

- **Test Builds**: Builds both Debug and Release configurations for macOS and Windows
- **Test Execution**: Runs any existing test projects
- **Linting**: Code formatting verification
- **Security Scan**: Dependency vulnerability scanning

### 🚀 Build and Release (`build-and-release.yml`)
**Triggers:** All pushes and pull requests on all branches, with special handling for releases
**Purpose:** Full builds and release management

- **Smart Branch Detection**: Differentiates between release branches (master, main, release/*) and feature branches
- **Full Builds**: Creates complete macOS app bundles and Windows executables
- **Conditional Artifacts**: 
  - Release branches: Full DMG/ZIP packages with proper naming
  - Feature branches: Simple ZIP packages with branch-specific naming
- **Release Creation**: Automatically creates GitHub releases for release branches and tags

### 🌙 Nightly Build and Test (`nightly.yml`)
**Triggers:** Daily at 2 AM UTC, manual dispatch
**Purpose:** Comprehensive overnight testing and analysis

- **Full Builds**: Complete macOS and Windows builds with timestamps
- **Comprehensive Testing**: Runs all tests with detailed reporting
- **Code Coverage**: Analyzes test coverage for both projects
- **Performance Testing**: Build performance analysis
- **Security Audit**: Comprehensive security scanning

## Branch Strategy

### Release Branches
- `master` / `main`: Primary release branches
- `release/*`: Release candidate branches
- **Behavior**: Full builds, DMG creation, automatic releases

### Feature Branches
- All other branches (feature/, hotfix/, etc.)
- **Behavior**: Simple builds, ZIP artifacts, no automatic releases

## Artifact Naming Convention

### Release Branches
- macOS: `FPV-Trackside-Core-macOS-v{version}.zip` / `.dmg`
- Windows: `FPV-Trackside-Core-Windows-v{version}.zip`

### Feature Branches
- macOS: `FPV-Trackside-Core-macOS-{branch}-v{version}.zip`
- Windows: `FPV-Trackside-Core-Windows-{branch}-v{version}.zip`

### Nightly Builds
- macOS: `FPV-Trackside-Core-macOS-nightly-{branch}-{timestamp}.zip`
- Windows: `FPV-Trackside-Core-Windows-nightly-{branch}-{timestamp}.zip`

## Project Structure

The pipeline handles two main projects:

### macOS Project (`FPVMacSideCore/`)
- **Solution**: `FPVTrackside.sln`
- **Main Project**: `FPVMacsideCore.csproj`
- **Target**: Apple Silicon (ARM64)
- **Output**: macOS App Bundle + DMG

### Windows Project (`FPVTracksideCore/`)
- **Solution**: `FPVTracksideCore.sln`
- **Main Project**: `FPVTracksideCore.csproj`
- **Target**: Windows x64
- **Output**: Windows Executable + ZIP

## Shared Libraries

Both projects share common libraries:
- `Compositor/`: Graphics composition engine
- `RaceLib/`: Core racing logic
- `Sound/`: Audio processing
- `Timing/`: Timing system integration
- `UI/`: User interface components
- `DB/`: Database layer
- `Tools/`: Utility functions
- `ExternalData/`: External data integration
- `ImageServer/`: Image processing server
- `ffmpegMediaPlatform/`: FFmpeg integration

## Environment Variables

- `DOTNET_VERSION`: Set to '6.0.x' for all workflows
- All workflows use the latest .NET 6.0 SDK

## Manual Triggers

### Workflow Dispatch
All workflows support manual triggering via GitHub Actions UI:

1. **Build and Release**: 
   - `create_release`: Boolean to force release creation
   - `target_branch`: Optional branch specification

2. **Build Test**: No additional parameters

3. **Nightly**: No additional parameters

## Monitoring and Debugging

### Build Status
- ✅ **Green**: All checks passed
- ⚠️ **Yellow**: Some non-critical checks failed (formatting, warnings)
- ❌ **Red**: Critical failures (compilation errors, test failures)

### Common Issues
1. **Formatting Issues**: Run `dotnet format` locally
2. **Dependency Issues**: Check for outdated or vulnerable packages
3. **Build Failures**: Verify .NET 6.0 compatibility
4. **Test Failures**: Check test project configurations

### Logs and Artifacts
- All builds produce downloadable artifacts
- Detailed logs available in GitHub Actions UI
- Nightly builds include comprehensive analysis reports

## Performance Considerations

- **CI**: Fast feedback (< 10 minutes)
- **Build Test**: Comprehensive validation (< 15 minutes)
- **Build and Release**: Full builds (~30-60 minutes)
- **Nightly**: Complete analysis (~2 hours)

## Security Features

- Dependency vulnerability scanning
- License compliance checking
- Code quality analysis
- Security audit in nightly builds

## Contributing

When contributing to the project:

1. **Feature Branches**: Create from main/master
2. **Testing**: Ensure all CI checks pass
3. **Formatting**: Run `dotnet format` before committing
4. **Documentation**: Update relevant documentation

## Support

For pipeline issues:
1. Check the GitHub Actions logs
2. Verify .NET 6.0 compatibility
3. Review the workflow configurations
4. Contact the development team

---

**Last Updated**: December 2024
**Pipeline Version**: 2.0
**Supported Platforms**: macOS (Apple Silicon), Windows (x64)