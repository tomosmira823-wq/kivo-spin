using System.Text.Json;

namespace KivoSpin.Core
{
    public enum TriggerKey { Shift = 0, Ctrl = 1, Alt = 2 }
    public enum WheelSize { Small = 0, Medium = 1, Large = 2 }

    public class AppSettings
    {
        public TriggerKey Trigger { get; set; } = TriggerKey.Shift;
        public WheelSize Size { get; set; } = WheelSize.Medium;
        public bool SaveNextToOriginal { get; set; } = true;
        public string OutputFolder { get; set; } = "";
        public int Quality { get; set; } = 88;
        public int CompressQuality { get; set; } = 72;
        public int CompressMaxSide { get; set; } = 2560;
        public int PdfDpi { get; set; } = 200;
        public bool StartWithWindows { get; set; } = true;
        public bool AutoInstallUpdates { get; set; } = false;
        public bool OpenFolderWhenDone { get; set; } = false;
        public bool Paused { get; set; } = false;
        public bool FirstRunDone { get; set; } = false;
        public string LastAnnouncementId { get; set; } = "";
        public string LastBgColor { get; set; } = "#FFFFFF";

        // ---------------------------------------------------------------

        public static AppSettings Current { get; private set; } = new AppSettings();

        private static string FilePath => Path.Combine(AppPaths.RoamingDir, "settings.json");

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Error("Settings load failed", ex);
                Current = new AppSettings();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.RoamingDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.Error("Settings save failed", ex);
            }
        }

        public int WheelRadius => Size switch
        {
            WheelSize.Small => 140,
            WheelSize.Large => 205,
            _ => 170
        };

        public int TriggerVk => Trigger switch
        {
            TriggerKey.Ctrl => Interop.Native.VK_CONTROL,
            TriggerKey.Alt => Interop.Native.VK_MENU,
            _ => Interop.Native.VK_SHIFT
        };

        public string TriggerName => Trigger switch
        {
            TriggerKey.Ctrl => "Ctrl",
            TriggerKey.Alt => "Alt",
            _ => "Shift"
        };
    }
}
