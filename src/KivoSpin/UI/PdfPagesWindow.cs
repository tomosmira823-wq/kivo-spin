using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KivoSpin.Core;

namespace KivoSpin.UI
{
    /// <summary>PDF page editor: rotate, delete, reorder, extract.</summary>
    public class PdfPagesWindow : KivoWindow
    {
        public List<PdfTools.PageOp> Result { get; private set; }
        public bool ExtractOnly { get; private set; }

        private class PageCard
        {
            public PdfTools.PageOp Op;
            public Border Card;
            public Image Thumb;
            public TextBlock Number;
            public bool Selected;
        }

        private readonly List<PageCard> _cards = new List<PageCard>();
        private readonly WrapPanel _wrap = new WrapPanel();
        private readonly TextBlock _status;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private int _anchor = -1;

        public PdfPagesWindow(string pdf, int pageCount) : base("Pages  ·  " + Path.GetFileName(pdf), 900)
        {
            var root = new StackPanel();

            var tools = new WrapPanel { Margin = new Thickness(-4, 0, 0, 8) };
            tools.Children.Add(Tool("Rotate left", "", () => RotateSel(-90), mirror: true));
            tools.Children.Add(Tool("Rotate right", "", () => RotateSel(90)));
            tools.Children.Add(Tool("Move earlier", "", () => MoveSel(-1)));
            tools.Children.Add(Tool("Move later", "", () => MoveSel(1)));
            tools.Children.Add(Tool("Delete", "", DeleteSel));
            tools.Children.Add(Tool("Select all", "", () => { foreach (var c in _cards) c.Selected = true; Repaint(); }));
            root.Children.Add(tools);

            var scroll = new ScrollViewer
            {
                Height = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _wrap
            };
            root.Children.Add(new Border
            {
                Background = Theme.WellBrush, BorderBrush = Theme.EdgeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Child = scroll
            });

            _status = Theme.Text("Click to select, Ctrl or Shift for several.", 12, Theme.InkDimBrush);
            _status.Margin = new Thickness(2, 10, 0, 0);
            root.Children.Add(_status);

            var extract = new KButton("Extract selected", false, "");
            extract.Click += () =>
            {
                var sel = _cards.Where(c => c.Selected).Select(c => c.Op).ToList();
                if (sel.Count == 0) { _status.Text = "Select the pages you want to pull out first."; return; }
                Result = sel; ExtractOnly = true; Close();
            };
            var cancel = new KButton("Cancel");
            cancel.Click += () => Close();
            var save = new KButton("Save copy", true, "");
            save.Click += () =>
            {
                if (_cards.Count == 0) { _status.Text = "There are no pages left."; return; }
                Result = _cards.Select(c => c.Op).ToList(); Close();
            };
            root.Children.Add(Footer(new UIElement[] { extract }, new UIElement[] { cancel, save }));
            SetBody(root);

            for (int i = 0; i < pageCount; i++)
            {
                var card = MakeCard(new PdfTools.PageOp { Source = i, AddRotation = 0 });
                _cards.Add(card);
                _wrap.Children.Add(card.Card);
            }
            Renumber();

            Closed += (s, e) => _cts.Cancel();
            var token = _cts.Token;
            Task.Run(() =>
            {
                try
                {
                    PdfRender.RenderPages(pdf, 30, null, (i, bmp, eff) =>
                    {
                        Ui.Post(() =>
                        {
                            var c = _cards.FirstOrDefault(x => x.Op.Source == i);
                            if (c != null) c.Thumb.Source = bmp;
                        });
                    }, () => token.IsCancellationRequested);
                }
                catch (Exception ex) { Log.Error("Thumbnail render failed", ex); }
            });
        }

        private KButton Tool(string text, string glyph, Action act, bool mirror = false)
        {
            var b = new KButton(text, false, glyph);
            if (mirror && b.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock g)
            {
                g.RenderTransformOrigin = new Point(0.5, 0.5);
                g.RenderTransform = new ScaleTransform(-1, 1);
            }
            b.Click += act;
            return b;
        }

        private PageCard MakeCard(PdfTools.PageOp op)
        {
            var pc = new PageCard { Op = op };
            pc.Thumb = new Image { Width = 120, Height = 150, Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5) };
            pc.Number = new TextBlock { FontFamily = Theme.Display, FontSize = 9.5, Foreground = Theme.InkDimBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            var sp = new StackPanel();
            sp.Children.Add(new Border { Background = Theme.B(0xF4, 0xF4, 0xF4), Child = pc.Thumb, Width = 120, Height = 150 });
            sp.Children.Add(pc.Number);
            pc.Card = new Border
            {
                Child = sp,
                Padding = new Thickness(8),
                Margin = new Thickness(6),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };
            pc.Card.MouseLeftButtonUp += (s, e) => Click(pc);
            return pc;
        }

        private void Click(PageCard pc)
        {
            int idx = _cards.IndexOf(pc);
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (shift && _anchor >= 0)
            {
                int a = Math.Min(_anchor, idx), b = Math.Max(_anchor, idx);
                for (int i = 0; i < _cards.Count; i++) _cards[i].Selected = (i >= a && i <= b) || (ctrl && _cards[i].Selected);
            }
            else if (ctrl)
            {
                pc.Selected = !pc.Selected;
                _anchor = idx;
            }
            else
            {
                foreach (var c in _cards) c.Selected = c == pc;
                _anchor = idx;
            }
            Repaint();
        }

        private void RotateSel(int deg)
        {
            var sel = _cards.Where(c => c.Selected).ToList();
            if (sel.Count == 0) { _status.Text = "Select a page first."; return; }
            foreach (var c in sel) c.Op.AddRotation = (((c.Op.AddRotation + deg) % 360) + 360) % 360;
            Repaint();
        }

        private void MoveSel(int dir)
        {
            var idxs = _cards.Select((c, i) => (c, i)).Where(t => t.c.Selected).Select(t => t.i).ToList();
            if (idxs.Count == 0) { _status.Text = "Select a page first."; return; }
            if (dir < 0)
            {
                if (idxs[0] == 0) return;
                foreach (var i in idxs) (_cards[i - 1], _cards[i]) = (_cards[i], _cards[i - 1]);
            }
            else
            {
                if (idxs[idxs.Count - 1] == _cards.Count - 1) return;
                for (int k = idxs.Count - 1; k >= 0; k--) { int i = idxs[k]; (_cards[i + 1], _cards[i]) = (_cards[i], _cards[i + 1]); }
            }
            Rebuild();
        }

        private void DeleteSel()
        {
            int n = _cards.RemoveAll(c => c.Selected);
            if (n == 0) { _status.Text = "Select a page first."; return; }
            Rebuild();
            _status.Text = n == 1 ? "1 page removed." : n + " pages removed.";
        }

        private void Rebuild()
        {
            _wrap.Children.Clear();
            foreach (var c in _cards) _wrap.Children.Add(c.Card);
            Renumber();
            Repaint();
        }

        private void Renumber()
        {
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].Number.Text = (i + 1) + (_cards[i].Op.Source != i ? $"  (was {_cards[i].Op.Source + 1})" : "");
            Repaint();
        }

        private void Repaint()
        {
            foreach (var c in _cards)
            {
                c.Card.BorderBrush = c.Selected ? Theme.AccentBrush : Brushes.Transparent;
                c.Card.Background = c.Selected ? Theme.B(0x22, Theme.Accent) : Brushes.Transparent;
                c.Thumb.RenderTransform = new RotateTransform(c.Op.AddRotation);
            }
            int s = _cards.Count(c => c.Selected);
            if (s > 0) _status.Text = s == 1 ? "1 page selected." : s + " pages selected.";
        }
    }
}
