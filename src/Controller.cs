using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace UPsound
{
    /// <summary>
    /// O UPsound em segundo plano: guarda o nível, fala com o motor, cuida do ícone da bandeja e dos
    /// atalhos globais. A janela só é criada quando o usuário abre, e some (sem fechar o app) ao fechar.
    /// </summary>
    sealed class Controller
    {
        public readonly Settings Settings;
        public readonly ApoEngine Engine;
        public bool HotkeysOk { get; private set; }
        public bool Exiting { get; private set; }
        public event Action Changed;

        readonly DispatcherTimer saveTimer, trimTimer;
        HwndSource host;
        TrayIcon tray;
        MainWindow window;
        IntPtr iconOn, iconOff;
        bool taskbarDark;
        int taskbarCreatedMsg;
        DateTime lastDeactivated = DateTime.MinValue;

        public Controller(Settings settings, ApoEngine engine)
        {
            Settings = settings;
            Engine = engine;
            HotkeysOk = true;
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            saveTimer.Tick += delegate { saveTimer.Stop(); Settings.Save(); };
            // Depois de esconder a janela, devolve ao Windows a memória que ela usava.
            trimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            trimTimer.Tick += delegate { trimTimer.Stop(); TrimMemory(); };
            Engine.StateChanged += delegate { UpdateTray(); Raise(); };
        }

        public void Start(bool showWindow)
        {
            // Janela invisível que recebe os cliques do ícone, os atalhos e os avisos do sistema.
            var p = new HwndSourceParameters("UPsoundHost") { Width = 0, Height = 0, WindowStyle = 0 };
            host = new HwndSource(p);
            host.AddHook(WndProc);
            taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");

            HotkeysOk = RegisterHotKey(host.Handle, 1, MOD_CONTROL | MOD_ALT, VK_PRIOR)
                      & RegisterHotKey(host.Handle, 2, MOD_CONTROL | MOD_ALT, VK_NEXT)
                      & RegisterHotKey(host.Handle, 3, MOD_CONTROL | MOD_ALT, VK_HOME);

            NativeMenu.AllowDarkMenus();
            tray = new TrayIcon(host.Handle);
            LoadTrayIcons();
            Engine.Refresh();
            Apply();

            if (showWindow) ShowWindow();
            else trimTimer.Start();
        }

        // ---------- Estado ----------

        public double Effective { get { return Settings.Enabled ? Settings.Level : 100; } }

        public void SetLevel(double v)
        {
            Settings.Level = Math.Max(BoostCapsule.Min, Math.Min(BoostCapsule.Max, Math.Round(v / 5) * 5));
            Settings.Enabled = true;
            Apply();
        }

        public void SetEnabled(bool on) { Settings.Enabled = on; Apply(); }

        public void SetResetOnExit(bool on) { Settings.ResetOnExit = on; Apply(); }

        public void SetAutoStart(bool on) { Install.SetAutoStart(on, Install.CurrentExe); Raise(); }

        void Apply()
        {
            Engine.Request(Effective);
            UpdateTray();
            saveTimer.Stop();
            saveTimer.Start();
            Raise();
        }

        void Raise() { if (Changed != null) Changed(); }

        // ---------- Janela ----------

        public MainWindow EnsureWindow()
        {
            if (window == null)
            {
                window = new MainWindow(this);
                window.Deactivated += delegate { lastDeactivated = DateTime.UtcNow; };
            }
            return window;
        }

        public void ShowWindow()
        {
            trimTimer.Stop();
            EnsureWindow().ShowInCorner();
        }

        public void HideWindow()
        {
            if (window == null || !window.IsVisible) return;
            window.HideAnimated();
            trimTimer.Start();
        }

        void ToggleWindow()
        {
            // Clicar no ícone tira o foco da janela antes do clique chegar: por isso a tolerância.
            bool recentlyActive = window != null && (window.IsActive || (DateTime.UtcNow - lastDeactivated).TotalMilliseconds < 400);
            if (window != null && window.IsVisible && window.WindowState != WindowState.Minimized && recentlyActive) HideWindow();
            else ShowWindow();
        }

        public void Exit()
        {
            if (Exiting) return;
            Exiting = true;
            saveTimer.Stop();
            Settings.Save();
            Engine.ApplyNow(Settings.ResetOnExit ? 100 : Effective);
            if (tray != null) tray.Dispose();
            if (host != null)
            {
                for (int id = 1; id <= 3; id++) UnregisterHotKey(host.Handle, id);
                host.Dispose();
            }
            if (window != null) window.Close();
            Application.Current.Shutdown();
        }

        // ---------- Bandeja ----------

        void LoadTrayIcons()
        {
            taskbarDark = !TaskbarIsLight();
            string v = taskbarDark ? "dark" : "light";
            int size = TrayIcon.SmallIconSize;
            if (iconOn != IntPtr.Zero) DestroyIcon(iconOn);
            if (iconOff != IntPtr.Zero) DestroyIcon(iconOff);
            iconOn = TrayIcon.LoadIcon("tray-" + v + "-on.ico", size);
            iconOff = TrayIcon.LoadIcon("tray-" + v + "-off.ico", size);
        }

        void UpdateTray()
        {
            if (tray == null) return;
            string tip;
            if (Engine.State != EngineState.Ready) tip = "UPsound: motor de áudio não conectado";
            else if (!Settings.Enabled) tip = "UPsound: reforço desligado";
            else tip = "UPsound: " + Settings.Level.ToString("0") + "%";
            tray.Update(Settings.Enabled && Settings.Level > 100 ? iconOn : iconOff, tip);
        }

        void ShowMenu()
        {
            int lvl = Settings.Enabled ? (int)Settings.Level : -1;
            var items = new[]
            {
                new NativeMenu.Item { Id = 1, Text = "Abrir UPsound", Bold = true },
                new NativeMenu.Item { Separator = true },
                new NativeMenu.Item { Id = 2, Text = "Reforço ativo", Checked = Settings.Enabled },
                new NativeMenu.Item { Id = 100, Text = "100%", Radio = true, Checked = lvl == 100 },
                new NativeMenu.Item { Id = 150, Text = "150%", Radio = true, Checked = lvl == 150 },
                new NativeMenu.Item { Id = 200, Text = "200%", Radio = true, Checked = lvl == 200 },
                new NativeMenu.Item { Id = 300, Text = "300%", Radio = true, Checked = lvl == 300 },
                new NativeMenu.Item { Id = 500, Text = "500%", Radio = true, Checked = lvl == 500 },
                new NativeMenu.Item { Separator = true },
                new NativeMenu.Item { Id = 3, Text = "Iniciar com o Windows", Checked = Install.AutoStart },
                new NativeMenu.Item { Id = 4, Text = "Sair" },
            };
            int cmd = NativeMenu.Show(host.Handle, items);
            if (cmd == 1) ShowWindow();
            else if (cmd == 2) SetEnabled(!Settings.Enabled);
            else if (cmd >= 100) SetLevel(cmd);
            else if (cmd == 3) SetAutoStart(!Install.AutoStart);
            else if (cmd == 4) Exit();
        }

        // ---------- Mensagens do Windows ----------

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == TrayIcon.CallbackMessage)
            {
                int ev = lParam.ToInt32() & 0xFFFF;
                if (ev == WM_LBUTTONUP) ToggleWindow();
                else if (ev == WM_RBUTTONUP) ShowMenu();
                handled = true;
            }
            else if (msg == WM_HOTKEY)
            {
                switch (wParam.ToInt32())
                {
                    case 1: SetLevel(Settings.Level + 10); break;
                    case 2: SetLevel(Settings.Level - 10); break;
                    case 3: SetEnabled(!Settings.Enabled); break;
                }
                handled = true;
            }
            else if (msg == Install.ShowMessage) { ShowWindow(); handled = true; }
            else if (msg == Install.ExitMessage) { Exit(); handled = true; }
            else if (msg == taskbarCreatedMsg) { LoadTrayIcons(); tray.Readd(); }
            else if (msg == WM_SETTINGCHANGE) OnSettingChange();
            return IntPtr.Zero;
        }

        void OnSettingChange()
        {
            // Troca de tema claro/escuro do Windows: app e ícone da bandeja acompanham.
            bool dark = Theme.SystemPrefersDark();
            if (dark != Theme.Current.Dark && !Program.ThemeForced)
            {
                Theme.Current = Theme.Create(dark);
                NativeMenu.RefreshTheme();
                if (window != null) window.ApplyTheme();
            }
            if (TaskbarIsLight() == taskbarDark)
            {
                LoadTrayIcons();
                UpdateTray();
            }
        }

        static bool TaskbarIsLight()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        static void TrimMemory()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
        }

        const int WM_HOTKEY = 0x0312, WM_SETTINGCHANGE = 0x001A, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_HOME = 0x24;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
    }
}
