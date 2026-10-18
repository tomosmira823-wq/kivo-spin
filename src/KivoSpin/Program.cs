using KivoSpin.Core;
using Velopack;

namespace KivoSpin
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Velopack install/update/uninstall hooks run here and exit early when needed.
            if (args.Any(a => a.Contains("veloapp-uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                try { StartupManager.Disable(); } catch { }
            }
            try { VelopackApp.Build().Run(); }
            catch (Exception ex) { Log.Error("Velopack startup failed", ex); }

            using var mutex = new Mutex(true, "KivoSpin.SingleInstance.v1", out bool firstInstance);
            if (!firstInstance)
            {
                // Already running: ask that copy to open its settings, then quit.
                try { EventWaitHandle.OpenExisting(App.ShowSignalName).Set(); } catch { }
                return;
            }

            var app = new App(args);
            app.Run();
            GC.KeepAlive(mutex);
        }
    }
}
