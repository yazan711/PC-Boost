using CleanRamBoosterApp.Services;

namespace CleanRamBoosterApp.Models
{
    public enum TurboCategory
    {
        SystemServices,     // Windows Optional Services
        NonWindowsServices, // Third-Party / Manufacturer Services
        BackgroundApps      // Running User-Mode Applications
    }

    public class TurboBoostItem
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TurboCategory Category { get; set; }
        public bool IsService { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public bool IsSelected { get; set; } = true;
        public long MemoryUsageBytes { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;

        public string MemoryUsageDisplay => MemoryUsageBytes > 0 
            ? $"\u200E{(MemoryUsageBytes / (1024.0 * 1024.0)):N1} MB\u200E"
            : "-";

        public string CategoryDisplay
        {
            get
            {
                bool isAr = LocalizationService.IsArabic;
                return Category switch
                {
                    TurboCategory.BackgroundApps => isAr ? "تطبيقات الخلفية" : "Background Apps",
                    TurboCategory.NonWindowsServices => isAr ? "خدمات خارجية" : "Non-Windows Services",
                    _ => isAr ? "خدمات النظام" : "System Services"
                };
            }
        }

        public string StatusDisplay => IsSelected ? "Targeted" : "Ignored";

        public string StatusDisplayLocalized => IsSelected
            ? (LocalizationService.IsArabic ? "مستهدف" : "Targeted")
            : (LocalizationService.IsArabic ? "مستبعد" : "Ignored");
    }
}
