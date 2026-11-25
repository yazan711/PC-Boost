using System.ComponentModel;

namespace CleanRamBoosterApp.Models
{
    public enum StartupType
    {
        RegistryUser,
        RegistryMachine,
        RegistryWow64,
        StartupFolderUser,
        StartupFolderCommon,
        WindowsService,
        ScheduledTask,
        MicrosoftStoreApp
    }

    public enum StartupRecommendation
    {
        Necessary,
        Optional,
        SafeToDisable,
        Orphaned
    }

    public class StartupItem : INotifyPropertyChanged
    {
        private bool isEnabled = true;
        private bool isIgnored;
        private bool isUnknown;
        private bool isSelected;
        private bool isQuickSelected = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void NotifyPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public StartupType LocationType { get; set; }
        public string LocationDisplay { get; set; } = string.Empty;

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                if (isEnabled != value)
                {
                    isEnabled = value;
                    NotifyPropertyChanged(nameof(IsEnabled));
                    NotifyPropertyChanged(nameof(StatusDisplay));
                    NotifyPropertyChanged(nameof(StatusDisplayLocalized));
                }
            }
        }

        public bool IsIgnored
        {
            get => isIgnored;
            set
            {
                if (isIgnored != value)
                {
                    isIgnored = value;
                    NotifyPropertyChanged(nameof(IsIgnored));
                    NotifyPropertyChanged(nameof(IsUnknownVisibility));
                    NotifyPropertyChanged(nameof(QuickCheckboxVisibility));
                    NotifyPropertyChanged(nameof(IsIgnoredBadgeVisibility));
                    NotifyPropertyChanged(nameof(IgnoreActionText));
                    NotifyPropertyChanged(nameof(StateTagDisplay));
                }
            }
        }

        public bool IsUnknown
        {
            get => isUnknown;
            set
            {
                if (isUnknown != value)
                {
                    isUnknown = value;
                    NotifyPropertyChanged(nameof(IsUnknown));
                    NotifyPropertyChanged(nameof(IsUnknownVisibility));
                    NotifyPropertyChanged(nameof(StateTagDisplay));
                }
            }
        }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected != value)
                {
                    isSelected = value;
                    NotifyPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public bool IsQuickSelected
        {
            get => isQuickSelected;
            set
            {
                if (isQuickSelected != value)
                {
                    isQuickSelected = value;
                    NotifyPropertyChanged(nameof(IsQuickSelected));
                }
            }
        }

        public System.Windows.Visibility IsUnknownVisibility =>
            (IsUnknown && !IsIgnored) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility QuickCheckboxVisibility =>
            !IsIgnored ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public System.Windows.Visibility IsIgnoredBadgeVisibility =>
            IsIgnored ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string IgnoreActionText => Services.LocalizationService.IsArabic
            ? (IsIgnored ? "إلغاء التجاهل" : "تجاهل")
            : (IsIgnored ? "Unignore" : "Ignore");

        public string StateTagDisplay => Services.LocalizationService.IsArabic
            ? (IsUnknown ? "جديد" : (IsIgnored ? "متجاهل" : string.Empty))
            : (IsUnknown ? "NEW" : (IsIgnored ? "Ignored" : string.Empty));

        public StartupRecommendation Recommendation { get; set; } = StartupRecommendation.Optional;
        public string BootImpact { get; set; } = "Low";
        public string ServiceName { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public string OriginalRegistryKey { get; set; } = string.Empty;
        public string RegistryValueName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string InstallFolder { get; set; } = string.Empty;
        public string IconAssetPath { get; set; } = string.Empty;
        public string PackageFamilyName { get; set; } = string.Empty;
        public string BackgroundPermissionMode { get; set; } = string.Empty;

        public string MonitorKey => LocationType switch
        {
            StartupType.RegistryUser or StartupType.RegistryMachine or StartupType.RegistryWow64
                => $"{LocationType}|{(!string.IsNullOrEmpty(RegistryValueName) ? RegistryValueName : Name).Trim().ToLowerInvariant()}",
            StartupType.StartupFolderUser or StartupType.StartupFolderCommon
                => $"{LocationType}|{System.IO.Path.GetFileName(!string.IsNullOrEmpty(FilePath) ? FilePath : Name).Trim().ToLowerInvariant()}",
            StartupType.ScheduledTask
                => $"ScheduledTask|{(!string.IsNullOrEmpty(TaskName) ? TaskName : Name).Trim().ToLowerInvariant()}",
            StartupType.WindowsService
                => $"WindowsService|{(!string.IsNullOrEmpty(ServiceName) ? ServiceName : Name).Trim().ToLowerInvariant()}",
            StartupType.MicrosoftStoreApp
                => $"MicrosoftStoreApp|{(!string.IsNullOrEmpty(PackageFamilyName) ? PackageFamilyName : Name).Trim().ToLowerInvariant()}",
            _ => $"{LocationType}|{Name.Trim().ToLowerInvariant()}"
        };

        public string StatusDisplay => IsEnabled ? "Enabled" : "Disabled";
        public string StatusDisplayLocalized => Services.LocalizationService.IsArabic
            ? (IsEnabled ? "مفعل" : "معطل")
            : StatusDisplay;

        public string BootImpactDisplay => Services.LocalizationService.IsArabic
            ? (BootImpact.Contains("High", System.StringComparison.OrdinalIgnoreCase) ? "عالي"
                : BootImpact.Contains("Med", System.StringComparison.OrdinalIgnoreCase) ? "متوسط"
                : "منخفض")
            : BootImpact;

        public string RecommendationDisplay => Recommendation switch
        {
            StartupRecommendation.Necessary => "Necessary",
            StartupRecommendation.Optional => "Optional",
            StartupRecommendation.SafeToDisable => "Safe to Disable",
            _ => "Unknown"
        };

        public string RecommendationDisplayLocalized => Services.LocalizationService.IsArabic
            ? Recommendation switch
            {
                StartupRecommendation.Necessary => "ضروري",
                StartupRecommendation.Optional => "اختياري",
                StartupRecommendation.SafeToDisable => "آمن للتعطيل",
                StartupRecommendation.Orphaned => "مفقود",
                _ => "غير معروف"
            }
            : RecommendationDisplay;

        public string LocationDisplayLocalized
        {
            get
            {
                if (!Services.LocalizationService.IsArabic) return LocationDisplay;
                return LocationType switch
                {
                    StartupType.MicrosoftStoreApp => "تطبيق متجر مايكروسوفت",
                    StartupType.RegistryUser => "السجل (المستخدم)",
                    StartupType.RegistryMachine => "السجل (النظام)",
                    StartupType.RegistryWow64 => "السجل (32-بت)",
                    StartupType.StartupFolderUser => "مجلد بدء التشغيل",
                    StartupType.StartupFolderCommon => "مجلد التشغيل العام",
                    StartupType.ScheduledTask => "مهمة مجدولة",
                    StartupType.WindowsService => !string.IsNullOrEmpty(ServiceName) ? $"خدمة ({ServiceName})" : "خدمة ويندوز",
                    _ => LocationDisplay
                };
            }
        }
    }
}
