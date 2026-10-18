using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KivoSpin.Core;

namespace KivoSpin.UI
{
    /// <summary>Draws the radial wheel: metal bezel, LED ring, glass wedges, hub and caption.</summary>
    public class WheelVisual : FrameworkElement
    {
        public const double Margin0 = 34;    // bezel + LEDs outside R
        public const double CaptionSpace = 46;

        public double R = 170;
        public double Inner => R * 0.37;
        public List<WheelItem> Items = new List<WheelItem>();
        public int Hover = -1;
        public bool HubHover;
        public double HubProgress;
        public bool Revealed;
        public string HubTitle = "", HubSub = "", Caption = "";
        public bool Compact; // smaller text (welcome preview)

        public Point Center => new Point(R + Margin0, R + Margin0);
        public double TotalWidth => 2 * (R + Margin0);
        public double TotalHeight => 2 * (R + Margin0) + CaptionSpace;

        private double Span => Items.Count == 0 ? 360 : 360.0 / Items.Count;

        /// <summary>-2 = hub, -1 = nothing, otherwise wedge index.</summary>
        public int HitTest(Point p)
        {
            var c = Center;
            double dx = p.X - c.X, dy = p.Y - c.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= Inner) return -2;
            if (d > R + 24 || Items.Count == 0) return -1;
            double ang = Math.Atan2(dy, dx) * 180 / Math.PI;      // -180..180, 0 = right, 90 = down
            double rel = ang + 90 + Span / 2;                    // first wedge centred at top
            rel = ((rel % 360) + 360) % 360;
            int idx = (int)(rel / Span);
            return Math.Clamp(idx, 0, Items.Count - 1);
        }

        private static Point Polar(Point c, double r, double deg)
        {
            double a = deg * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }

        private FormattedText Txt(string s, FontFamily f, double size, Brush b) =>
            new FormattedText(s ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(f, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, b,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

        protected override void OnRender(DrawingContext dc)
        {
            var c = Center;

            // Near-invisible disc so the window receives drag events even before it is revealed.
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null, c, R + Margin0 - 2, R + Margin0 - 2);
            if (!Revealed) return;

            // drop shadow
            var shadow = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Point(c.X, c.Y + 6),
                GradientOrigin = new Point(c.X, c.Y + 6),
                RadiusX = R + Margin0,
                RadiusY = R + Margin0
            };
            shadow.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 0.82));
            shadow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
            dc.DrawEllipse(shadow, null, new Point(c.X, c.Y + 6), R + Margin0, R + Margin0);

            // bezel
            var bezel = new LinearGradientBrush(Color.FromRgb(0x4C, 0x51, 0x59), Color.FromRgb(0x18, 0x1A, 0x1E), 90);
            dc.DrawEllipse(bezel, new Pen(Theme.EdgeBrush, 1.2), c, R + 23, R + 23);
            dc.DrawEllipse(Theme.Brushed, null, c, R + 23, R + 23);
            dc.DrawEllipse(null, new Pen(Theme.B(0x22, Colors.White), 1), c, R + 21.5, R + 21.5);
            dc.DrawEllipse(Theme.WellBrush, new Pen(Theme.EdgeBrush, 1), c, R + 5, R + 5);

            // LED ring
            const int leds = 60;
            for (int i = 0; i < leds; i++)
            {
                double deg = -90 + i * 360.0 / leds;
                bool lit = false;
                if (Hover >= 0 && Items.Count > 0)
                {
                    double centre = -90 + Hover * Span;
                    double diff = Math.Abs(((deg - centre + 540) % 360) - 180);
                    lit = diff <= Span / 2 + 0.5;
                }
                else if (HubHover)
                {
                    lit = (i / (double)leds) <= HubProgress;
                }
                var p = Polar(c, R + 13.5, deg);
                if (lit)
                {
                    dc.DrawEllipse(Theme.B(0x55, Theme.Accent), null, p, 4.6, 4.6);
                    dc.DrawEllipse(Theme.AccentBrush, null, p, 1.9, 1.9);
                }
                else dc.DrawEllipse(Theme.B(0x2B, 0x2F, 0x35), null, p, 1.6, 1.6);
            }

            // wedges
            double gap = Items.Count > 1 ? 2.2 : 0;
            double rIn = Inner + 4, rOut = R;
            var glass = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = c, GradientOrigin = c, RadiusX = R, RadiusY = R
            };
            glass.GradientStops.Add(new GradientStop(Color.FromRgb(0x2D, 0x31, 0x38), Inner / R));
            glass.GradientStops.Add(new GradientStop(Color.FromRgb(0x17, 0x19, 0x1C), 1.0));
            var lit2 = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = c, GradientOrigin = c, RadiusX = R, RadiusY = R
            };
            lit2.GradientStops.Add(new GradientStop(Color.FromRgb(0x7D, 0xFF, 0xD3), Inner / R));
            lit2.GradientStops.Add(new GradientStop(Theme.Accent, 0.7));
            lit2.GradientStops.Add(new GradientStop(Color.FromRgb(0x1F, 0xBF, 0x8A), 1.0));
            var deadFill = Theme.B(0x13, 0x14, 0x17);

            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                double mid = -90 + i * Span;
                double a0 = mid - Span / 2 + gap / 2, a1 = mid + Span / 2 - gap / 2;
                bool hot = i == Hover && item.Enabled;

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    if (Items.Count == 1)
                    {
                        ctx.BeginFigure(Polar(c, rOut, -90), true, true);
                        ctx.ArcTo(Polar(c, rOut, 89.9), new Size(rOut, rOut), 0, false, SweepDirection.Clockwise, true, false);
                        ctx.ArcTo(Polar(c, rOut, 269.8), new Size(rOut, rOut), 0, false, SweepDirection.Clockwise, true, false);
                    }
                    else
                    {
                        ctx.BeginFigure(Polar(c, rIn, a0), true, true);
                        ctx.LineTo(Polar(c, rOut, a0), true, false);
                        ctx.ArcTo(Polar(c, rOut, a1), new Size(rOut, rOut), 0, (a1 - a0) > 180, SweepDirection.Clockwise, true, false);
                        ctx.LineTo(Polar(c, rIn, a1), true, false);
                        ctx.ArcTo(Polar(c, rIn, a0), new Size(rIn, rIn), 0, (a1 - a0) > 180, SweepDirection.Counterclockwise, true, false);
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(!item.Enabled ? deadFill : hot ? lit2 : glass,
                    new Pen(hot ? Theme.B(0x9A, 0xFF, 0xE0) : Theme.EdgeBrush, hot ? 1.2 : 1), geo);

                // label
                Brush ink = !item.Enabled ? Theme.B(0x44, 0x49, 0x50) : hot ? Theme.B(Theme.AccentInk) : Theme.B(0xD6, 0xDA, 0xDF);
                double rm = (rIn + rOut) / 2 + 2;
                var lp = Polar(c, rm, mid);
                double arc = 2 * Math.PI * rm * (Span / 360.0) * 0.8;
                bool hasGlyph = !string.IsNullOrEmpty(item.Glyph);
                double size = Compact ? 7.5 : (hasGlyph ? 8.8 : 11.5) * (R / 170.0);
                var ft = Txt(item.Label, Theme.Display, size, ink);
                if (ft.Width > arc) ft.SetFontSize(size * arc / ft.Width);
                if (hasGlyph)
                {
                    var gt = Txt(item.Glyph, Theme.Icons, (Compact ? 11 : 17) * (R / 170.0), ink);
                    dc.DrawText(gt, new Point(lp.X - gt.Width / 2, lp.Y - gt.Height + 2));
                    dc.DrawText(ft, new Point(lp.X - ft.Width / 2, lp.Y + 4));
                }
                else
                {
                    dc.DrawText(ft, new Point(lp.X - ft.Width / 2, lp.Y - ft.Height / 2));
                }
            }

            // hub
            double hr = Inner - 2;
            var hubFill = new RadialGradientBrush(Color.FromRgb(0x2B, 0x2F, 0x35), Color.FromRgb(0x0F, 0x11, 0x13))
            { GradientOrigin = new Point(0.4, 0.3) };
            dc.DrawEllipse(Theme.WellBrush, null, c, hr + 3, hr + 3);
            dc.DrawEllipse(hubFill, new Pen(HubHover ? Theme.AccentBrush : Theme.EdgeBrush, HubHover ? 1.5 : 1), c, hr, hr);
            dc.DrawEllipse(null, new Pen(Theme.B(0x18, Colors.White), 1), c, hr - 5, hr - 5);
            if (HubHover && HubProgress > 0)
            {
                double end = -90 + 359.9 * Math.Min(1, HubProgress);
                var arcGeo = new StreamGeometry();
                using (var ctx = arcGeo.Open())
                {
                    ctx.BeginFigure(Polar(c, hr - 5, -90), false, false);
                    ctx.ArcTo(Polar(c, hr - 5, end), new Size(hr - 5, hr - 5), 0, (end + 90) > 180, SweepDirection.Clockwise, true, false);
                }
                arcGeo.Freeze();
                dc.DrawGeometry(null, new Pen(Theme.AccentBrush, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arcGeo);
            }
            double htSize = (Compact ? 7.5 : 10.5) * (R / 170.0);
            var ht = Txt(HubTitle, Theme.Display, htSize, HubHover ? Theme.AccentBrush : Theme.InkBrush);
            if (ht.Width > hr * 1.6) ht.SetFontSize(htSize * hr * 1.6 / ht.Width);
            dc.DrawText(ht, new Point(c.X - ht.Width / 2, c.Y - ht.Height + 1));
            var hs = Txt(HubSub, Theme.Body, (Compact ? 7 : 10) * (R / 170.0), Theme.InkDimBrush);
            if (hs.Width > hr * 1.7) hs.SetFontSize(Math.Max(6, (Compact ? 7 : 10) * (R / 170.0) * hr * 1.7 / hs.Width));
            dc.DrawText(hs, new Point(c.X - hs.Width / 2, c.Y + 4));

            // caption pill
            if (!string.IsNullOrEmpty(Caption))
            {
                var ct = Txt(Caption, Theme.Body, 12.5, Theme.B(Theme.AccentInk));
                double maxW = TotalWidth - 20;
                if (ct.Width > maxW - 28) { ct.MaxTextWidth = maxW - 28; ct.Trimming = TextTrimming.CharacterEllipsis; ct.MaxLineCount = 1; }
                double w = Math.Min(maxW, ct.Width + 28), h = 26;
                var rect = new Rect(c.X - w / 2, c.Y + R + Margin0 + 8, w, h);
                dc.DrawRoundedRectangle(Theme.B(0x60, Colors.Black), null, new Rect(rect.X, rect.Y + 2, rect.Width, rect.Height), 13, 13);
                dc.DrawRoundedRectangle(Theme.AccentBrush, null, rect, 13, 13);
                dc.DrawText(ct, new Point(rect.X + (w - Math.Min(ct.Width, maxW - 28)) / 2, rect.Y + (h - ct.Height) / 2));
            }
        }
    }
}
