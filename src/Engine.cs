using System;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Microsoft.Win32;

namespace UPsound
{
    enum EngineState { Ready, NotInstalled, NoAccess, Failed }

    /// <summary>
    /// Motor de áudio do UPsound. O ganho é aplicado pelo Equalizer APO (gratuito, código aberto),
    /// que processa todo o som do sistema antes de chegar aos alto-falantes. O UPsound escreve
    /// um arquivo próprio (UPsound.txt) com o pré-amplificador em dB e o inclui no config.txt.
    /// O Equalizer APO recarrega a configuração na hora, sem reiniciar nada.
    /// </summary>
    sealed class ApoEngine
    {
        public const string FileName = "UPsound.txt";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Regex IncludeLine = new Regex(@"(?im)^[ \t]*Include:[ \t]*UPsound\.txt[ \t]*\r?$");

        readonly string overrideDir;
        readonly DispatcherTimer timer;
        double desired = 100, written = double.NaN;
        int retries;

        public EngineState State { get; private set; }
        public string ConfigDir { get; private set; }
        public string Detail { get; private set; }
        public event Action StateChanged;

        public ApoEngine(string overrideDir)
        {
            this.overrideDir = overrideDir;
            State = EngineState.NotInstalled;
            // Agrupa as mudanças enquanto o usuário arrasta: no máximo uma gravação a cada 40 ms.
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
            timer.Tick += delegate { timer.Stop(); Flush(); };
        }

        public void Refresh()
        {
            string dir = overrideDir ?? FindConfigDir();
            ConfigDir = dir;
            if (dir == null || !Directory.Exists(dir)) { SetState(EngineState.NotInstalled, null); return; }
            try
            {
                EnsureInclude(dir);
                written = double.NaN;
                SetState(EngineState.Ready, null);
                Flush();
            }
            catch (UnauthorizedAccessException) { SetState(EngineState.NoAccess, null); }
            catch (Exception ex) { SetState(EngineState.Failed, ex.Message); }
        }

        /// <summary>Pede um novo nível (100 a 500%). A gravação acontece em seguida, agrupada.</summary>
        public void Request(double percent)
        {
            desired = percent;
            if (State == EngineState.Ready && !timer.IsEnabled) timer.Start();
        }

        /// <summary>Grava imediatamente (usado ao fechar).</summary>
        public void ApplyNow(double percent)
        {
            desired = percent;
            timer.Stop();
            Flush();
        }

        void Flush()
        {
            if (State != EngineState.Ready || desired == written) return;
            try
            {
                WriteGain(ConfigDir, desired);
                written = desired;
                retries = 0;
            }
            catch (UnauthorizedAccessException) { SetState(EngineState.NoAccess, null); }
            catch (IOException ex)
            {
                // O Equalizer APO pode estar lendo o arquivo neste instante: tenta de novo.
                if (++retries <= 8) timer.Start();
                else SetState(EngineState.Failed, ex.Message);
            }
        }

        void SetState(EngineState s, string detail)
        {
            bool changed = s != State || detail != Detail;
            State = s;
            Detail = detail;
            if (changed && StateChanged != null) StateChanged();
        }

        public static double ToDecibels(double percent)
        {
            return 20 * Math.Log10(Math.Max(percent, 1) / 100.0);
        }

        static void WriteGain(string dir, double percent)
        {
            string text =
                "# Gerado pelo UPsound. Nao edite: o app reescreve este arquivo.\r\n" +
                "# Nivel: " + percent.ToString("0", Inv) + "%\r\n" +
                "Preamp: " + ToDecibels(percent).ToString("0.00", Inv) + " dB\r\n";
            File.WriteAllText(Path.Combine(dir, FileName), text, new UTF8Encoding(false));
        }

        /// <summary>Garante a linha "Include: UPsound.txt" no topo do config.txt (vale para todos os dispositivos).</summary>
        static void EnsureInclude(string dir)
        {
            string inc = Path.Combine(dir, FileName);
            if (!File.Exists(inc)) WriteGain(dir, 100);

            string cfg = Path.Combine(dir, "config.txt");
            string text = "";
            Encoding enc = new UTF8Encoding(false);
            if (File.Exists(cfg))
            {
                using (var reader = new StreamReader(cfg, enc, true))
                {
                    text = reader.ReadToEnd();
                    enc = reader.CurrentEncoding;
                }
            }
            if (IncludeLine.IsMatch(text)) return;

            string header =
                "# UPsound: reforco de volume. Apague esta linha e a de baixo para remover.\r\n" +
                "Include: " + FileName + "\r\n";
            File.WriteAllText(cfg, header + text, enc);
        }

        public static string FindConfigDir()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var k = hklm.OpenSubKey(@"SOFTWARE\EqualizerAPO"))
                    {
                        if (k == null) continue;
                        var cp = k.GetValue("ConfigPath") as string;
                        if (!string.IsNullOrEmpty(cp) && Directory.Exists(cp)) return cp;
                        var ip = k.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(ip) && Directory.Exists(Path.Combine(ip, "config"))) return Path.Combine(ip, "config");
                    }
                }
                catch { }
            }
            string pf = Environment.GetEnvironmentVariable("ProgramW6432");
            if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string def = Path.Combine(pf, @"EqualizerAPO\config");
            return Directory.Exists(def) ? def : null;
        }

        /// <summary>
        /// Executado uma vez como administrador (UAC). Cria UPsound.txt, inclui no config.txt e dá
        /// permissão de escrita só nesse arquivo e só para a conta que pediu. A pasta continua protegida.
        /// </summary>
        public static int GrantAccess(string dir, string userSid)
        {
            try
            {
                EnsureInclude(dir);
                var file = new FileInfo(Path.Combine(dir, FileName));
                var sec = file.GetAccessControl();
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(userSid),
                    FileSystemRights.Modify, AccessControlType.Allow));
                file.SetAccessControl(sec);
                return 0;
            }
            catch { return 1; }
        }
    }
}
