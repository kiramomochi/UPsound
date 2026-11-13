using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace UPsound
{
    /// <summary>Elemento desenhado à mão que anima por quadro e para de pedir quadros quando assenta.</summary>
    abstract class AnimatedElement : FrameworkElement
    {
        bool hooked;
        TimeSpan last;

        protected AnimatedElement()
        {
            FocusVisualStyle = null;
            SnapsToDevicePixels = false;
        }

        protected void Animate()
        {
            if (!Motion.Enabled) { Step(1); InvalidateVisual(); return; }
            if (hooked) return;
            hooked = true;
            last = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }

        void OnFrame(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            double dt = last == TimeSpan.Zero ? 1 / 60.0 : (now - last).TotalSeconds;
            if (dt <= 0) return;
            last = now;
            bool more = Step(Math.Min(dt, 0.1));
            InvalidateVisual();
            if (!more)
            {
                CompositionTarget.Rendering -= OnFrame;
                hooked = false;
            }
        }

        /// <summary>Avança a animação; devolve verdadeiro enquanto ainda há movimento.</summary>
        protected abstract bool Step(double dt);

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

        protected FormattedText Label(string text, double size, FontWeight weight, Color color)
        {
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(Theme.Font, FontStyles.Normal, weight, FontStretches.Normal),
                size, Theme.B(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        protected static bool ShowFocus(UIElement el)
        {
            return el.IsKeyboardFocused && KeyboardNavigation_IsKeyboardMode();
        }

        static bool KeyboardNavigation_IsKeyboardMode()
        {
            // O anel de foco aparece quando a navegação é por teclado (Tab), como nos apps da Apple.
            return InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        }
    }

    /// <summary>
    /// O controle principal: uma cápsula vertical de 0 a 500%. O primeiro quinto é o volume
    /// normal do sistema (sempre cheio); acima dele, o reforço, que esquenta de laranja a vermelho.
    /// </summary>
    sealed class BoostCapsule : AnimatedElement
    {
        public const double Min = 100, Max = 500;
        const double W = 104, Hgt = 300, Inset = 6, R = 30;

        double level = 150, shown = 150;
        bool active = true;
        double activeShown = 1;

        public event Action<double> UserChanged;

        public BoostCapsule()
        {
            Width = W + Inset * 2;
            Height = Hgt + Inset * 2;
            Focusable = true;
            Cursor = Cursors.Hand;
            AutomationProperties.SetName(this, "Nível de reforço");
            AutomationProperties.SetHelpText(this, "Setas para cima e para baixo ajustam de 5 em 5 por cento");
        }

        public double Level
        {
            get { return level; }
            set
            {
                double v = Math.Max(Min, Math.Min(Max, value));
                if (v == level) return;
                level = v;
                Animate();
            }
        }

        public bool Active
        {
            get { return active; }
            set { if (value == active) return; active = value; Animate(); }
        }

        public void SnapToTarget()
        {
            shown = level; activeShown = active ? 1 : 0; InvalidateVisual();
        }

        protected override bool Step(double dt)
        {
            shown = Motion.Approach(shown, level, dt, 16);
            activeShown = Motion.Approach(activeShown, active ? 1 : 0, dt, 10);
            return shown != level || activeShown != (active ? 1 : 0);
        }

        double YFor(double v) { return Inset + Hgt - v / Max * Hgt; }

        protected override void OnRender(DrawingContext dc)
        {
            var t = Theme.Current;
            var r = new Rect(Inset, Inset, W, Hgt);
            double yBase = YFor(100), yLevel = YFor(shown);

            dc.PushClip(new RectangleGeometry(r, R, R));
            dc.DrawRectangle(Theme.B(t.Track), null, r);

            if (shown > 100.01)
            {
                var fill = new Rect(Inset, yLevel, W, yBase - yLevel);
                // O degradê é fixo na cápsula: quanto mais alto o reforço, mais vermelho aparece no topo.
                var heat = new LinearGradientBrush(t.BoostLow, t.BoostHigh, new Point(0, yBase), new Point(0, Inset));
                heat.MappingMode = BrushMappingMode.Absolute;
                heat.Freeze();
                if (activeShown > 0)
                {
                    dc.PushOpacity(activeShown);
                    dc.DrawRectangle(heat, null, fill);
                    dc.Pop();
                }
                if (activeShown < 1)
                {
                    dc.PushOpacity(1 - activeShown);
                    dc.DrawRectangle(Theme.B(t.Idle, 0.45), null, fill);
                    dc.Pop();
                }
            }

            // Marcas discretas em 200, 300 e 400%.
            for (int v = 200; v <= 400; v += 100)
            {
                // Marca colada na frente do preenchimento vira um risco na borda: some ali.
                if (Math.Abs(v - shown) < 8) continue;
                double y = YFor(v);
                bool onFill = v < shown - 2;
                var pen = new Pen(onFill ? Theme.B(Colors.White, 0.55) : Theme.B(t.Text, 0.16), 1.5);
                pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                dc.DrawLine(pen, new Point(Inset + W - 18, y), new Point(Inset + W - 10, y));
            }

            // Volume normal do sistema: o bloco de base, separado por uma fresta.
            dc.DrawRectangle(Theme.B(t.Base), null, new Rect(Inset, yBase, W, Inset + Hgt - yBase));
            dc.DrawRectangle(Theme.B(t.Window), null, new Rect(Inset, yBase - 1, W, 2));
            dc.Pop();

            int lit = !active ? 1 : level <= 150 ? 1 : level <= 300 ? 2 : 3;
            DrawSpeaker(dc, new Point(Inset + W / 2, (yBase + Inset + Hgt) / 2), lit, t.BaseGlyph);

            if (ShowFocus(this))
            {
                var ring = r;
                ring.Inflate(3.5, 3.5);
                dc.DrawRoundedRectangle(null, new Pen(Theme.B(t.Accent), 2.5), ring, R + 3.5, R + 3.5);
            }
        }

        static void DrawSpeaker(DrawingContext dc, Point c, int lit, Color color)
        {
            double ox = c.X - 4, oy = c.Y;
            var body = new StreamGeometry();
            using (var g = body.Open())
            {
                g.BeginFigure(new Point(ox - 10, oy - 4), true, true);
                g.LineTo(new Point(ox - 5.5, oy - 4), true, true);
                g.LineTo(new Point(ox + 0.5, oy - 9.5), true, true);
                g.LineTo(new Point(ox + 0.5, oy + 9.5), true, true);
                g.LineTo(new Point(ox - 5.5, oy + 4), true, true);
                g.LineTo(new Point(ox - 10, oy + 4), true, true);
            }
            body.Freeze();
            var brush = Theme.B(color);
            dc.DrawGeometry(brush, new Pen(brush, 1.5) { LineJoin = PenLineJoin.Round }, body);

            for (int i = 0; i < 3; i++)
            {
                double rad = 5.5 + i * 4.5, a = 42 * Math.PI / 180;
                var p1 = new Point(ox + 1 + rad * Math.Cos(a), oy - rad * Math.Sin(a));
                var p2 = new Point(ox + 1 + rad * Math.Cos(a), oy + rad * Math.Sin(a));
                var wave = new StreamGeometry();
                using (var g = wave.Open())
                {
                    g.BeginFigure(p1, false, false);
                    g.ArcTo(p2, new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, true);
                }
                wave.Freeze();
                var pen = new Pen(Theme.B(color, i < lit ? 1 : 0.22), 2);
                pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                dc.DrawGeometry(null, pen, wave);
            }
        }

        void SetFromY(double y)
        {
            double v = (Inset + Hgt - y) / Hgt * Max;
            Commit(Math.Round(v / 5) * 5);
        }

        void Commit(double v)
        {
            v = Math.Max(Min, Math.Min(Max, v));
            bool changed = v != level || !active;
            Level = v;
            if (changed && UserChanged != null) UserChanged(v);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            CaptureMouse();
            SetFromY(e.GetPosition(this).Y);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured) SetFromY(e.GetPosition(this).Y);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            Commit(level + (e.Delta > 0 ? 5 : -5));
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up: case Key.Right: Commit(level + 5); break;
                case Key.Down: case Key.Left: Commit(level - 5); break;
                case Key.PageUp: Commit(level + 25); break;
                case Key.PageDown: Commit(level - 25); break;
                case Key.Home: Commit(Min); break;
                case Key.End: Commit(Max); break;
                default: return;
            }
            e.Handled = true;
        }
    }

    /// <summary>Interruptor no estilo iOS/macOS.</summary>
    sealed class ToggleSwitch : AnimatedElement
    {
        const double W = 42, Hgt = 26;
        bool isOn;
        double pos;

        public event Action<bool> Toggled;

        public ToggleSwitch(string name)
        {
            Width = W + 6; Height = Hgt + 6;
            Focusable = true;
            Cursor = Cursors.Hand;
            AutomationProperties.SetName(this, name);
        }

        public bool IsOn
        {
            get { return isOn; }
            set { if (value == isOn) return; isOn = value; Animate(); }
        }

        public void SnapToTarget() { pos = isOn ? 1 : 0; InvalidateVisual(); }

        protected override bool Step(double dt)
        {
            pos = Motion.Approach(pos, isOn ? 1 : 0, dt, 18);
            return pos != (isOn ? 1 : 0);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var t = Theme.Current;
            var r = new Rect(3, 3, W, Hgt);
            dc.DrawRoundedRectangle(Theme.B(Theme.Lerp(t.ToggleOff, t.Accent, pos)), null, r, Hgt / 2, Hgt / 2);
            double d = Hgt - 4, x = r.X + 2 + pos * (W - Hgt) + d / 2, y = r.Y + Hgt / 2;
            dc.DrawEllipse(Theme.B(Colors.Black, t.Dark ? 0.28 : 0.12), null, new Point(x, y + 1), d / 2 + 0.5, d / 2 + 0.5);
            dc.DrawEllipse(Theme.B(Colors.White), null, new Point(x, y), d / 2, d / 2);
            if (ShowFocus(this))
            {
                var ring = r; ring.Inflate(2.5, 2.5);
                dc.DrawRoundedRectangle(null, new Pen(Theme.B(t.Accent, 0.9), 2), ring, Hgt / 2 + 2.5, Hgt / 2 + 2.5);
            }
        }

        void Flip()
        {
            IsOn = !isOn;
            if (Toggled != null) Toggled(isOn);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus(); Flip(); e.Handled = true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Enter) { Flip(); e.Handled = true; }
        }
    }

    /// <summary>Controle segmentado com o marcador deslizando entre as opções.</summary>
    sealed class Segmented : AnimatedElement
    {
        readonly int[] values;
        int selected = -1;
        double thumbX, thumbAlpha;

        public event Action<int> Picked;

        public Segmented(int[] values)
        {
            this.values = values;
            Height = 30;
            Focusable = true;
            Cursor = Cursors.Hand;
            AutomationProperties.SetName(this, "Predefinições");
        }

        public int SelectedValue
        {
            set
            {
                int idx = Array.IndexOf(values, value);
                if (idx == selected) return;
                if (selected < 0 && idx >= 0) thumbX = idx;
                selected = idx;
                Animate();
            }
        }

        public void SnapToTarget()
        {
            if (selected >= 0) thumbX = selected;
            thumbAlpha = selected >= 0 ? 1 : 0;
            InvalidateVisual();
        }

        protected override bool Step(double dt)
        {
            if (selected >= 0) thumbX = Motion.Approach(thumbX, selected, dt, 20);
            thumbAlpha = Motion.Approach(thumbAlpha, selected >= 0 ? 1 : 0, dt, 14);
            return (selected >= 0 && thumbX != selected) || thumbAlpha != (selected >= 0 ? 1 : 0);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var t = Theme.Current;
            double w = ActualWidth, h = ActualHeight, seg = (w - 4) / values.Length;
            dc.DrawRoundedRectangle(Theme.B(t.SegTrack), null, new Rect(0, 0, w, h), 8, 8);

            if (thumbAlpha > 0)
            {
                var th = new Rect(2 + thumbX * seg, 2, seg, h - 4);
                dc.PushOpacity(thumbAlpha);
                if (!t.Dark) dc.DrawRoundedRectangle(Theme.B(Colors.Black, 0.10), null, new Rect(th.X, th.Y + 0.75, th.Width, th.Height), 6.5, 6.5);
                dc.DrawRoundedRectangle(Theme.B(t.SegThumb), null, th, 6, 6);
                dc.Pop();
            }

            for (int i = 0; i < values.Length; i++)
            {
                bool on = i == selected;
                var ft = Label(values[i].ToString(CultureInfo.InvariantCulture), 12, on ? FontWeights.SemiBold : FontWeights.Medium, on ? t.Text : t.Secondary);
                dc.DrawText(ft, new Point(2 + i * seg + (seg - ft.Width) / 2, (h - ft.Height) / 2));
            }

            if (ShowFocus(this))
                dc.DrawRoundedRectangle(null, new Pen(Theme.B(t.Accent), 2), new Rect(-2, -2, w + 4, h + 4), 10, 10);
        }

        void Pick(int idx)
        {
            idx = Math.Max(0, Math.Min(values.Length - 1, idx));
            if (Picked != null) Picked(values[idx]);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            Pick((int)((e.GetPosition(this).X - 2) / ((ActualWidth - 4) / values.Length)));
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int cur = selected < 0 ? 0 : selected;
            if (e.Key == Key.Right) { Pick(selected < 0 ? 0 : cur + 1); e.Handled = true; }
            else if (e.Key == Key.Left) { Pick(selected < 0 ? 0 : cur - 1); e.Handled = true; }
        }
    }

    /// <summary>Fechar e minimizar no canto esquerdo, como nas janelas do macOS.</summary>
    sealed class TrafficLights : FrameworkElement
    {
        static readonly Color Red = Theme.H("#FF5F57"), Yellow = Theme.H("#FEBC2E");
        bool windowActive = true, hover;

        public event Action CloseClicked, MinimizeClicked;

        public TrafficLights()
        {
            Width = 46; Height = 20;
            AutomationProperties.SetName(this, "Fechar ou minimizar");
        }

        public bool WindowActive { set { windowActive = value; InvalidateVisual(); } }

        protected override void OnRender(DrawingContext dc)
        {
            var t = Theme.Current;
            // Área de clique invisível para o hover cobrir o espaço entre os botões.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height));
            bool lit = windowActive || hover;
            Dot(dc, new Point(10, 10), lit ? Red : t.DotInactive, true);
            Dot(dc, new Point(30, 10), lit ? Yellow : t.DotInactive, false);
        }

        void Dot(DrawingContext dc, Point c, Color fill, bool close)
        {
            dc.DrawEllipse(Theme.B(fill), new Pen(Theme.B(Colors.Black, 0.12), 0.5), c, 6, 6);
            if (!hover) return;
            var pen = new Pen(Theme.B(Colors.Black, 0.55), 1.3);
            pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
            if (close)
            {
                dc.DrawLine(pen, new Point(c.X - 2.6, c.Y - 2.6), new Point(c.X + 2.6, c.Y + 2.6));
                dc.DrawLine(pen, new Point(c.X - 2.6, c.Y + 2.6), new Point(c.X + 2.6, c.Y - 2.6));
            }
            else dc.DrawLine(pen, new Point(c.X - 3.2, c.Y), new Point(c.X + 3.2, c.Y));
        }

        protected override void OnMouseEnter(MouseEventArgs e) { hover = true; InvalidateVisual(); }
        protected override void OnMouseLeave(MouseEventArgs e) { hover = false; InvalidateVisual(); }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { e.Handled = true; }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            double x = e.GetPosition(this).X;
            if (x < 20) { if (CloseClicked != null) CloseClicked(); }
            else if (MinimizeClicked != null) MinimizeClicked();
            e.Handled = true;
        }
    }

    /// <summary>Botão de texto: cheio (ação principal) ou só texto na cor de destaque.</summary>
    sealed class PillButton : AnimatedElement
    {
        string text;
        readonly bool primary;
        bool hover, pressed;

        public event Action Click;

        public PillButton(string text, bool primary)
        {
            this.text = text;
            this.primary = primary;
            Height = 28;
            Focusable = true;
            Cursor = Cursors.Hand;
            AutomationProperties.SetName(this, text);
        }

        public string Text
        {
            get { return text; }
            set { text = value; AutomationProperties.SetName(this, value); InvalidateMeasure(); InvalidateVisual(); }
        }

        protected override bool Step(double dt) { return false; }

        protected override Size MeasureOverride(Size available)
        {
            var ft = Label(text, 12, FontWeights.SemiBold, Colors.Black);
            return new Size(Math.Ceiling(ft.Width) + (primary ? 28 : 12), 28);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var t = Theme.Current;
            var r = new Rect(0, 0, ActualWidth, ActualHeight);
            Color fg;
            if (primary)
            {
                var bg = pressed ? Theme.Lerp(t.Accent, Colors.Black, 0.18) : hover ? Theme.Lerp(t.Accent, Colors.White, 0.10) : t.Accent;
                dc.DrawRoundedRectangle(Theme.B(bg), null, r, 7, 7);
                fg = Colors.White;
            }
            else
            {
                dc.DrawRectangle(Brushes.Transparent, null, r);
                fg = pressed ? Theme.Lerp(t.Accent, t.Text, 0.4) : t.Accent;
            }
            var ft = Label(text, 12, FontWeights.SemiBold, fg);
            if (!primary && hover) ft.SetTextDecorations(TextDecorations.Underline);
            dc.DrawText(ft, new Point((r.Width - ft.Width) / 2, (r.Height - ft.Height) / 2));
            if (ShowFocus(this))
            {
                var ring = r; ring.Inflate(2, 2);
                dc.DrawRoundedRectangle(null, new Pen(Theme.B(t.Accent, 0.6), 2), ring, 9, 9);
            }
        }

        protected override void OnMouseEnter(MouseEventArgs e) { hover = true; InvalidateVisual(); }
        protected override void OnMouseLeave(MouseEventArgs e) { hover = false; pressed = false; InvalidateVisual(); }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { pressed = true; CaptureMouse(); InvalidateVisual(); e.Handled = true; }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            bool fire = pressed && IsMouseOver;
            pressed = false;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            if (fire && Click != null) Click();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if ((e.Key == Key.Space || e.Key == Key.Enter) && Click != null) { Click(); e.Handled = true; }
        }
    }
}
