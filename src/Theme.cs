using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace UPsound
{
    /// <summary>Paleta clara e escura no espírito dos apps da Apple. Segue o tema do Windows.</summary>
    sealed class Theme
    {
        public bool Dark;
        public Color Window, Card, Text, Secondary, Track, Base, BaseGlyph, Outline, Separator;
        public Color Accent, BoostLow, BoostHigh, Warn, Ok, Idle;
        public Color SegTrack, SegThumb, ToggleOff, DotInactive;
        public double ShadowOpacity;

        public static Theme Current = Create(SystemPrefersDark());

        public static bool SystemPrefersDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static Theme Create(bool dark)
        {
            var t = new Theme();
            t.Dark = dark;
            if (dark)
            {
                t.Window = H("#1C1C1E"); t.Card = H("#2C2C2E"); t.Text = H("#F5F5F7"); t.Secondary = H("#98989F");
                t.Track = H("#323235"); t.Base = H("#F5F5F7"); t.BaseGlyph = H("#1C1C1E");
                t.Outline = H("#24FFFFFF"); t.Separator = H("#3A3A3C");
                t.Accent = H("#0A84FF"); t.BoostLow = H("#FF9F0A"); t.BoostHigh = H("#FF453A");
                t.Warn = H("#FF9F0A"); t.Ok = H("#32D74B"); t.Idle = H("#8E8E93");
                t.SegTrack = H("#2C2C2E"); t.SegThumb = H("#5A5A5E"); t.ToggleOff = H("#3A3A3D"); t.DotInactive = H("#48484B");
                t.ShadowOpacity = 0.55;
            }
            else
            {
                t.Window = H("#F5F5F7"); t.Card = H("#FFFFFF"); t.Text = H("#1D1D1F"); t.Secondary = H("#6E6E73");
                t.Track = H("#E3E3E8"); t.Base = H("#1D1D1F"); t.BaseGlyph = H("#F5F5F7");
                t.Outline = H("#1A000000"); t.Separator = H("#E5E5EA");
                t.Accent = H("#007AFF"); t.BoostLow = H("#FF9500"); t.BoostHigh = H("#FF3B30");
                t.Warn = H("#C93400"); t.Ok = H("#34C759"); t.Idle = H("#AEAEB2");
                t.SegTrack = H("#E3E3E8"); t.SegThumb = H("#FFFFFF"); t.ToggleOff = H("#DCDCE0"); t.DotInactive = H("#D1D1D6");
                t.ShadowOpacity = 0.22;
            }
            return t;
        }

        public static Color H(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        public static SolidColorBrush B(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static SolidColorBrush B(Color c, double opacity)
        {
            return B(Color.FromArgb((byte)Math.Round(c.A * opacity), c.R, c.G, c.B));
        }

        public static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (byte)Math.Round(a.A + (b.A - a.A) * t),
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static readonly FontFamily Font = new FontFamily("Segoe UI");
    }

    static class Motion
    {
        /// <summary>Falso quando o Windows está com "Mostrar animações" desligado.</summary>
        public static bool Enabled = SystemParameters.ClientAreaAnimation;

        public static double Approach(double cur, double target, double dt, double speed)
        {
            if (!Enabled) return target;
            double v = cur + (target - cur) * (1 - Math.Exp(-dt * speed));
            if (Math.Abs(target - v) < 0.0005 * (1 + Math.Abs(target))) v = target;
            return v;
        }
    }
}
