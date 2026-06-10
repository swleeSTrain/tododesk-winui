# Contributing

Thanks for considering a contribution to TodoDesk.

## Development Setup

1. Install the .NET SDK 10.
2. Make sure the WinUI 3 / Windows App SDK templates and build tools are available.
3. Build the app:

```powershell
dotnet build .\TodoApp\TodoApp.csproj -p:Platform=x64
```

4. Run the app:

```powershell
dotnet run --project .\TodoApp\TodoApp.csproj -p:Platform=x64
```

## Pull Requests

- Keep changes focused.
- Prefer native WinUI controls for Fluent UI changes.
- Keep custom visual effects scoped to custom themes.
- Include a short description of the user-visible change.
- Mention the platform and SDK version used for verification.

## Coding Style

- Use nullable-aware C#.
- Prefer existing project patterns over new abstractions.
- Keep XAML resources scoped to the surface that needs them.
- Avoid committing generated build output, local screenshots, certificates, or user-specific files.

## Reporting Issues

When filing an issue, include:

- Windows version
- .NET SDK version
- CPU architecture
- Steps to reproduce
- Expected and actual behavior
- Screenshots if the issue is visual
