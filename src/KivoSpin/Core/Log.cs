namespace KivoSpin.Core
{
    public static class Log
    {
        private static readonly object Gate = new object();
        public static string FilePath => Path.Combine(AppPaths.LocalDir, "log.txt");

        public static void Info(string msg) => Write("INFO", msg);

        public static void Error(string msg, Exception ex = null) =>
            Write("ERROR", ex == null ? msg : msg + " :: " + ex);

        private static void Write(string level, string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.LocalDir);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 1_000_000) fi.Delete();
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }

    /// <summary>An error whose message is safe and useful to show the user as-is.</summary>
    public class UserFacingException : Exception
    {
        public UserFacingException(string message) : base(message) { }
    }
}
