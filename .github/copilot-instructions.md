# .NET Arcade Services

The .NET Arcade Services repository contains the Product Construction Service (previously Maestro) and DARC CLI tool for managing .NET dependency flow. ALWAYS reference these instructions first and only fallback to additional search and context gathering if the information here is incomplete or found to be in error.

## Working Effectively

### Prerequisites and Environment Setup
- Install .NET 8 SDK (the build script will download the specific required version automatically)
- For full local development: Install Docker Desktop (required for Product Construction Service)
- For Windows development: Install Visual Studio with Azure Development and ASP.NET workloads
- Configure git for long paths: `git config --global core.longpaths true`

### Build Commands (NEVER CANCEL - Use Long Timeouts)
Bootstrap and build the repository:
- Restore dependencies: `./eng/common/build.sh --restore` (Linux/macOS) or `Build.cmd -restore` (Windows)
  - NEVER CANCEL: Takes ~60 seconds on first run. Set timeout to 240+ seconds.
- Build: `./eng/common/build.sh --build` (Linux/macOS) or `Build.cmd -build` (Windows)  
  - NEVER CANCEL: Takes ~40 seconds after restore. Set timeout to 180+ seconds.
- Full build with restore: `./build.sh` (Linux/macOS) or `Build.cmd` (Windows)
  - NEVER CANCEL: Takes ~90 seconds total. Set timeout to 300+ seconds.

### Test Execution
Run unit tests: `./.dotnet/dotnet test --no-build`
- NEVER CANCEL: Takes ~70 seconds for unit tests, but codeflow tests can take much longer. Set timeout to 600+ seconds (10+ minutes).
- Note: Some tests may fail in non-Windows environments due to path differences
- Standard `./eng/common/build.sh --test` does NOT work - use dotnet test directly

### Key Projects and Tools
- **Product Construction Service (PCS)**: Main dependency flow service
  - `src/ProductConstructionService/` - API, web UI, workers
  - Local development: `dotnet run --project src/ProductConstructionService/ProductConstructionService.AppHost`
  - Requires Docker Desktop and proper Aspire configuration
- **DARC CLI**: Command-line tool for dependency management
  - `src/Microsoft.DotNet.Darc/Darc/`  
  - Run with: `./.dotnet/dotnet run --project src/Microsoft.DotNet.Darc/Darc -- --help`
- **Maestro (Legacy)**: `src/Maestro/` - Being migrated to PCS

## Validation and Testing

### Always Run These Steps Before Committing
1. Build successfully: `./build.sh` or `Build.cmd`
2. Run unit tests (expect some environment-specific failures): `./.dotnet/dotnet test --no-build`
3. Verify DARC tool works: `./.dotnet/dotnet run --project src/Microsoft.DotNet.Darc/Darc --no-build -- --help`

### Manual Validation Scenarios
After making changes, validate by testing these workflows:
- **DARC CLI**: Run `darc --help` and verify command list appears correctly
- **Build System**: Ensure `./build.sh` completes without new errors
- **Product Construction Service**: If Docker is available, verify service starts without errors

### Expected Build Failures and Limitations
- Some unit tests fail in non-Windows environments due to path handling differences (normal)
- Product Construction Service requires Docker Desktop which may not work in all environments
- Integration tests require specific Azure and GitHub authentication setup (skip in development)

## Coding Guidelines
- Rules apply to our C# code.
- Use function-reflective, descriptive names without implementation details.
- Avoid magic strings and numbers; use constants or configuration.
- For multi-task methods, use descriptive names and document the process.
- Long method names are acceptable.
- Prefer async/await over Task.Result or .Wait()
- Async methods must have an "Async" suffix.
- Use cancellation tokens for async operations when appropriate
- Avoid using booleans solely to signal success, unless failure is an expected frequent outcome (e.g. TryGet methods).
- Do not return null unless explicitly indicated (e.g., TryCreate).
- Validate early; if validation fails, throw or return immediately.
- After validation, use if/else—not returns—for control flow.
- Keep methods focused; split them if too large.
- Use immutable objects (e.g., records, readonly properties) when possible.
- Never throw generic Exceptions.
- Use structural logging consistently.
- Each method should log its own actions.
- Use nullable reference types and annotate appropriately.
- Follow the AAA (Arrange-Act-Assert) pattern for unit tests.
- Prefer ILogger<T> over ILogger for better context in logs.
- Services should be registered with appropriate lifetimes (singleton, scoped, transient).

## Repository Structure
- `docs/` - Documentation (DevGuide.md, scenarios.md)
- `eng/` - Engineering scripts, build tools and pipeline templates
- `src/` - Contains the main source code for arcade services
  - `ProductConstructionService/` - Current dependency flow service
  - `Microsoft.DotNet.Darc/` - DARC CLI tool
  - `Maestro/` - Shared service libraries (former version of the PCS service)
- `test/` - Test projects and test utilities
- `.github/` - GitHub workflows and templates

## Key Technologies
- .NET 8 (see global.json for exact version)
- Azure DevOps APIs
- ASP.NET Core for web APIs
- Entity Framework Core for data access
- Azure Container Apps for hosting
- .NET Aspire for local development orchestration
- Redis for caching
- Docker for local development

## Common File Locations
- Main solution: `arcade-services.sln`
- Global configuration: `global.json`, `Directory.Build.props`
- Build configuration: `eng/` folder
- API controllers: `src/ProductConstructionService/ProductConstructionService.Api/Api/`
- Data models: `src/Maestro/Maestro.Data/Models/`

## Development Tips
- Use the project-specific .NET SDK: `./.dotnet/dotnet` instead of global `dotnet`
- Check `docs/DevGuide.md` for detailed local development setup
- For Entity Framework operations, see instructions in DevGuide.md
- Build artifacts go to `artifacts/` folder (excluded from git)
- Clean build artifacts: `./eng/common/build.sh --clean`
- The repository uses Arcade SDK build system - leverage existing build targets

## Common Tasks Reference

### Repository Root Contents
Key files you'll encounter:
```
arcade-services.sln       # Main solution file
global.json              # .NET SDK version requirements  
Directory.Build.props    # MSBuild properties for all projects
build.sh / Build.cmd     # Entry point build scripts
docs/DevGuide.md        # Comprehensive development guide
src/                    # Source code
├── ProductConstructionService/  # Current main service
├── Microsoft.DotNet.Darc/      # CLI tool
└── Maestro/                    # Shared service libraries (former version of the PCS service)
test/                   # Test projects
eng/common/             # Arcade build scripts
```

### Expected Command Outputs
When build succeeds, you'll see output like:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:39.97
```

When DARC runs successfully:
```
Microsoft.DotNet.Darc 0.0.99-dev
© Microsoft Corporation. All rights reserved.
[command help output]
```
