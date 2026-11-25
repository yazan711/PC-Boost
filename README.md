# CleanRam Suite - Advanced PC Care & Memory Optimizer (Zero-Bloat Edition)

A high-performance Windows optimization suite modeled directly after **IObit Advanced SystemCare**, but engineered to be **100% free of marketing popups, adware bundles, upselling, background telemetry, or driver bloat**.

---

## 🚀 Advanced SystemCare Parity & Enhancements

### 1. 🛡️ 1-Click Care (Expandable App & File Tree)
- **Hierarchical Dropdown / TreeView View:**
  - **Category Roots:** 🔒 Privacy Sweep, 🧹 Junk File Clean, ⚡ System Optimization.
  - **Application Groups:** Google Chrome, Microsoft Edge, Mozilla Firefox, Windows Explorer & Recent Items, User Temp (%TEMP%), System Temp & Logs, Windows Prefetch, SoftwareDistribution Update Cache, Memory Crash Dumps, and Recycle Bin.
  - **File Breakdown:** Expandable sub-items detailing exact file paths, item counts, and size in KB/MB/GB (e.g. `Web & Image Cache - 145 MB (1,200 files)`, `Cookies & Site Storage - 2.1 MB`).
  - **Granular Checkboxes:** Check or uncheck entire applications or individual sub-items with real-time recalculation of total reclaimable megabytes.
  - **One-Click Cleaning:** Safe deletion of selected files and instant application of system responsiveness tweaks.

### 2. 🔄 Startup & Next-Reboot Manager
- **ASC-Style Friendly Display Names:** Queries `FileVersionInfo.FileDescription` and `ProductName` to display clean branded names (e.g., `Microsoft OneDrive`, `Discord`, `Spotify`, `Steam Client Bootstrapper`, `NVIDIA Settings`) rather than raw command strings.
- **Boot Impact Calculation:** Identifies `High Impact`, `Medium Impact`, and `Low Impact` items based on resource weight.
- **Smart Recommendations:** `🛡️ Necessary`, `✅ Safe to Disable`, `⚠️ Orphaned / Missing`, `Optional`.
- **Comprehensive View Filters:**
  - `All Items`
  - `Enabled Only`
  - `Disabled Only`
  - `Startup Programs` (Registry & Startup folders)
  - `Windows Services` (Automatic services)
  - `Scheduled Tasks` (Boot & Logon scheduled tasks)
  - `Safe to Disable` (Safe startup accelerators)
- **Actions:** 1-Click Toggle Status (or double-click row), Permanent Delete, and "Open File Location in Explorer".

### 3. 🚀 Turbo Boost Mode (3 Menus / Categories)
- **Profiles:**
  - **💼 Work & Productivity Mode:** Preserves network drives, office suites, and Print Spooler; halts background updaters and third-party media launchers.
  - **🎮 Extreme Gaming Mode:** Halts Windows Search indexer, print queues, telemetry, and non-essential background processes for maximum FPS.
- **3 Dedicated Menus / Tabs:**
  1. **⚙️ System Services:** Windows optional services (`WSearch`, `Spooler`, `DiagTrack`, `SysMain`, `DPS`, `TrkWks`, `WerSvc`, `TabletInputService`, `RemoteRegistry`, `XboxServices`, `Fax`, etc.).
  2. **🏢 Non-Windows Services:** Third-party background services dynamically identified via executable inspection (Google Update, Edge Update, Adobe, Steam, Epic, Razer, Corsair, Logitech, AnyDesk, TeamViewer, etc.).
  3. **🎮 Background Apps:** Running user-mode apps and tray programs (Discord, Spotify, Steam, OneDrive, torrent clients, gaming overlays).
- **Search & Bulk Controls:** Real-time search per menu, Select All, Deselect All, and reversible service restoration on Turn Off.

### 4. 🧰 Power User Toolbox
- **🔧 Win Fix (20 ASC Repairs):**
  - **Desktop & Explorer:** Rebuild Icon & Thumbnail Cache, Restore Missing Desktop Icons (This PC, Recycle Bin, Control Panel, Network), Restart Windows Explorer, Fix Frozen Taskbar & Start Menu, Reset Windows Search Indexer.
  - **Network & Internet:** Flush DNS Resolver Cache, Reset Winsock Catalog, Reset TCP/IP Stack, Reset Proxy Hijacks, Release & Renew IP Lease, Restore Default Firewall.
  - **System & Security Policies:** Enable Task Manager (policy unlock), Enable Registry Editor (regedit unlock), Enable Command Prompt (CMD unlock), Reset Print Spooler Queue, Reset Windows Update Components, Repair Windows Installer (MSI).
  - **File Associations:** Fix `.EXE`, `.LNK`, `.BAT`, `.REG`, and `.TXT` execution handlers.
- **🔍 Duplicate File Finder:** Safe directory traversal (skips reparse points & junction loops), asynchronous background scanning with live progress bar and file counters, MD5 hash verification, and batch duplicate deletion.
- **📦 Large File Finder:** Safe asynchronous drive scanner (supports `C:\`, `D:\`, or custom folders without permission crashes on `System Volume Information`), live progress bar, file size filters (>50MB, >100MB, >500MB, >1GB, >5GB), and "Open in Explorer".
- **⏰ Auto Shutdown Scheduler:**
  - **Mode 1:** ⏱️ Duration Countdown (Hours + Minutes).
  - **Mode 2:** 📅 Specific Date & Time (Calendar Date Picker + Time Picker).
  - **Mode 3:** 🔄 Daily Schedule at specific hour/minute.
  - **Actions:** Shut Down, Restart, Sleep, Hibernate, Lock Workstation.
- **📁 Empty Folder Scanner:** Recursive directory scrubber for orphaned empty directory trees.
- **🖥️ System Information:** Real-time hardware and kernel specs.
- **⚙️ System Control (God Mode):** Instant master shortcut to hidden Windows administrative consoles.

---

## 📁 Ready-To-Run Distribution

- **Main Application:** [`ReadyToRun_App/CleanRamBoosterApp.exe`](file:///C:/Users/Yazan/Desktop/Reverce%20Engginer/ReadyToRun_App/CleanRamBoosterApp.exe)
- **Delphi VCL 32-Bit Source:** [`Delphi_Source/`](file:///C:/Users/Yazan/Desktop/Reverce%20Engginer/Delphi_Source/)
- **C# .NET 8 Source:** [`CleanRamBoosterApp/`](file:///C:/Users/Yazan/Desktop/Reverce%20Engginer/CleanRamBoosterApp/)
