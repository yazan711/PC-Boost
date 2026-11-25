# PC Boost — Source Code Handover Guide

Clean, buildable `.NET 8` (`net8.0-windows`) solution for **PC Boost (`v2.0.0`)** including automated test suite (`CleanRamBoosterApp.Tests`), Inno Setup 6 installer script, and one-command release pipeline (`build-release.ps1`).

---

## Project Specifications

- **Solution:** `PCBoost.sln`
  - **Main App:** `CleanRamBoosterApp/CleanRamBoosterApp.csproj` (`net8.0-windows`, `WinExe`, zero external NuGet dependencies)
  - **Automated Tests:** `CleanRamBoosterApp.Tests/CleanRamBoosterApp.Tests.csproj` (`net8.0-windows`, zero external NuGet dependencies — 100% offline runnable)
- **UI architecture:** Hybrid **WPF + Windows Forms** (`<UseWPF>true</UseWPF>`, `<UseWindowsForms>true</UseWindowsForms>`, `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`)
  - `<Using Remove="System.Windows.Forms" />` and `<Using Remove="System.Drawing" />` are enabled in the `.csproj` to prevent implicit namespace clashes with WPF.
- **Unsafe code & nullable:** `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (used for native DXGI 64-bit GPU VRAM enumeration), `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
- **Elevation manifest:** `CleanRamBoosterApp/app.manifest` (`requestedExecutionLevel level="requireAdministrator"`)
- **Bundled assets:** `CleanRamBoosterApp/Resources/app.ico` and `CleanRamBoosterApp/Resources/app_icon.png` (`CopyToOutputDirectory = PreserveNewest`)

---

## Directory Structure (`CleanRamBoosterApp/`)

- **`Program.cs`** — Entry point (`[STAThread] Main`), single-instance mutex (`Global\CleanRamBooster_SingleInstance_Mutex_99F`), full crash & exception handlers (`WpfApp.DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`, `Application.ThreadException`, `AppDomain.UnhandledException`), and CLI flags (`--tray` / `--minimized`, `--replace-pid`, `--disable-startup`, `--toggle-startup`, `--delete-startup`, `--set-admin-startup`). Zero hardcoded machine paths.
- **`WpfUI/`** — Primary WPF application UI:
  - `WpfMainWindow.cs` — Main WPF shell, sidebar, RAM gauge/tray icon, Auto-Clean loop, Startup Guard alert queue.
  - Views: `DashboardView.cs`, `TurboView.cs` (with 1-click System Restore Point), `CareView.cs` (determinate deep scan, optional System Restore Point, HTML export), `StartupView.cs` + `StartupView.Filtering.cs` + `StartupView.Actions.cs` (with 1-click Undo history & inline UAC elevation), `ToolboxView.cs` (modeless multi-window launcher), `SettingsView.cs` (CPU cores/threads, separate dGPU/iGPU + live 3s DXGI/WMI VRAM, Open Logs & Export Diagnostics).
  - Dialogs & Controls: `AppDialog.cs`, `StartupAlertWindow.cs`, `WpfTheme.cs` (rounded theme-aware button styles including dark/light disabled states), `WpfIcons.cs`, `CircularGaugeControl.cs`, `MetricCardControl.cs`, `SegmentedRamBarControl.cs`, `StatusPillControl.cs`.
- **`Forms/`** — Modeless WinForms Power User Toolbox windows:
  - `ToolboxEmptyFoldersForm.cs`, `ToolboxDuplicateFinderForm.cs`, `ToolboxLargeFilesForm.cs` (all three use `VirtualMode = true` + unlimited scanning + background deletion with zero UI freeze).
  - `ToolboxSystemInfoForm.cs`, `ToolboxWinFixForm.cs`, `ToolboxAutoShutdownForm.cs`.
- **`UI/`** — WinForms theme & helpers used by Toolbox forms (`UITheme.cs`, `DoubleBufferedCardPanel.cs`, `VectorIcons.cs`).
- **`Services/`** — Native Windows system & utility services:
  - `HardwareInfoService.cs` + `HardwareInfoService.Gpu.cs` + `HardwareInfoService.Reports.cs`
  - `StartupService.cs` + `StartupService.Operations.cs`, `StartupMonitorService.cs`, `StartupHistoryService.cs`
  - `MemoryOptimizationService.cs`, `TurboBoostService.cs`, `WinServiceManager.cs`, `AutoStartService.cs`
  - `CareService.cs`, `DiskToolsService.cs`, `WinFixService.cs`, `SystemRestoreService.cs`
  - `AdminPrivilegeService.cs`, `DiagnosticLogger.cs`, `SettingsService.cs` (atomic `.tmp` + `.bak` writes), `LocalizationService.cs`, `MuiHelper.cs`.
- **`Models/`** — `CareScanResult.cs`, `StartupItem.cs`, `TurboBoostItem.cs`.
- **`Installer/PCBoostSetup.iss`** — Inno Setup 6 installer script (English + Arabic wizard, `PrivilegesRequired=admin`, optional Authenticode `SignTool` support, uninstall cleanup of scheduled tasks and HKCU `Run` entries).

---

## Build, Test & Release Pipeline

Run from the workspace root:

1. **One-command full build, test, portable bundle, source export & `setup.exe`:**
   ```powershell
   .\build-release.ps1
   ```
   *(Optional Authenticode signing: `.\build-release.ps1 -CertPath "path\to\cert.pfx" -CertPassword "..."` or set `$env:SIGN_CERT_PFX` / `$env:SIGN_CERT_PASSWORD`.)*

2. **Build & run automated tests manually:**
   ```powershell
   dotnet build PCBoost.sln -c Release
   dotnet run --project CleanRamBoosterApp.Tests\CleanRamBoosterApp.Tests.csproj -c Release --no-build
   ```
