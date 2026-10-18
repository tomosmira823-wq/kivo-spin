using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ImageMagick;
using KivoSpin.Core;

namespace KivoSpin.UI
{
    /// <summary>Quick image edits: rotate, flip, resize, brightness/contrast/saturation, black &amp; white.</summary>
    public class EditWindow : KivoWindow
    {
        public EditSpec Result { get; private set; }

        private readonly EditSpec _spec = new EditSpec();
        private readonly MagickImage _previewBase;
        private readonly int _fullW, _fullH;
        private readonly Image _image = new Image { Stretch = Stretch.Uniform };
        private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        private readonly TextBox _wBox, _hBox;
        private readonly TextBlock _dims;
        private KToggle _lock;
        private bool _syncing;

        public EditWindow(MagickImage previewBase, int fullW, int fullH, string fileName) : base("Edit  ·  " + fileName, 900)
        {
            _previewBase = previewBase;
            _fullW = fullW; _fullH = fullH;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });

            var well = new Border
            {
                Background = Theme.WellBrush, BorderBrush = Theme.EdgeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Height = 520, Child = _image
            };
            grid.Children.Add(well);

            var side = new StackPanel { Margin = new Thickness(20, -10, 0, 0) };
            Grid.SetColumn(side, 1);
            grid.Children.Add(side);

            side.Children.Add(Theme.Label("Rotate & flip"));
            var rotRow = new WrapPanel();
            rotRow.Children.Add(Btn("", "", () => { _spec.Rotation = (_spec.Rotation + 270) % 360; SwapResize(); }));
            rotRow.Children.Add(Btn("", "", () => { _spec.Rotation = (_spec.Rotation + 90) % 360; SwapResize(); }, mirror: true));
            rotRow.Children.Add(Btn("Flip H", null, () => _spec.FlipH = !_spec.FlipH));
            rotRow.Children.Add(Btn("Flip V", null, () => _spec.FlipV = !_spec.FlipV));
            side.Children.Add(rotRow);

            side.Children.Add(Theme.Label("Brightness"));
            side.Children.Add(Slider(v => _spec.Brightness = (int)v));
            side.Children.Add(Theme.Label("Contrast"));
            side.Children.Add(Slider(v => _spec.Contrast = (int)v));
            side.Children.Add(Theme.Label("Saturation"));
            side.Children.Add(Slider(v => _spec.Saturation = (int)v));

            var bwRow = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var bw = new KToggle(false);
            _bw = bw;
            bw.Toggled += on => { _spec.Grayscale = on; Schedule(); };
            DockPanel.SetDock(bw, Dock.Right);
            bwRow.Children.Add(bw);
            bwRow.Children.Add(new TextBlock { Text = "BLACK & WHITE", FontFamily = Theme.Display, FontSize = 9.5, Foreground = Theme.InkDimBrush, VerticalAlignment = VerticalAlignment.Center });
            side.Children.Add(bwRow);

            side.Children.Add(Theme.Label("Resize (px)"));
            var rs = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            _wBox = Theme.Input(fullW.ToString(), 76);
            _hBox = Theme.Input(fullH.ToString(), 76);
            rs.Children.Add(_wBox);
            rs.Children.Add(new TextBlock { Text = " × ", Foreground = Theme.InkDimBrush, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            rs.Children.Add(_hBox);
            _lock = new KToggle(true) { Margin = new Thickness(12, 0, 6, 0) };
            rs.Children.Add(_lock);
            rs.Children.Add(new TextBlock { Text = "", FontFamily = Theme.Icons, Foreground = Theme.InkDimBrush, VerticalAlignment = VerticalAlignment.Center });
            side.Children.Add(rs);
            _dims = Theme.Text("", 11.5, Theme.InkFaintBrush);
            _dims.Margin = new Thickness(0, 6, 0, 0);
            side.Children.Add(_dims);

            var pct = new WrapPanel { Margin = new Thickness(-4, 6, 0, 0) };
            foreach (var p in new[] { 25, 50, 75, 100 })
            {
                int pp = p;
                pct.Children.Add(Btn(p + "%", null, () => SetPercent(pp), small: true));
            }
            side.Children.Add(pct);

            _wBox.TextChanged += (s, e) => OnSizeTyped(true);
            _hBox.TextChanged += (s, e) => OnSizeTyped(false);

            var reset = new KButton("Reset");
            reset.Click += () =>
            {
                foreach (var sl in _sliders) sl.Value = 0;
                _bw.IsOn = false;
                _spec.Rotation = 0; _spec.FlipH = _spec.FlipV = false; _spec.Grayscale = false;
                _spec.Brightness = _spec.Contrast = _spec.Saturation = 0;
                _syncing = true; _wBox.Text = _fullW.ToString(); _hBox.Text = _fullH.ToString(); _syncing = false;
                Schedule();
            };
            var cancel = new KButton("Cancel");
            cancel.Click += () => Close();
            var save = new KButton("Save copy", true, "");
            save.Click += () => { ReadResize(); Result = _spec; Close(); };

            var root = new StackPanel();
            root.Children.Add(grid);
            root.Children.Add(Footer(new UIElement[] { reset }, new UIElement[] { cancel, save }));
            SetBody(root);

            _debounce.Tick += (s, e) => { _debounce.Stop(); Render(); };
            Closed += (s, e) => _previewBase.Dispose();
            UpdateDims();
            Render();
        }

        private readonly List<KSlider> _sliders = new List<KSlider>();
        private KToggle _bw;

        private KButton Btn(string text, string glyph, Action act, bool mirror = false, bool small = false)
        {
            var b = new KButton(text, false, glyph) { Margin = new Thickness(0, 0, 6, 6) };
            if (small) b.Padding = new Thickness(10, 6, 10, 6);
            if (mirror) { b.RenderTransformOrigin = new Point(0.5, 0.5); b.RenderTransform = new ScaleTransform(-1, 1); }
            b.Click += () => { act(); Schedule(); };
            return b;
        }

        private KSlider Slider(Action<double> set)
        {
            var s = new KSlider(-100, 100, 0, 270) { Format = v => (v > 0 ? "+" : "") + v.ToString("0") };
            s.ValueChanged += v => { set(v); Schedule(); };
            _sliders.Add(s);
            return s;
        }

        private void Schedule() { _debounce.Stop(); _debounce.Start(); }

        private void Render()
        {
            try
            {
                using var img = _previewBase.Clone();
                Tools.ApplyEdit(img, _spec, includeResize: false);
                _image.Source = Imaging.ToBitmapSource(img);
            }
            catch (Exception ex) { Log.Error("Edit preview failed", ex); }
            UpdateDims();
        }

        private (int w, int h) RotatedFull => _spec.Rotation % 180 == 0 ? (_fullW, _fullH) : (_fullH, _fullW);

        private void SwapResize()
        {
            _syncing = true;
            var t = _wBox.Text; _wBox.Text = _hBox.Text; _hBox.Text = t;
            _syncing = false;
        }

        private void SetPercent(int p)
        {
            var (w, h) = RotatedFull;
            _syncing = true;
            _wBox.Text = Math.Max(1, (int)Math.Round(w * p / 100.0)).ToString();
            _hBox.Text = Math.Max(1, (int)Math.Round(h * p / 100.0)).ToString();
            _syncing = false;
            UpdateDims();
        }

        private void OnSizeTyped(bool widthEdited)
        {
            if (_syncing) return;
            var (w, h) = RotatedFull;
            if (_lock.IsOn)
            {
                _syncing = true;
                if (widthEdited && int.TryParse(_wBox.Text, out int nw) && nw > 0)
                    _hBox.Text = Math.Max(1, (int)Math.Round(nw * (double)h / w)).ToString();
                else if (!widthEdited && int.TryParse(_hBox.Text, out int nh) && nh > 0)
                    _wBox.Text = Math.Max(1, (int)Math.Round(nh * (double)w / h)).ToString();
                _syncing = false;
            }
            UpdateDims();
        }

        private void ReadResize()
        {
            var (w, h) = RotatedFull;
            int.TryParse(_wBox.Text, out int nw);
            int.TryParse(_hBox.Text, out int nh);
            if (nw <= 0 || nh <= 0 || (nw == w && nh == h)) { _spec.ResizeW = 0; _spec.ResizeH = 0; return; }
            _spec.ResizeW = Math.Min(nw, 30000);
            _spec.ResizeH = Math.Min(nh, 30000);
        }

        private void UpdateDims()
        {
            if (_dims == null) return;
            var (w, h) = RotatedFull;
            _dims.Text = $"Original {w} × {h} px";
        }
    }
}
