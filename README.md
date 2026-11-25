# PC Boost — Windows Optimization & System Utility Suite

A fast, native, zero-bloat Windows 10/11 optimization and power-user suite built on **.NET 8** and **pure WPF** with **zero third-party NuGet dependencies**. Designed for real system-level control without marketing popups, telemetry, adware, or bundled drivers.

![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D6)
![Framework](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4)
![Languages](https://img.shields.io/badge/UI-English%20%26%20Arabic%20(RTL)-10B981)
![Built With](https://img.shields.io/badge/Built%20with-AI%20Gemini-8E75B2)
![License](https://img.shields.io/badge/License-MIT-blue)

> Built using **AI Gemini** (Google Gemini).

---

## Key Features

### 1. Memory & Performance Center (RAM & Speed)
- Real-time physical memory gauge, segmented RAM usage bar, and instant **Boost & Optimize RAM** action.
- Native Win32 working-set trimming (`EmptyWorkingSet`) and Standby List purging (`NtSetSystemInformation`).
- Configurable **Background Auto-Clean** trigger threshold and system tray quick actions.

### 2. Turbo Boost Mode
- Switch between **Gaming & Heavy Apps** and **Work & Productivity** profiles.
- Safely stops selected non-essential Windows services, third-party services, and background apps — and restores stopped services cleanly when turned off.
- Built-in 1-click **System Restore Point** creation before making changes.

### 3. Complete PC Care & Cleanup
- 12-step determinate **Deep Scan** across multi-profile Chrome, Edge, Brave, Firefox, Windows Explorer recent items, User/System Temp, Prefetch, Windows Update cache, Delivery Optimization, D3D Shader Cache, WER crash dumps, actual Recycle Bin (`SHQueryRecycleBinW`), and real registry responsiveness tweaks.
- Expandable per-category sub-items with exact paths, file counts, and sizes.
- Optional **Create System Restore Point before cleaning** checkbox and **HTML Report Export**.

### 4. Startup & Next-Reboot Manager + Live Startup Guard
- Scans Registry Run/RunOnce (HKCU/HKLM/WOW6432Node), User & Common Startup Folders, Automatic Windows Services, Scheduled Tasks (Task Scheduler 2.0 COM), and Microsoft Store background apps.
- Smart safety recommendations (`Necessary`, `Optional`, `Safe to Disable`, `Orphaned`), Quick Optimization tab, Ignored list, inline UAC elevation, and **1-Click Undo History**.
- Real-time **Startup Guard** notifications whenever a new desktop or Microsoft Store app registers itself at boot.

### 5. Pure-WPF Power User Toolbox (Modeless Multi-Window)
Every utility opens as an independent modeless WPF window with live Dark/Light theme syncing and full Arabic RTL layout:
- **System Information:** 8 hardware categories, live 1-second CPU LED meter & oscilloscope clock waveform, Physical Memory Slots visual cards bar, separated **Dedicated GPU (dGPU)** vs **Integrated GPU (iGPU)** detection, 64-bit DXGI + WMI live VRAM telemetry, and HTML/Text export.
- **Win Fix (20 1-Click Repairs):** Desktop/Explorer, Network/Winsock/DNS, System/Security policies, and File Associations — backed by automatic System Restore Points and `.reg` snapshot rollback.
- **Duplicate File Finder:** Unlimited non-blocking scan with exact size + head/tail MD5 hashing, virtualized grid, Smart Auto-Select (keep 1 original per group), and async deletion.
- **Large File Finder:** Unlimited drive/folder scanner with size filters, virtualized grid, and smart handling for kernel-locked `hiberfil.sys` and `pagefile.sys`.
- **Empty Folder Scanner:** Unlimited recursive bottom-up empty folder detection and pruning with zero UI freeze.
- **Auto Shutdown Scheduler:** Countdown Timer, Specific Date & Time, and Daily Schedule for Shut Down, Restart, Sleep, Hibernate, or Lock Workstation.
- Quick launchers for **Default Programs** and **Windows God Mode**.

### 6. Settings, Hardware Overview & Diagnostics
- **Quick Hardware Overview:** CPU physical cores & logical threads, separated dGPU / iGPU, live 3-second VRAM usage & shared memory, installed RAM, OS version, and uptime.
- Full **Dark & Light Mode**, **English & Arabic (RTL)** language switching, elevated boot task configuration, atomic `settings.json` persistence, rotating `%AppData%\CleanRamBooster\app.log`, **Open Logs Folder**, and **Export Diagnostics Bundle**.

---

## Building from Source

### Prerequisites
- Windows 10 or Windows 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- *(Optional, for building `PC-Boost-Setup.exe`)* [Inno Setup 6](https://jrsoftware.org/isinfo.php)

### Quick Build & Test
```powershell
# Build solution (Release)
dotnet build PCBoost.sln -c Release

# Run automated offline test suite (no NuGet packages required)
dotnet run --project CleanRamBoosterApp.Tests\CleanRamBoosterApp.Tests.csproj -c Release --no-build
```

### Full Release Packaging (Portable + `PC-Boost-Setup.exe` + Optional Authenticode Signing)
```powershell
# Unsigned release build (app + tests + portable zip + PC-Boost-Setup.exe)
.\build-release.ps1 -NoSign

# Signed release build with a code-signing certificate (.pfx)
.\build-release.ps1 -CertPath "path\to\certificate.pfx" -CertPassword "your-password"
```
See [HANDOVER.md](HANDOVER.md) for detailed architecture notes and local signing setup (`scripts/Setup-CodeSigning.ps1`).

---

## Project Structure

```text
├── PCBoost.sln                       # Visual Studio / dotnet solution
├── build-release.ps1                 # End-to-end build, test, sign & packaging script
├── scripts/Setup-CodeSigning.ps1     # Local SHA-256 Authenticode cert helper
├── CleanRamBoosterApp/               # Main .NET 8 WPF application
│   ├── WpfUI/                        # Main shell, views, themes, icons, dialogs
│   │   └── Toolbox/                  # 6 pure-WPF Power User Toolbox windows
│   ├── Services/                     # Native Win32/DXGI/WMI/COM services
│   ├── Models/                       # Data models
│   ├── Installer/PCBoostSetup.iss    # Inno Setup 6 installer script (EN + AR)
│   └── Resources/                    # Application icons
└── CleanRamBoosterApp.Tests/         # Zero-dependency automated test suite
```

## License

This project is licensed under the [MIT License](LICENSE).
