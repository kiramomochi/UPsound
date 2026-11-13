using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UPsound.Setup
{
    /// <summary>Barra de progresso indeterminada: um trecho laranja que vai e volta na cápsula.</summary>
    sealed class IndeterminateBar : AnimatedElement
    {
        double t;
        public bool Running;

        public IndeterminateBar() { Height = 6; }

        public void Begin() { Running = true; Animate(); }

        protected override bool Step(double dt) { t += dt; return Running; }

        protected override void OnRender(DrawingContext dc)
        {
            var th = Theme.Current;
            double w = ActualWidth, h = ActualHeight;
            var r = new Rect(0, 0, w, h);
            dc.DrawRoundedRectangle(Theme.B(th.Track), null, r, h / 2, h / 2);
            dc.PushClip(new RectangleGeometry(r, h / 2, h / 2));
            var heat = new LinearGradientBrush(th.BoostLow, th.BoostHigh, 0);
            heat.Freeze();
            if (!Motion.Enabled) dc.DrawRoundedRectangle(heat, null, new Rect(0, 0, w * 0.4, h), h / 2, h / 2);
            else
            {
                // Vai e volta com aceleração suave nas pontas.
                double seg = w * 0.32, phase = (Math.Sin(t * 2.4 - Math.PI / 2) + 1) / 2;
                dc.DrawRoundedRectangle(heat, null, new Rect(-seg + phase * (w + seg), 0, seg, h), h / 2, h / 2);
            }
            dc.Pop();
        }
    }

    sealed class SetupWindow : Window
    {
        enum Page { Welcome, Progress, Done, Error }

        readonly bool apoPresent, reinstall;
        readonly string statusFile;
        readonly DispatcherTimer poll;
        Process worker;
        bool apoInstalledNow, busy;

        Border shadow, surface, card, sep1, sep2;
        TrafficLights lights;
        TextBlock title, subtitle, note, stepTitle, stepBody;
        TextBlock c1Name, c1Desc, c1Tag, c2Name, c2Desc, c2Tag, startLabel;
        ToggleSwitch startSwitch;
        PillButton primary, secondary;
        IndeterminateBar bar;
        StackPanel welcomePage, stepPage;
        Image icon;
        Page page;

        public SetupWindow()
        {
            apoPresent = ApoEngine.FindConfigDir() != null;
            reinstall = File.Exists(Install.ExePath);
            statusFile = Path.Combine(Path.GetTempPath(), "UPsound-setup-status.txt");

            Title = "Instalar UPsound";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanMinimize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            FontFamily = Theme.Font;

            poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            poll.Tick += delegate { Poll(); };

            Build();
            ApplyTheme();
            Show(Page.Welcome);
            startSwitch.IsOn = true;
            startSwitch.SnapToTarget();

            Closing += (s, e) => { if (busy) e.Cancel = true; };
            KeyDown += (s, e) => { if (e.Key == Key.Escape && !busy) Close(); };
        }

        static TextBlock Text(string s, double size, FontWeight weight)
        {
            return new TextBlock { Text = s, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
        }

        void Build()
        {
            var outer = new Grid { Margin = new Thickness(28, 18, 28, 38) };
            shadow = new Border { CornerRadius = new CornerRadius(14) };
            surface = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Width = 440 };
            outer.Children.Add(shadow);
            outer.Children.Add(surface);
            Content = outer;
            surface.MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch (InvalidOperationException) { } };

            var layout = new StackPanel();
            surface.Child = layout;

            var header = new Grid { Height = 40 };
            lights = new TrafficLights { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            lights.CloseClicked += () => { if (!busy) Close(); };
            lights.MinimizeClicked += () => WindowState = WindowState.Minimized;
            Activated += delegate { lights.WindowActive = true; };
            Deactivated += delegate { lights.WindowActive = false; };
            header.Children.Add(lights);
            layout.Children.Add(header);

            var body = new StackPanel { Margin = new Thickness(32, 0, 32, 28) };
            layout.Children.Add(body);

            icon = new Image { Width = 84, Height = 84, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("UPsound.png"))
            {
                if (s != null)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.StreamSource = s; bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 252; bi.EndInit();
                    bi.Freeze();
                    icon.Source = bi;
                }
            }
            body.Children.Add(icon);

            title = Text("UPsound", 26, FontWeights.SemiBold);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(title);
            subtitle = Text("O som do computador até 500%, a um clique do relógio.", 13, FontWeights.Normal);
            subtitle.HorizontalAlignment = HorizontalAlignment.Center;
            subtitle.TextAlignment = TextAlignment.Center;
            subtitle.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(subtitle);

            // Página inicial
            welcomePage = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
            card = new Border { CornerRadius = new CornerRadius(10) };
            var list = new StackPanel();
            list.Children.Add(Component(out c1Name, out c1Desc, out c1Tag, "UPsound " + Install.Version,
                "O app e o ícone no canto da barra de tarefas", reinstall ? "Atualizar" : "Novo"));
            sep1 = new Border { Height = 1, Margin = new Thickness(16, 0, 0, 0) };
            list.Children.Add(sep1);
            list.Children.Add(Component(out c2Name, out c2Desc, out c2Tag, "Equalizer APO " + Worker.ApoVersion,
                "Motor de áudio gratuito, licença GPL", apoPresent ? "Já instalado" : "Incluído"));
            sep2 = new Border { Height = 1, Margin = new Thickness(16, 0, 0, 0) };
            list.Children.Add(sep2);
            var row = new Grid { Height = 46 };
            startLabel = Text("Iniciar com o Windows", 13, FontWeights.Normal);
            startLabel.VerticalAlignment = VerticalAlignment.Center;
            startLabel.Margin = new Thickness(16, 0, 0, 0);
            startSwitch = new ToggleSwitch("Iniciar com o Windows") { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
            row.Children.Add(startLabel);
            row.Children.Add(startSwitch);
            list.Children.Add(row);
            card.Child = list;
            welcomePage.Children.Add(card);

            note = Text(apoPresent
                ? "O Equalizer APO já está neste computador: o UPsound só se conecta a ele."
                : "Durante a instalação, o Equalizer APO abre uma janela própria: marque seus alto-falantes ou fones e clique em OK.",
                12, FontWeights.Normal);
            note.LineHeight = 17;
            note.Margin = new Thickness(2, 14, 2, 0);
            welcomePage.Children.Add(note);
            body.Children.Add(welcomePage);

            // Página de andamento, conclusão e erro
            stepPage = new StackPanel { Margin = new Thickness(0, 28, 0, 0) };
            stepTitle = Text("", 15, FontWeights.SemiBold);
            stepTitle.HorizontalAlignment = HorizontalAlignment.Center;
            stepTitle.TextAlignment = TextAlignment.Center;
            stepBody = Text("", 12, FontWeights.Normal);
            stepBody.LineHeight = 17;
            stepBody.TextAlignment = TextAlignment.Center;
            stepBody.Margin = new Thickness(8, 6, 8, 0);
            stepBody.MinHeight = 34;
            bar = new IndeterminateBar { Margin = new Thickness(40, 20, 40, 4) };
            stepPage.Children.Add(stepTitle);
            stepPage.Children.Add(stepBody);
            stepPage.Children.Add(bar);
            body.Children.Add(stepPage);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            secondary = new PillButton("Cancelar", false) { Margin = new Thickness(0, 0, 8, 0) };
            primary = new PillButton("Instalar", true);
            buttons.Children.Add(secondary);
            buttons.Children.Add(primary);
            body.Children.Add(buttons);
            primary.Click += OnPrimary;
            secondary.Click += () => { if (!busy) Close(); };
        }

        Grid Component(out TextBlock name, out TextBlock desc, out TextBlock tag, string n, string d, string t)
        {
            var g = new Grid { Margin = new Thickness(16, 10, 16, 10) };
            var sp = new StackPanel();
            name = Text(n, 13, FontWeights.SemiBold);
            desc = Text(d, 12, FontWeights.Normal);
            desc.Margin = new Thickness(0, 2, 0, 0);
            sp.Children.Add(name);
            sp.Children.Add(desc);
            tag = Text(t, 12, FontWeights.Medium);
            tag.HorizontalAlignment = HorizontalAlignment.Right;
            tag.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(sp);
            g.Children.Add(tag);
            return g;
        }

        void ApplyTheme()
        {
            var t = Theme.Current;
            shadow.Background = Theme.B(t.Window);
            shadow.Effect = new DropShadowEffect { BlurRadius = 38, ShadowDepth = 12, Direction = 270, Color = Colors.Black, Opacity = t.ShadowOpacity, RenderingBias = RenderingBias.Quality };
            surface.Background = Theme.B(t.Window);
            surface.BorderBrush = Theme.B(t.Outline);
            card.Background = Theme.B(t.Card);
            sep1.Background = sep2.Background = Theme.B(t.Separator);
            title.Foreground = c1Name.Foreground = c2Name.Foreground = startLabel.Foreground = stepTitle.Foreground = Theme.B(t.Text);
            subtitle.Foreground = note.Foreground = c1Desc.Foreground = c2Desc.Foreground = c1Tag.Foreground = c2Tag.Foreground = stepBody.Foreground = Theme.B(t.Secondary);
        }

        void Show(Page p)
        {
            page = p;
            welcomePage.Visibility = p == Page.Welcome ? Visibility.Visible : Visibility.Collapsed;
            stepPage.Visibility = p == Page.Welcome ? Visibility.Collapsed : Visibility.Visible;
            bar.Visibility = p == Page.Progress ? Visibility.Visible : Visibility.Collapsed;
            bar.Running = p == Page.Progress;
            if (bar.Running) bar.Begin();
            primary.Visibility = p == Page.Progress ? Visibility.Hidden : Visibility.Visible;
            secondary.Visibility = p == Page.Welcome || p == Page.Error ? Visibility.Visible : Visibility.Collapsed;

            switch (p)
            {
                case Page.Welcome:
                    primary.Text = reinstall ? "Atualizar" : "Instalar";
                    secondary.Text = "Cancelar";
                    break;
                case Page.Progress:
                    SetStep("Preparando", "Confirme a permissão de administrador do Windows.");
                    break;
                case Page.Done:
                    primary.Text = "Concluir";
                    SetStep("Tudo pronto",
                        "O UPsound está no canto da barra de tarefas, perto do relógio. Clique no ícone para abrir." +
                        (apoInstalledNow ? " Se o som não aumentar, reinicie o computador uma vez." : ""));
                    break;
                case Page.Error:
                    primary.Text = "Tentar de novo";
                    secondary.Text = "Fechar";
                    break;
            }
        }

        void SetStep(string head, string text)
        {
            stepTitle.Text = head;
            stepBody.Text = text;
        }

        void OnPrimary()
        {
            if (page == Page.Done) { Close(); return; }
            if (page == Page.Welcome || page == Page.Error) StartInstall();
        }

        void StartInstall()
        {
            try { File.Delete(statusFile); } catch { }
            Show(Page.Progress);
            try
            {
                string sid = WindowsIdentity.GetCurrent().User.Value;
                worker = Process.Start(new ProcessStartInfo(Install.CurrentExe, "--worker " + sid + " \"" + statusFile + "\"")
                { Verb = "runas", UseShellExecute = true });
            }
            catch (Win32Exception)
            {
                Show(Page.Error);
                SetStep("Instalação cancelada", "O Windows precisa da permissão de administrador para instalar o motor de áudio.");
                return;
            }
            busy = true;
            SetStep("Instalando", "Isso leva poucos segundos.");
            poll.Start();
        }

        void Poll()
        {
            string line = null;
            try
            {
                if (File.Exists(statusFile))
                    using (var fs = new FileStream(statusFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(fs, Encoding.UTF8))
                        line = r.ReadToEnd();
            }
            catch (IOException) { }

            if (!string.IsNullOrEmpty(line))
            {
                int bar = line.IndexOf('|');
                string a = bar < 0 ? line : line.Substring(0, bar), b = bar < 0 ? "" : line.Substring(bar + 1);
                if (a == "DONE") apoInstalledNow = b == "1";
                else if (a == "ERROR") SetStep("Não deu para concluir", b);
                else SetStep(a, b);
            }

            if (worker == null || !worker.HasExited) return;
            poll.Stop();
            busy = false;
            if (worker.ExitCode == 0 && File.Exists(Install.ExePath))
            {
                Install.SetAutoStart(startSwitch.IsOn, Install.ExePath);
                try { Process.Start(new ProcessStartInfo(Install.ExePath) { UseShellExecute = true }); } catch { }
                Show(Page.Done);
                Activate();
            }
            else
            {
                string msg = stepBody.Text;
                Show(Page.Error);
                if (stepTitle.Text != "Não deu para concluir") SetStep("Não deu para concluir", string.IsNullOrEmpty(msg) ? "A instalação parou antes do fim." : msg);
            }
        }

        /// <summary>Captura de cada página para conferir o visual (--snapshot).</summary>
        public void SaveSnapshot(string path, string which)
        {
            if (which == "progress") { Show(Page.Progress); SetStep("Escolha seus alto-falantes", "Na janela do Equalizer APO, marque o dispositivo que você usa para ouvir (alto-falantes ou fones) e clique em OK."); }
            else if (which == "done") { apoInstalledNow = true; Show(Page.Done); }
            else if (which == "error") { Show(Page.Error); SetStep("Não deu para concluir", "O Equalizer APO não terminou de instalar. Rode o instalador do UPsound de novo."); }
            var el = (FrameworkElement)Content;
            el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            el.Arrange(new Rect(el.DesiredSize));
            el.UpdateLayout();
            double w = el.ActualWidth + el.Margin.Left + el.Margin.Right, h = el.ActualHeight + el.Margin.Top + el.Margin.Bottom;
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * 2), (int)Math.Ceiling(h * 2), 192, 192, PixelFormats.Pbgra32);
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
    }

    static class SetupProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "--worker") return Worker.Run(args[1], args[2]);

            int ti = Array.IndexOf(args, "--theme");
            if (ti >= 0 && ti + 1 < args.Length) Theme.Current = Theme.Create(args[ti + 1] == "dark");

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var win = new SetupWindow();
            int si = Array.IndexOf(args, "--snapshot");
            if (si >= 0 && si + 2 < args.Length)
            {
                Motion.Enabled = false;
                win.SaveSnapshot(args[si + 1], args[si + 2]);
                return 0;
            }
            app.Run(win);
            return 0;
        }
    }
}
