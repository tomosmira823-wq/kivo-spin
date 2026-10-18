using System.Windows;
using System.Windows.Threading;
using KivoSpin.Core;
using KivoSpin.UI;
using WinForms = System.Windows.Forms;

namespace KivoSpin
{
    public class App : Application
    {
        public static App Instance { get; private set; }
        public const string ShowSignalName = "KivoSpin.ShowSettings.v1";

        private readonly string[] _args;
        private WinForms.NotifyIcon _tray;
        private WinForms.ToolStripMenuItem _pauseItem;
        private DragWatcher _watcher;
        private WheelWindow _wheel;
        private EventWaitHandle _showSignal;
        private bool _exiting;

        public App(string[] args)
        {
            _args = args ?? new string[0];
            Instance = this;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log.Error("Unhandled (domain)", a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, a) => { Log.Error("Unobserved task", a.Exception); a.SetObserved(); };

            AppSettings.Load();
            Log.Info("Kivo Spin starting " + Updater.CurrentVersion);

            _wheel = new WheelWindow();
            _wheel.ActionChosen += (id, files) => ActionRunner.Run(id, files);

            _watcher = new DragWatcher();
            _watcher.Trigger += (x, y) => _wheel.ShowAt(x, y);
            _watcher.Released += () => _wheel.OnMouseReleased();
            _watcher.Start();

            SetupTray();
            StartupManager.Sync();
            ListenForSecondInstance();
            Updater.Init();

            bool background = _args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (!AppSettings.Current.FirstRunDone)
            {
                AppSettings.Current.FirstRunDone = true;
                AppSettings.Save();
                new WelcomeWindow().Show();
            }
            else if (!background)
            {
                ToastWindow.Show("Kivo Spin is running", $"Drag any file and hold {AppSettings.Current.TriggerName} to spin.");
            }
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
            try { ToastWindow.Show("Something went wrong", e.Exception is UserFacingException ? e.Exception.Message : "Kivo Spin hit a problem but is still running."); }
            catch { }
        }

        private void SetupTray()
        {
            System.Drawing.Icon icon;
            try
            {
                var res = GetResourceStream(new Uri("pack://application:,,,/Assets/KivoSpin.ico"));
                icon = res != null ? new System.Drawing.Icon(res.Stream, new System.Drawing.Size(16, 16)) : System.Drawing.SystemIcons.Application;
            }
            catch { icon = System.Drawing.SystemIcons.Application; }

            var menu = new WinForms.ContextMenuStrip();
            var header = new WinForms.ToolStripMenuItem("Kivo Spin " + Updater.CurrentVersion) { Enabled = false };
            menu.Items.Add(header);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Settings…", null, (s, e) => SettingsWindow.ShowSingle());
            menu.Items.Add("How it works", null, (s, e) => new WelcomeWindow().Show());
            _pauseItem = new WinForms.ToolStripMenuItem("Pause", null, (s, e) => TogglePause());
            menu.Items.Add(_pauseItem);
            menu.Items.Add("Check for updates", null, async (s, e) => await Updater.CheckAsync(manual: true));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Quit Kivo Spin", null, (s, e) => Quit());
            menu.Opening += (s, e) =>
            {
                header.Text = "Kivo Spin " + Updater.CurrentVersion;
                _pauseItem.Checked = AppSettings.Current.Paused;
            };

            _tray = new WinForms.NotifyIcon
            {
                Icon = icon,
                Text = "Kivo Spin – drag a file + " + AppSettings.Current.TriggerName,
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.MouseClick += (s, e) => { if (e.Button == WinForms.MouseButtons.Left) SettingsWindow.ShowSingle(); };
        }

        private void TogglePause()
        {
            AppSettings.Current.Paused = !AppSettings.Current.Paused;
            AppSettings.Save();
            ToastWindow.Show(AppSettings.Current.Paused ? "Kivo Spin paused" : "Kivo Spin is back",
                AppSettings.Current.Paused ? "The wheel won't appear until you resume." : $"Drag a file and hold {AppSettings.Current.TriggerName}.");
        }

        private void ListenForSecondInstance()
        {
            try
            {
                _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
                var t = new Thread(() =>
                {
                    while (!_exiting)
                    {
                        try
                        {
                            if (_showSignal.WaitOne(1000) && !_exiting)
                                Dispatcher.BeginInvoke(new Action(SettingsWindow.ShowSingle));
                        }
                        catch { return; }
                    }
                }) { IsBackground = true, Name = "KivoSpin.SecondInstance" };
                t.Start();
            }
            catch (Exception ex) { Log.Error("Signal setup failed", ex); }
        }

        /// <summary>Remove the tray icon etc. Safe to call more than once.</summary>
        public void PrepareForExit()
        {
            _exiting = true;
            try { _watcher?.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; } } catch { }
        }

        public void Quit()
        {
            if (ActionRunner.Busy && !KDialog.Confirm("Quit Kivo Spin?", "A file is still being processed. Quit anyway?", "Quit", "Keep running"))
                return;
            PrepareForExit();
            Updater.ApplyOnExit();
            AppSettings.Save();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            PrepareForExit();
            base.OnExit(e);
        }
    }
}
