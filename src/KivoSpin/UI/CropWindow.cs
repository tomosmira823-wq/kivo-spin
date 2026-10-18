using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KivoSpin.UI
{
    /// <summary>Crop dialog: drag a box, pick an aspect ratio, or type exact pixel sizes.</summary>
    public class CropWindow : KivoWindow
    {
        public Int32Rect? Result { get; private set; }

        private readonly CropOverlay _overlay;
        private readonly TextBox _w, _h;
        private bool _syncing;
        private static readonly (string label, double ratio)[] Ratios =
            { ("Free", 0), ("1:1", 1), ("16:9", 16.0 / 9), ("9:16", 9.0 / 16), ("4:3", 4.0 / 3), ("3:4", 3.0 / 4) };

        public CropWindow(BitmapSource preview, int fullW, int fullH, string fileName) : base("Crop  ·  " + fileName, 760)
        {
            double maxW = 708, maxH = 470;
            double s = Math.Min(maxW / fullW, maxH / fullH);
            double dw = fullW * s, dh = fullH * s;

            var root = new StackPanel();

            var stage = new Grid { Width = dw, Height = dh, HorizontalAlignment = HorizontalAlignment.Center };
            stage.Children.Add(new Image { Source = preview, Stretch = Stretch.Fill, Width = dw, Height = dh });
            _overlay = new CropOverlay(fullW, fullH, s) { Width = dw, Height = dh };
            stage.Children.Add(_overlay);
            var well = new Border
            {
                Background = Theme.WellBrush,
                BorderBrush = Theme.EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Child = stage,
                Height = maxH + 26
            };
            root.Children.Add(well);

            // aspect row
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var aspectPanel = new StackPanel();
            aspectPanel.Children.Add(Theme.Label("Aspect ratio"));
            var seg = new KSegment(Ratios.Select(r => r.label), 0);
            seg.Changed += i => _overlay.SetAspect(Ratios[i].ratio);
            aspectPanel.Children.Add(seg);
            row.Children.Add(aspectPanel);

            var sizePanel = new StackPanel();
            sizePanel.Children.Add(Theme.Label("Size (px)"));
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
            _w = Theme.Input("", 74);
            _h = Theme.Input("", 74);
            sizeRow.Children.Add(Theme.Text("W ", 12, Theme.InkDimBrush, false));
            sizeRow.Children.Add(_w);
            sizeRow.Children.Add(new TextBlock { Text = "  H ", Foreground = Theme.InkDimBrush, FontFamily = Theme.Body, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            sizeRow.Children.Add(_h);
            sizePanel.Children.Add(sizeRow);
            Grid.SetColumn(sizePanel, 2);
            row.Children.Add(sizePanel);
            root.Children.Add(row);

            var reset = new KButton("Reset");
            reset.Click += () => { seg.Selected = 0; _overlay.SetAspect(0); _overlay.Reset(); };
            var cancel = new KButton("Cancel");
            cancel.Click += () => Close();
            var apply = new KButton("Apply", true, "");
            apply.Click += () =>
            {
                var r = _overlay.Selection;
                Result = new Int32Rect((int)Math.Round(r.X), (int)Math.Round(r.Y), Math.Max(1, (int)Math.Round(r.Width)), Math.Max(1, (int)Math.Round(r.Height)));
                Close();
            };
            root.Children.Add(Footer(new UIElement[] { reset }, new UIElement[] { cancel, apply }));
            SetBody(root);

            _overlay.Changed += SyncBoxes;
            _w.LostFocus += (s2, e) => FromBoxes(true);
            _h.LostFocus += (s2, e) => FromBoxes(false);
            _w.KeyDown += (s2, e) => { if (e.Key == Key.Enter) FromBoxes(true); };
            _h.KeyDown += (s2, e) => { if (e.Key == Key.Enter) FromBoxes(false); };
            KeyDown += (s2, e) => { if (e.Key == Key.Enter && !(_w.IsKeyboardFocused || _h.IsKeyboardFocused)) apply.PerformClick(); };
            SyncBoxes();
        }

        private void SyncBoxes()
        {
            _syncing = true;
            _w.Text = ((int)Math.Round(_overlay.Selection.Width)).ToString();
            _h.Text = ((int)Math.Round(_overlay.Selection.Height)).ToString();
            _syncing = false;
        }

        private void FromBoxes(bool widthEdited)
        {
            if (_syncing) return;
            if (!int.TryParse(_w.Text, out int w) || !int.TryParse(_h.Text, out int h)) { SyncBoxes(); return; }
            _overlay.SetSize(w, h, widthEdited);
        }
    }

    /// <summary>The draggable crop box. Selection is kept in full-resolution image pixels.</summary>
    public class CropOverlay : FrameworkElement
    {
        public event Action Changed;
        public Rect Selection { get; private set; }

        private readonly double _fw, _fh, _s;
        private double _aspect;
        private enum Zone { None, Move, L, R, T, B, TL, TR, BL, BR, New }
        private Zone _drag = Zone.None;
        private Point _start;
        private Rect _startSel;

        public CropOverlay(double fullW, double fullH, double scale)
        {
            _fw = fullW; _fh = fullH; _s = scale;
            Selection = new Rect(0, 0, fullW, fullH);
            ClipToBounds = false;
        }

        public void Reset()
        {
            Selection = new Rect(0, 0, _fw, _fh);
            Changed?.Invoke();
            InvalidateVisual();
        }

        public void SetAspect(double ratio)
        {
            _aspect = ratio;
            if (ratio > 0)
            {
                // Largest box of that ratio, 90% of the image, centred.
                double w = _fw, h = w / ratio;
                if (h > _fh) { h = _fh; w = h * ratio; }
                w *= 0.9; h *= 0.9;
                Selection = new Rect((_fw - w) / 2, (_fh - h) / 2, w, h);
                Changed?.Invoke();
                InvalidateVisual();
            }
        }

        public void SetSize(int w, int h, bool widthEdited)
        {
            double nw = Math.Clamp(w, 1, _fw), nh = Math.Clamp(h, 1, _fh);
            if (_aspect > 0)
            {
                if (widthEdited) nh = nw / _aspect; else nw = nh * _aspect;
                if (nw > _fw) { nw = _fw; nh = nw / _aspect; }
                if (nh > _fh) { nh = _fh; nw = nh * _aspect; }
            }
            double x = Math.Clamp(Selection.X, 0, _fw - nw), y = Math.Clamp(Selection.Y, 0, _fh - nh);
            Selection = new Rect(x, y, nw, nh);
            Changed?.Invoke();
            InvalidateVisual();
        }

        private Rect Disp => new Rect(Selection.X * _s, Selection.Y * _s, Selection.Width * _s, Selection.Height * _s);

        protected override void OnRender(DrawingContext dc)
        {
            var full = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(Brushes.Transparent, null, full);
            var d = Disp;
            var shade = new GeometryGroup { FillRule = FillRule.EvenOdd };
            shade.Children.Add(new RectangleGeometry(full));
            shade.Children.Add(new RectangleGeometry(d));
            dc.DrawGeometry(Theme.B(0xA0, Colors.Black), null, shade);

            var thin = new Pen(Theme.B(0x55, Colors.White), 0.8);
            for (int i = 1; i <= 2; i++)
            {
                dc.DrawLine(thin, new Point(d.X + d.Width * i / 3, d.Y), new Point(d.X + d.Width * i / 3, d.Bottom));
                dc.DrawLine(thin, new Point(d.X, d.Y + d.Height * i / 3), new Point(d.Right, d.Y + d.Height * i / 3));
            }
            dc.DrawRectangle(null, new Pen(Theme.AccentBrush, 1.6), d);
            foreach (var p in Handles(d))
                dc.DrawRectangle(Theme.AccentBrush, new Pen(Theme.B(Theme.AccentInk), 1), new Rect(p.X - 5, p.Y - 5, 10, 10));
        }

        private static IEnumerable<Point> Handles(Rect d)
        {
            double mx = d.X + d.Width / 2, my = d.Y + d.Height / 2;
            yield return new Point(d.Left, d.Top); yield return new Point(mx, d.Top); yield return new Point(d.Right, d.Top);
            yield return new Point(d.Left, my); yield return new Point(d.Right, my);
            yield return new Point(d.Left, d.Bottom); yield return new Point(mx, d.Bottom); yield return new Point(d.Right, d.Bottom);
        }

        private Zone HitZone(Point p)
        {
            var d = Disp;
            const double t = 9;
            bool nearL = Math.Abs(p.X - d.Left) <= t, nearR = Math.Abs(p.X - d.Right) <= t;
            bool nearT = Math.Abs(p.Y - d.Top) <= t, nearB = Math.Abs(p.Y - d.Bottom) <= t;
            bool inX = p.X > d.Left - t && p.X < d.Right + t, inY = p.Y > d.Top - t && p.Y < d.Bottom + t;
            if (nearL && nearT) return Zone.TL;
            if (nearR && nearT) return Zone.TR;
            if (nearL && nearB) return Zone.BL;
            if (nearR && nearB) return Zone.BR;
            if (nearL && inY) return Zone.L;
            if (nearR && inY) return Zone.R;
            if (nearT && inX) return Zone.T;
            if (nearB && inX) return Zone.B;
            if (d.Contains(p)) return Zone.Move;
            return Zone.New;
        }

        private static Cursor CursorFor(Zone z) => z switch
        {
            Zone.TL or Zone.BR => Cursors.SizeNWSE,
            Zone.TR or Zone.BL => Cursors.SizeNESW,
            Zone.L or Zone.R => Cursors.SizeWE,
            Zone.T or Zone.B => Cursors.SizeNS,
            Zone.Move => Cursors.SizeAll,
            _ => Cursors.Cross
        };

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _start = e.GetPosition(this);
            _drag = HitZone(_start);
            _startSel = Selection;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            _drag = Zone.None;
            ReleaseMouseCapture();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag == Zone.None) { Cursor = CursorFor(HitZone(p)); return; }

            double dx = (p.X - _start.X) / _s, dy = (p.Y - _start.Y) / _s;
            double l = _startSel.Left, t = _startSel.Top, r = _startSel.Right, b = _startSel.Bottom;

            switch (_drag)
            {
                case Zone.Move:
                    double w0 = _startSel.Width, h0 = _startSel.Height;
                    l = Math.Clamp(l + dx, 0, _fw - w0); t = Math.Clamp(t + dy, 0, _fh - h0);
                    Selection = new Rect(l, t, w0, h0);
                    Changed?.Invoke(); InvalidateVisual();
                    return;
                case Zone.New:
                    l = _start.X / _s; t = _start.Y / _s; r = p.X / _s; b = p.Y / _s;
                    break;
                case Zone.L: l += dx; break;
                case Zone.R: r += dx; break;
                case Zone.T: t += dy; break;
                case Zone.B: b += dy; break;
                case Zone.TL: l += dx; t += dy; break;
                case Zone.TR: r += dx; t += dy; break;
                case Zone.BL: l += dx; b += dy; break;
                case Zone.BR: r += dx; b += dy; break;
            }

            bool anchorRight = _drag == Zone.L || _drag == Zone.TL || _drag == Zone.BL;
            bool anchorBottom = _drag == Zone.T || _drag == Zone.TL || _drag == Zone.TR;
            if (l > r) { (l, r) = (r, l); anchorRight = !anchorRight; }
            if (t > b) { (t, b) = (b, t); anchorBottom = !anchorBottom; }
            l = Math.Max(0, l); t = Math.Max(0, t); r = Math.Min(_fw, r); b = Math.Min(_fh, b);
            double w = Math.Max(4, r - l), h = Math.Max(4, b - t);

            if (_aspect > 0)
            {
                if (_drag == Zone.T || _drag == Zone.B) w = h * _aspect;
                else h = w / _aspect;
                // keep inside the image
                double maxW = anchorRight ? r : _fw - l;
                double maxH = anchorBottom ? b : _fh - t;
                if (w > maxW) { w = maxW; h = w / _aspect; }
                if (h > maxH) { h = maxH; w = h * _aspect; }
                if (_drag == Zone.T || _drag == Zone.B)
                {
                    double cx = (_startSel.Left + _startSel.Right) / 2;
                    l = Math.Clamp(cx - w / 2, 0, _fw - w);
                }
                else if (anchorRight) l = r - w;
                if (_drag == Zone.L || _drag == Zone.R)
                {
                    double cy = (_startSel.Top + _startSel.Bottom) / 2;
                    t = Math.Clamp(cy - h / 2, 0, _fh - h);
                }
                else if (anchorBottom) t = b - h;
            }

            Selection = new Rect(Math.Max(0, l), Math.Max(0, t), Math.Min(w, _fw), Math.Min(h, _fh));
            Changed?.Invoke();
            InvalidateVisual();
        }
    }
}
