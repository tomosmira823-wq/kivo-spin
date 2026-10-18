using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KivoSpin.UI
{
    /// <summary>Hardware-style push button.</summary>
    public class KButton : Border
    {
        public event Action Click;
        private readonly bool _primary;
        private readonly TextBlock _text;
        private readonly TextBlock _glyph;
        private bool _pressed;

        public KButton(string text, bool primary = false, string glyph = null)
        {
            _primary = primary;
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(14, 8, 14, 8);
            Margin = new Thickness(4);
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            Focusable = false;
            SnapsToDevicePixels = true;

            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(glyph))
            {
                _glyph = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                if (!string.IsNullOrEmpty(text)) _glyph.Margin = new Thickness(0, 0, 8, 0);
                sp.Children.Add(_glyph);
            }
            _text = new TextBlock { Text = (text ?? "").ToUpperInvariant(), FontFamily = Theme.Display, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(text)) sp.Children.Add(_text);
            Child = sp;

            Paint(false);
            MouseEnter += (s, e) => Paint(true);
            MouseLeave += (s, e) => Paint(false);
            MouseLeftButtonDown += (s, e) => { _pressed = true; CaptureMouse(); Paint(true, true); e.Handled = true; };
            MouseLeftButtonUp += (s, e) =>
            {
                if (_pressed)
                {
                    _pressed = false;
                    ReleaseMouseCapture();
                    Paint(IsMouseOver);
                    if (IsMouseOver && IsEnabled) Click?.Invoke();
                }
                e.Handled = true;
            };
            IsEnabledChanged += (s, e) => Opacity = IsEnabled ? 1 : 0.38;
        }

        public string Text { get => _text.Text; set => _text.Text = (value ?? "").ToUpperInvariant(); }

        public void PerformClick() { if (IsEnabled) Click?.Invoke(); }

        private void Paint(bool hover, bool down = false)
        {
            Color top, bottom, border; Brush ink;
            if (_primary)
            {
                top = hover ? Color.FromRgb(0x6A, 0xFF, 0xCB) : Theme.Accent;
                bottom = down ? Theme.AccentDeep : Color.FromRgb(0x23, 0xC9, 0x93);
                border = Color.FromRgb(0x0B, 0x4A, 0x37);
                ink = Theme.B(Theme.AccentInk);
            }
            else
            {
                top = hover ? Color.FromRgb(0x3E, 0x43, 0x4B) : Color.FromRgb(0x33, 0x37, 0x3E);
                bottom = down ? Color.FromRgb(0x18, 0x1A, 0x1D) : Color.FromRgb(0x1F, 0x22, 0x26);
                border = Theme.Edge;
                ink = hover ? Theme.AccentBrush : Theme.InkBrush;
            }
            var g = new LinearGradientBrush(top, bottom, 90);
            g.Freeze();
            Background = g;
            BorderBrush = Theme.B(border);
            _text.Foreground = ink;
            if (_glyph != null) _glyph.Foreground = ink;
        }
    }

    /// <summary>LED-track slider.</summary>
    public class KSlider : FrameworkElement
    {
        public double Minimum { get; set; }
        public double Maximum { get; set; } = 100;
        public double Step { get; set; } = 1;
        public event Action<double> ValueChanged;
        public Func<double, string> Format { get; set; } = v => v.ToString("0");

        private double _value;
        public double Value
        {
            get => _value;
            set
            {
                var v = Math.Clamp(Math.Round(value / Step) * Step, Minimum, Maximum);
                if (Math.Abs(v - _value) < 1e-9) return;
                _value = v;
                InvalidateVisual();
                ValueChanged?.Invoke(v);
            }
        }

        public KSlider(double min, double max, double value, double width = 260)
        {
            Minimum = min; Maximum = max; _value = Math.Clamp(value, min, max);
            Width = width; Height = 30;
            Cursor = Cursors.Hand;
            Margin = new Thickness(0, 2, 0, 2);
        }

        private const double ValueWidth = 46;
        private double TrackLeft => 8;
        private double TrackRight => ActualWidth - ValueWidth - 8;

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, cy = h / 2;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            double l = TrackLeft, r = TrackRight;
            if (r <= l) return;
            double t = (Value - Minimum) / Math.Max(1e-9, Maximum - Minimum);
            double x = l + (r - l) * t;

            dc.DrawRoundedRectangle(Theme.WellBrush, new Pen(Theme.EdgeBrush, 1), new Rect(l, cy - 3, r - l, 6), 3, 3);
            // LED segments
            int segs = (int)((r - l) / 7);
            for (int i = 0; i <= segs; i++)
            {
                double sx = l + i * (r - l) / Math.Max(1, segs);
                bool lit = sx <= x;
                dc.DrawRectangle(lit ? Theme.AccentBrush : Theme.B(0x26, Theme.Ink), null, new Rect(sx - 1, cy - 1.5, 2, 3));
            }
            // knob
            var knob = new RadialGradientBrush(Color.FromRgb(0x5A, 0x60, 0x69), Color.FromRgb(0x1C, 0x1F, 0x23)) { GradientOrigin = new Point(0.35, 0.3) };
            knob.Freeze();
            dc.DrawEllipse(Theme.B(0x55, Colors.Black), null, new Point(x, cy + 1.5), 9.5, 9.5);
            dc.DrawEllipse(knob, new Pen(Theme.EdgeBrush, 1), new Point(x, cy), 8.5, 8.5);
            dc.DrawEllipse(Theme.AccentBrush, null, new Point(x, cy), 2.2, 2.2);

            var ft = new FormattedText(Format(Value), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Theme.Display, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 10.5, Theme.AccentBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(w - ft.Width, cy - ft.Height / 2));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            CaptureMouse();
            SetFrom(e.GetPosition(this).X);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured) SetFrom(e.GetPosition(this).X);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            ReleaseMouseCapture();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Value += Math.Sign(e.Delta) * Step;
            e.Handled = true;
        }

        private void SetFrom(double x)
        {
            double t = (x - TrackLeft) / Math.Max(1, TrackRight - TrackLeft);
            Value = Minimum + Math.Clamp(t, 0, 1) * (Maximum - Minimum);
        }
    }

    /// <summary>Row of pill buttons, one selected.</summary>
    public class KSegment : Border
    {
        public event Action<int> Changed;
        private readonly List<Border> _pills = new List<Border>();
        private int _selected;

        public int Selected
        {
            get => _selected;
            set { _selected = value; Repaint(); }
        }

        public KSegment(IEnumerable<string> options, int selected = 0)
        {
            Background = Theme.WellBrush;
            BorderBrush = Theme.EdgeBrush;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(9);
            Padding = new Thickness(3);
            HorizontalAlignment = HorizontalAlignment.Left;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            int i = 0;
            foreach (var o in options)
            {
                int idx = i++;
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(11, 6, 11, 6),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = o.ToUpperInvariant(), FontFamily = Theme.Display, FontSize = 9.5 }
                };
                pill.MouseLeftButtonUp += (s, e) => { Selected = idx; Changed?.Invoke(idx); e.Handled = true; };
                pill.MouseEnter += (s, e) => Repaint();
                pill.MouseLeave += (s, e) => Repaint();
                _pills.Add(pill);
                sp.Children.Add(pill);
            }
            Child = sp;
            _selected = selected;
            Repaint();
        }

        private void Repaint()
        {
            for (int i = 0; i < _pills.Count; i++)
            {
                var p = _pills[i];
                var tb = (TextBlock)p.Child;
                bool on = i == _selected;
                if (on)
                {
                    var g = new LinearGradientBrush(Theme.Accent, Color.FromRgb(0x23, 0xC9, 0x93), 90);
                    g.Freeze();
                    p.Background = g;
                    tb.Foreground = Theme.B(Theme.AccentInk);
                }
                else
                {
                    p.Background = p.IsMouseOver ? Theme.B(0x22, Theme.Ink) : Brushes.Transparent;
                    tb.Foreground = p.IsMouseOver ? Theme.InkBrush : Theme.InkDimBrush;
                }
            }
        }
    }

    /// <summary>Toggle switch with an LED.</summary>
    public class KToggle : FrameworkElement
    {
        public event Action<bool> Toggled;
        private bool _on;
        public bool IsOn { get => _on; set { _on = value; InvalidateVisual(); } }

        public KToggle(bool on)
        {
            _on = on;
            Width = 46; Height = 24;
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var r = new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1);
            dc.DrawRoundedRectangle(_on ? Theme.B(0x40, Theme.Accent) : Theme.WellBrush, new Pen(_on ? Theme.B(Theme.AccentDeep) : Theme.EdgeBrush, 1), r, r.Height / 2, r.Height / 2);
            double kx = _on ? r.Right - r.Height / 2 : r.Left + r.Height / 2;
            var knob = new RadialGradientBrush(Color.FromRgb(0x62, 0x68, 0x71), Color.FromRgb(0x22, 0x25, 0x2A)) { GradientOrigin = new Point(0.35, 0.3) };
            knob.Freeze();
            dc.DrawEllipse(knob, new Pen(Theme.EdgeBrush, 1), new Point(kx, r.Top + r.Height / 2), r.Height / 2 - 3, r.Height / 2 - 3);
            dc.DrawEllipse(_on ? Theme.AccentBrush : Theme.B(0x40, 0x45, 0x4C), null, new Point(kx, r.Top + r.Height / 2), 2.4, 2.4);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            IsOn = !IsOn;
            Toggled?.Invoke(IsOn);
            e.Handled = true;
        }
    }

    /// <summary>Panel corner screw.</summary>
    public class Screw : FrameworkElement
    {
        private readonly double _angle;
        public Screw(double angle) { _angle = angle; Width = 12; Height = 12; IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            var c = new Point(6, 6);
            var body = new RadialGradientBrush(Color.FromRgb(0x6B, 0x71, 0x7A), Color.FromRgb(0x23, 0x26, 0x2B)) { GradientOrigin = new Point(0.35, 0.3) };
            body.Freeze();
            dc.DrawEllipse(body, new Pen(Theme.EdgeBrush, 1), c, 5, 5);
            dc.PushTransform(new RotateTransform(_angle, c.X, c.Y));
            dc.DrawLine(new Pen(Theme.B(0x0D, 0x0E, 0x10), 1.6), new Point(c.X - 3.4, c.Y), new Point(c.X + 3.4, c.Y));
            dc.Pop();
        }
    }
}
