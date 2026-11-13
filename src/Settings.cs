using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace UPsound
{
    /// <summary>Preferências guardadas em %APPDATA%\UPsound\settings.ini.</summary>
    sealed class Settings
    {
        public double Level = 150;
        public bool Enabled = true;
        public bool ResetOnExit = true;

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UPsound", "settings.ini"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    int eq = raw.IndexOf('=');
                    if (eq < 0) continue;
                    string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                    double d;
                    if (k == "level" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        s.Level = Math.Max(BoostCapsule.Min, Math.Min(BoostCapsule.Max, Math.Round(d / 5) * 5));
                    else if (k == "enabled") s.Enabled = v == "1";
                    else if (k == "resetOnExit") s.ResetOnExit = v == "1";
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath,
                    "level=" + Level.ToString("0", CultureInfo.InvariantCulture) + "\r\n" +
                    "enabled=" + (Enabled ? "1" : "0") + "\r\n" +
                    "resetOnExit=" + (ResetOnExit ? "1" : "0") + "\r\n", Encoding.UTF8);
            }
            catch { }
        }
    }
}
