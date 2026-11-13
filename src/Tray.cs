using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UPsound
{
    /// <summary>Ícone na área de notificação (perto do relógio), direto pela API do Windows: sem WinForms.</summary>
    sealed class TrayIcon : IDisposable
    {
        public const int CallbackMessage = 0x8000 + 77; // WM_APP + 77

        readonly IntPtr hwnd;
        bool added;
        IntPtr icon;
        string tip = "UPsound";

        public TrayIcon(IntPtr hwnd) { this.hwnd = hwnd; }

        public void Update(IntPtr newIcon, string newTip)
        {
            icon = newIcon;
            tip = newTip.Length > 127 ? newTip.Substring(0, 127) : newTip;
            var d = Data();
            d.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            if (!added) added = Shell_NotifyIcon(NIM_ADD, ref d);
            else if (!Shell_NotifyIcon(NIM_MODIFY, ref d)) added = Shell_NotifyIcon(NIM_ADD, ref d);
        }

        /// <summary>Depois que o Explorer reinicia, o ícone precisa ser adicionado de novo.</summary>
        public void Readd()
        {
            added = false;
            Update(icon, tip);
        }

        public void Dispose()
        {
            if (!added) return;
            var d = Data();
            Shell_NotifyIcon(NIM_DELETE, ref d);
            added = false;
        }

        NOTIFYICONDATA Data()
        {
            var d = new NOTIFYICONDATA();
            d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
            d.hWnd = hwnd;
            d.uID = 1;
            d.uCallbackMessage = CallbackMessage;
            d.hIcon = icon;
            d.szTip = tip;
            d.szInfo = "";
            d.szInfoTitle = "";
            return d;
        }

        /// <summary>Carrega um .ico embutido no tamanho pedido (usa a imagem PNG mais próxima).</summary>
        public static IntPtr LoadIcon(string resource, int size)
        {
            byte[] ico;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) return IntPtr.Zero;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                ico = ms.ToArray();
            }
            int count = BitConverter.ToUInt16(ico, 4), pick = -1, pickSize = 0;
            for (int i = 0; i < count; i++)
            {
                int w = ico[6 + 16 * i] == 0 ? 256 : ico[6 + 16 * i];
                bool better = pick < 0
                    || (w >= size && (pickSize < size || w < pickSize))
                    || (w < size && pickSize < size && w > pickSize);
                if (better) { pick = i; pickSize = w; }
            }
            int len = BitConverter.ToInt32(ico, 6 + 16 * pick + 8), off = BitConverter.ToInt32(ico, 6 + 16 * pick + 12);
            var img = new byte[len];
            Array.Copy(ico, off, img, 0, len);
            return CreateIconFromResourceEx(img, len, true, 0x00030000, size, size, 0);
        }

        public static int SmallIconSize { get { int s = GetSystemMetrics(49); return s > 0 ? s : 16; } }

        const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
        const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
        [DllImport("user32.dll")] static extern IntPtr CreateIconFromResourceEx(byte[] bits, int size, bool icon, int ver, int cx, int cy, int flags);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    }

    /// <summary>Menu do botão direito, nativo do Windows (segue o tema escuro quando o sistema permite).</summary>
    static class NativeMenu
    {
        public sealed class Item
        {
            public int Id; public string Text; public bool Checked, Radio, Bold, Separator;
        }

        public static int Show(IntPtr owner, Item[] items)
        {
            IntPtr menu = CreatePopupMenu();
            try
            {
                foreach (var it in items)
                {
                    if (it.Separator) { AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null); continue; }
                    AppendMenu(menu, MF_STRING | (it.Checked && !it.Radio ? MF_CHECKED : 0), (UIntPtr)(uint)it.Id, it.Text);
                    if (it.Radio && it.Checked) CheckMenuRadioItem(menu, (uint)it.Id, (uint)it.Id, (uint)it.Id, 0);
                    if (it.Bold) SetMenuDefaultItem(menu, (uint)it.Id, 0);
                }
                POINT p;
                GetCursorPos(out p);
                SetForegroundWindow(owner);
                int cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, p.X, p.Y, owner, IntPtr.Zero);
                PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
                return cmd;
            }
            finally { DestroyMenu(menu); }
        }

        /// <summary>Pede ao Windows menus escuros quando o tema dos apps é escuro (Windows 10 1809+).</summary>
        public static void AllowDarkMenus()
        {
            try { SetPreferredAppMode(1); FlushMenuThemes(); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

        public static void RefreshTheme()
        {
            try { FlushMenuThemes(); } catch (EntryPointNotFoundException) { } catch (DllNotFoundException) { }
        }

        const uint MF_STRING = 0, MF_CHECKED = 8, MF_SEPARATOR = 0x800;
        const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100, TPM_NONOTIFY = 0x80;

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] static extern bool CheckMenuRadioItem(IntPtr menu, uint first, uint last, uint check, uint flags);
        [DllImport("user32.dll")] static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPos);
        [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();
    }
}
