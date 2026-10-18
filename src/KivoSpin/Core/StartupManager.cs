using Microsoft.Win32;

namespace KivoSpin.Core
{
    /// <summary>"Start with Windows" via the per-user Run key.</summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "KivoSpin";

        public static void Sync()
        {
            if (AppSettings.Current.StartWithWindows) Enable(); else Disable();
        }

        public static void Enable()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                key?.SetValue(ValueName, $"\"{exe}\" --background");
            }
            catch (Exception ex) { Log.Error("Startup enable failed", ex); }
        }

        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key?.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
            }
            catch (Exception ex) { Log.Error("Startup disable failed", ex); }
        }
    }
}
