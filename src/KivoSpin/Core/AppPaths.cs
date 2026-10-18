namespace KivoSpin.Core
{
    /// <summary>Where Kivo Spin keeps its own files, and how output files are named.</summary>
    public static class AppPaths
    {
        public static string RoamingDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KivoSpin");

        public static string LocalDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KivoSpinData");

        public static string ModelsDir => Path.Combine(LocalDir, "models");

        public static string TempDir
        {
            get
            {
                var d = Path.Combine(Path.GetTempPath(), "KivoSpin");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string FallbackOutputDir
        {
            get
            {
                var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Kivo Spin");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string NewTempFile(string ext) =>
            Path.Combine(TempDir, Guid.NewGuid().ToString("N") + "." + ext.TrimStart('.'));

        /// <summary>Folder results go in for a given source file.</summary>
        public static string OutputDirFor(string source)
        {
            var s = AppSettings.Current;
            string dir = (!s.SaveNextToOriginal && !string.IsNullOrWhiteSpace(s.OutputFolder) && Directory.Exists(s.OutputFolder))
                ? s.OutputFolder
                : Path.GetDirectoryName(source);

            if (string.IsNullOrEmpty(dir) || !CanWrite(dir) || IsTempDir(dir))
                dir = FallbackOutputDir;
            return dir;
        }

        /// <summary>"Photo.heic" + "png" → "Photo.png" (or "Photo 2.png" if taken). Suffix adds " (suffix)".</summary>
        public static string Output(string source, string ext, string suffix = null)
        {
            var dir = OutputDirFor(source);
            var name = Path.GetFileNameWithoutExtension(source);
            if (!string.IsNullOrEmpty(suffix)) name += " (" + suffix + ")";
            return Unique(Path.Combine(dir, name + "." + ext.TrimStart('.')));
        }

        public static string OutputFolderFor(string source, string suffix)
        {
            var dir = OutputDirFor(source);
            var baseName = Path.GetFileNameWithoutExtension(source) + " (" + suffix + ")";
            var path = Path.Combine(dir, baseName);
            int i = 2;
            while (Directory.Exists(path) || File.Exists(path))
                path = Path.Combine(dir, baseName + " " + i++);
            Directory.CreateDirectory(path);
            return path;
        }

        public static string Unique(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int i = 2;
            string candidate;
            do { candidate = Path.Combine(dir, $"{name} {i++}{ext}"); }
            while (File.Exists(candidate) || Directory.Exists(candidate));
            return candidate;
        }

        public static bool CanWrite(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                var probe = Path.Combine(dir, ".kivospin-" + Guid.NewGuid().ToString("N"));
                File.WriteAllBytes(probe, new byte[0]);
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // Files dragged straight out of a zip land in %TEMP%; don't save results where nobody will find them.
        private static bool IsTempDir(string dir)
        {
            try
            {
                var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
                return Path.GetFullPath(dir).StartsWith(temp, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
