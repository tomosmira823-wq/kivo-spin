using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using KivoSpin.Core;
using Velopack;
using Velopack.Sources;

namespace KivoSpin.UI
{
    /// <summary>
    /// Auto-updates from GitHub Releases (Velopack) and shows "update coming" announcements from announcement.json
    /// in the repo. Checks 20 s after start, then every 4 hours.
    /// </summary>
    public static class Updater
    {
        private static UpdateManager _mgr;
        private static UpdateInfo _ready;
        private static bool _busy;
        private static readonly DispatcherTimer Timer = new DispatcherTimer();
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public static string RepoUrl
        {
            get
            {
                try
                {
                    return Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                        .FirstOrDefault(a => a.Key == "UpdateRepo")?.Value?.Trim().TrimEnd('/') ?? "";
                }
                catch { return ""; }
            }
        }

        public static string CurrentVersion
        {
            get
            {
                try { if (_mgr != null && _mgr.IsInstalled && _mgr.CurrentVersion != null) return _mgr.CurrentVersion.ToString(); } catch { }
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public static void Init()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("KivoSpin/" + CurrentVersion);
            if (!string.IsNullOrWhiteSpace(RepoUrl))
            {
                try { _mgr = new UpdateManager(new GithubSource(RepoUrl, null, false)); }
                catch (Exception ex) { Log.Error("Update manager init failed", ex); }
            }
            Timer.Interval = TimeSpan.FromSeconds(20);
            Timer.Tick += async (s, e) =>
            {
                Timer.Interval = TimeSpan.FromHours(4);
                await CheckAsync(manual: false);
            };
            Timer.Start();
        }

        public static async Task CheckAsync(bool manual)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await CheckAnnouncementAsync();

                if (_mgr == null || !_mgr.IsInstalled)
                {
                    if (manual)
                        ToastWindow.Show("Updates", string.IsNullOrWhiteSpace(RepoUrl)
                            ? "This copy wasn't built with an update source."
                            : "Updates work in the installed version (run the Kivo Spin setup).");
                    return;
                }

                if (_ready != null) { PromptRestart(); return; }

                var info = await _mgr.CheckForUpdatesAsync();
                if (info == null)
                {
                    if (manual) ToastWindow.Show("You're up to date", $"Kivo Spin {CurrentVersion} is the latest version.");
                    return;
                }

                string v = info.TargetFullRelease.Version.ToString();
                ToastWindow toast = manual ? ToastWindow.Progress($"Downloading {v}", "Getting the update ready…") : null;
                await _mgr.DownloadUpdatesAsync(info, p => Ui.Post(() => toast?.Update(null, $"Getting the update ready… {p}%", p / 100.0)));
                _ready = info;

                if (AppSettings.Current.AutoInstallUpdates && !ActionRunner.Busy && !AnyWindowOpen())
                {
                    ToastWindow.Show($"Updating to {v}", "Kivo Spin will restart in a moment.");
                    await Task.Delay(3000);
                    if (!ActionRunner.Busy) { ApplyNow(); return; }
                }
                PromptRestart();
            }
            catch (Exception ex)
            {
                Log.Error("Update check failed", ex);
                if (manual) ToastWindow.Show("Couldn't check for updates", "Check your internet connection and try again.");
            }
            finally { _busy = false; }
        }

        private static bool AnyWindowOpen() =>
            Application.Current.Windows.OfType<KivoWindow>().Any(w => w.IsVisible);

        private static void PromptRestart()
        {
            if (_ready == null) return;
            string v = _ready.TargetFullRelease.Version.ToString();
            string notes = Summarise(_ready.TargetFullRelease.NotesMarkdown);
            ToastWindow.Show($"Kivo Spin {v} is ready",
                string.IsNullOrEmpty(notes) ? "Restart to finish updating. It takes a few seconds." : notes,
                true,
                new ToastAction("Restart now", ApplyNow, true),
                new ToastAction("Later", () => { }));
        }

        public static void ApplyNow()
        {
            if (_mgr == null || _ready == null) return;
            if (ActionRunner.Busy)
            {
                ToastWindow.Show("Finishing your files first", "Kivo Spin will update when the current job is done. Choose Restart now again in a moment.");
                return;
            }
            try
            {
                App.Instance?.PrepareForExit();
                _mgr.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
            }
            catch (Exception ex)
            {
                Log.Error("Apply update failed", ex);
                ToastWindow.Show("Update didn't install", "It will try again next time Kivo Spin starts.");
            }
        }

        /// <summary>Called on quit: if an update is downloaded, install it silently after we exit.</summary>
        public static void ApplyOnExit()
        {
            try
            {
                if (_mgr != null && _ready != null)
                    _mgr.WaitExitThenApplyUpdates(_ready.TargetFullRelease, true, false);
            }
            catch (Exception ex) { Log.Error("Apply-on-exit failed", ex); }
        }

        private static string Summarise(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "";
            var text = Regex.Replace(markdown, @"[#*_`>\[\]]", "");
            text = Regex.Replace(text, @"\(https?://[^)]+\)", "");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text.Length > 160 ? text.Substring(0, 157) + "…" : text;
        }

        // ---------------------------------------------------- announcements ("an update is coming")

        private class Announcement
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Message { get; set; }
            public string Link { get; set; }
            public bool Active { get; set; } = true;
            public string MinVersion { get; set; }
            public string MaxVersion { get; set; }
        }

        private static async Task CheckAnnouncementAsync()
        {
            if (string.IsNullOrWhiteSpace(RepoUrl) || !RepoUrl.Contains("github.com/")) return;
            try
            {
                var raw = RepoUrl.Replace("https://github.com/", "https://raw.githubusercontent.com/") + "/main/announcement.json";
                var json = await Http.GetStringAsync(raw + "?t=" + DateTime.UtcNow.Ticks);
                var a = JsonSerializer.Deserialize<Announcement>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (a == null || !a.Active || string.IsNullOrWhiteSpace(a.Id)) return;
                if (a.Id == AppSettings.Current.LastAnnouncementId) return;
                if (!InRange(a.MinVersion, a.MaxVersion)) return;

                AppSettings.Current.LastAnnouncementId = a.Id;
                AppSettings.Save();
                var actions = new List<ToastAction>();
                if (!string.IsNullOrWhiteSpace(a.Link)) actions.Add(new ToastAction("Read more", () => Ui.Open(a.Link), true));
                actions.Add(new ToastAction("OK", () => { }));
                ToastWindow.Show(a.Title ?? "Kivo Spin", a.Message ?? "", true, actions.ToArray());
            }
            catch (Exception ex) { Log.Info("No announcement: " + ex.Message); }
        }

        private static bool InRange(string min, string max)
        {
            if (!Version.TryParse(CurrentVersion, out var cur)) return true;
            if (!string.IsNullOrWhiteSpace(min) && Version.TryParse(min, out var mn) && cur < mn) return false;
            if (!string.IsNullOrWhiteSpace(max) && Version.TryParse(max, out var mx) && cur > mx) return false;
            return true;
        }
    }
}
