# RebootWatch

A lightweight Windows system tray utility that shows your last boot time and what caused the reboot — at a glance, without opening Event Viewer.

## Features

- **Tray icon** color-coded by reboot type (green = planned, yellow = unexpected, red = BSOD)
- **Hover tooltip** with boot time, uptime, and cause
- **Click popup** with scrollable reboot history (last 10 events)
- **Reboot classification** — Windows Update, user restart, power loss, BSOD, software install
- **Auto-start** toggle via right-click menu
- **Optional toast notification** on login

## Build

```powershell
dotnet publish src/RebootWatch -r win-x64 --self-contained -p:PublishSingleFile=true -c Release
```

## Requirements

- .NET 8 SDK (build only — published EXE is self-contained)
- Windows 10/11 x64
- No admin rights required
