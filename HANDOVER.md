# PC Boost — Source Code Handover Guide

Clean, buildable `.NET 8` (`net8.0-windows`) solution for **PC Boost (`v2.0.0`)** featuring a **100% pure-WPF UI** (main shell + all 6 Toolbox windows), automated offline test suite (`CleanRamBoosterApp.Tests`), Authenticode code-signing tooling (`scripts/Setup-CodeSigning.ps1`), Inno Setup 6 installer script, and one-command release pipeline (`build-release.ps1`).

---

## Project Specifications

- **Solution:** `PCBoost.sln`
  - **Main App:** `CleanRamBoosterApp/CleanRamBoosterApp.csproj` (`net8.0-windows`, `WinExe`, zero external NuGet dependencies)
  - **Automated Tests:** `CleanRamBoosterApp.Tests/CleanRamBoosterApp.Tests.csproj` (`net8.0-windows`, zero external NuGet dependencies — 100% offline runnable)
- **UI architecture:** Pure **WPF** for all windows, views, dialogs, and Toolbox tools (`<UseWindowsForms>true</UseWindowsForms>` is retained solely for Windows' built-in system tray `NotifyIcon` without third-party NuGet dependencies).
- **Unsafe code & nullable:** `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (native DXGI 64-bit GPU VRAM enumeration), `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
- **Elevation manifest:** `CleanRamBoosterApp/app.manifest` (`requestedExecutionLevel level="requireAdministrator"`)

---

## Directory Structure (`CleanRamBoosterApp/`)

- **`Program.cs`** — Entry point (`[STAThread] Main`), single-instance mutex, full crash & exception handlers (`WpfApp.DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`, `Application.ThreadException`, `AppDomain.UnhandledException`), and CLI flags (`--tray`, `--replace-pid`, `--disable-startup`, `--toggle-startup`, `--delete-startup`, `--set-admin-startup`).
- **`WpfUI/`** — Primary WPF application UI:
  - `WpfMainWindow.cs` — Main WPF shell, sidebar, RAM gauge/tray icon, Auto-Clean loop, Startup Guard alert queue.
  - Views: `DashboardView.cs`, `TurboView.cs` (with 1-click System Restore Point), `CareView.cs` (determinate deep scan, optional System Restore Point, HTML export), `StartupView.cs` + `StartupView.Filtering.cs` + `StartupView.Actions.cs` (with 1-click Undo history & inline UAC elevation), `ToolboxView.cs` (modeless multi-window launcher with live theme refresh), `SettingsView.cs` (CPU cores/threads, separate dGPU/iGPU + live 3s DXGI/WMI VRAM, Open Logs & Export Diagnostics).
  - **`WpfUI/Toolbox/`** — All 6 pure-WPF modeless Power User Toolbox windows (all inheriting `ToolboxWindowBase.cs` with live Dark/Light DWM theming & Arabic RTL support):
    - `ToolboxSystemInfoWindow.cs` (8 hardware categories, live WPF CPU LED meter + oscilloscope waveform, Physical Memory Slots visual bar, HTML/Text export)
    - `ToolboxWinFixWindow.cs` (1-click Windows repairs, automatic System Restore Point & registry backup rollback)
    - `ToolboxAutoShutdownWindow.cs` (Countdown / Specific Date & Time / Daily power scheduler via native `powrprof.dll` & `shutdown.exe`)
    - `ToolboxEmptyFoldersWindow.cs`, `ToolboxDuplicateFinderWindow.cs`, `ToolboxLargeFilesWindow.cs` (unlimited scans, head/tail MD5 deduplication, WPF virtualized `DataGrid`, async background deletion, zero UI freeze).
  - Dialogs & Controls: `AppDialog.cs`, `StartupAlertWindow.cs`, `WpfTheme.cs`, `WpfIcons.cs`, `CircularGaugeControl.cs`, `MetricCardControl.cs`, `SegmentedRamBarControl.cs`, `StatusPillControl.cs`.
- **`Services/`** — Native Windows system & utility services (`HardwareInfoService*.cs`, `StartupService*.cs`, `StartupMonitorService.cs`, `StartupHistoryService.cs`, `MemoryOptimizationService.cs`, `TurboBoostService.cs`, `WinServiceManager.cs`, `AutoStartService.cs`, `CareService.cs`, `DiskToolsService.cs`, `WinFixService.cs`, `SystemRestoreService.cs`, `AdminPrivilegeService.cs`, `DiagnosticLogger.cs`, `SettingsService.cs`, `LocalizationService.cs`, `MuiHelper.cs`).
- **`Models/`** — `CareScanResult.cs`, `StartupItem.cs`, `TurboBoostItem.cs`.
- **`Installer/PCBoostSetup.iss`** — Inno Setup 6 installer script (English + Arabic wizard, `PrivilegesRequired=admin`, uninstall cleanup of scheduled tasks and HKCU `Run` entries).
- **`scripts/Setup-CodeSigning.ps1`** — Generates/manages a SHA-256 Authenticode code-signing certificate in gitignored `certs/` for local testing or CI.

---

## Build, Code Signing & Release Pipeline

1. **Generate a local SHA-256 code-signing certificate (optional, once per dev machine):**
   ```powershell
   .\scripts\Setup-CodeSigning.ps1
   # Optional: trust the local cert for your Windows user only:
   .\scripts\Setup-CodeSigning.ps1 -TrustCurrentUser
   ```

2. **One-command build, test, sign, portable bundle, source export & `setup.exe`:**
   ```powershell
   .\build-release.ps1
   ```
   - Automatically uses `signtool.exe` (Windows SDK) if installed, or falls back to PowerShell `Set-AuthenticodeSignature` (SHA-256).
   - For **public distribution without Windows SmartScreen warnings**, pass your commercial OV/EV code-signing certificate:
     ```powershell
     .\build-release.ps1 -CertPath "C:\path\to\commercial-cert.pfx" -CertPassword "your-password"
     ```
   - Or pass `-NoSign` for an unsigned build.

3. **Build & run automated tests manually:**
   ```powershell
   dotnet build PCBoost.sln -c Release
   dotnet run --project CleanRamBoosterApp.Tests\CleanRamBoosterApp.Tests.csproj -c Release --no-build
   ```
