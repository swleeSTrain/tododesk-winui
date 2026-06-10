# TodoDesk

TodoDesk is a WinUI 3 desktop issue board for Windows. It started as a todo app and now uses an issue-style workflow with list and board views, local JSON persistence, and multiple visual themes.

The Fluent theme uses native WinUI controls. The glass, XP-inspired, and Aero-inspired themes are custom visual experiments.

## Features

- Issue list with status, priority, owner, due date, project, and labels
- Board view grouped by workflow status
- Local persistence with `ApplicationData.Current.LocalFolder`
- Native WinUI Fluent mode
- Custom experimental themes: Glass, XP-inspired, and Aero-inspired
- Win2D/Composition-backed glass distortion effects for custom themes

## Requirements

- Windows 10 version 1809 or later
- .NET SDK 10
- Windows App SDK workload/templates available for WinUI 3 development

This project targets:

```text
net10.0-windows10.0.26100.0
Microsoft.WindowsAppSDK 2.1.3
Microsoft.Graphics.Win2D 1.4.0
```

## Build

From the repository root:

```powershell
dotnet build .\TodoApp\TodoApp.csproj -p:Platform=x64
```

Other supported platforms from the project file are `x86`, `x64`, and `ARM64`.

## Run

```powershell
dotnet run --project .\TodoApp\TodoApp.csproj -p:Platform=x64
```

The app stores issue data as `issues.json` in the app's local application data folder.

## Project Layout

```text
TodoApp/
  App.xaml                 App resources and theme dictionaries
  MainWindow.xaml          Window shell and title bar host
  MainPage.xaml            Main UI, native Fluent root, and custom theme root
  VisualThemeManager.cs    Theme definitions and resource palette switching
  LiquidBackdropEffect.cs  Win2D/Composition glass effect support
  Assets/                  App icons and package assets
```

## Theming Notes

TodoDesk keeps two UI surfaces:

- `FluentNativeRoot`: native WinUI controls for the default Fluent experience
- `CustomThemeRoot`: custom visual treatments for Glass, XP-inspired, and Aero-inspired themes

The custom themes are original, theme-inspired treatments. They are not affiliated with Microsoft, Apple, or any other platform vendor.

## License

MIT. See [LICENSE](LICENSE).
