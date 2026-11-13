using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace UPsound
{
    /// <summary>Caminhos, inicialização com o Windows e mensagens entre instâncias. Usado pelo app e pelo instalador.</summary>
    static class Install
    {
        public const string Version = "1.0.0";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\UPsound";

        public static readonly int ShowMessage = RegisterWindowMessage("UPsound.ShowWindow");
        public static readonly int ExitMessage = RegisterWindowMessage("UPsound.Exit");

        public static string Dir
        {
            get
            {
                string pf = Environment.GetEnvironmentVariable("ProgramW6432");
                if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Path.Combine(pf, "UPsound");
            }
        }

        public static string ExePath { get { return Path.Combine(Dir, "UPsound.exe"); } }

        public static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "UPsound.lnk"); }
        }

        public static string CurrentExe { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        /// <summary>Inicia com o Windows (para a conta atual), já em segundo plano.</summary>
        public static bool AutoStart
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("UPsound") != null; }
                catch { return false; }
            }
        }

        public static void SetAutoStart(bool on, string exe)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("UPsound", "\"" + exe + "\" --tray");
                    else k.DeleteValue("UPsound", false);
                }
            }
            catch { }
        }

        public static bool IsAdmin
        {
            get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        }

        /// <summary>Pede ao UPsound aberto que feche (atualização ou desinstalação) e espera.</summary>
        public static void CloseRunningApp()
        {
            int self = Process.GetCurrentProcess().Id;
            var others = Array.FindAll(Process.GetProcessesByName("UPsound"), p => p.Id != self);
            if (others.Length == 0) return;
            PostMessage((IntPtr)0xFFFF, ExitMessage, IntPtr.Zero, IntPtr.Zero);
            foreach (var p in others)
            {
                try { if (!p.WaitForExit(4000)) p.Kill(); } catch { }
            }
        }

        public static void BringExistingToFront()
        {
            PostMessage((IntPtr)0xFFFF, ShowMessage, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>UPsound.exe --uninstall: remove o app, a ligação com o Equalizer APO e, se pedido, o próprio motor.</summary>
        public static int Uninstall()
        {
            if (!IsAdmin)
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo(CurrentExe, "--uninstall") { Verb = "runas", UseShellExecute = true });
                    return p == null ? 1 : 0;
                }
                catch (System.ComponentModel.Win32Exception) { return 1; }
            }

            if (MessageBox.Show("Remover o UPsound deste computador?", "UPsound",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return 1;

            CloseRunningApp();

            string cfg = ApoEngine.FindConfigDir();
            if (cfg != null)
            {
                try
                {
                    string path = Path.Combine(cfg, "config.txt");
                    if (File.Exists(path))
                    {
                        Encoding enc;
                        string text;
                        using (var r = new StreamReader(path, new UTF8Encoding(false), true)) { text = r.ReadToEnd(); enc = r.CurrentEncoding; }
                        text = Regex.Replace(text, @"(?im)^[ \t]*#[ \t]*UPsound:[^\n]*\n", "");
                        text = Regex.Replace(text, @"(?im)^[ \t]*Include:[ \t]*UPsound\.txt[ \t]*\r?\n?", "");
                        File.WriteAllText(path, text, enc);
                    }
                    File.Delete(Path.Combine(cfg, ApoEngine.FileName));
                }
                catch { }

                string apoDir = Path.GetDirectoryName(cfg.TrimEnd('\\'));
                string apoUninstaller = Path.Combine(apoDir, "Uninstall.exe");
                if (File.Exists(apoUninstaller) && MessageBox.Show(
                        "Remover também o Equalizer APO, o motor de áudio que o UPsound usa?\n\n" +
                        "Se outro programa (ou você) usa o Equalizer APO, escolha Não.",
                        "UPsound", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    try { Process.Start(new ProcessStartInfo(apoUninstaller) { UseShellExecute = true }); } catch { }
                }
            }

            SetAutoStart(false, "");
            try { File.Delete(ShortcutPath); } catch { }
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    hklm.DeleteSubKeyTree(UninstallKey, false);
            }
            catch { }
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UPsound"), true); } catch { }

            // A pasta do programa só pode ser apagada depois que este processo sair.
            string dir = Path.GetDirectoryName(CurrentExe);
            if (string.Equals(dir.TrimEnd('\\'), Dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + dir + "\"")
                    { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                }
                catch { }
            }

            MessageBox.Show("O UPsound foi removido.", "UPsound", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    }
}
