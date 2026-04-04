# RebootWatch — Product Requirements Document

## Overview

RebootWatch is a lightweight Windows system tray utility that displays the last boot time and classifies what caused the most recent reboot (Windows Update, user-initiated, power loss, BSOD, etc.). It provides at-a-glance system restart history without needing to dig through Event Viewer.

**Nothing like this exists today.** PowerToys, BGInfo, and Task Manager each show partial info (uptime or system details) but none combine boot time + reboot cause in a tray icon with history.

## Target Platform

- Windows 10/11 (x64)
- No admin rights required (System Event Log is readable by standard users)

## Tech Stack

- **Language:** C# (.NET 8+)
- **UI Framework:** WPF (for popup window) + `System.Windows.Forms.NotifyIcon` (for tray icon)
- **Event Log Access:** `System.Diagnostics.Eventing.Reader`
- **Boot Time:** WMI via `System.Management` (`Win32_OperatingSystem.LastBootUpTime`)
- **Publish:** Single-file, self-contained executable (no runtime dependency for end user)

## Features

### P0 — Must Have

#### 1. System Tray Icon
- Persistent icon in the Windows notification area
- Color-coded by last reboot type:
  - **Green:** Normal/planned reboot (update, user-initiated)
  - **Yellow:** Unexpected shutdown (Event 6008 / Kernel-Power 41 without BugCheck)
  - **Red:** BSOD/GSOD crash (Kernel-Power 41 + BugCheck 1001)

#### 2. Hover Tooltip
- On mouse hover, show a concise summary:
  ```
  Last boot: Apr 4, 02:34 (4h 32m ago)
  Cause: Windows Update (OS Upgrade)
  ```

#### 3. Click Popup — Reboot History
- Left-click the tray icon to open a small WPF popup window
- Shows the current session info and a scrollable list of recent reboots (last 10)
- Each entry shows: timestamp, classified cause, and an icon/badge for the type
- Layout:
  ```
  ┌─────────────────────────────────────────┐
  │  RebootWatch                        ✕   │
  ├─────────────────────────────────────────┤
  │  ● Current Session                      │
  │    Booted: 2026-04-04 02:34             │
  │    Uptime: 4h 32m                       │
  │    Cause: Windows Update (Planned)      │
  ├─────────────────────────────────────────┤
  │  Recent History                         │
  │  ┌────────────┬─────────────────────┐   │
  │  │ Apr 4 02:34 │ Windows Update     │   │
  │  │ Apr 2 03:50 │ Windows Update     │   │
  │  │ Mar 24 20:14│ ⚠ Power Loss      │   │
  │  │ Mar 22 04:34│ Windows Update     │   │
  │  │ Mar 19 04:04│ Windows Update     │   │
  │  └────────────┴─────────────────────┘   │
  └─────────────────────────────────────────┘
  ```

#### 4. Reboot Cause Classification Engine
- Parse Windows Event Log (System channel) to classify each reboot:

| Event ID | Source / Process | Classification |
|----------|-----------------|----------------|
| 1074 | `TrustedInstaller.exe` | **Windows Update** |
| 1074 | `MoUsoCoreWorker.exe` | **Windows Update** |
| 1074 | `shutdown.exe` (user account) | **User Shutdown/Restart** |
| 1074 | `setup.exe` or other process (user account) | **Software Install** |
| 1074 | SYSTEM, reason "Operating System: Upgrade" | **Windows Update** |
| 1074 | SYSTEM, reason "Service pack" | **Windows Update** |
| 6008 | EventLog | **Unexpected Shutdown** (dirty) |
| 41 | Microsoft-Windows-Kernel-Power | **Power Loss / Crash** |
| 41 + 1001 (WER BugCheck) | Kernel-Power + WER | **BSOD/GSOD** |
| 6009 with no preceding 1074/6008 | EventLog | **Normal Boot** (cold start) |

- When Event 41 occurs without a corresponding BugCheck (Event 1001), classify as "Power Loss" rather than "BSOD"
- Parse the `Reason Code` and `Shut-down Type` fields from Event 1074 for additional detail

#### 5. Right-Click Context Menu
- **Refresh** — Re-query event log
- **Start with Windows** — Toggle auto-start (adds/removes `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` entry)
- **Exit** — Close the app

### P1 — Should Have

#### 6. Login Toast Notification
- On app startup (i.e., login), show a Windows toast notification:
  ```
  RebootWatch
  Last restart: Windows Update (Planned) at 02:34
  ```
- Configurable: can be turned off in context menu

#### 7. Dark/Light Mode
- Follow the Windows system theme (`AppsUseLightTheme` registry key)
- Popup window and context menu should respect the current theme

### P2 — Nice to Have

#### 8. Export History
- Right-click menu option to export reboot history to CSV or JSON

#### 9. Live Uptime Counter
- Tooltip uptime value updates periodically (every 60s) without requiring hover-off/hover-on

## Architecture

```
RebootWatch/
├── RebootWatch.sln
├── src/
│   └── RebootWatch/
│       ├── RebootWatch.csproj
│       ├── Program.cs                      — Entry point, single-instance check
│       ├── App.xaml / App.xaml.cs           — WPF application host
│       ├── TrayApplicationContext.cs        — NotifyIcon lifecycle, context menu, tooltip
│       ├── Services/
│       │   ├── RebootClassifier.cs          — Event Log query + classification logic
│       │   ├── BootInfoProvider.cs           — WMI last boot time + uptime calculation
│       │   └── StartupManager.cs            — Registry auto-start toggle
│       ├── Models/
│       │   └── RebootEvent.cs               — Data model for a classified reboot
│       ├── Views/
│       │   ├── HistoryPopup.xaml             — Popup window XAML
│       │   └── HistoryPopup.xaml.cs          — Code-behind / view model
│       └── Assets/
│           ├── icon-green.ico
│           ├── icon-yellow.ico
│           └── icon-red.ico
└── README.md
```

## Key Design Decisions

1. **NotifyIcon from WinForms, not WPF** — WPF has no native tray icon support. Use `System.Windows.Forms.NotifyIcon` hosted in a WPF app. This is the standard pattern.
2. **No background service** — The app runs as a standard user process. It queries the Event Log on startup and on-demand (Refresh). No polling loop needed for reboot data.
3. **Single-instance** — Use a named mutex to prevent multiple instances.
4. **No admin required** — The System Event Log is readable by all users. No elevation needed.
5. **Self-contained publish** — `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` for a single EXE with no .NET install dependency.

## Non-Goals

- This is not a system monitor (no CPU/RAM/disk stats)
- No remote machine support — local machine only
- No Windows service component
- No installer — single EXE, user manages placement

## Success Criteria

- Tray icon appears on login and correctly shows boot time on hover
- Reboot cause is correctly classified for all common scenarios (update, user, power loss, BSOD)
- Popup shows last 10 reboots with accurate timestamps and classifications
- App uses < 20MB RAM and < 1% CPU at idle
- Single EXE, no install dependencies
