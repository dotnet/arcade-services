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
- `docs/` - Documentation
- `eng/` - Engineering scripts, build tools and pipeline templates
- `src/` - Contains the main source code for arcade services
- `test/` - Test projects and test utilities
- `Microsoft.DotNet.Darc/` - Darc CLI tool for interacting with dependency management services

## Key Technologies
- .NET 8
- Azure DevOps APIs
- ASP.NET Core for web APIs
- Entity Framework Core for data access
- Azure Container Apps for hosting
- .NET Aspire for local development

## Development Environment
- You can restore required .NET and dependencies by calling:
  - `./eng/common/build.sh -restore` on Linux/macOS
  - `.\eng\common\build.ps1 -restore` on Windows
