using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KivoSpin.Core;
using KivoSpin.Interop;

namespace KivoSpin.UI
{
    /// <summary>
    /// The pop-up wheel. Shown invisibly under the cursor when the trigger fires; reveals itself only when an
    /// actual file drag enters it. Dropping on a wedge runs that action. Hovering the hub flips Convert ↔ Tools.
    /// </summary>
    public class WheelWindow : Window
    {
        public event Action<string, List<string>> ActionChosen;

        private readonly WheelVisual _vis = new WheelVisual();
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly DispatcherTimer _dwell = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        private IntPtr _hwnd;
        private List<string> _files = new List<string>();
        private List<WheelItem>[] _pages = new List<WheelItem>[2];
        private int _page;
        private DateTime _hubSince;
        private bool _hubLocked;
        private bool _dropped;
        private int _radius;
        private int _lastPx, _lastPy;

        private const double HubDwellSeconds = 0.45;

        public WheelWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            AllowDrop = true;
            Focusable = false;
            Title = "Kivo Spin Wheel";
            UseLayoutRounding = true;

            _vis.RenderTransform = _scale;
            Content = _vis;
            ApplySize();

            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
                Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            };
            DpiChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => Position(_lastPx, _lastPy)));

            DragEnter += OnDragEnter;
            DragOver += OnDragOver;
            DragLeave += OnDragLeave;
            Drop += OnDrop;
            _dwell.Tick += DwellTick;
        }

        private void ApplySize()
        {
            _radius = AppSettings.Current.WheelRadius;
            _vis.R = _radius;
            Width = _vis.TotalWidth;
            Height = _vis.TotalHeight;
            _vis.Width = Width;
            _vis.Height = Height;
            _vis.RenderTransformOrigin = new Point(0.5, _vis.Center.Y / _vis.TotalHeight);
        }

        // ------------------------------------------------------------ show / hide

        public void ShowAt(int px, int py)
        {
            if (_radius != AppSettings.Current.WheelRadius) ApplySize();
            _dropped = false;
            _files = new List<string>();
            _vis.Revealed = false;
            _vis.Hover = -1;
            _vis.HubHover = false;
            _vis.Caption = "";
            _vis.Opacity = 1;
            _scale.ScaleX = _scale.ScaleY = 1;
            _vis.InvalidateVisual();

            if (!IsVisible) Show();
            Position(px, py);
        }

        private void Position(int px, int py)
        {
            _lastPx = px; _lastPy = py;
            if (_hwnd == IntPtr.Zero) return;
            Native.GetWindowRect(_hwnd, out var r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            int cy = (int)Math.Round(h * (_vis.Center.Y / _vis.TotalHeight));
            int x = px - w / 2, y = py - cy;
            // Keep the whole wheel (and its caption) on the monitor the cursor is on.
            try
            {
                var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(px, py)).WorkingArea;
                x = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
                y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - h));
            }
            catch { }
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        /// <summary>Mouse button went up somewhere (from DragWatcher).</summary>
        public void OnMouseReleased()
        {
            if (!IsVisible) return;
            if (!_vis.Revealed) { HideWheel(); return; }
            // A drop may still be on its way through OLE; give it a moment.
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            t.Tick += (s, e) => { t.Stop(); if (!_dropped) HideWheel(); };
            t.Start();
        }

        public void HideWheel()
        {
            _dwell.Stop();
            if (IsVisible) Hide();
            _vis.Revealed = false;
        }

        private void Reveal()
        {
            _vis.Revealed = true;
            _vis.InvalidateVisual();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var grow = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            _vis.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        }

        // ------------------------------------------------------------ drag & drop

        private static List<string> ReadFiles(DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
                var arr = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (arr == null) return null;
                var files = new List<string>();
                foreach (var p in arr)
                {
                    if (File.Exists(p)) files.Add(p);
                    else if (Directory.Exists(p))
                    {
                        // A dropped folder: use the supported files directly inside it.
                        try
                        {
                            files.AddRange(Directory.GetFiles(p).Where(f => FileKinds.Of(f) != FileKind.Unknown)
                                .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).Take(500));
                        }
                        catch { }
                    }
                }
                return files.Count > 0 ? files : null;
            }
            catch { return null; }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (!_vis.Revealed)
            {
                var files = ReadFiles(e);
                if (files == null)
                {
                    // Not a file drag (e.g. text): get out of the way so the drop lands where the user meant.
                    e.Effects = DragDropEffects.None;
                    Dispatcher.BeginInvoke(new Action(HideWheel));
                    return;
                }
                _files = files;
                _pages[0] = WheelPages.Convert(_files);
                _pages[1] = WheelPages.Tools(_files);
                // If nothing on the convert page applies (e.g. can't convert), open on tools.
                _page = _pages[0].Any(i => i.Enabled) ? 0 : 1;
                _hubLocked = false;
                ApplyPage();
                Reveal();
            }
            UpdateHover(e);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            UpdateHover(e);
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            e.Handled = true;
            _vis.Hover = -1;
            SetHub(false);
            _vis.Caption = BaseCaption();
            _vis.InvalidateVisual();
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            // Always report Copy (never Move) so the source app never deletes the original file.
            e.Effects = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.Link & e.AllowedEffects;
            _dropped = true;
            int hit = _vis.HitTest(e.GetPosition(_vis));
            var items = _pages[_page];
            WheelItem chosen = (hit >= 0 && items != null && hit < items.Count && items[hit].Enabled) ? items[hit] : null;
            var files = _files.ToList();
            HideWheel();
            if (chosen != null)
                Dispatcher.BeginInvoke(new Action(() => ActionChosen?.Invoke(chosen.Id, files)), DispatcherPriority.Background);
        }

        private void UpdateHover(DragEventArgs e)
        {
            if (!_vis.Revealed) { e.Effects = DragDropEffects.None; return; }
            int hit = _vis.HitTest(e.GetPosition(_vis));
            var items = _pages[_page];

            if (hit == -2)
            {
                SetHub(true);
                _vis.Hover = -1;
                _vis.Caption = _page == 0 ? "Hold here for tools" : "Hold here to convert";
                e.Effects = DragDropEffects.None;
            }
            else
            {
                SetHub(false);
                _hubLocked = false;
                _vis.Hover = hit;
                if (hit >= 0 && items != null && hit < items.Count)
                {
                    var it = items[hit];
                    _vis.Caption = it.Enabled ? FileLabel() + "  " + Arrow(it) : it.Label + " isn't available for this file";
                    e.Effects = it.Enabled ? CopyEffect(e) : DragDropEffects.None;
                }
                else
                {
                    _vis.Caption = BaseCaption();
                    e.Effects = DragDropEffects.None;
                }
            }
            _vis.InvalidateVisual();
        }

        private static DragDropEffects CopyEffect(DragEventArgs e)
        {
            if ((e.AllowedEffects & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            if ((e.AllowedEffects & DragDropEffects.Link) != 0) return DragDropEffects.Link;
            return DragDropEffects.None;
        }

        private static string Arrow(WheelItem it) => it.Id.StartsWith("to:") ? it.Verb : "· " + it.Verb;

        private string FileLabel() => _files.Count == 1 ? Path.GetFileName(_files[0]) : _files.Count + " files";
        private string BaseCaption() => _files.Count == 0 ? "" : FileLabel();

        private void SetHub(bool on)
        {
            if (on && !_vis.HubHover)
            {
                _vis.HubHover = true;
                _hubSince = DateTime.UtcNow;
                _vis.HubProgress = 0;
                if (!_hubLocked) _dwell.Start();
            }
            else if (!on && _vis.HubHover)
            {
                _vis.HubHover = false;
                _vis.HubProgress = 0;
                _dwell.Stop();
            }
        }

        private void DwellTick(object sender, EventArgs e)
        {
            if (!_vis.HubHover || _hubLocked) { _dwell.Stop(); return; }
            double t = (DateTime.UtcNow - _hubSince).TotalSeconds / HubDwellSeconds;
            _vis.HubProgress = Math.Min(1, t);
            if (t >= 1)
            {
                _dwell.Stop();
                _hubLocked = true; // must leave the hub before it can flip again
                _page = 1 - _page;
                _vis.HubProgress = 0;
                ApplyPage();
                // little spin
                var spin = new DoubleAnimation(0.93, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, spin);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, spin);
            }
            _vis.InvalidateVisual();
        }

        private void ApplyPage()
        {
            _vis.Items = _pages[_page] ?? new List<WheelItem>();
            _vis.HubTitle = _page == 0 ? "CONVERT" : "TOOLS";
            bool none = _vis.Items.All(i => !i.Enabled);
            _vis.HubSub = none ? "Not supported" : (_page == 0 ? "hold → tools" : "hold → convert");
            _vis.Caption = BaseCaption();
            _vis.InvalidateVisual();
        }
    }
}
