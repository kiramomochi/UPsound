using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace UPsound.Setup
{
    /// <summary>
    /// A parte do instalador que roda como administrador (um único pedido do UAC). Escreve o andamento
    /// num arquivo que a janela lê: "titulo|texto", e no fim "DONE|1" (motor instalado agora) ou "ERROR|mensagem".
    /// </summary>
    static class Worker
    {
        public const string ApoVersion = "1.4.2";
        static string statusFile;

        public static int Run(string userSid, string status)
        {
            statusFile = status;
            try
            {
                Report("Preparando", "Fechando versões anteriores do UPsound.");
                Install.CloseRunningApp();

                Report("Copiando o UPsound", "Instalando em Arquivos de Programas.");
                Directory.CreateDirectory(Install.Dir);
                Extract("UPsound.exe", Install.ExePath);
                File.WriteAllText(Path.Combine(Install.Dir, "Equalizer APO - aviso de licenca.txt"), ApoNotice, Encoding.UTF8);
                CreateShortcut(Install.ShortcutPath, Install.ExePath);
                RegisterUninstall();

                bool apoNew = false;
                if (ApoEngine.FindConfigDir() == null)
                {
                    Report("Instalando o motor de áudio", "Equalizer APO " + ApoVersion + ". Isso leva alguns segundos.");
                    string tmp = Path.Combine(Path.GetTempPath(), "UPsound-setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Directory.CreateDirectory(tmp);
                    string apo = Path.Combine(tmp, "EqualizerAPO-x64-" + ApoVersion + ".exe");
                    Extract("EqualizerAPO.exe", apo);

                    var p = Process.Start(new ProcessStartInfo(apo, "/S") { UseShellExecute = false });
                    // O instalador do Equalizer APO abre o seletor de dispositivos: esperamos o usuário escolher.
                    while (!p.WaitForExit(400)) NoteSelector();
                    while (SelectorRunning()) { NoteSelector(); Thread.Sleep(400); }
                    try { Directory.Delete(tmp, true); } catch { }

                    if (ApoEngine.FindConfigDir() == null)
                        return Fail("O Equalizer APO não terminou de instalar. Rode o instalador do UPsound de novo.");
                    apoNew = true;
                }

                Report("Conectando o UPsound ao motor", "Quase lá.");
                if (ApoEngine.GrantAccess(ApoEngine.FindConfigDir(), userSid) != 0)
                    return Fail("Não foi possível preparar a configuração do Equalizer APO.");

                if (apoNew)
                {
                    NeutralizeStockPreamp(ApoEngine.FindConfigDir());
                    Report("Reiniciando o áudio do Windows", "O som pode sumir por um instante.");
                    RunHidden("net.exe", "stop audiosrv /y");
                    RunHidden("net.exe", "start audiosrv");
                }

                Write("DONE|" + (apoNew ? "1" : "0"));
                return 0;
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        /// <summary>
        /// O Equalizer APO instala uma configuração de exemplo (Preamp -6 dB, reforço de graves e EQ).
        /// Numa instalação nova, feita por nós, ela sai: o som em 100% fica igual ao original do Windows.
        /// O exemplo fica salvo em config.txt.antes-do-UPsound.
        /// </summary>
        static void NeutralizeStockPreamp(string dir)
        {
            try
            {
                string cfg = Path.Combine(dir, "config.txt");
                File.Copy(cfg, Path.Combine(dir, "config.txt.antes-do-UPsound"), true);
                File.WriteAllText(cfg,
                    "# UPsound: reforco de volume. Apague esta linha e a de baixo para remover.\r\n" +
                    "Include: " + ApoEngine.FileName + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        static bool selectorNoted;

        static void NoteSelector()
        {
            if (selectorNoted || !SelectorRunning()) return;
            selectorNoted = true;
            Report("Escolha seus alto-falantes",
                "Na janela do Equalizer APO, marque o dispositivo que você usa para ouvir (alto-falantes ou fones) e clique em OK.");
        }

        static bool SelectorRunning()
        {
            return Process.GetProcessesByName("DeviceSelector").Length > 0 || Process.GetProcessesByName("Configurator").Length > 0;
        }

        static int Fail(string msg)
        {
            Write("ERROR|" + msg);
            return 1;
        }

        static void Report(string title, string body) { Write(title + "|" + body); }

        static void Write(string line)
        {
            for (int i = 0; i < 5; i++)
            {
                try { File.WriteAllText(statusFile, line, Encoding.UTF8); return; }
                catch (IOException) { Thread.Sleep(50); }
            }
        }

        static void Extract(string resource, string target)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using (var f = File.Create(target))
                s.CopyTo(f);
        }

        static void RunHidden(string exe, string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                p.WaitForExit(20000);
            }
            catch { }
        }

        static void CreateShortcut(string lnk, string target)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Reforço de volume até 500%" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }
        }

        static void RegisterUninstall()
        {
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = hklm.CreateSubKey(Install.UninstallKey))
            {
                k.SetValue("DisplayName", "UPsound");
                k.SetValue("DisplayVersion", Install.Version);
                k.SetValue("Publisher", "UPsound");
                k.SetValue("DisplayIcon", Install.ExePath + ",0");
                k.SetValue("InstallLocation", Install.Dir);
                k.SetValue("UninstallString", "\"" + Install.ExePath + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(Install.ExePath).Length / 1024) + 1, RegistryValueKind.DWord);
            }
        }

        const string ApoNotice =
            "O UPsound usa o Equalizer APO como motor de audio.\r\n\r\n" +
            "Equalizer APO " + ApoVersion + " - Copyright (C) Jonas Thedering\r\n" +
            "Licenca: GNU General Public License, versao 2 (GPLv2).\r\n" +
            "Texto da licenca: https://www.gnu.org/licenses/old-licenses/gpl-2.0.html\r\n" +
            "Codigo-fonte desta versao: https://sourceforge.net/projects/equalizerapo/files/" + ApoVersion + "/\r\n" +
            "Projeto: https://sourceforge.net/projects/equalizerapo/\r\n\r\n" +
            "O instalador do Equalizer APO e distribuido sem modificacoes. Para remove-lo, use\r\n" +
            "Configuracoes > Aplicativos > Equalizer APO.\r\n";
    }
}
