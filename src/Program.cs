using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;

namespace UPsound
{
    static class Program
    {
        public static bool ThemeForced;

        [STAThread]
        static int Main(string[] args)
        {
            // Modo elevado de uso único, chamado pelo botão "Permitir acesso".
            if (args.Length >= 3 && args[0] == "--grant") return ApoEngine.GrantAccess(args[1], args[2]);

            string snapshot = Opt(args, "--snapshot");
            string theme = Opt(args, "--theme");
            string configDir = Opt(args, "--config-dir");
            bool trayOnly = Array.IndexOf(args, "--tray") >= 0;
            if (theme != null) { Theme.Current = Theme.Create(theme == "dark"); ThemeForced = true; }

            if (Array.IndexOf(args, "--uninstall") >= 0)
            {
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                return Install.Uninstall();
            }

            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\UPsound.SingleInstance", out createdNew))
            {
                if (!createdNew && snapshot == null)
                {
                    // Já está rodando: abrir de novo só mostra a janela (a menos que seja a partida do Windows).
                    if (!trayOnly) Install.BringExistingToFront();
                    return 0;
                }

                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) => { Log(e.Exception); e.Handled = true; };

                if (snapshot != null)
                {
                    Motion.Enabled = false;
                    var settings = new Settings();
                    string level = Opt(args, "--level");
                    if (level != null) settings.Level = double.Parse(level, CultureInfo.InvariantCulture);
                    settings.Enabled = Array.IndexOf(args, "--off") < 0;
                    var engine = new ApoEngine(configDir);
                    engine.Refresh();
                    var snapCtl = new Controller(settings, engine);
                    string sc = Opt(args, "--scale");
                    snapCtl.EnsureWindow().SaveSnapshot(snapshot, sc == null ? 2 : double.Parse(sc, CultureInfo.InvariantCulture));
                    return 0;
                }

                var ctl = new Controller(Settings.Load(), new ApoEngine(configDir));
                // Ao desligar ou sair da conta, devolve o som ao normal se essa for a preferência.
                app.SessionEnding += delegate { ctl.Exit(); };
                app.Startup += delegate { ctl.Start(!trayOnly); };
                app.Run();
            }
            return 0;
        }

        static string Opt(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static void Log(Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UPsound");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "erros.log"), DateTime.Now.ToString("s") + "  " + ex + "\r\n\r\n");
            }
            catch { }
        }
    }
}
