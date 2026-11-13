using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace UPsound
{
    sealed class MainWindow : Window
    {
        static readonly int[] PresetValues = { 100, 150, 200, 300, 500 };
        static readonly CultureInfo PtBr = new CultureInfo("pt-BR");
        const string ApoUrl = "https://sourceforge.net/projects/equalizerapo/";
        // Folga transparente em volta do cartão, para a sombra.
        static readonly Thickness ShadowRoom = new Thickness(28, 18, 28, 38);

        readonly Controller app;
        Grid outer;
        TranslateTransform slide;
        Border shadow, surface, prefsCard, setupCard, sep1, sep2, statusDot;
        TrafficLights lights;
        TextBlock title, number, dbText, warnText, presetLabel, hintText, statusText, setupTitle, setupBody;
        TextBlock boostLabel, startLabel, resetLabel;
        Run numRun, pctRun;
        BoostCapsule capsule;
        Segmented presets;
        ToggleSwitch boostSwitch, startSwitch, resetSwitch;
        PillButton setupPrimary, setupSecondary;
        Action primaryAction;
        bool hiding;

        public MainWindow(Controller app)
        {
            this.app = app;

            Title = "UPsound";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanMinimize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            FontFamily = Theme.Font;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            Build();
            Wire();
            ApplyTheme();
            Sync();
            capsule.SnapToTarget();
            presets.SnapToTarget();
            boostSwitch.SnapToTarget(); startSwitch.SnapToTarget(); resetSwitch.SnapToTarget();
        }

        // ---------- Montagem da interface ----------

        void Build()
        {
            outer = new Grid { Margin = ShadowRoom };
            slide = new TranslateTransform();
            outer.RenderTransform = slide;
            shadow = new Border { CornerRadius = new CornerRadius(14) };
            surface = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Width = 404 };
            outer.Children.Add(shadow);
            outer.Children.Add(surface);
            Content = outer;

            var layout = new StackPanel();
            surface.Child = layout;

            // Barra de título
            var header = new Grid { Height = 46 };
            title = Text("UPsound", 13, FontWeights.SemiBold);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.VerticalAlignment = VerticalAlignment.Center;
            lights = new TrafficLights { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            header.Children.Add(title);
            header.Children.Add(lights);
            layout.Children.Add(header);

            // Cápsula + leitura
            var main = new Grid { Margin = new Thickness(20, 0, 26, 0) };
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            capsule = new BoostCapsule();
            main.Children.Add(capsule);

            var right = new Grid { Margin = new Thickness(20, 6, 0, 6) };
            Grid.SetColumn(right, 1);
            for (int i = 0; i < 7; i++)
                right.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            main.Children.Add(right);

            number = new TextBlock { Margin = new Thickness(-3, -14, 0, 0) };
            numRun = new Run("150") { FontSize = 62, FontWeight = FontWeights.SemiBold };
            pctRun = new Run("%") { FontSize = 26, FontWeight = FontWeights.Medium };
            number.Inlines.Add(numRun);
            number.Inlines.Add(pctRun);
            Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
            AddRow(right, number, 0);

            dbText = Text("", 13, FontWeights.Normal);
            dbText.Margin = new Thickness(0, -4, 0, 0);
            AddRow(right, dbText, 1);

            warnText = Text("Acima de 300% o som pode distorcer", 12, FontWeights.Medium);
            warnText.Margin = new Thickness(0, 10, 0, 0);
            warnText.TextWrapping = TextWrapping.Wrap;
            AddRow(right, warnText, 2);

            presetLabel = Text("Predefinições", 12, FontWeights.Medium);
            presetLabel.Margin = new Thickness(1, 0, 0, 8);
            AddRow(right, presetLabel, 4);

            presets = new Segmented(PresetValues);
            AddRow(right, presets, 5);

            hintText = Text("", 11, FontWeights.Normal);
            hintText.Margin = new Thickness(1, 12, 0, 0);
            hintText.TextWrapping = TextWrapping.Wrap;
            hintText.LineHeight = 15;
            AddRow(right, hintText, 6);
            layout.Children.Add(main);

            // Preferências
            prefsCard = new Border { CornerRadius = new CornerRadius(10), Margin = new Thickness(24, 20, 24, 0) };
            var list = new StackPanel();
            boostSwitch = new ToggleSwitch("Reforço ativo");
            startSwitch = new ToggleSwitch("Iniciar com o Windows");
            resetSwitch = new ToggleSwitch("Voltar a 100% ao sair");
            list.Children.Add(Row(out boostLabel, "Reforço ativo", boostSwitch));
            sep1 = new Border { Height = 1, Margin = new Thickness(16, 0, 0, 0) };
            list.Children.Add(sep1);
            list.Children.Add(Row(out startLabel, "Iniciar com o Windows", startSwitch));
            sep2 = new Border { Height = 1, Margin = new Thickness(16, 0, 0, 0) };
            list.Children.Add(sep2);
            list.Children.Add(Row(out resetLabel, "Voltar a 100% ao sair", resetSwitch));
            prefsCard.Child = list;
            layout.Children.Add(prefsCard);

            // Aviso de configuração do motor
            setupCard = new Border { CornerRadius = new CornerRadius(10), Margin = new Thickness(24, 12, 24, 0), Padding = new Thickness(16, 14, 16, 14) };
            var sp = new StackPanel();
            setupTitle = Text("", 13, FontWeights.SemiBold);
            setupBody = Text("", 12, FontWeights.Normal);
            setupBody.TextWrapping = TextWrapping.Wrap;
            setupBody.LineHeight = 17;
            setupBody.Margin = new Thickness(0, 4, 0, 0);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            setupPrimary = new PillButton("", true);
            setupSecondary = new PillButton("Verificar de novo", false) { Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(setupPrimary);
            buttons.Children.Add(setupSecondary);
            sp.Children.Add(setupTitle);
            sp.Children.Add(setupBody);
            sp.Children.Add(buttons);
            setupCard.Child = sp;
            layout.Children.Add(setupCard);

            // Rodapé de estado
            var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 14, 24, 18) };
            statusDot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) };
            statusText = Text("", 12, FontWeights.Normal);
            footer.Children.Add(statusDot);
            footer.Children.Add(statusText);
            layout.Children.Add(footer);
        }

        static TextBlock Text(string s, double size, FontWeight weight)
        {
            return new TextBlock { Text = s, FontSize = size, FontWeight = weight };
        }

        static void AddRow(Grid g, UIElement el, int row)
        {
            Grid.SetRow(el, row);
            g.Children.Add(el);
        }

        static Grid Row(out TextBlock label, string text, FrameworkElement control)
        {
            var g = new Grid { Height = 44 };
            label = Text(text, 13, FontWeights.Normal);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(16, 0, 0, 0);
            control.HorizontalAlignment = HorizontalAlignment.Right;
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(0, 0, 9, 0);
            g.Children.Add(label);
            g.Children.Add(control);
            return g;
        }

        // ---------- Comportamento ----------

        void Wire()
        {
            capsule.UserChanged += v => app.SetLevel(v);
            presets.Picked += v => app.SetLevel(v);
            boostSwitch.Toggled += on => app.SetEnabled(on);
            startSwitch.Toggled += on => app.SetAutoStart(on);
            resetSwitch.Toggled += on => app.SetResetOnExit(on);
            lights.CloseClicked += () => app.HideWindow();
            lights.MinimizeClicked += () => WindowState = WindowState.Minimized;
            setupPrimary.Click += () => { if (primaryAction != null) primaryAction(); };
            setupSecondary.Click += () => app.Engine.Refresh();
            app.Changed += Sync;

            surface.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch (InvalidOperationException) { } }
            };
            Activated += delegate
            {
                lights.WindowActive = true;
                // Ao voltar para a janela (por exemplo, depois de instalar o Equalizer APO), confere de novo.
                if (app.Engine.State != EngineState.Ready) app.Engine.Refresh();
            };
            Deactivated += delegate { lights.WindowActive = false; };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { app.HideWindow(); e.Handled = true; }
            };
            // Fechar pelo Alt+F4 ou pela barra de tarefas só esconde: o UPsound segue no canto.
            Closing += (s, e) => { if (!app.Exiting) { e.Cancel = true; app.HideWindow(); } };
        }

        /// <summary>Leva o estado do app para a interface.</summary>
        void Sync()
        {
            var st = app.Settings;
            double eff = app.Effective;
            capsule.Level = st.Level;
            capsule.Active = st.Enabled;
            boostSwitch.IsOn = st.Enabled;
            startSwitch.IsOn = Install.AutoStart;
            resetSwitch.IsOn = st.ResetOnExit;
            presets.SelectedValue = st.Enabled ? (int)st.Level : -1;

            numRun.Text = eff.ToString("0", PtBr);
            if (!st.Enabled) dbText.Text = "Reforço desligado";
            else if (eff <= 100) dbText.Text = "Volume original do sistema";
            else dbText.Text = "+" + ApoEngine.ToDecibels(eff).ToString("0.0", PtBr) + " dB acima do sistema";
            warnText.Visibility = st.Enabled && eff > 300 ? Visibility.Visible : Visibility.Hidden;

            hintText.Text = app.HotkeysOk
                ? "Ctrl + Alt + PgUp / PgDn ajusta de qualquer janela. Ctrl + Alt + Home liga e desliga."
                : "Atalhos globais indisponíveis: outro programa já usa Ctrl + Alt + PgUp / PgDn.";
            UpdateEngineUi();
        }

        void UpdateEngineUi()
        {
            var t = Theme.Current;
            var engine = app.Engine;
            setupSecondary.Visibility = Visibility.Visible;
            switch (engine.State)
            {
                case EngineState.Ready:
                    setupCard.Visibility = Visibility.Collapsed;
                    statusDot.Background = Theme.B(t.Ok);
                    statusText.Text = "Ativo no canto da barra de tarefas";
                    statusText.ToolTip = "Equalizer APO: " + engine.ConfigDir;
                    break;
                case EngineState.NotInstalled:
                    ShowSetup("Instale o motor de áudio",
                        "O UPsound usa o Equalizer APO, gratuito, para amplificar todo o som do Windows. " +
                        "Reinstale o UPsound para incluí-lo, ou baixe e marque seus alto-falantes na instalação.",
                        "Baixar Equalizer APO", OpenApoPage);
                    statusText.Text = "Sem motor de áudio: o reforço ainda não chega ao som";
                    break;
                case EngineState.NoAccess:
                    ShowSetup("Falta uma permissão",
                        "O Windows protege a pasta do Equalizer APO. Autorize uma vez como administrador " +
                        "e o UPsound passa a ajustar o volume sozinho.",
                        "Permitir acesso", RequestAccess);
                    statusText.Text = "Aguardando permissão para aplicar o reforço";
                    break;
                default:
                    ShowSetup("Não foi possível aplicar o reforço",
                        string.IsNullOrEmpty(engine.Detail) ? "Erro desconhecido ao gravar a configuração." : engine.Detail,
                        "Tentar de novo", engine.Refresh);
                    setupSecondary.Visibility = Visibility.Collapsed;
                    statusText.Text = "Erro no motor de áudio";
                    break;
            }
            if (engine.State != EngineState.Ready)
            {
                statusDot.Background = Theme.B(t.Warn);
                statusText.ToolTip = null;
            }
        }

        void ShowSetup(string head, string body, string action, Action run)
        {
            setupCard.Visibility = Visibility.Visible;
            setupTitle.Text = head;
            setupBody.Text = body;
            setupPrimary.Text = action;
            primaryAction = run;
        }

        static void OpenApoPage()
        {
            try { Process.Start(new ProcessStartInfo(ApoUrl) { UseShellExecute = true }); }
            catch (Win32Exception) { }
        }

        void RequestAccess()
        {
            var engine = app.Engine;
            if (engine.ConfigDir == null) { engine.Refresh(); return; }
            try
            {
                var psi = new ProcessStartInfo(Install.CurrentExe,
                    "--grant \"" + engine.ConfigDir + "\" " + WindowsIdentity.GetCurrent().User.Value)
                { Verb = "runas", UseShellExecute = true };
                var p = Process.Start(psi);
                if (p == null) return;
                p.EnableRaisingEvents = true;
                p.Exited += delegate { Dispatcher.BeginInvoke(new Action(engine.Refresh)); };
            }
            catch (Win32Exception) { /* o usuário cancelou o UAC */ }
        }

        public void ApplyTheme()
        {
            var t = Theme.Current;
            shadow.Background = Theme.B(t.Window);
            shadow.Effect = new DropShadowEffect { BlurRadius = 38, ShadowDepth = 12, Direction = 270, Color = Colors.Black, Opacity = t.ShadowOpacity, RenderingBias = RenderingBias.Quality };
            surface.Background = Theme.B(t.Window);
            surface.BorderBrush = Theme.B(t.Outline);
            prefsCard.Background = Theme.B(t.Card);
            setupCard.Background = Theme.B(t.Card);
            sep1.Background = sep2.Background = Theme.B(t.Separator);

            title.Foreground = boostLabel.Foreground = startLabel.Foreground = resetLabel.Foreground =
                setupTitle.Foreground = number.Foreground = Theme.B(t.Text);
            pctRun.Foreground = dbText.Foreground = presetLabel.Foreground = hintText.Foreground =
                setupBody.Foreground = statusText.Foreground = Theme.B(t.Secondary);
            warnText.Foreground = Theme.B(t.Warn);

            foreach (UIElement el in new UIElement[] { capsule, presets, boostSwitch, startSwitch, resetSwitch, lights, setupPrimary, setupSecondary })
                el.InvalidateVisual();
            UpdateEngineUi();
        }

        // ---------- Abrir no canto e esconder ----------

        /// <summary>Abre colada no canto inferior direito, acima do relógio, subindo 10 px enquanto aparece.</summary>
        public void ShowInCorner()
        {
            hiding = false;
            BeginAnimation(OpacityProperty, null);
            if (!IsVisible)
            {
                outer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = outer.DesiredSize;
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - size.Width + ShadowRoom.Right - 12;
                Top = wa.Bottom - size.Height + ShadowRoom.Bottom - 12;

                if (Motion.Enabled)
                {
                    Opacity = 0;
                    slide.Y = 10;
                    Show();
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
                    slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
                }
                else
                {
                    Opacity = 1;
                    slide.Y = 0;
                    Show();
                }
            }
            else
            {
                Opacity = 1;
                slide.BeginAnimation(TranslateTransform.YProperty, null);
                slide.Y = 0;
            }
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        public void HideAnimated()
        {
            if (!Motion.Enabled) { Hide(); return; }
            if (hiding) return;
            hiding = true;
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += delegate
            {
                if (!hiding) return; // reaberta durante o fade
                hiding = false;
                Hide();
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
            };
            BeginAnimation(OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(slide.Y, 6, TimeSpan.FromMilliseconds(120)));
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var h = new WindowInteropHelper(this).Handle;
            // Sem isso, clicar no ícone da barra de tarefas não minimiza uma janela sem moldura.
            SetWindowLong(h, GWL_STYLE, GetWindowLong(h, GWL_STYLE) | WS_MINIMIZEBOX);
        }

        /// <summary>Renderiza a janela num PNG sem abri-la. Usado para conferir o visual.</summary>
        public void SaveSnapshot(string path, double scale)
        {
            capsule.SnapToTarget(); presets.SnapToTarget();
            boostSwitch.SnapToTarget(); startSwitch.SnapToTarget(); resetSwitch.SnapToTarget();
            var el = (FrameworkElement)Content;
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            el.Arrange(new Rect(el.DesiredSize));
            el.UpdateLayout();
            double w = el.ActualWidth + el.Margin.Left + el.Margin.Right, h = el.ActualHeight + el.Margin.Top + el.Margin.Bottom;
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Theme.B(Theme.Current.Dark ? Theme.H("#3A3F4B") : Theme.H("#C9CED8")), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(el), null, new Rect(el.Margin.Left, el.Margin.Top, el.ActualWidth, el.ActualHeight));
            }
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        const int GWL_STYLE = -16, WS_MINIMIZEBOX = 0x00020000;
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    }
}
