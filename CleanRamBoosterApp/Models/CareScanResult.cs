using System;
using System.Collections.Generic;
using System.Linq;

namespace CleanRamBoosterApp.Models
{
    public enum CareCategory
    {
        PrivacySweep,
        JunkFiles,
        SystemOptimization
    }

    public sealed class CareScanProgress
    {
        public int StepIndex { get; set; }
        public int TotalSteps { get; set; }
        public string CurrentArea { get; set; } = string.Empty;
        public int FilesFoundSoFar { get; set; }
        public long BytesFoundSoFar { get; set; }
    }

    public sealed class CareCleanStats
    {
        public long FreedBytes { get; set; }
        public int FilesDeleted { get; set; }
        public int TweaksApplied { get; set; }
        public int LockedSkipped { get; set; }
        public int AccessDenied { get; set; }
    }

    public sealed class CareCleanSummary
    {
        public long ActualFreedBytes { get; set; }
        public int FilesDeleted { get; set; }
        public int TweaksApplied { get; set; }
        public int LockedSkipped { get; set; }
        public int AccessDenied { get; set; }
        public int CleanedCategories { get; set; }
    }

    public class CareSubItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string PathSnippet
        {
            get => TargetPath;
            set => TargetPath = value;
        }
        public long SizeBytes { get; set; }
        public int FileCount { get; set; }
        public bool IsSelected { get; set; } = true;
        public bool IsCleaned { get; set; } = false;
        public bool RequiresAdmin { get; set; } = false;
        public bool IsSystemTweak { get; set; } = false;
        public bool IsAlreadyApplied { get; set; } = false;
        public Action? CleanAction { get; set; }
        public Func<CareCleanStats>? CleanWithStats { get; set; }
    }

    public class CareAppGroup
    {
        public CareCategory Category { get; set; }
        public string AppName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconEmoji { get; set; } = "📁";
        public List<CareSubItem> SubItems { get; set; } = new();
        public bool IsSelected { get; set; } = true;
        public bool IsExpanded { get; set; } = false;

        public long TotalBytes => SubItems.Where(s => s.IsSelected).Sum(s => s.SizeBytes);
        public int TotalFiles => SubItems.Where(s => s.IsSelected && !s.IsSystemTweak).Sum(s => s.FileCount);
        public long AllScannedBytes => SubItems.Sum(s => s.SizeBytes);
        public int AllScannedFiles => SubItems.Where(s => !s.IsSystemTweak).Sum(s => s.FileCount);
        public int PendingTweaksCount => SubItems.Count(s => s.IsSystemTweak && !s.IsAlreadyApplied);
        public int SelectedPendingTweaksCount => SubItems.Count(s => s.IsSelected && s.IsSystemTweak && !s.IsAlreadyApplied);
        public int AppliedTweaksCount => SubItems.Count(s => s.IsSystemTweak && s.IsAlreadyApplied);
        public bool HasActionableItems => AllScannedBytes > 0 || AllScannedFiles > 0 || PendingTweaksCount > 0;
    }

    public class CareItem
    {
        public CareCategory Category { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public int FileCount { get; set; }
        public bool IsSelected { get; set; } = true;
        public bool IsCleaned { get; set; } = false;
        public Action? CleanAction { get; set; }
    }
}
