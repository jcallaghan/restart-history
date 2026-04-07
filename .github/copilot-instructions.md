# Restart History — Copilot Instructions

## Project Overview
Windows system tray utility showing restart history, causes, and patterns with optional AI insights via GitHub Copilot SDK. Built with .NET 8, WPF + WinForms hybrid.

## Tech Stack
- **Runtime:** .NET 8.0, C#, target `net8.0-windows10.0.17763.0`
- **UI:** WPF (flyout popup) + WinForms `NotifyIcon` (tray icon — WPF has no native tray support)
- **AI:** GitHub.Copilot.SDK v0.2.1 (Copilot CLI stdio mode)
- **Notifications:** Microsoft.Toolkit.Uwp.Notifications
- **System info:** System.Management (WMI), System.Diagnostics.Eventing.Reader (Event Log)

## Project Layout
```
src/RestartHistory/
├── Models/RestartEvent.cs          # RestartCause enum, RestartSeverity enum, display properties
├── Services/
│   ├── RestartClassifier.cs        # Event Log queries, restart cause classification
│   ├── BootInfoProvider.cs         # WMI boot time, uptime formatting
│   ├── CopilotInsightsService.cs   # Copilot SDK: caching, streaming, background pre-fetch
│   └── StartupManager.cs           # "Start with Windows" registry toggle
├── Views/
│   ├── HistoryPopup.xaml           # Main flyout UI (dashboard + summary pages)
│   └── HistoryPopup.xaml.cs        # Navigation, activity selection, history highlighting
├── TrayApplicationContext.cs       # Tray icon, context menu, settings, Copilot init
├── Program.cs                      # Entry point, single-instance mutex
└── App.xaml                        # WPF application
```

## Architecture Patterns
- Fresh popup created per tray click (not reused) — avoids stale state
- Copilot analysis runs in background on startup + refresh; results cached in memory
- Per-event explanations pre-fetched for all non-green items after summaries complete
- Settings persisted as JSON at `%LOCALAPPDATA%\RestartHistory\settings.json`
- Single instance enforced via `Global\RestartHistory_SingleInstance_Mutex`
- Popup repositions on `SystemEvents.DisplaySettingsChanged` (dock/undock support)

## Conventions
- **Terminology:** Use "restart" not "reboot" in all user-facing text
- **Labels:** "Blue Screen / Crash" not "BSOD", "User Restart" not "User Shutdown/Restart"
- **UI style:** Windows 11 clipboard menu aesthetic — dark theme, rounded corners, Segoe Fluent Icons
- **Icon sizes:** Stat tile icons use `FontSize="18"` with `Height="20"` for consistency
- **Cursor:** Green/success history items show arrow cursor (not clickable), yellow/red show hand
- **No over-engineering:** Single project, no abstractions for one-off operations

## Build & Run
```powershell
dotnet build RestartHistory.sln
dotnet run --project src/RestartHistory/RestartHistory.csproj
dotnet publish src/RestartHistory -r win-x64 --self-contained -p:PublishSingleFile=true -c Release -o ./publish
```

## Event Classification Reference
| Source | Event ID | Classification |
|--------|----------|----------------|
| EventLog | 6005 | Service started (boot marker) |
| EventLog | 6006 | Clean shutdown |
| EventLog | 6008 | Unexpected shutdown |
| Kernel-Power | 41 | Power loss / crash |
| Kernel-General | 12 | System startup |
| WindowsUpdateClient | 19/20 | Windows Update restart |
| USER32 | 1074 | User-initiated restart |
| BugCheck | 1001 | Blue screen with stop code |
