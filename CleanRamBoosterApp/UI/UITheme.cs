using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CleanRamBoosterApp.UI
{
    /// <summary>
    /// Minimal shared icon & text-sanitization helper retained for tray icon loading
    /// and leading-emoji stripping after porting all UI windows to pure WPF.
    /// </summary>
    public static class UITheme
    {
        public static Icon? AppIcon { get; set; }
        public static Image? AppIconBitmap { get; set; }
        private static bool _iconLoaded;

        public static void EnsureAppIconLoaded()
        {
            if (_iconLoaded) return;
            _iconLoaded = true;
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] possibleIco =
                {
                    Path.Combine(baseDir, "app.ico"),
                    Path.Combine(baseDir, "Resources", "app.ico")
                };
                foreach (var p in possibleIco)
                {
                    if (File.Exists(p))
                    {
                        AppIcon = new Icon(p);
                        break;
                    }
                }
                if (AppIcon == null)
                {
                    AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }

                string[] possiblePng =
                {
                    Path.Combine(baseDir, "app_icon.png"),
                    Path.Combine(baseDir, "Resources", "app_icon.png")
                };
                foreach (var p in possiblePng)
                {
                    if (File.Exists(p))
                    {
                        using var fs = new FileStream(p, FileMode.Open, FileAccess.Read);
                        AppIconBitmap = Image.FromStream(fs);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Services.DiagnosticLogger.LogWarning("UITheme", "Failed to load application icons", ex);
            }
        }

        public static bool ContainsArabic(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if ((c >= '\u0600' && c <= '\u06FF') ||
                    (c >= '\u0750' && c <= '\u077F') ||
                    (c >= '\u08A0' && c <= '\u08FF') ||
                    (c >= '\uFB50' && c <= '\uFDFF') ||
                    (c >= '\uFE70' && c <= '\uFEFF'))
                {
                    return true;
                }
            }
            return false;
        }

        public static string StripLeadingIcon(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string s = text.Trim();
            int i = 0;
            while (i < s.Length)
            {
                if (char.IsSurrogatePair(s, i))
                {
                    i += 2;
                    continue;
                }
                char ch = s[i];
                if (ch == '\uFE0F' || ch == '\uFE0E' || ch == '\u200D' ||
                    (ch >= '\u2600' && ch <= '\u27BF') ||
                    (ch >= '\u2300' && ch <= '\u23FF') ||
                    char.IsWhiteSpace(ch))
                {
                    i++;
                    continue;
                }
                break;
            }
            return i > 0 && i < s.Length ? s.Substring(i).TrimStart() : s;
        }
    }
}
