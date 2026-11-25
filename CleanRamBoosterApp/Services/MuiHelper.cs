using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CleanRamBoosterApp.Services
{
    public static class MuiHelper
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHLoadIndirectString(string pszSourceBuf, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        public static string Resolve(string? str, string fallback = "")
        {
            if (string.IsNullOrWhiteSpace(str)) return fallback;
            if (!str.StartsWith("@")) return str.Trim();

            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int hr = SHLoadIndirectString(str, sb, sb.Capacity, IntPtr.Zero);
                if (hr == 0 && sb.Length > 0)
                {
                    string res = sb.ToString().Trim();
                    if (!res.StartsWith("@") && !string.IsNullOrEmpty(res))
                        return res;
                }

                // Try expanding environment variables (e.g. %SystemRoot%, %windir%)
                string exp = Environment.ExpandEnvironmentVariables(str);
                hr = SHLoadIndirectString(exp, sb, sb.Capacity, IntPtr.Zero);
                if (hr == 0 && sb.Length > 0)
                {
                    string res = sb.ToString().Trim();
                    if (!res.StartsWith("@") && !string.IsNullOrEmpty(res))
                        return res;
                }
            }
            catch { }

            // If it still starts with '@', fallback to the clean service name or friendly title
            if (!string.IsNullOrWhiteSpace(fallback) && !fallback.StartsWith("@"))
                return fallback.Trim();

            return str.Trim();
        }
    }
}
