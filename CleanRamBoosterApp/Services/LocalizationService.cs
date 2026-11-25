using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CleanRamBoosterApp.Services
{
    public enum AppLanguage
    {
        English,
        Arabic
    }

    public static class LocalizationService
    {
        public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.English;
        public static bool IsArabic => CurrentLanguage == AppLanguage.Arabic;

        public static event Action? OnLanguageChanged;

        private static string? _englishFontFamily;
        private static string? _arabicFontFamily;

        // Cache for created fonts to prevent GDI handle leaks
        private static readonly Dictionary<string, Font> _fontCache = new();

        static LocalizationService()
        {
            InitializeFontFamilies();
            string savedLang = SettingsService.Instance.Current.Language;
            if (string.Equals(savedLang, "ar", StringComparison.OrdinalIgnoreCase))
            {
                CurrentLanguage = AppLanguage.Arabic;
            }
            else
            {
                CurrentLanguage = AppLanguage.English;
            }
        }

        private static void InitializeFontFamilies()
        {
            var installedFamilies = FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // English font priority: Segoe UI Variable Text (clean optical strokes) -> Segoe UI -> Inter -> Arial
            string[] preferredEnglish = { "Segoe UI Variable Text", "Segoe UI", "Inter", "SF Pro Display", "Arial" };
            foreach (var name in preferredEnglish)
            {
                if (installedFamilies.Contains(name))
                {
                    _englishFontFamily = name;
                    break;
                }
            }
            _englishFontFamily ??= "Segoe UI";

            // Arabic font priority: IBM Plex Sans Arabic (crisp balanced weight) -> Cairo -> Tajawal -> Segoe UI -> Tahoma
            string[] preferredArabic = { "IBM Plex Sans Arabic", "Cairo", "Tajawal", "Dubai", "Segoe UI", "Tahoma" };
            foreach (var name in preferredArabic)
            {
                if (installedFamilies.Contains(name))
                {
                    _arabicFontFamily = name;
                    break;
                }
            }
            _arabicFontFamily ??= "Segoe UI";
        }

        public static string ActiveFontFamilyName => IsArabic ? _arabicFontFamily! : _englishFontFamily!;

        public static System.Windows.Media.FontFamily ActiveWpfFontFamily => IsArabic
            ? new System.Windows.Media.FontFamily("IBM Plex Sans Arabic, Cairo, Segoe UI Variable Text, Segoe UI")
            : new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, Inter, Tahoma");

        public static Font GetFont(float size, FontStyle style = FontStyle.Regular)
        {
            string family = ActiveFontFamilyName;
            float targetSize = size;

            string cacheKey = $"{family}_{targetSize}_{style}";
            if (_fontCache.TryGetValue(cacheKey, out var cachedFont))
            {
                return cachedFont;
            }

            try
            {
                var font = new Font(family, targetSize, style);
                _fontCache[cacheKey] = font;
                return font;
            }
            catch
            {
                var fallback = new Font("Segoe UI", targetSize, style);
                _fontCache[cacheKey] = fallback;
                return fallback;
            }
        }

        public static void SetLanguage(AppLanguage language)
        {
            if (CurrentLanguage == language) return;

            CurrentLanguage = language;
            SettingsService.Instance.Update(s => s.Language = language == AppLanguage.Arabic ? "ar" : "en");
            OnLanguageChanged?.Invoke();
        }

        public static string T(string key, string? defaultEnglish = null)
        {
            if (IsArabic && ArabicStrings.TryGetValue(key, out var arVal))
            {
                return arVal;
            }
            if (EnglishStrings.TryGetValue(key, out var enVal))
            {
                return enVal;
            }
            return defaultEnglish ?? key;
        }

        // ====================================================================
        // LOCALIZATION DICTIONARIES
        // ====================================================================
        private static readonly Dictionary<string, string> EnglishStrings = new()
        {
            // App Shell & Navigation
            ["AppTitle"] = "PC Boost",
            ["NavRam"] = "⚡  RAM & Speed",
            ["NavTurbo"] = "🚀  Turbo Boost",
            ["NavCare"] = "🛡️  Care & Cleanup",
            ["NavStartup"] = "🔄  Startup Manager",
            ["NavToolbox"] = "🧰  Toolbox",
            ["NavSettings"] = "⚙️  Settings & Info",
            ["AdminActive"] = "🛡️ Administrator Active",
            ["StandardUser"] = "⚠️ Standard User (Limited)",
            ["RamUsageTitle"] = "RAM USAGE",
            ["ThemeDark"] = "🌙 Dark Theme",
            ["ThemeLight"] = "☀️ Light Theme",
            ["SwitchLangButton"] = "🌐 العربية",

            // Dashboard
            ["DashHeader"] = "⚡ Memory & Performance Center",
            ["DashSub"] = "Real-time physical working set monitoring with intelligent zero-bloat RAM purging.",
            ["MetricInUse"] = "IN-USE RAM",
            ["MetricAvailable"] = "AVAILABLE RAM",
            ["StatusInUse"] = "In-Use",
            ["StatusAvailable"] = "Available",
            ["BarLegend"] = "■ In-Use (Blue)     ■ Available RAM (Emerald)",
            ["BtnBoostRam"] = "🚀 BOOST & OPTIMIZE RAM",
            ["AutoCleanEnable"] = "Enable Background Auto-Clean",
            ["AutoCleanTrigger"] = "Trigger when RAM exceeds:",
            ["KeepInTray"] = "Keep in Tray on Close",
            ["LogTitle"] = "Live Activity & Optimization Log",
            ["BtnCopy"] = "📋 Copy",
            ["BtnClear"] = "🗑️ Clear",
            ["StatusOptimal"] = "OPTIMAL STATE",
            ["StatusWarning"] = "WARNING: HIGH LOAD",
            ["StatusCritical"] = "CRITICAL: RAM LOW",
            ["StatusTotalRam"] = "Total RAM",
            ["DashTurboTitle"] = "Turbo Boost:",
            ["DashTurboStateOn"] = "ON",
            ["DashTurboStateOff"] = "OFF",
            ["DashTurboStateIgnored"] = "Ignored",
            ["DashTurboDescOff"] = "Stop unnecessary programs to release RAM and boost your PC.",
            ["DashTurboDescOn"] = "{0} active • freeing background apps & services.",
            ["DashTurboDescIgnored"] = "Turbo Boost prompt is currently ignored.",
            ["DashTurboTurnOn"] = "Turn On",
            ["DashTurboTurnOff"] = "Turn Off",
            ["DashTurboGaming"] = "Gaming & Heavy Apps",
            ["DashTurboWork"] = "Work & Productivity",
            ["DashTurboOpenFull"] = "Open Turbo Boost View...",
            ["DashStartupTitle"] = "Startup Optimizer",
            ["DashStartupOptimize"] = "Optimize",
            ["DashStartupIgnoredDesc"] = "Startup optimization prompt is ignored.",
            ["DashStartupOptimalDesc"] = "Startup items are optimized for fast boot.",
            ["DashIgnore"] = "Ignore",
            ["DashUnignore"] = "Unignore",

            // Turbo Boost
            ["TurboHeader"] = "🚀 Turbo Boost Mode",
            ["TurboSub"] = "Temporarily stop non-essential services and background apps to free CPU & RAM for heavy tasks.",
            ["TurboGamingTitle"] = "🎮 Gaming & Heavy Apps",
            ["TurboGamingSub"] = "Stops all non-essential Windows & 3rd-party services to prioritize GPU and low latency.",
            ["TurboWorkTitle"] = "💼 Work & Productivity",
            ["TurboWorkSub"] = "Keeps print/network/office services running while disabling background telemetry bloat.",
            ["TurboBtnTurnOn"] = "🚀 TURN ON TURBO BOOST",
            ["TurboBtnTurnOff"] = "⏹️ RESTORE NORMAL STATE",
            ["TurboMenuSys"] = "⚙️ System Services",
            ["TurboMenuNonWin"] = "🏢 Non-Windows",
            ["TurboMenuApps"] = "🎮 Background Apps",
            ["TurboColName"] = "Process / Service Name",
            ["TurboColType"] = "Type",
            ["TurboColRam"] = "RAM Usage",
            ["TurboColStatus"] = "Status",
            ["BtnSelectAll"] = "Select All",
            ["BtnDeselectAll"] = "Deselect All",

            // Care & Cleanup
            ["CareHeader"] = "🛡️ Complete PC Care & Cleanup",
            ["CareSub"] = "Deep clean junk files, temporary caches, registry remnants, and boost system responsiveness.",
            ["CareBtnScan"] = "🔍 START DEEP SCAN",
            ["CareBtnClean"] = "✨ CLEAN & REPAIR SELECTED",
            ["CareBtnRescan"] = "🔄 Rescan All",
            ["CareSummaryCleaned"] = "Optimization Complete: {0} freed across {1} categories.",

            // Startup Manager
            ["StartupHeader"] = "🔄 Startup & Next-Reboot Manager",
            ["StartupSub"] = "Detect and manage programs, services, scheduled tasks, and Microsoft Store background apps at Windows boot.",
            ["StartupViewFilter"] = "View Filter:",
            ["StartupSearchPrompt"] = "Search...",
            ["StartupBtnRefresh"] = "🔄 Refresh",
            ["StartupBtnToggle"] = "⚡ Toggle",
            ["StartupBtnLocation"] = "📂 Location",
            ["StartupBtnDelete"] = "🗑️ Delete",
            ["StartupBtnDisableAllStore"] = "🚫 Disable All to Never",
            ["StartupTabAll"] = "All",
            ["StartupTabEnabled"] = "Enabled",
            ["StartupTabDisabled"] = "Disabled",
            ["StartupTabApps"] = "Apps",
            ["StartupTabServices"] = "Services",
            ["StartupTabTasks"] = "Tasks",
            ["StartupTabStoreApps"] = "Microsoft Store Apps",
            ["StartupTabSafe"] = "Safe",
            ["StartupTabQuick"] = "Quick Optimization",
            ["StartupTabUnknown"] = "Unknown",
            ["StartupTabIgnored"] = "Ignored",
            ["StartupTagIgnored"] = "Ignored",
            ["StartupBtnOptimizeQuick"] = "Optimize",
            ["StartupColName"] = "Program / Service Name",
            ["StartupColPublisher"] = "Publisher",
            ["StartupColStatus"] = "Status",
            ["StartupColImpact"] = "Boot Impact",
            ["StartupColRec"] = "Recommendation",
            ["StartupColLocation"] = "Startup Type",
            ["StartupColAction"] = "Operation",
            ["StartupOpIgnore"] = "Ignore",
            ["StartupOpUnignore"] = "Unignore",
            ["StartupBtnMarkKnown"] = "Mark Known",
            ["StartupBtnOptimizeSafe"] = "Optimize Safe",

            // Toolbox
            ["ToolboxHeader"] = "🧰 All-In-One Power User Toolbox",
            ["ToolboxSub"] = "Handy suite of native repair tools, storage analyzers, and hidden Windows power controls.",
            ["ToolWinFixTitle"] = "🔧 Win Fix",
            ["ToolWinFixDesc"] = "1-Click repair for broken desktop icons, flush DNS & winsock, and restart Windows Explorer.",
            ["ToolSysInfoTitle"] = "🖥️ System Information",
            ["ToolSysInfoDesc"] = "Hardware specs: CPU model/cores/clock, GPU, RAM, Motherboard, BIOS, Disks, and Uptime.",
            ["ToolAutoShutdownTitle"] = "⏰ Auto Shutdown",
            ["ToolAutoShutdownDesc"] = "Schedule automatic PC shutdown, restart, sleep, or hibernate with countdown timer.",
            ["ToolDuplicateTitle"] = "🔍 Duplicate File Finder",
            ["ToolDuplicateDesc"] = "Scan directory trees and eliminate identical duplicate files by size and MD5 hash.",
            ["ToolLargeFilesTitle"] = "📦 Large File Finder",
            ["ToolLargeFilesDesc"] = "Identify and locate storage space hogs (>100MB, >500MB, >1GB) in seconds.",
            ["ToolEmptyFoldersTitle"] = "📁 Empty Folder Scanner",
            ["ToolEmptyFoldersDesc"] = "Recursively search directory trees and clean abandoned empty folder structures.",
            ["ToolDefaultAppsTitle"] = "🌐 Default Programs",
            ["ToolDefaultAppsDesc"] = "Quick access to Windows default apps and protocol associations settings.",
            ["ToolGodModeTitle"] = "⚙️ System Control (God Mode)",
            ["ToolGodModeDesc"] = "Direct master access to hidden Windows settings and God Mode control panel.",
            ["TagRepair"] = "REPAIR",
            ["TagSystem"] = "SYSTEM",
            ["TagStorage"] = "STORAGE",
            ["TagWindows"] = "WINDOWS",
            ["BtnOpen"] = "Open",

            // Settings & Info
            ["SettingsHeader"] = "⚙️ Application Settings & System Specs",
            ["SettingsSub"] = "Configure PC Boost behavior, background automation, and system integration.",
            ["SettingStartWindows"] = "Start automatically with Windows boot (in System Tray)",
            ["SettingRunAsAdminOnBoot"] = "Run as Administrator on next reboot (no UAC prompt)",
            ["SettingMinimizeTray"] = "Minimize to System Tray on close",
            ["SettingDisableRamNotif"] = "Disable notifications when RAM is cleaned",
            ["SettingStartupAlert"] = "Alert when a new desktop app or Microsoft Store app is added to startup",
            ["SettingTrayNotify"] = "Show balloon notifications on Auto-Clean",

            // New Startup Item Alert Popup
            ["AlertNewStartupTitle"] = "A new startup item detected",
            ["AlertNewStartupSub"] = "It can be disabled to reduce your PC's boot time.",
            ["AlertNewStoreStartupSub"] = "This Microsoft Store app can run in the background at startup. Disable to set it to Never.",
            ["AlertDetailsShow"] = "Details",
            ["AlertDetailsHide"] = "Hide Details",
            ["AlertDisableNow"] = "Disable Now",
            ["AlertIgnoreItem"] = "Ignore this startup item",
            ["AlertDetailAutoRun"] = "Auto Run Program:",
            ["AlertDetailStorePath"] = "App File / Path:",
            ["AlertDetailInstallFolder"] = "Install Folder:",
            ["AlertDetailPublisher"] = "Publisher:",
            ["AlertDetailSource"] = "Startup Source:",
            ["AlertDetailCommand"] = "Launch Command:",
            ["AlertDetailPackage"] = "Package ID:",
            ["AlertCopyPathTip"] = "Copy path",
            ["AlertOpenSettingsTip"] = "Startup Alert Settings",
            ["AlertDetailTaskName"] = "Task Name:",
            ["AlertElevateDisablePrompt"] = "Administrator permission is required to disable \"{0}\".\n\nElevate via Windows Administrator prompt (UAC) to disable it now?",
            ["AlertDisableFailed"] = "Could not disable \"{0}\". Please run PC Boost as Administrator for system startup entries.",
            ["SettingDarkTheme"] = "Dark Theme Mode",
            ["SettingLangLabel"] = "Application Language:",
            ["SettingHardwareTitle"] = "🖥️ Quick Hardware Overview",
            ["BtnExportHtml"] = "📄 Export Full HTML Report",
            ["BtnRestartApp"] = "🔄 Restart PC Boost",
            ["BtnOpenLogs"] = "Open Logs Folder",
            ["BtnExportDiagnostics"] = "Export Diagnostics",
            ["CareCreateRestorePoint"] = "Create Windows System Restore Point before cleaning",
            ["BtnExportShort"] = "Export",
            ["BtnRestorePoint"] = "Restore Point",
            ["StartupBtnUndo"] = "Undo",

            // System Tray Menu
            ["TrayOpen"] = "Open PC Boost",
            ["TrayQuickBoost"] = "⚡ Quick Boost RAM",
            ["TrayFlushStandby"] = "🧹 Flush Standby Cache",
            ["TrayToggleTurbo"] = "🚀 Turbo Boost (Toggle)",
            ["TrayExit"] = "Exit Completely",

            // Common
            ["BtnClose"] = "Close",
            ["BtnCancel"] = "Cancel",
            ["BtnApply"] = "Apply",
            ["BtnSave"] = "Save",
            ["BtnExport"] = "Export Report...",
            ["BtnCopySpecs"] = "📋 Copy Specs",
            ["BtnViewDetails"] = "🔍 View Detailed Specs"
        };

        private static readonly Dictionary<string, string> ArabicStrings = new()
        {
            // App Shell & Navigation
            ["AppTitle"] = "PC Boost",
            ["NavRam"] = "⚡  الذاكرة والسرعة",
            ["NavTurbo"] = "🚀  الوضع الفائق",
            ["NavCare"] = "🛡️  العناية والتنظيف",
            ["NavStartup"] = "🔄  إدارة بدء التشغيل",
            ["NavToolbox"] = "🧰  صندوق الأدوات",
            ["NavSettings"] = "⚙️  الإعدادات والمعلومات",
            ["AdminActive"] = "🛡️ مسؤول النظام (صلاحيات كاملة)",
            ["StandardUser"] = "⚠️ مستخدم عادي (صلاحيات محدودة)",
            ["RamUsageTitle"] = "استهلاك الذاكرة",
            ["ThemeDark"] = "🌙 الوضع الداكن",
            ["ThemeLight"] = "☀️ الوضع الفاتح",
            ["SwitchLangButton"] = "🌐 English",

            // Dashboard
            ["DashHeader"] = "⚡ مركز الذاكرة والأداء",
            ["DashSub"] = "مراقبة الذاكرة الفعلية في الوقت الفعلي مع تفريغ ذكي فوري وبدون أعباء.",
            ["MetricInUse"] = "الذاكرة المستخدمة",
            ["MetricAvailable"] = "الذاكرة المتاحة",
            ["StatusInUse"] = "المستخدمة",
            ["StatusAvailable"] = "المتاحة",
            ["StatusTotalRam"] = "إجمالي الرام",
            ["BarLegend"] = "■ المستخدمة (أزرق)     ■ الذاكرة المتاحة (أخضر)",
            ["BtnBoostRam"] = "🚀 تسريع وتحسين الذاكرة",
            ["AutoCleanEnable"] = "تفعيل التنظيف التلقائي في الخلفية",
            ["AutoCleanTrigger"] = "عند تجاوز الذاكرة:",
            ["KeepInTray"] = "تصغير لشريط المهام",
            ["LogTitle"] = "سجل النشاط والتحسين المباشر",
            ["BtnCopy"] = "📋 نسخ",
            ["BtnClear"] = "🗑️ مسح",
            ["StatusOptimal"] = "حالة ممتازة",
            ["StatusWarning"] = "تحذير: الذاكرة مرتفعة",
            ["StatusCritical"] = "حالة حرجة: الذاكرة منخفضة",
            ["DashTurboTitle"] = "الوضع الفائق:",
            ["DashTurboStateOn"] = "مفعّل",
            ["DashTurboStateOff"] = "متوقف",
            ["DashTurboStateIgnored"] = "متجاهل",
            ["DashTurboDescOff"] = "إيقاف البرامج غير الضرورية لتحرير الذاكرة وتسريع الجهاز.",
            ["DashTurboDescOn"] = "{0} نشط • تحرير الذاكرة والخدمات الخلفية.",
            ["DashTurboDescIgnored"] = "تم تجاهل تنبيه الوضع الفائق حالياً.",
            ["DashTurboTurnOn"] = "تشغيل",
            ["DashTurboTurnOff"] = "إيقاف",
            ["DashTurboGaming"] = "وضع الألعاب والبرامج الثقيلة",
            ["DashTurboWork"] = "وضع العمل والإنتاجية",
            ["DashTurboOpenFull"] = "فتح صفحة الوضع الفائق...",
            ["DashStartupTitle"] = "محسّن بدء التشغيل",
            ["DashStartupOptimize"] = "تحسين",
            ["DashStartupIgnoredDesc"] = "تم تجاهل فحص بدء التشغيل حالياً.",
            ["DashStartupOptimalDesc"] = "عناصر بدء التشغيل محسّنة لإقلاع أسرع.",
            ["DashIgnore"] = "تجاهل",
            ["DashUnignore"] = "إلغاء التجاهل",

            // Turbo Boost
            ["TurboHeader"] = "🚀 وضع التسريع الفائق للأداء",
            ["TurboSub"] = "إيقاف الخدمات والتطبيقات غير الضرورية مؤقتاً لتحرير المعالج والذاكرة للألعاب والمهام الثقيلة.",
            ["TurboGamingTitle"] = "🎮 وضع الألعاب والبرامج الثقيلة",
            ["TurboGamingSub"] = "إيقاف جميع الخدمات غير الأساسية لتركيز كرت الشاشة وتقليل زمن التأخير.",
            ["TurboWorkTitle"] = "💼 وضع العمل والإنتاجية",
            ["TurboWorkSub"] = "الحفاظ على خدمات الطباعة والشبكة وأوفيس مع إيقاف برامج التتبع في الخلفية.",
            ["TurboBtnTurnOn"] = "🚀 تشغيل الوضع الفائق الآن",
            ["TurboBtnTurnOff"] = "⏹️ إيقاف الوضع الفائق واستعادة الخدمات",
            ["TurboMenuSys"] = "⚙️ خدمات النظام",
            ["TurboMenuNonWin"] = "🏢 غير تابعة لويندوز",
            ["TurboMenuApps"] = "🎮 تطبيقات الخلفية",
            ["TurboColName"] = "اسم العملية / الخدمة",
            ["TurboColType"] = "النوع",
            ["TurboColRam"] = "استهلاك الذاكرة",
            ["TurboColStatus"] = "الحالة",
            ["BtnSelectAll"] = "تحديد الكل",
            ["BtnDeselectAll"] = "إلغاء التحديد",

            // Care & Cleanup
            ["CareHeader"] = "🛡️ الفحص الشامل والعناية بنظام ويندوز",
            ["CareSub"] = "تنظيف عميق للملفات الزائدة والمؤقتة ومخلفات الريجستري لتحسين استجابة النظام.",
            ["CareBtnScan"] = "🔍 بدء الفحص الشامل",
            ["CareBtnClean"] = "✨ تنظيف وإصلاح المحدد",
            ["CareBtnRescan"] = "🔄 إعادة الفحص",
            ["CareSummaryCleaned"] = "اكتملت العملية: تم تحرير {0} بنجاح عبر {1} فئات.",

            // Startup Manager
            ["StartupHeader"] = "🔄 إدارة برامج وبدء تشغيل النظام",
            ["StartupSub"] = "اكتشاف والتحكم في البرامج والخدمات والمهام وتطبيقات متجر مايكروسوفت في الخلفية عند إقلاع ويندوز.",
            ["StartupViewFilter"] = "تصفية العرض:",
            ["StartupSearchPrompt"] = "بحث في البرامج...",
            ["StartupBtnRefresh"] = "🔄 تحديث",
            ["StartupBtnToggle"] = "⚡ تبديل",
            ["StartupBtnLocation"] = "📂 المسار",
            ["StartupBtnDelete"] = "🗑️ حذف",
            ["StartupBtnDisableAllStore"] = "🚫 تعطيل الكل (أبداً)",
            ["StartupTabAll"] = "الكل",
            ["StartupTabEnabled"] = "المفعلة",
            ["StartupTabDisabled"] = "المعطلة",
            ["StartupTabApps"] = "التطبيقات",
            ["StartupTabServices"] = "الخدمات",
            ["StartupTabTasks"] = "المهام",
            ["StartupTabStoreApps"] = "تطبيقات متجر مايكروسوفت",
            ["StartupTabSafe"] = "آمن للتعطيل",
            ["StartupTabQuick"] = "التحسين السريع",
            ["StartupTabUnknown"] = "غير معروف",
            ["StartupTabIgnored"] = "تم تجاهلها",
            ["StartupTagIgnored"] = "متجاهل",
            ["StartupBtnOptimizeQuick"] = "تحسين",
            ["StartupColName"] = "اسم البرنامج / الخدمة",
            ["StartupColPublisher"] = "الناشر",
            ["StartupColStatus"] = "الحالة",
            ["StartupColImpact"] = "تأثير الإقلاع",
            ["StartupColRec"] = "التوصية",
            ["StartupColLocation"] = "نوع البدء",
            ["StartupColAction"] = "الإجراء",
            ["StartupOpIgnore"] = "تجاهل",
            ["StartupOpUnignore"] = "إلغاء التجاهل",
            ["StartupBtnMarkKnown"] = "تحديد كمعروف",
            ["StartupBtnOptimizeSafe"] = "تعطيل الآمن",

            // Toolbox
            ["ToolboxHeader"] = "🧰 صندوق الأدوات الشامل للمحترفين",
            ["ToolboxSub"] = "مجموعة أدوات أصلية لإصلاح النظام، تحليل وحدات التخزين، وأدوات تحكم ويندوز المتقدمة.",
            ["ToolWinFixTitle"] = "🔧 مصلح ويندوز",
            ["ToolWinFixDesc"] = "إصلاح أيقونات سطح المكتب التالفة، تفريغ DNS والشبكة، وإعادة تشغيل مستكشف ويندوز بضغطة زر.",
            ["ToolSysInfoTitle"] = "🖥️ معلومات النظام",
            ["ToolSysInfoDesc"] = "مواصفات العتاد بالتفصيل: المعالج، كرت الشاشة، الرام، اللوحة الأم، نظام BIOS، الأقراص، وزمن التشغيل.",
            ["ToolAutoShutdownTitle"] = "⏰ الإيقاف التلقائي",
            ["ToolAutoShutdownDesc"] = "جدولة إيقاف تشغيل الكمبيوتر، إعادة التشغيل، أو وضع السكون بمؤقت تنازلي أو وقت محدد.",
            ["ToolDuplicateTitle"] = "🔍 كاشف الملفات المكررة",
            ["ToolDuplicateDesc"] = "فحص المجلدات واكتشاف وحذف الملفات المكررة المتطابقة بالحجم وبصمة التحقق.",
            ["ToolLargeFilesTitle"] = "📦 كاشف الملفات الضخمة",
            ["ToolLargeFilesDesc"] = "اكتشاف وتحديد الملفات الضخمة المستهلكة للمساحة (أكبر من 100\u00A0MB، 500\u00A0MB، 1\u00A0GB) بسرعة فائقة.",
            ["ToolEmptyFoldersTitle"] = "📁 ماسح المجلدات الفارغة",
            ["ToolEmptyFoldersDesc"] = "بحث متعمق وتنظيف هياكل المجلدات الفارغة والمهجورة.",
            ["ToolDefaultAppsTitle"] = "🌐 البرامج الافتراضية",
            ["ToolDefaultAppsDesc"] = "وصول سريع لإعدادات ربط البرامج والامتدادات الافتراضية في ويندوز.",
            ["ToolGodModeTitle"] = "⚙️ لوحة التحكم الشاملة",
            ["ToolGodModeDesc"] = "وصول مباشر لجميع إعدادات وتحكمات ويندوز المخفية في نافذة واحدة.",
            ["TagRepair"] = "إصلاح",
            ["TagSystem"] = "نظام",
            ["TagStorage"] = "تخزين",
            ["TagWindows"] = "ويندوز",
            ["BtnOpen"] = "فتح",

            // Settings & Info
            ["SettingsHeader"] = "⚙️ إعدادات البرنامج ومواصفات النظام",
            ["SettingsSub"] = "تخصيص سلوك البرنامج، الأتمتة في الخلفية، وتكامل النظام.",
            ["SettingStartWindows"] = "التشغيل تلقائياً مع بدء تشغيل ويندوز (في شريط المهام)",
            ["SettingRunAsAdminOnBoot"] = "التشغيل كمسؤول عند إعادة التشغيل التالي (بدون طلب UAC)",
            ["SettingMinimizeTray"] = "التصغير لشريط المهام بدلاً من الإغلاق",
            ["SettingDisableRamNotif"] = "تعطيل إشعارات تنظيف الذاكرة (عدم إظهار التنبيهات المنبثقة)",
            ["SettingStartupAlert"] = "التنبيه عند إضافة برنامج أو تطبيق متجر مايكروسوفت جديد إلى بدء التشغيل",
            ["SettingTrayNotify"] = "إظهار إشعارات سطح المكتب عند التنظيف التلقائي",

            // New Startup Item Alert Popup
            ["AlertNewStartupTitle"] = "تم اكتشاف عنصر بدء تشغيل جديد",
            ["AlertNewStartupSub"] = "يمكن تعطيله لتقليل وقت إقلاع جهازك.",
            ["AlertNewStoreStartupSub"] = "يُسمح لتطبيق متجر مايكروسوفت هذا بالعمل في الخلفية عند الإقلاع. عطّله لضبطه على (أبداً).",
            ["AlertDetailsShow"] = "التفاصيل",
            ["AlertDetailsHide"] = "إخفاء التفاصيل",
            ["AlertDisableNow"] = "تعطيل الآن",
            ["AlertIgnoreItem"] = "تجاهل عنصر بدء التشغيل هذا",
            ["AlertDetailAutoRun"] = "برنامج التشغيل التلقائي:",
            ["AlertDetailStorePath"] = "مسار التطبيق:",
            ["AlertDetailInstallFolder"] = "مجلد التثبيت:",
            ["AlertDetailPublisher"] = "الناشر:",
            ["AlertDetailSource"] = "مصدر بدء التشغيل:",
            ["AlertDetailCommand"] = "أمر التشغيل:",
            ["AlertDetailPackage"] = "معرف الحزمة:",
            ["AlertCopyPathTip"] = "نسخ المسار",
            ["AlertOpenSettingsTip"] = "إعدادات التنبيهات",
            ["AlertDetailTaskName"] = "اسم المهمة:",
            ["AlertElevateDisablePrompt"] = "يتطلب تعطيل \"{0}\" صلاحيات المسؤول.\n\nهل تريد السماح بصلاحيات المسؤول (UAC) لتعطيله الآن؟",
            ["AlertDisableFailed"] = "تعذر تعطيل \"{0}\". يرجى تشغيل PC Boost كمسؤول لعناصر بدء التشغيل الخاصة بالنظام.",
            ["SettingDarkTheme"] = "تفعيل الوضع الداكن",
            ["SettingLangLabel"] = "لغة التطبيق:",
            ["SettingHardwareTitle"] = "🖥️ نظرة سريعة على عتاد الجهاز",
            ["BtnExportHtml"] = "📄 تصدير تقرير HTML كامل",
            ["BtnRestartApp"] = "🔄 إعادة تشغيل البرنامج",
            ["BtnOpenLogs"] = "فتح مجلد السجلات",
            ["BtnExportDiagnostics"] = "تصدير ملف التشخيص",
            ["CareCreateRestorePoint"] = "إنشاء نقطة استعادة لنظام ويندوز قبل التنظيف",
            ["BtnExportShort"] = "تصدير",
            ["BtnRestorePoint"] = "نقطة استعادة",
            ["StartupBtnUndo"] = "تراجع",

            // System Tray Menu
            ["TrayOpen"] = "فتح PC Boost",
            ["TrayQuickBoost"] = "⚡ تنظيف سريع للذاكرة",
            ["TrayFlushStandby"] = "🧹 تفريغ الذاكرة المؤقتة",
            ["TrayToggleTurbo"] = "🚀 الوضع الفائق (تبديل)",
            ["TrayExit"] = "خروج نهائي",

            // Common
            ["BtnClose"] = "إغلاق",
            ["BtnCancel"] = "إلغاء",
            ["BtnApply"] = "تطبيق",
            ["BtnSave"] = "حفظ",
            ["BtnExport"] = "تصدير التقرير...",
            ["BtnCopySpecs"] = "📋 نسخ المواصفات",
            ["BtnViewDetails"] = "🔍 عرض تفاصيل العتاد بالكامل"
        };
    }
}
