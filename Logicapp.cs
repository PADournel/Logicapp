// Logicapp — zones repliables pour ranger les icônes du bureau.
// Compilé avec le compilateur C# intégré à Windows (.NET Framework 4.8) : aucune dépendance.
// Chaque zone est un vrai dossier (à côté du dossier Bureau) ; glisser une icône dedans la déplace.
// Les zones sont dessinées entièrement par l'app (fenêtres à transparence par pixel) :
// le fond peut être translucide ou une image, tandis que les icônes et le texte restent nets.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using VBFS = Microsoft.VisualBasic.FileIO;

namespace Logicapp
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new System.Threading.Mutex(true, "Logicapp_Instance_Unique", out created))
            {
                if (!created) return;
                // Une erreur ne doit jamais fermer l'app : on la note dans erreurs.log et on continue.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => LogError(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                new ZoneApp();
                Application.Run();
            }
        }

        public static void LogError(Exception ex)
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "erreurs.log");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 512 * 1024) fi.Delete(); // pas de fichier qui grossit sans fin
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\r\n\r\n");
            }
            catch { }
        }
    }

    // =====================================================================================
    //  Appels Windows
    // =====================================================================================
    static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetWindowLongPtr64(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        static extern int SetWindowLong32(IntPtr h, int idx, int v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern int GetWindowLong32(IntPtr h, int idx);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr h);
        [DllImport("psapi.dll")]
        public static extern bool EmptyWorkingSet(IntPtr h);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO sfi, uint size, uint flags);
        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObj, int idChild, uint thread, uint time);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // Rendu à transparence par pixel
        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int flags);

        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        static readonly uint MyPid = (uint)Process.GetCurrentProcess().Id;

        public static bool IsOwnWindow(IntPtr h)
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            return pid == MyPid;
        }

        public static bool IsDesktopWindow(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            var sb = new StringBuilder(64);
            GetClassName(h, sb, 64);
            string c = sb.ToString();
            return c == "WorkerW" || c == "Progman";
        }

        // Fenêtre après laquelle s'insérer pour être juste au-dessus du bureau (Progman), sous toutes les applications.
        // (Sur Windows 11 récent, passer « tout au fond » cache la zone derrière le bureau.)
        public static bool TryGetSlotAboveDesktop(IntPtr self, out IntPtr after)
        {
            after = IntPtr.Zero;
            IntPtr pm = FindWindow("Progman", null);
            if (pm == IntPtr.Zero) return false;
            IntPtr prev = GetWindow(pm, 3 /*GW_HWNDPREV*/);
            while (prev != IntPtr.Zero)
            {
                if (prev == self) { after = self; return true; }
                if (!IsOwnWindow(prev)) break;
                prev = GetWindow(prev, 3);
            }
            if (prev == IntPtr.Zero || (GetWindowLong32(prev, -20) & 0x8) != 0) after = IntPtr.Zero; // HWND_TOP
            else after = prev;
            return true;
        }

        // Rattache la fenêtre au bureau.
        public static void SetOwner(IntPtr h, IntPtr owner)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, -8, owner);
            else SetWindowLong32(h, -8, owner.ToInt32());
        }
    }

    // =====================================================================================
    //  Icônes du shell (toutes tailles, avec la flèche des raccourcis)
    // =====================================================================================
    static class ShellIcons
    {
        [DllImport("shell32.dll", EntryPoint = "#727")]
        static extern int SHGetImageList(int iImageList, ref Guid riid, out IntPtr ppv);
        [DllImport("comctl32.dll")]
        static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, uint flags);
        [DllImport("user32.dll")]
        static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public int[] bmiColors;
        }
        [DllImport("gdi32.dll")]
        static extern int GetObject(IntPtr h, int size, ref BITMAP bm);
        [DllImport("gdi32.dll")]
        static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFO bi, uint usage);

        static Guid IID_IImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
        static readonly Dictionary<int, IntPtr> lists = new Dictionary<int, IntPtr>();

        static IntPtr List(int shil)
        {
            IntPtr p;
            if (!lists.TryGetValue(shil, out p))
            {
                try { SHGetImageList(shil, ref IID_IImageList, out p); } catch { p = IntPtr.Zero; }
                lists[shil] = p;
            }
            return p;
        }

        public static Bitmap Get(string path, int size, out string displayName)
        {
            var sfi = new Native.SHFILEINFO();
            Native.SHGetFileInfo(path, 0, ref sfi, (uint)Marshal.SizeOf(sfi),
                0x4000 /*SYSICONINDEX*/ | 0x100 /*ICON*/ | 0x40 /*OVERLAYINDEX*/ | 0x200 /*DISPLAYNAME*/);
            displayName = sfi.szDisplayName;
            if (sfi.hIcon != IntPtr.Zero) Native.DestroyIcon(sfi.hIcon);
            int idx = sfi.iIcon & 0xFFFFFF, ov = (sfi.iIcon >> 24) & 0xFF;
            int shil = size <= 16 ? 1 : size <= 32 ? 0 : size <= 48 ? 2 : 4; // SMALL, LARGE, EXTRALARGE, JUMBO
            Bitmap b = FromList(shil, idx, ov);
            // Pas de vraie version 256 px : Windows renvoie une petite icône dans un coin -> on prend la 48 px.
            if (b != null && shil == 4 && Extent(b) <= 48) { b.Dispose(); b = FromList(2, idx, ov); }
            if (b == null) return null;
            if (b.Width == size && b.Height == size) return b;
            var r = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(r))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(b, new Rectangle(0, 0, size, size));
            }
            b.Dispose();
            return r;
        }

        static Bitmap FromList(int shil, int idx, int ov)
        {
            IntPtr l = List(shil);
            if (l == IntPtr.Zero) return null;
            IntPtr h = ImageList_GetIcon(l, idx, 1u /*ILD_TRANSPARENT*/ | ((uint)ov << 8));
            if (h == IntPtr.Zero) return null;
            try { return IconToBitmap(h); }
            finally { Native.DestroyIcon(h); }
        }

        // Conversion qui conserve la transparence (Icon.ToBitmap la perd).
        static Bitmap IconToBitmap(IntPtr h)
        {
            ICONINFO ii;
            if (!GetIconInfo(h, out ii)) return null;
            try
            {
                if (ii.hbmColor == IntPtr.Zero) using (var ic = Icon.FromHandle(h)) return ic.ToBitmap();
                var bm = new BITMAP();
                if (GetObject(ii.hbmColor, Marshal.SizeOf(typeof(BITMAP)), ref bm) == 0) return null;
                int w = bm.bmWidth, ht = Math.Abs(bm.bmHeight);
                if (w <= 0 || ht <= 0 || w > 1024 || ht > 1024) return null;
                var bi = new BITMAPINFO { bmiColors = new int[256] };
                bi.biSize = 40; bi.biWidth = w; bi.biHeight = -ht; bi.biPlanes = 1; bi.biBitCount = 32; bi.biCompression = 0;
                var buf = new byte[w * ht * 4];
                IntPtr dc = Native.GetDC(IntPtr.Zero);
                int lines;
                try { lines = GetDIBits(dc, ii.hbmColor, 0, (uint)ht, buf, ref bi, 0); }
                finally { Native.ReleaseDC(IntPtr.Zero, dc); }
                if (lines == 0) return null;
                bool alpha = false;
                for (int i = 3; i < buf.Length; i += 4) if (buf[i] != 0) { alpha = true; break; }
                if (!alpha) using (var ic = Icon.FromHandle(h)) return ic.ToBitmap();
                var res = new Bitmap(w, ht, PixelFormat.Format32bppArgb);
                var dst = res.LockBits(new Rectangle(0, 0, w, ht), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < ht; y++)
                        Marshal.Copy(buf, y * w * 4, new IntPtr(dst.Scan0.ToInt64() + (long)y * dst.Stride), w * 4);
                }
                finally { res.UnlockBits(dst); }
                return res;
            }
            catch { return null; }
            finally
            {
                if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
                if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
            }
        }

        static int Extent(Bitmap b)
        {
            var rect = new Rectangle(0, 0, b.Width, b.Height);
            var d = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(d.Stride) * b.Height;
                var buf = new byte[bytes];
                Marshal.Copy(d.Scan0, buf, 0, bytes);
                int mx = 0, my = 0;
                for (int y = 0; y < b.Height; y++)
                    for (int x = 0; x < b.Width; x++)
                        if (buf[y * d.Stride + x * 4 + 3] != 0) { if (x > mx) mx = x; if (y > my) my = y; }
                return Math.Max(mx, my) + 1;
            }
            finally { b.UnlockBits(d); }
        }
    }

    // =====================================================================================
    //  Style d'une zone (enregistré dans zones.cfg)
    // =====================================================================================
    class ZoneStyle
    {
        public int IconSize = 48, Spacing = 0, CornerRadius = 8, BorderWidth = 0;
        public int BgAlpha = 200, BgImageAlpha = 255, TitleAlpha = 230, BorderAlpha = 60;
        public int BgImageMode = 0;   // 0 Remplir, 1 Ajuster, 2 Étirer, 3 Mosaïque, 4 Centrer
        public int TitleAlign = 0;    // 0 Gauche, 1 Centre, 2 Droite
        public bool LabelShadow = true, ShowCount = true;
        public string BgImage = "";
        public string LabelFont = "Segoe UI|9|0", TitleFont = "Segoe UI Semibold|9.5|0";
        public Color LabelColor = Color.White;
        public Color BgColor = Color.FromArgb(30, 32, 38);
        public Color TitleColor = Color.FromArgb(50, 54, 66);
        public Color TitleTextColor = Color.FromArgb(236, 238, 244);
        public Color BorderColor = Color.White;

        public ZoneStyle Clone() { return (ZoneStyle)MemberwiseClone(); }

        public void Write(StringBuilder sb)
        {
            W(sb, "IconSize", IconSize); W(sb, "Spacing", Spacing);
            W(sb, "LabelFont", LabelFont); W(sb, "LabelColor", Hex(LabelColor)); W(sb, "LabelShadow", LabelShadow ? 1 : 0);
            W(sb, "BgColor", Hex(BgColor)); W(sb, "BgAlpha", BgAlpha);
            W(sb, "BgImage", BgImage); W(sb, "BgImageMode", BgImageMode); W(sb, "BgImageAlpha", BgImageAlpha);
            W(sb, "TitleFont", TitleFont); W(sb, "TitleTextColor", Hex(TitleTextColor));
            W(sb, "TitleColor", Hex(TitleColor)); W(sb, "TitleAlpha", TitleAlpha);
            W(sb, "TitleAlign", TitleAlign); W(sb, "ShowCount", ShowCount ? 1 : 0);
            W(sb, "CornerRadius", CornerRadius); W(sb, "BorderWidth", BorderWidth);
            W(sb, "BorderColor", Hex(BorderColor)); W(sb, "BorderAlpha", BorderAlpha);
        }

        public bool Read(string k, string v)
        {
            switch (k)
            {
                case "IconSize": IconSize = I(v, IconSize, 16, 256); return true;
                case "Spacing": Spacing = I(v, Spacing, 0, 80); return true;
                case "LabelFont": LabelFont = v; return true;
                case "LabelColor": LabelColor = C(v, LabelColor); return true;
                case "LabelShadow": LabelShadow = v == "1"; return true;
                case "BgColor": BgColor = C(v, BgColor); return true;
                case "BgAlpha": BgAlpha = I(v, BgAlpha, 0, 255); return true;
                case "BgImage": BgImage = v; return true;
                case "BgImageMode": BgImageMode = I(v, BgImageMode, 0, 4); return true;
                case "BgImageAlpha": BgImageAlpha = I(v, BgImageAlpha, 0, 255); return true;
                case "TitleFont": TitleFont = v; return true;
                case "TitleTextColor": TitleTextColor = C(v, TitleTextColor); return true;
                case "TitleColor": TitleColor = C(v, TitleColor); return true;
                case "TitleAlpha": TitleAlpha = I(v, TitleAlpha, 0, 255); return true;
                case "TitleAlign": TitleAlign = I(v, TitleAlign, 0, 2); return true;
                case "ShowCount": ShowCount = v == "1"; return true;
                case "CornerRadius": CornerRadius = I(v, CornerRadius, 0, 40); return true;
                case "BorderWidth": BorderWidth = I(v, BorderWidth, 0, 10); return true;
                case "BorderColor": BorderColor = C(v, BorderColor); return true;
                case "BorderAlpha": BorderAlpha = I(v, BorderAlpha, 0, 255); return true;
            }
            return false;
        }

        static void W(StringBuilder sb, string k, object v)
        {
            sb.Append(k).Append('=').Append(Convert.ToString(v, CultureInfo.InvariantCulture)).AppendLine();
        }

        static int I(string v, int def, int min, int max)
        {
            int r;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return def;
            return Math.Max(min, Math.Min(max, r));
        }

        static Color C(string v, Color def)
        {
            try { Color c = ColorTranslator.FromHtml(v.Trim()); return Color.FromArgb(255, c.R, c.G, c.B); }
            catch { return def; }
        }

        public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        public static Font MakeFont(string spec)
        {
            string[] p = (spec ?? "").Split('|');
            string fam = p.Length > 0 && p[0].Trim().Length > 0 ? p[0].Trim() : "Segoe UI";
            float size = 9; int style = 0;
            if (p.Length > 1 && !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out size)) size = 9;
            if (p.Length > 2) int.TryParse(p[2], out style);
            if (size < 5 || size > 72) size = 9;
            try { return new Font(fam, size, (FontStyle)(style & 15), GraphicsUnit.Point); }
            catch { return new Font("Segoe UI", size, GraphicsUnit.Point); }
        }

        public static string FontSpec(Font f)
        {
            return f.Name + "|" + f.SizeInPoints.ToString("0.##", CultureInfo.InvariantCulture) + "|" + (int)f.Style;
        }

        public static string Describe(string spec)
        {
            using (var f = MakeFont(spec))
                return f.Name + ", " + f.SizeInPoints.ToString("0.#") + " pt" + (f.Bold ? ", gras" : "") + (f.Italic ? ", italique" : "");
        }
    }

    // =====================================================================================
    //  Application : icône de notification, configuration, liste des zones
    // =====================================================================================
    class ZoneApp
    {
        public static ZoneApp I;
        public readonly string DesktopDir, Root, ConfigPath, ImagesDir;
        public bool Locked, PeekOnHover = true, Quitting;
        public readonly float Scale;
        public ZoneStyle DefaultStyle = new ZoneStyle();
        public readonly List<ZoneForm> Zones = new List<ZoneForm>();

        readonly NotifyIcon tray;
        readonly Timer desktopWait, fgCheck;
        readonly Native.WinEventProc fgProc; // gardé en champ pour ne pas être libéré par le GC
        ToolStripMenuItem miLock, miPeek, miStartup;
        bool firstRun, waiting, lastFgDesktop;
        int fgTicks;

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "Logicapp";
        const string OldAppName = "BureauZones"; // ancien nom (avant le renommage)

        class ZoneRec { public string Name; public int X, Y, W = 320, H = 220; public bool Collapsed; public ZoneStyle Style; }

        public ZoneApp()
        {
            I = this;
            DesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string parent = Path.GetDirectoryName(DesktopDir);
            if (string.IsNullOrEmpty(parent)) parent = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Root = Path.Combine(parent, "Zones du bureau");
            Directory.CreateDirectory(Root);
            ConfigPath = Path.Combine(Root, "zones.cfg");
            ImagesDir = Path.Combine(Root, ".fonds");
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;

            firstRun = !File.Exists(ConfigPath);
            bool migrated = RemoveOldStartup(); // ancienne entrée « BureauZones » -> « Logicapp »
            if (firstRun || migrated || IsStartupEnabled()) SetStartup(true); // (re)écrit le chemin de l'exe

            tray = new NotifyIcon();
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { tray.Icon = SystemIcons.Application; }
            tray.Text = "Logicapp";
            tray.ContextMenuStrip = BuildTrayMenu();
            tray.Visible = true;

            desktopWait = new Timer { Interval = 1500 };
            desktopWait.Tick += delegate { TryStart(); };

            // Win+D / « Afficher le bureau » : prévenu par Windows quand la fenêtre active change (pas de boucle).
            fgCheck = new Timer { Interval = 250 };
            fgCheck.Tick += delegate
            {
                if (++fgTicks >= (lastFgDesktop ? 6 : 1)) fgCheck.Stop();
                foreach (var z in Zones) { if (lastFgDesktop) z.RaiseIfCoveredByDesktop(); else z.SinkIfRaised(); }
            };
            fgProc = OnForegroundChanged;
            Native.SetWinEventHook(3 /*EVENT_SYSTEM_FOREGROUND*/, 3, IntPtr.Zero, fgProc, 0, 0, 0);

            TryStart();
        }

        public int S(int v) { return (int)Math.Round(v * Scale); }

        void OnForegroundChanged(IntPtr hook, uint ev, IntPtr hwnd, int idObj, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero || Native.IsOwnWindow(hwnd)) return;
            lastFgDesktop = Native.IsDesktopWindow(hwnd);
            fgTicks = 0;
            fgCheck.Stop();
            fgCheck.Start();
        }

        // Attend que le bureau (Progman) existe — au démarrage de Windows ou après un redémarrage de l'Explorateur.
        void TryStart()
        {
            if (Native.FindWindow("Progman", null) == IntPtr.Zero) { desktopWait.Start(); return; }
            desktopWait.Stop();
            foreach (var z in Zones) z.Destroy();
            Zones.Clear();
            LoadZones();
            waiting = false;
            if (firstRun)
            {
                firstRun = false;
                tray.ShowBalloonTip(10000, "Logicapp",
                    "Glissez des icônes du bureau dans une zone.\nClic sur le titre : replier / déplier.\nRoue crantée : apparence de la zone.",
                    ToolTipIcon.Info);
            }
            TrimSoon();
        }

        public void OnZoneLost()
        {
            if (Quitting || waiting) return;
            waiting = true;
            desktopWait.Start();
        }

        void LoadZones()
        {
            bool createDefault = firstRun;
            Locked = false; PeekOnHover = true; DefaultStyle = new ZoneStyle();
            var recs = new List<ZoneRec>();
            if (File.Exists(ConfigPath))
            {
                string section = null; ZoneRec cur = null;
                foreach (string raw in File.ReadAllLines(ConfigPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2);
                        if (section == "Zone") { cur = new ZoneRec { Style = DefaultStyle.Clone() }; recs.Add(cur); }
                        continue;
                    }
                    if (section == null && line.Contains("\t")) // ancien format (version 1)
                    {
                        string[] p = line.Split('\t');
                        int x, y, w, h;
                        if (p.Length >= 6 && int.TryParse(p[1], out x) && int.TryParse(p[2], out y) &&
                            int.TryParse(p[3], out w) && int.TryParse(p[4], out h))
                            recs.Add(new ZoneRec { Name = p[0], X = x, Y = y, W = w, H = h, Collapsed = p[5] == "1", Style = new ZoneStyle() });
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (section == "Global")
                    {
                        if (k == "Locked") Locked = v == "1";
                        else if (k == "PeekOnHover") PeekOnHover = v != "0";
                    }
                    else if (section == "Default") DefaultStyle.Read(k, v);
                    else if (section == "Zone" && cur != null)
                    {
                        int n;
                        switch (k)
                        {
                            case "Name": cur.Name = v; break;
                            case "X": if (int.TryParse(v, out n)) cur.X = n; break;
                            case "Y": if (int.TryParse(v, out n)) cur.Y = n; break;
                            case "Width": if (int.TryParse(v, out n)) cur.W = n; break;
                            case "Height": if (int.TryParse(v, out n)) cur.H = n; break;
                            case "Collapsed": cur.Collapsed = v == "1"; break;
                            default: cur.Style.Read(k, v); break;
                        }
                    }
                }
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in recs)
            {
                string name = Sanitize(r.Name ?? "");
                if (name.Length == 0 || !seen.Add(name)) continue;
                AddZone(name, new Rectangle(r.X, r.Y, r.W, r.H), r.Collapsed, r.Style);
            }
            // Dossiers ajoutés à la main dans « Zones du bureau » : on en fait des zones aussi.
            foreach (string dir in Directory.GetDirectories(Root))
            {
                string n = Path.GetFileName(dir);
                if (seen.Contains(n) || n.StartsWith(".")) continue;
                if ((File.GetAttributes(dir) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                seen.Add(n);
                AddZone(n, DefaultBounds(Zones.Count), false, DefaultStyle.Clone());
            }
            if (Zones.Count == 0 && createDefault) NewZone(false);
            miLock.Checked = Locked;
            miPeek.Checked = PeekOnHover;
            Save();
        }

        void AddZone(string name, Rectangle r, bool collapsed, ZoneStyle style)
        {
            var z = new ZoneForm(name, EnsureVisible(r), collapsed, style);
            Zones.Add(z);
            z.Show();
        }

        Rectangle DefaultBounds(int i)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = S(340), h = S(240);
            return new Rectangle(wa.Left + (wa.Width - w) / 2 + (i % 4) * S(28), wa.Top + S(40) + (i % 4) * S(36), w, h);
        }

        Rectangle EnsureVisible(Rectangle r)
        {
            foreach (Screen s in Screen.AllScreens)
                if (s.WorkingArea.IntersectsWith(new Rectangle(r.X, r.Y, r.Width, S(24)))) return r;
            Rectangle d = DefaultBounds(Zones.Count);
            return new Rectangle(d.X, d.Y, r.Width, r.Height);
        }

        static Rectangle Clamp(Rectangle r)
        {
            Rectangle wa = Screen.FromRectangle(r).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(r.X, wa.Right - r.Width));
            int y = Math.Max(wa.Top, Math.Min(r.Y, wa.Bottom - r.Height));
            return new Rectangle(x, y, r.Width, r.Height);
        }

        public void NewZone(bool atCursor)
        {
            string baseName = "Nouvelle zone", n = baseName;
            int i = 2;
            while (Directory.Exists(Path.Combine(Root, n))) n = baseName + " " + (i++);
            int w = S(340), h = S(240);
            Rectangle r;
            if (atCursor) { Point c = Cursor.Position; r = new Rectangle(c.X - w / 2, c.Y - S(12), w, h); }
            else r = DefaultBounds(Zones.Count);
            AddZone(n, Clamp(r), false, DefaultStyle.Clone());
            Save();
        }

        public void RemoveZone(ZoneForm z)
        {
            Zones.Remove(z);
            z.Destroy();
            Save();
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Logicapp — configuration des zones (rechargée à chaque démarrage)");
            sb.AppendLine("[Global]");
            sb.AppendLine("Locked=" + (Locked ? "1" : "0"));
            sb.AppendLine("PeekOnHover=" + (PeekOnHover ? "1" : "0"));
            sb.AppendLine();
            sb.AppendLine("[Default]");
            DefaultStyle.Write(sb);
            foreach (var z in Zones)
            {
                sb.AppendLine();
                sb.AppendLine("[Zone]");
                sb.AppendLine("Name=" + z.ZoneName);
                sb.AppendLine("X=" + z.Left);
                sb.AppendLine("Y=" + z.Top);
                sb.AppendLine("Width=" + z.Width);
                sb.AppendLine("Height=" + z.ExpandedHeight);
                sb.AppendLine("Collapsed=" + (z.Collapsed ? "1" : "0"));
                z.Style.Write(sb);
            }
            try
            {
                string tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(ConfigPath)) File.Replace(tmp, ConfigPath, null);
                else File.Move(tmp, ConfigPath);
            }
            catch
            {
                try { File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8); } catch { }
            }
        }

        public void ToggleLock()
        {
            Locked = !Locked;
            miLock.Checked = Locked;
            Save();
        }

        // Copie l'image de fond dans « Zones du bureau\.fonds » (synchronisée avec la config).
        public string ImportImage(string src)
        {
            try
            {
                if (src.StartsWith(ImagesDir + "\\", StringComparison.OrdinalIgnoreCase)) return src;
                var di = Directory.CreateDirectory(ImagesDir);
                di.Attributes |= FileAttributes.Hidden;
                string dest = UniquePath(Path.Combine(ImagesDir, Path.GetFileName(src)));
                File.Copy(src, dest);
                return dest;
            }
            catch { return src; }
        }

        ContextMenuStrip BuildTrayMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Nouvelle zone", null, delegate { NewZone(false); });
            m.Items.Add(new ToolStripSeparator());
            miLock = new ToolStripMenuItem("Verrouiller les zones (position et taille)", null, delegate { ToggleLock(); });
            miPeek = new ToolStripMenuItem("Ouvrir les zones repliées au survol", null, delegate
            {
                PeekOnHover = !PeekOnHover; miPeek.Checked = PeekOnHover; Save();
            });
            miStartup = new ToolStripMenuItem("Démarrer avec Windows", null, delegate
            {
                SetStartup(!IsStartupEnabled()); miStartup.Checked = IsStartupEnabled();
            });
            m.Items.Add(miLock);
            m.Items.Add(miPeek);
            m.Items.Add(miStartup);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Ouvrir le dossier des zones", null, delegate { OpenExplorer(Root); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Quitter", null, delegate { Quit(); });
            m.Opening += delegate { miStartup.Checked = IsStartupEnabled(); };
            return m;
        }

        void Quit()
        {
            Quitting = true;
            Save();
            foreach (var z in Zones) z.Destroy();
            tray.Visible = false;
            tray.Dispose();
            Application.Exit();
        }

        public void TrimSoon()
        {
            Timer t = null;
            t = new Timer { Interval = 4000 };
            t.Tick += delegate
            {
                t.Stop(); t.Dispose();
                GC.Collect(); GC.WaitForPendingFinalizers();
                try { Native.EmptyWorkingSet(Process.GetCurrentProcess().Handle); } catch { }
            };
            t.Start();
        }

        static bool RemoveOldStartup()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null || k.GetValue(OldAppName) == null) return false;
                    k.DeleteValue(OldAppName, false);
                    return true;
                }
            }
            catch { return false; }
        }

        static bool IsStartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(AppName) != null;
        }

        static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(AppName, false);
                }
            }
            catch { }
        }

        public static void OpenExplorer(string path)
        {
            try { Process.Start("explorer.exe", "\"" + path + "\""); } catch { }
        }

        public static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
            return s.Trim().TrimEnd('.').Trim();
        }

        public static string UniquePath(string p)
        {
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
            string dir = Path.GetDirectoryName(p), name = Path.GetFileNameWithoutExtension(p), ext = Path.GetExtension(p);
            for (int i = 2; ; i++)
            {
                string c = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(c) && !Directory.Exists(c)) return c;
            }
        }

        // Déplace (ou copie) des fichiers/dossiers vers destDir, via les opérations de fichiers de Windows
        // (barre de progression, demande d'autorisation administrateur si nécessaire).
        public void MoveInto(IEnumerable<string> items, string destDir, bool copy)
        {
            var errors = new List<string>();
            string dd = destDir.TrimEnd('\\');
            foreach (string raw in items)
            {
                string src = raw.TrimEnd('\\');
                string parent = Path.GetDirectoryName(src);
                if (string.Equals(parent, dd, StringComparison.OrdinalIgnoreCase)) continue;
                if ((dd + "\\").StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                bool isDir = Directory.Exists(src);
                if (!isDir && !File.Exists(src)) continue;
                string dest = UniquePath(Path.Combine(dd, Path.GetFileName(src)));
                try
                {
                    if (isDir)
                    {
                        if (copy) VBFS.FileSystem.CopyDirectory(src, dest, VBFS.UIOption.OnlyErrorDialogs);
                        else VBFS.FileSystem.MoveDirectory(src, dest, VBFS.UIOption.OnlyErrorDialogs);
                    }
                    else
                    {
                        if (copy) VBFS.FileSystem.CopyFile(src, dest, VBFS.UIOption.OnlyErrorDialogs);
                        else VBFS.FileSystem.MoveFile(src, dest, VBFS.UIOption.OnlyErrorDialogs);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { errors.Add(Path.GetFileName(src) + " : " + ex.Message); }
            }
            if (errors.Count > 0)
                Dialogs.Error("Certains éléments n'ont pas pu être déplacés :\n\n" + string.Join("\n", errors.ToArray()));
        }
    }

    // =====================================================================================
    //  Une zone : fenêtre dessinée à la main, à transparence par pixel
    // =====================================================================================
    class ZoneForm : Form
    {
        public string ZoneName, Folder;
        public bool Collapsed;
        public int ExpandedHeight;
        public ZoneStyle Style;
        public int ItemCount { get { return items.Count; } }
        public bool IsOpen { get { return !Collapsed || peeking; } }

        class Item { public string FullPath, Name; public bool IsDir; public Bitmap Icon; public Rectangle Cell; }
        enum DragMode { None, Title, Gear, Item, Band, Scroll }

        readonly List<Item> items = new List<Item>();
        readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> bandBase;
        readonly Timer refreshTimer, peekTimer;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly ToolTip tip = new ToolTip { InitialDelay = 600, ReshowDelay = 200 };
        FileSystemWatcher watcher;
        Font labelFont, titleFont, gearFont;
        string gearGlyph;
        int titleH, lineH, scrollY, contentHeight, hover = -1, anchor = -1, downItem = -1, scrollGrab;
        bool peeking, allowDestroy, hoverGear, mouseInside, dropHover, raising, raised;
        DragMode drag;
        Point downPt;
        Rectangle band;
        Bitmap canvas, bgCache;
        string tipText = "";
        SettingsForm settingsForm;

        public ZoneForm(string name, Rectangle bounds, bool collapsed, ZoneStyle style)
        {
            var app = ZoneApp.I;
            ZoneName = name;
            Folder = Path.Combine(app.Root, name);
            Directory.CreateDirectory(Folder);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = name;
            AllowDrop = true;
            Collapsed = collapsed;
            Style = style ?? new ZoneStyle();
            CreateFonts();
            ExpandedHeight = Math.Max(bounds.Height, titleH + app.S(60));
            MinimumSize = new Size(app.S(140), titleH);
            Bounds = new Rectangle(bounds.X, bounds.Y, Math.Max(bounds.Width, app.S(140)), collapsed ? titleH : ExpandedHeight);

            refreshTimer = new Timer { Interval = 400 };
            refreshTimer.Tick += delegate { refreshTimer.Stop(); Reload(); };
            peekTimer = new Timer { Interval = 200 };
            peekTimer.Tick += delegate { CheckPeek(); };

            Reload();
            BuildBgCache();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x80000; // WS_EX_TOOLWINDOW (pas dans Alt+Tab) | WS_EX_LAYERED
                return cp;
            }
        }

        void CreateFonts()
        {
            var app = ZoneApp.I;
            if (labelFont != null) labelFont.Dispose();
            if (titleFont != null) titleFont.Dispose();
            if (gearFont != null) gearFont.Dispose();
            labelFont = ZoneStyle.MakeFont(Style.LabelFont);
            titleFont = ZoneStyle.MakeFont(Style.TitleFont);
            gearFont = new Font("Segoe MDL2 Assets", Math.Max(8f, Math.Min(16f, titleFont.SizeInPoints)), GraphicsUnit.Point);
            gearGlyph = "";
            if (gearFont.Name != "Segoe MDL2 Assets")
            {
                gearFont.Dispose();
                gearFont = new Font("Segoe UI Symbol", Math.Max(8f, titleFont.SizeInPoints), GraphicsUnit.Point);
                gearGlyph = "⚙";
            }
            titleH = (int)Math.Ceiling(titleFont.GetHeight()) + app.S(12);
            lineH = (int)Math.Ceiling(labelFont.GetHeight());
        }

        // Applique un style (aperçu en direct depuis la fenêtre de réglages).
        public void ApplyStyle(ZoneStyle s)
        {
            bool icons = Style.IconSize != s.IconSize;
            bool bg = Style.BgImage != s.BgImage || Style.BgImageMode != s.BgImageMode || Style.BgImageAlpha != s.BgImageAlpha;
            Style = s;
            CreateFonts();
            MinimumSize = new Size(ZoneApp.I.S(140), titleH);
            if (ExpandedHeight < titleH + ZoneApp.I.S(60)) ExpandedHeight = titleH + ZoneApp.I.S(60);
            if (!IsOpen) Height = titleH;
            if (icons) Reload();
            if (bg) BuildBgCache();
            LayoutItems();
            Render();
        }

        // ---------- Fenêtre ----------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero) Native.SetOwner(Handle, progman);
            Render();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartWatcher();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);
            // Fenêtre détruite par Windows (Explorateur redémarré) : on reconstruit les zones.
            if (!allowDestroy && !RecreatingHandle) ZoneApp.I.OnZoneLost();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowDestroy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; // pas d'Alt+F4
            base.OnFormClosing(e);
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutItems();
            Render();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            if (!Collapsed && !peeking) ExpandedHeight = Height;
            if (bgCache != null && (bgCache.Width != Width || bgCache.Height != ExpandedHeight)) { BuildBgCache(); Render(); }
            ZoneApp.I.Save();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x46 && !raising) // WM_WINDOWPOSCHANGING : toujours au fond, juste au-dessus du bureau
            {
                var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(Native.WINDOWPOS));
                IntPtr after;
                if (Native.TryGetSlotAboveDesktop(Handle, out after))
                {
                    if (after == Handle) wp.flags |= 0x4u;           // déjà à la bonne place : SWP_NOZORDER
                    else { wp.hwndInsertAfter = after; wp.flags &= ~0x4u; }
                    Marshal.StructureToPtr(wp, m.LParam, false);
                }
            }
            base.WndProc(ref m);
            if (m.Msg == 0x84 && m.Result == (IntPtr)1 && !ZoneApp.I.Locked && !Collapsed) // WM_NCHITTEST : bords
            {
                long lp = m.LParam.ToInt64();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int edge = ZoneApp.I.S(6);
                bool l = p.X < edge && p.Y > titleH, r = p.X >= Width - edge && p.Y > titleH, b = p.Y >= Height - edge;
                int hit = 0;
                if (b && p.X >= Width - edge * 2) hit = 17; else if (b && p.X < edge * 2) hit = 16;
                else if (b) hit = 15; else if (l) hit = 10; else if (r) hit = 11;
                if (hit != 0) m.Result = (IntPtr)hit;
            }
        }

        public void Destroy()
        {
            allowDestroy = true;
            peekTimer.Stop();
            refreshTimer.Stop();
            if (watcher != null) { watcher.Dispose(); watcher = null; }
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
            try { Close(); Dispose(); } catch { }
            foreach (var it in items) if (it.Icon != null) it.Icon.Dispose();
            if (canvas != null) canvas.Dispose();
            if (bgCache != null) bgCache.Dispose();
        }

        // Après Win+D, le bureau passe devant tout : si la zone est recouverte par lui, on la remonte.
        public void RaiseIfCoveredByDesktop()
        {
            if (!IsHandleCreated || !Visible) return;
            Point pt = PointToScreen(new Point(Width / 2, Math.Min(titleH / 2, Height - 1)));
            IntPtr root = Native.GetAncestor(Native.WindowFromPoint(pt), 2 /*GA_ROOT*/);
            if (root == Handle || !Native.IsDesktopWindow(root)) return;
            raising = true;
            Native.SetWindowPos(Handle, new IntPtr(-1) /*HWND_TOPMOST*/, 0, 0, 0, 0, 0x13 /*NOSIZE|NOMOVE|NOACTIVATE*/);
            raising = false;
            raised = true;
        }

        // Retour à une application : la zone redescend juste au-dessus du bureau.
        public void SinkIfRaised()
        {
            if (!raised || !IsHandleCreated) return;
            raised = false;
            raising = true;
            Native.SetWindowPos(Handle, new IntPtr(-2) /*HWND_NOTOPMOST*/, 0, 0, 0, 0, 0x13);
            raising = false;
            Native.SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x13); // recalculé par WM_WINDOWPOSCHANGING
        }

        // ---------- Repli / survol ----------

        public void ToggleCollapse()
        {
            if (peeking) { StopPeek(false); Collapsed = false; }
            else
            {
                if (!Collapsed) ExpandedHeight = Height;
                Collapsed = !Collapsed;
            }
            ApplyState();
            ZoneApp.I.Save();
        }

        void ApplyState()
        {
            Height = IsOpen ? ExpandedHeight : titleH;
            LayoutItems();
            Render();
        }

        void StartPeek()
        {
            if (!Collapsed || peeking) return;
            peeking = true;
            ApplyState();
            peekTimer.Start();
        }

        void StopPeek(bool apply)
        {
            peeking = false;
            peekTimer.Stop();
            if (apply) ApplyState();
        }

        void CheckPeek()
        {
            if (!peeking) { peekTimer.Stop(); return; }
            if (menu.Visible || Control.MouseButtons != MouseButtons.None || drag != DragMode.None) return;
            if (settingsForm != null && !settingsForm.IsDisposed) return;
            Rectangle r = Bounds;
            r.Inflate(ZoneApp.I.S(8), ZoneApp.I.S(8));
            if (!r.Contains(Cursor.Position)) StopPeek(true);
        }

        // ---------- Géométrie ----------

        int Pad { get { return ZoneApp.I.S(6); } }
        Rectangle GearRect { get { return new Rectangle(Width - titleH, 0, titleH, titleH); } }

        Rectangle ContentRect
        {
            get
            {
                int p = Pad;
                return new Rectangle(p, titleH + p / 2, Math.Max(0, Width - 2 * p), Math.Max(0, Height - titleH - p - p / 2));
            }
        }

        Size CellSize()
        {
            var app = ZoneApp.I;
            int isz = app.S(Style.IconSize), sp = app.S(Style.Spacing);
            return new Size(Math.Max(isz + app.S(22), app.S(76)) + sp, app.S(6) + isz + app.S(4) + 2 * lineH + app.S(8) + sp);
        }

        void LayoutItems()
        {
            Rectangle cr = ContentRect;
            Size cs = CellSize();
            contentHeight = DoLayout(cr.Width, cr, cs);
            if (contentHeight > cr.Height) contentHeight = DoLayout(cr.Width - ZoneApp.I.S(8), cr, cs); // place pour l'ascenseur
            ClampScroll();
        }

        int DoLayout(int width, Rectangle cr, Size cs)
        {
            int cols = Math.Max(1, width / Math.Max(1, cs.Width));
            int cw = Math.Max(cs.Width, width / cols);
            if (cols == 1) cw = Math.Max(1, width);
            for (int i = 0; i < items.Count; i++)
                items[i].Cell = new Rectangle(cr.X + (i % cols) * cw, cr.Y + (i / cols) * cs.Height, cw, cs.Height);
            int rows = (items.Count + cols - 1) / cols;
            return rows * cs.Height;
        }

        int MaxScroll { get { return Math.Max(0, contentHeight - ContentRect.Height); } }
        void ClampScroll() { scrollY = Math.Max(0, Math.Min(scrollY, MaxScroll)); }

        Rectangle CellRect(int i) { Rectangle r = items[i].Cell; r.Offset(0, -scrollY); return r; }

        Rectangle HitRect(int i)
        {
            Rectangle c = CellRect(i);
            int a = ZoneApp.I.S(3);
            return new Rectangle(c.X + a, c.Y + a, c.Width - 2 * a, c.Height - 2 * a);
        }

        int HitItem(Point p)
        {
            if (!IsOpen || !ContentRect.Contains(p)) return -1;
            for (int i = 0; i < items.Count; i++) if (HitRect(i).Contains(p)) return i;
            return -1;
        }

        Rectangle TrackRect
        {
            get { Rectangle cr = ContentRect; return new Rectangle(cr.Right - ZoneApp.I.S(6), cr.Y, ZoneApp.I.S(6), cr.Height); }
        }

        Rectangle ThumbRect
        {
            get
            {
                Rectangle cr = ContentRect;
                if (contentHeight <= 0 || MaxScroll <= 0) return Rectangle.Empty;
                int th = Math.Max(ZoneApp.I.S(24), cr.Height * cr.Height / contentHeight);
                int y = cr.Y + (cr.Height - th) * scrollY / MaxScroll;
                return new Rectangle(cr.Right - ZoneApp.I.S(5), y, ZoneApp.I.S(4), th);
            }
        }

        // ---------- Dessin ----------

        static GraphicsPath RoundRect(RectangleF r, float rad, bool bottom)
        {
            var p = new GraphicsPath();
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
            if (rad < 0.5f) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            if (bottom)
            {
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            }
            else
            {
                p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            }
            p.CloseFigure();
            return p;
        }

        void BuildBgCache()
        {
            if (bgCache != null) { bgCache.Dispose(); bgCache = null; }
            string path = Style.BgImage;
            int w = Width, h = ExpandedHeight;
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || w <= 0 || h <= 0) return;
            try
            {
                Bitmap src;
                using (var fs = File.OpenRead(path))
                using (var img = Image.FromStream(fs)) src = new Bitmap(img);
                var c = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (src)
                using (var g = Graphics.FromImage(c))
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(new ColorMatrix { Matrix33 = Style.BgImageAlpha / 255f });
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    int iw = src.Width, ih = src.Height;
                    Rectangle dest = Rectangle.Empty;
                    switch (Style.BgImageMode)
                    {
                        case 0:
                        case 1:
                            {
                                float s = Style.BgImageMode == 0 ? Math.Max((float)w / iw, (float)h / ih) : Math.Min((float)w / iw, (float)h / ih);
                                int dw = (int)Math.Ceiling(iw * s), dh = (int)Math.Ceiling(ih * s);
                                dest = new Rectangle((w - dw) / 2, (h - dh) / 2, dw, dh);
                                break;
                            }
                        case 2: dest = new Rectangle(0, 0, w, h); break;
                        case 4: dest = new Rectangle((w - iw) / 2, (h - ih) / 2, iw, ih); break;
                        case 3:
                            using (var tb = new TextureBrush(src, new Rectangle(0, 0, iw, ih), ia)) g.FillRectangle(tb, 0, 0, w, h);
                            break;
                    }
                    if (!dest.IsEmpty) g.DrawImage(src, dest, 0, 0, iw, ih, GraphicsUnit.Pixel, ia);
                }
                bgCache = c;
            }
            catch { }
        }

        public void Render()
        {
            if (!IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0) return;
            var app = ZoneApp.I;
            int w = Width, h = Height;
            if (canvas == null || canvas.Width != w || canvas.Height != h)
            {
                if (canvas != null) canvas.Dispose();
                canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            }
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float rad = app.S(Style.CornerRadius);
                bool open = IsOpen;

                using (var shape = RoundRect(new RectangleF(0, 0, w, h), rad, true))
                {
                    // Fond : couleur (alpha mini 1 pour que la zone reste cliquable) puis image
                    using (var b = new SolidBrush(Color.FromArgb(Math.Max(1, Style.BgAlpha), Style.BgColor))) g.FillPath(b, shape);
                    if (bgCache != null)
                        using (var tb = new TextureBrush(bgCache, WrapMode.Clamp))
                        {
                            int th = open ? h : ExpandedHeight;
                            if (bgCache.Width != w || bgCache.Height != th) tb.ScaleTransform((float)w / bgCache.Width, (float)th / bgCache.Height);
                            g.FillPath(tb, shape);
                        }
                }

                // Barre de titre
                using (var tp = RoundRect(new RectangleF(0, 0, w, titleH), rad, !open))
                using (var b = new SolidBrush(Color.FromArgb(Style.TitleAlpha, Style.TitleColor)))
                    g.FillPath(b, tp);
                string text = (open ? "▾  " : "▸  ") + ZoneName + (Collapsed && Style.ShowCount ? "   (" + items.Count + ")" : "");
                RectangleF tr = Style.TitleAlign == 1
                    ? new RectangleF(titleH, 0, Math.Max(1, w - 2 * titleH), titleH)
                    : new RectangleF(app.S(10), 0, Math.Max(1, w - app.S(10) - titleH), titleH);
                using (var sf = new StringFormat(StringFormatFlags.NoWrap))
                using (var b = new SolidBrush(Style.TitleTextColor))
                {
                    sf.LineAlignment = StringAlignment.Center;
                    sf.Alignment = Style.TitleAlign == 0 ? StringAlignment.Near : Style.TitleAlign == 1 ? StringAlignment.Center : StringAlignment.Far;
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    g.DrawString(text, titleFont, b, tr, sf);

                    // Roue crantée
                    Rectangle gr = GearRect;
                    if (hoverGear)
                    {
                        int d = (int)(titleH * 0.72);
                        using (var hb = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
                            g.FillEllipse(hb, gr.X + (gr.Width - d) / 2, gr.Y + (gr.Height - d) / 2, d, d);
                    }
                    int ga = hoverGear ? 255 : mouseInside ? 190 : 110;
                    sf.Alignment = StringAlignment.Center;
                    using (var gb = new SolidBrush(Color.FromArgb(ga, Style.TitleTextColor)))
                        g.DrawString(gearGlyph, gearFont, gb, new RectangleF(gr.X, gr.Y + 1, gr.Width, gr.Height), sf);
                }

                // Icônes
                if (open)
                {
                    Rectangle cr = ContentRect;
                    g.SetClip(cr);
                    int isz = app.S(Style.IconSize);
                    using (var sf = new StringFormat())
                    using (var lb = new SolidBrush(Style.LabelColor))
                    using (var sb = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.Trimming = StringTrimming.EllipsisWord;
                        sf.FormatFlags = StringFormatFlags.LineLimit;
                        for (int i = 0; i < items.Count; i++)
                        {
                            Rectangle cell = CellRect(i);
                            if (cell.Bottom < cr.Top || cell.Top > cr.Bottom) continue;
                            bool sel = selected.Contains(items[i].FullPath);
                            if (sel || i == hover)
                            {
                                using (var hp = RoundRect(HitRect(i), app.S(5), true))
                                {
                                    using (var hb = new SolidBrush(sel ? Color.FromArgb(i == hover ? 120 : 95, 0, 120, 215) : Color.FromArgb(32, 255, 255, 255)))
                                        g.FillPath(hb, hp);
                                    if (sel) using (var pen = new Pen(Color.FromArgb(150, 90, 170, 255))) g.DrawPath(pen, hp);
                                }
                            }
                            var ir = new Rectangle(cell.X + (cell.Width - isz) / 2, cell.Y + app.S(6), isz, isz);
                            if (items[i].Icon != null) g.DrawImage(items[i].Icon, ir);
                            var lr = new RectangleF(cell.X + app.S(4), ir.Bottom + app.S(4), cell.Width - app.S(8), 2 * lineH + 2);
                            if (Style.LabelShadow)
                            {
                                var sr = lr; sr.Offset(1, 1);
                                g.DrawString(items[i].Name, labelFont, sb, sr, sf);
                            }
                            g.DrawString(items[i].Name, labelFont, lb, lr, sf);
                        }
                    }
                    // Ascenseur
                    Rectangle thumb = ThumbRect;
                    if (!thumb.IsEmpty)
                        using (var tp = RoundRect(thumb, thumb.Width / 2f, true))
                        using (var tb = new SolidBrush(Color.FromArgb(drag == DragMode.Scroll ? 200 : mouseInside ? 140 : 70, 255, 255, 255)))
                            g.FillPath(tb, tp);
                    // Sélection au lasso
                    if (drag == DragMode.Band && band.Width > 0 && band.Height > 0)
                    {
                        using (var bb = new SolidBrush(Color.FromArgb(50, 0, 120, 215))) g.FillRectangle(bb, band);
                        using (var pen = new Pen(Color.FromArgb(180, 0, 120, 215))) g.DrawRectangle(pen, band);
                    }
                    g.ResetClip();
                }

                // Bordure
                if (Style.BorderWidth > 0)
                {
                    float bw = app.S(Style.BorderWidth);
                    using (var pen = new Pen(Color.FromArgb(Style.BorderAlpha, Style.BorderColor), bw))
                    using (var bp = RoundRect(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), Math.Max(0, rad - bw / 2), true))
                        g.DrawPath(pen, bp);
                }
                if (dropHover)
                {
                    float bw = app.S(2);
                    using (var pen = new Pen(Color.FromArgb(230, 0, 120, 215), bw))
                    using (var bp = RoundRect(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), Math.Max(0, rad - bw / 2), true))
                        g.DrawPath(pen, bp);
                }
            }
            PushLayered(canvas);
        }

        void PushLayered(Bitmap bmp)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(screen), hb = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hb = bmp.GetHbitmap(Color.FromArgb(0));
                old = Native.SelectObject(mem, hb);
                var size = new Native.SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new Native.POINT();
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, mem, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
            }
            finally
            {
                if (old != IntPtr.Zero) Native.SelectObject(mem, old);
                if (hb != IntPtr.Zero) Native.DeleteObject(hb);
                Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }

        // ---------- Contenu ----------

        void StartWatcher()
        {
            if (watcher != null) watcher.Dispose();
            watcher = null;
            try
            {
                var w = new FileSystemWatcher(Folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                    SynchronizingObject = this
                };
                FileSystemEventHandler h = delegate { QueueReload(); };
                w.Created += h; w.Deleted += h; w.Changed += h;
                w.Renamed += delegate { QueueReload(); };
                w.Error += delegate { QueueReload(); };
                w.EnableRaisingEvents = true;
                watcher = w;
            }
            catch { }
        }

        void QueueReload() { refreshTimer.Stop(); refreshTimer.Start(); }

        public void Reload()
        {
            foreach (var it in items) if (it.Icon != null) it.Icon.Dispose();
            items.Clear();
            int isz = ZoneApp.I.S(Style.IconSize);
            try
            {
                foreach (string p in Directory.GetFileSystemEntries(Folder))
                {
                    FileAttributes a;
                    try { a = File.GetAttributes(p); } catch { continue; }
                    if ((a & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    string dn;
                    Bitmap ic = ShellIcons.Get(p, isz, out dn);
                    items.Add(new Item
                    {
                        FullPath = p,
                        IsDir = (a & FileAttributes.Directory) != 0,
                        Name = string.IsNullOrEmpty(dn) ? Path.GetFileName(p) : dn,
                        Icon = ic
                    });
                }
            }
            catch { }
            items.Sort((x, y) => x.IsDir != y.IsDir ? (x.IsDir ? -1 : 1)
                : string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
            selected.RemoveWhere(s => !items.Exists(it => string.Equals(it.FullPath, s, StringComparison.OrdinalIgnoreCase)));
            hover = -1;
            LayoutItems();
            Render();
        }

        List<string> SelectedPaths()
        {
            var r = new List<string>();
            foreach (var it in items) if (selected.Contains(it.FullPath)) r.Add(it.FullPath);
            return r;
        }

        int FirstSelected()
        {
            for (int i = 0; i < items.Count; i++) if (selected.Contains(items[i].FullPath)) return i;
            return -1;
        }

        void OpenSelected()
        {
            foreach (string p in SelectedPaths())
            {
                try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true, WorkingDirectory = Folder }); }
                catch (Win32Exception) { }
                catch (Exception ex) { Dialogs.Error(ex.Message); }
            }
        }

        void DeleteSelected()
        {
            var paths = SelectedPaths();
            if (paths.Count == 0) return;
            int f = FirstSelected();
            if (!Dialogs.Confirm(paths.Count == 1
                ? "Envoyer « " + items[f].Name + " » à la corbeille ?"
                : "Envoyer ces " + paths.Count + " éléments à la corbeille ?")) return;
            foreach (string p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                        VBFS.FileSystem.DeleteDirectory(p, VBFS.UIOption.OnlyErrorDialogs, VBFS.RecycleOption.SendToRecycleBin);
                    else
                        VBFS.FileSystem.DeleteFile(p, VBFS.UIOption.OnlyErrorDialogs, VBFS.RecycleOption.SendToRecycleBin);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Dialogs.Error(ex.Message); }
            }
            Reload();
        }

        void BeginRename()
        {
            int i = FirstSelected();
            if (i < 0) return;
            var app = ZoneApp.I;
            Rectangle c = CellRect(i);
            int top = c.Y + app.S(6) + app.S(Style.IconSize) + app.S(2);
            var r = new Rectangle(PointToScreen(new Point(c.X + app.S(2), top)), new Size(c.Width - app.S(4), 2 * lineH + app.S(8)));
            string path = items[i].FullPath, shown = items[i].Name;
            var box = new RenameBox(shown, labelFont, r);
            box.Committed += delegate (string newName) { RenameItem(path, shown, newName); };
            box.Show();
        }

        void RenameItem(string old, string shown, string label)
        {
            string newName = ZoneApp.Sanitize(label);
            if (newName.Length == 0 || newName == shown) return;
            bool isDir = Directory.Exists(old);
            if (!isDir)
            {
                string ext = Path.GetExtension(old);
                // Extension masquée (ex. raccourcis .lnk) : on la conserve.
                if (ext.Length > 0 && !shown.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) newName += ext;
            }
            string np = Path.Combine(Folder, newName);
            if (string.Equals(np, old, StringComparison.Ordinal)) return;
            try
            {
                if (isDir) Directory.Move(old, np); else File.Move(old, np);
                selected.Clear(); selected.Add(np);
            }
            catch (Exception ex) { Dialogs.Error("Impossible de renommer : " + ex.Message); }
            Reload();
        }

        // ---------- Souris / clavier ----------

        bool Moved(Point p)
        {
            Size d = SystemInformation.DragSize;
            return Math.Abs(p.X - downPt.X) > d.Width / 2 + 1 || Math.Abs(p.Y - downPt.Y) > d.Height / 2 + 1;
        }

        void SetTip(string t)
        {
            if (t == tipText) return;
            tipText = t;
            tip.SetToolTip(this, t);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            mouseInside = true;
            if (ZoneApp.I.PeekOnHover) StartPeek();
            Render();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (drag != DragMode.None) return;
            mouseInside = false; hover = -1; hoverGear = false;
            SetTip("");
            Render();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            downPt = e.Location;
            bool ctrl = (ModifierKeys & Keys.Control) != 0, shift = (ModifierKeys & Keys.Shift) != 0;
            if (e.Button == MouseButtons.Left)
            {
                if (e.Y < titleH) { drag = GearRect.Contains(e.Location) ? DragMode.Gear : DragMode.Title; return; }
                if (!IsOpen) return;
                if (MaxScroll > 0 && TrackRect.Contains(e.Location))
                {
                    Rectangle th = ThumbRect;
                    scrollGrab = th.Contains(e.Location) ? e.Y - th.Y : th.Height / 2;
                    drag = DragMode.Scroll;
                    DragScroll(e.Y);
                    return;
                }
                int i = HitItem(e.Location);
                if (i >= 0)
                {
                    string p = items[i].FullPath;
                    if (ctrl) { if (!selected.Remove(p)) selected.Add(p); anchor = i; }
                    else if (shift && anchor >= 0 && anchor < items.Count)
                    {
                        selected.Clear();
                        for (int k = Math.Min(anchor, i); k <= Math.Max(anchor, i); k++) selected.Add(items[k].FullPath);
                    }
                    else
                    {
                        if (!selected.Contains(p)) { selected.Clear(); selected.Add(p); }
                        anchor = i; downItem = i; drag = DragMode.Item;
                    }
                }
                else
                {
                    bandBase = ctrl ? new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!ctrl) selected.Clear();
                    band = new Rectangle(e.Location, Size.Empty);
                    drag = DragMode.Band;
                }
                Render();
            }
            else if (e.Button == MouseButtons.Right)
            {
                int i = HitItem(e.Location);
                if (i >= 0 && !selected.Contains(items[i].FullPath)) { selected.Clear(); selected.Add(items[i].FullPath); anchor = i; Render(); }
            }
        }

        void DragScroll(int y)
        {
            Rectangle cr = ContentRect, th = ThumbRect;
            int range = cr.Height - th.Height;
            if (range <= 0) return;
            scrollY = (y - scrollGrab - cr.Y) * MaxScroll / range;
            ClampScroll();
            Render();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            switch (drag)
            {
                case DragMode.Title:
                    if (!ZoneApp.I.Locked && Moved(e.Location))
                    {
                        drag = DragMode.None;
                        Native.ReleaseCapture();
                        Native.SendMessage(Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero);
                    }
                    return;
                case DragMode.Gear: return;
                case DragMode.Item:
                    if (Moved(e.Location)) { drag = DragMode.None; StartDragOut(); }
                    return;
                case DragMode.Scroll: DragScroll(e.Y); return;
                case DragMode.Band:
                    band = Rectangle.FromLTRB(Math.Min(downPt.X, e.X), Math.Min(downPt.Y, e.Y), Math.Max(downPt.X, e.X), Math.Max(downPt.Y, e.Y));
                    selected.Clear();
                    foreach (var s in bandBase) selected.Add(s);
                    for (int i = 0; i < items.Count; i++) if (HitRect(i).IntersectsWith(band)) selected.Add(items[i].FullPath);
                    Render();
                    return;
            }
            int h = HitItem(e.Location);
            bool gh = GearRect.Contains(e.Location);
            if (h != hover || gh != hoverGear)
            {
                hover = h; hoverGear = gh;
                SetTip(h >= 0 ? items[h].Name : gh ? "Apparence de la zone" : "");
                Render();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var d = drag;
            drag = DragMode.None;
            if (e.Button == MouseButtons.Left)
            {
                if (d == DragMode.Gear) { if (GearRect.Contains(e.Location)) OpenSettings(); }
                else if (d == DragMode.Title) ToggleCollapse();
                else if (d == DragMode.Item)
                {
                    if (downItem >= 0 && downItem < items.Count) { selected.Clear(); selected.Add(items[downItem].FullPath); }
                    Render();
                }
                else if (d == DragMode.Band || d == DragMode.Scroll) Render();
            }
            else if (e.Button == MouseButtons.Right) ShowMenu(e.Y >= titleH && HitItem(e.Location) >= 0);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || e.Y < titleH) return;
            int i = HitItem(e.Location);
            if (i < 0) return;
            if (!selected.Contains(items[i].FullPath)) { selected.Clear(); selected.Add(items[i].FullPath); }
            OpenSelected();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!IsOpen || MaxScroll <= 0) return;
            scrollY -= e.Delta * CellSize().Height / 240;
            ClampScroll();
            hover = HitItem(PointToClient(Cursor.Position));
            Render();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Delete) DeleteSelected();
            else if (e.KeyCode == Keys.F2) BeginRename();
            else if (e.KeyCode == Keys.F5) Reload();
            else if (e.KeyCode == Keys.Enter) OpenSelected();
            else if (e.KeyCode == Keys.Escape) { selected.Clear(); Render(); }
            else if (e.Control && e.KeyCode == Keys.A) { foreach (var it in items) selected.Add(it.FullPath); Render(); }
        }

        // ---------- Glisser-déposer ----------

        void StartDragOut()
        {
            var sc = new StringCollection();
            foreach (string p in SelectedPaths()) sc.Add(p);
            if (sc.Count == 0) return;
            var data = new DataObject();
            data.SetFileDropList(sc);
            DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            QueueReload();
        }

        static DragDropEffects EffectFor(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.None;
            bool ctrl = (e.KeyState & 8) != 0;
            if (ctrl && (e.AllowedEffect & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            if ((e.AllowedEffect & DragDropEffects.Move) != 0) return DragDropEffects.Move;
            return e.AllowedEffect & DragDropEffects.Copy;
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = EffectFor(e);
            if (e.Effect == DragDropEffects.None) return;
            StartPeek();
            dropHover = true;
            Render();
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            e.Effect = EffectFor(e);
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            dropHover = false;
            Render();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            dropHover = false;
            Render();
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            bool copy = e.Effect == DragDropEffects.Copy;
            BeginInvoke(new Action(delegate // hors du glisser-déposer pour libérer la source
            {
                ZoneApp.I.MoveInto(files, Folder, copy);
                Reload();
            }));
        }

        // ---------- Menus ----------

        void ShowMenu(bool onItem)
        {
            var app = ZoneApp.I;
            menu.Items.Clear();
            if (onItem)
            {
                var open = Add("Ouvrir", OpenSelected);
                open.Font = new Font(open.Font, FontStyle.Bold);
                Add("Afficher dans l'Explorateur", delegate
                {
                    var ps = SelectedPaths();
                    if (ps.Count > 0) try { Process.Start("explorer.exe", "/select,\"" + ps[0] + "\""); } catch { }
                });
                Add("Remettre sur le bureau", delegate { app.MoveInto(SelectedPaths(), app.DesktopDir, false); Reload(); });
                Add("Renommer", BeginRename);
                Add("Supprimer", DeleteSelected);
                menu.Items.Add(new ToolStripSeparator());
            }
            Add("Apparence…", OpenSettings);
            Add(peeking ? "Garder la zone dépliée" : (Collapsed ? "Déplier la zone" : "Replier la zone"), ToggleCollapse);
            Add("Renommer la zone…", delegate
            {
                string n = Dialogs.Input("Renommer la zone", "Nom de la zone :", ZoneName);
                if (n != null) RenameTo(n);
            });
            Add("Ouvrir le dossier de la zone", delegate { ZoneApp.OpenExplorer(Folder); });
            menu.Items.Add(new ToolStripSeparator());
            Add("Nouvelle zone", delegate { app.NewZone(true); });
            Add("Verrouiller les zones", app.ToggleLock).Checked = app.Locked;
            Add("Supprimer la zone…", DeleteZone);
            menu.Show(Cursor.Position);
        }

        ToolStripMenuItem Add(string text, Action action)
        {
            var mi = new ToolStripMenuItem(text, null, delegate { action(); });
            menu.Items.Add(mi);
            return mi;
        }

        void OpenSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
            settingsForm = new SettingsForm(this);
            settingsForm.Show();
        }

        public bool RenameTo(string n)
        {
            var app = ZoneApp.I;
            n = ZoneApp.Sanitize(n ?? "");
            if (n.Length == 0 || n == ZoneName) return true;
            if (n.StartsWith(".")) { Dialogs.Error("Le nom ne peut pas commencer par un point."); return false; }
            string nf = Path.Combine(app.Root, n);
            bool caseOnly = string.Equals(nf, Folder, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && Directory.Exists(nf)) { Dialogs.Error("Une zone porte déjà ce nom."); return false; }
            if (watcher != null) { watcher.Dispose(); watcher = null; }
            bool ok = true;
            try
            {
                if (caseOnly)
                {
                    string tmp = Folder + ".~renommage";
                    Directory.Move(Folder, tmp);
                    Directory.Move(tmp, nf);
                }
                else Directory.Move(Folder, nf);
                ZoneName = n; Folder = nf; Text = n;
            }
            catch (Exception ex) { Dialogs.Error("Impossible de renommer la zone : " + ex.Message); ok = false; }
            StartWatcher();
            Reload();
            app.Save();
            return ok;
        }

        void DeleteZone()
        {
            var app = ZoneApp.I;
            if (!Dialogs.Confirm("Supprimer la zone « " + ZoneName + " » ?\n\nSes éléments (" + ItemCount +
                ") seront remis sur le bureau.")) return;
            if (watcher != null) { watcher.Dispose(); watcher = null; }
            var list = new List<string>();
            foreach (string p in Directory.GetFileSystemEntries(Folder))
                if (!string.Equals(Path.GetFileName(p), "desktop.ini", StringComparison.OrdinalIgnoreCase)) list.Add(p);
            app.MoveInto(list, app.DesktopDir, false);
            foreach (string p in Directory.GetFileSystemEntries(Folder))
            {
                if (string.Equals(Path.GetFileName(p), "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                Dialogs.Error("Certains éléments n'ont pas pu être remis sur le bureau : la zone est conservée.");
                StartWatcher(); Reload();
                return;
            }
            try { Directory.Delete(Folder, true); } catch { }
            app.RemoveZone(this);
        }
    }

    // =====================================================================================
    //  Fenêtre de réglages (roue crantée) — aperçu en direct
    // =====================================================================================
    class SettingsForm : Form
    {
        readonly ZoneForm zone;
        ZoneStyle original, st;
        bool loading, committed;

        TextBox tbName, tbImage;
        NumericUpDown nuRadius, nuBorder;
        ColorButton cbBorder, cbBg, cbTitleText, cbTitleBar, cbLabel;
        Slider slBorderAlpha, slBgAlpha, slImgAlpha, slTitleAlpha, slIcon, slSpacing;
        ComboBox cmbMode, cmbAlign;
        FontButton fbTitle, fbLabel;
        CheckBox chkCount, chkShadow;
        Label lblInfo;

        static int S(int v) { return ZoneApp.I.S(v); }
        static string Pct(int v) { return v + " %"; }

        public SettingsForm(ZoneForm z)
        {
            zone = z;
            original = z.Style.Clone();
            st = z.Style.Clone();

            Text = "Apparence de la zone";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            ClientSize = new Size(S(500), S(470));

            var tabs = new TabControl { Location = new Point(S(10), S(10)), Size = new Size(S(480), S(400)) };
            Controls.Add(tabs);

            // --- Général ---
            var t = NewTab(tabs, "Général");
            tbName = new TextBox { Width = S(260) };
            Row(t, "Nom de la zone", tbName);
            nuRadius = new NumericUpDown { Minimum = 0, Maximum = 40, Width = S(70) };
            Row(t, "Coins arrondis (px)", nuRadius);
            nuBorder = new NumericUpDown { Minimum = 0, Maximum = 10, Width = S(70) };
            Row(t, "Épaisseur de bordure", nuBorder);
            cbBorder = new ColorButton();
            Row(t, "Couleur de bordure", cbBorder);
            slBorderAlpha = new Slider(0, 100, Pct);
            Row(t, "Opacité de bordure", slBorderAlpha);

            // --- Fond ---
            t = NewTab(tabs, "Fond");
            cbBg = new ColorButton();
            Row(t, "Couleur du fond", cbBg);
            slBgAlpha = new Slider(0, 100, Pct);
            Row(t, "Opacité du fond", slBgAlpha);
            var imgPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            tbImage = new TextBox { Width = S(170), ReadOnly = true };
            var btnBrowse = new Button { Text = "Parcourir…", AutoSize = true };
            var btnRemove = new Button { Text = "Retirer", AutoSize = true };
            imgPanel.Controls.AddRange(new Control[] { tbImage, btnBrowse, btnRemove });
            Row(t, "Image de fond", imgPanel);
            cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(160) };
            cmbMode.Items.AddRange(new object[] { "Remplir", "Ajuster", "Étirer", "Mosaïque", "Centrer" });
            Row(t, "Disposition de l'image", cmbMode);
            slImgAlpha = new Slider(0, 100, Pct);
            Row(t, "Opacité de l'image", slImgAlpha);
            Note(t, "Astuce : opacité du fond à 0 % + une image = image seule.\nL'opacité ne touche que le fond : icônes et textes restent nets.");

            // --- Titre ---
            t = NewTab(tabs, "Titre");
            fbTitle = new FontButton();
            Row(t, "Police du nom", fbTitle);
            cbTitleText = new ColorButton();
            Row(t, "Couleur du texte", cbTitleText);
            cbTitleBar = new ColorButton();
            Row(t, "Couleur de la barre", cbTitleBar);
            slTitleAlpha = new Slider(0, 100, Pct);
            Row(t, "Opacité de la barre", slTitleAlpha);
            cmbAlign = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(160) };
            cmbAlign.Items.AddRange(new object[] { "Gauche", "Centre", "Droite" });
            Row(t, "Alignement du nom", cmbAlign);
            chkCount = new CheckBox { Text = "Afficher le nombre d'éléments quand la zone est repliée", AutoSize = true };
            Row(t, "", chkCount);

            // --- Icônes ---
            t = NewTab(tabs, "Icônes");
            slIcon = new Slider(2, 16, v => (v * 8) + " px");
            Row(t, "Taille des icônes", slIcon);
            slSpacing = new Slider(0, 40, v => v + " px");
            Row(t, "Espacement", slSpacing);
            fbLabel = new FontButton();
            Row(t, "Police des noms", fbLabel);
            cbLabel = new ColorButton();
            Row(t, "Couleur des noms", cbLabel);
            chkShadow = new CheckBox { Text = "Ombre sous les noms (meilleure lisibilité)", AutoSize = true };
            Row(t, "", chkShadow);

            // --- Boutons ---
            int by = S(422);
            var btnAll = new Button { Text = "Appliquer à toutes les zones", AutoSize = true, Location = new Point(S(10), by) };
            var btnReset = new Button { Text = "Réinitialiser", AutoSize = true, Location = new Point(S(200), by) };
            var btnOk = new Button { Text = "OK", Size = new Size(S(80), S(28)), Location = new Point(S(320), by) };
            var btnCancel = new Button { Text = "Annuler", Size = new Size(S(80), S(28)), Location = new Point(S(408), by), DialogResult = DialogResult.Cancel };
            lblInfo = new Label { AutoSize = true, ForeColor = Color.SeaGreen, Location = new Point(S(12), S(452)) };
            Controls.AddRange(new Control[] { btnAll, btnReset, btnOk, btnCancel, lblInfo });
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            foreach (TabPage pg in tabs.TabPages) // l'espace restant va à une ligne vide finale
            {
                var tl = (TableLayoutPanel)pg.Controls[0];
                tl.RowCount++;
                tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }

            LoadControls();

            // Aperçu en direct
            EventHandler changed = delegate { OnChanged(); };
            nuRadius.ValueChanged += changed; nuBorder.ValueChanged += changed;
            foreach (var c in new[] { cbBorder, cbBg, cbTitleText, cbTitleBar, cbLabel }) c.ValueChanged += changed;
            foreach (var s in new[] { slBorderAlpha, slBgAlpha, slImgAlpha, slTitleAlpha, slIcon, slSpacing }) s.ValueChanged += changed;
            cmbMode.SelectedIndexChanged += changed; cmbAlign.SelectedIndexChanged += changed;
            fbTitle.ValueChanged += changed; fbLabel.ValueChanged += changed;
            chkCount.CheckedChanged += changed; chkShadow.CheckedChanged += changed;

            btnBrowse.Click += delegate
            {
                using (var d = new OpenFileDialog
                {
                    Title = "Choisir une image de fond",
                    Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Tous les fichiers|*.*"
                })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    try { using (var img = Image.FromFile(d.FileName)) { } }
                    catch { MessageBox.Show(this, "Ce fichier n'est pas une image lisible.", Text); return; }
                    tbImage.Text = ZoneApp.I.ImportImage(d.FileName);
                    OnChanged();
                }
            };
            btnRemove.Click += delegate { tbImage.Text = ""; OnChanged(); };
            btnAll.Click += delegate
            {
                ReadControls();
                foreach (var other in ZoneApp.I.Zones) other.ApplyStyle(st.Clone());
                ZoneApp.I.DefaultStyle = st.Clone();
                original = st.Clone();
                ZoneApp.I.Save();
                lblInfo.Text = "✓ Appliqué à toutes les zones (et aux prochaines zones créées)";
            };
            btnReset.Click += delegate
            {
                st = new ZoneStyle();
                LoadControls();
                zone.ApplyStyle(st.Clone());
            };
            btnOk.Click += delegate
            {
                ReadControls();
                zone.ApplyStyle(st.Clone());
                if (tbName.Text.Trim() != zone.ZoneName && !zone.RenameTo(tbName.Text)) return;
                committed = true;
                ZoneApp.I.Save();
                Close();
            };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!committed) zone.ApplyStyle(original.Clone()); // Annuler : on revient à l'état d'origine
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            ZoneApp.I.TrimSoon();
        }

        void OnChanged()
        {
            if (loading) return;
            ReadControls();
            zone.ApplyStyle(st.Clone());
            lblInfo.Text = "";
        }

        static int A2P(int a) { return (int)Math.Round(a / 2.55); }
        static int P2A(int p) { return (int)Math.Round(p * 2.55); }

        void LoadControls()
        {
            loading = true;
            tbName.Text = zone.ZoneName;
            nuRadius.Value = Math.Max(0, Math.Min(40, st.CornerRadius));
            nuBorder.Value = Math.Max(0, Math.Min(10, st.BorderWidth));
            cbBorder.Value = st.BorderColor;
            slBorderAlpha.Value = A2P(st.BorderAlpha);
            cbBg.Value = st.BgColor;
            slBgAlpha.Value = A2P(st.BgAlpha);
            tbImage.Text = st.BgImage;
            cmbMode.SelectedIndex = st.BgImageMode;
            slImgAlpha.Value = A2P(st.BgImageAlpha);
            fbTitle.Value = st.TitleFont;
            cbTitleText.Value = st.TitleTextColor;
            cbTitleBar.Value = st.TitleColor;
            slTitleAlpha.Value = A2P(st.TitleAlpha);
            cmbAlign.SelectedIndex = st.TitleAlign;
            chkCount.Checked = st.ShowCount;
            slIcon.Value = Math.Max(2, Math.Min(16, (st.IconSize + 4) / 8));
            slSpacing.Value = Math.Min(40, st.Spacing);
            fbLabel.Value = st.LabelFont;
            cbLabel.Value = st.LabelColor;
            chkShadow.Checked = st.LabelShadow;
            loading = false;
        }

        void ReadControls()
        {
            st.CornerRadius = (int)nuRadius.Value;
            st.BorderWidth = (int)nuBorder.Value;
            st.BorderColor = cbBorder.Value;
            st.BorderAlpha = P2A(slBorderAlpha.Value);
            st.BgColor = cbBg.Value;
            st.BgAlpha = P2A(slBgAlpha.Value);
            st.BgImage = tbImage.Text;
            st.BgImageMode = Math.Max(0, cmbMode.SelectedIndex);
            st.BgImageAlpha = P2A(slImgAlpha.Value);
            st.TitleFont = fbTitle.Value;
            st.TitleTextColor = cbTitleText.Value;
            st.TitleColor = cbTitleBar.Value;
            st.TitleAlpha = P2A(slTitleAlpha.Value);
            st.TitleAlign = Math.Max(0, cmbAlign.SelectedIndex);
            st.ShowCount = chkCount.Checked;
            st.IconSize = slIcon.Value * 8;
            st.Spacing = slSpacing.Value;
            st.LabelFont = fbLabel.Value;
            st.LabelColor = cbLabel.Value;
            st.LabelShadow = chkShadow.Checked;
        }

        static TableLayoutPanel NewTab(TabControl tabs, string title)
        {
            var page = new TabPage(title) { UseVisualStyleBackColor = true };
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true,
                Padding = new Padding(S(10), S(12), S(10), S(6))
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.Controls.Add(t);
            tabs.TabPages.Add(page);
            return t;
        }

        static void Row(TableLayoutPanel t, string label, Control c)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(6), S(12), S(6)) };
            c.Anchor = AnchorStyles.Left;
            c.Margin = new Padding(0, S(4), 0, S(4));
            t.Controls.Add(l, 0, r);
            t.Controls.Add(c, 1, r);
        }

        static void Note(TableLayoutPanel t, string text)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new Label { Text = text, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, S(12), 0, 0) };
            t.Controls.Add(l, 0, r);
            t.SetColumnSpan(l, 2);
        }
    }

    class Slider : Panel
    {
        readonly TrackBar bar;
        readonly Label lbl;
        readonly Func<int, string> fmt;
        public event EventHandler ValueChanged;

        public Slider(int min, int max, Func<int, string> fmt)
        {
            this.fmt = fmt;
            Size = new Size(ZoneApp.I.S(290), ZoneApp.I.S(32));
            bar = new TrackBar
            {
                Minimum = min, Maximum = max, TickStyle = TickStyle.None, AutoSize = false,
                Height = ZoneApp.I.S(30), Dock = DockStyle.Fill, SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10)
            };
            lbl = new Label { Dock = DockStyle.Right, Width = ZoneApp.I.S(58), TextAlign = ContentAlignment.MiddleRight };
            Controls.Add(bar);
            Controls.Add(lbl);
            bar.ValueChanged += delegate
            {
                lbl.Text = fmt(bar.Value);
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            };
            lbl.Text = fmt(bar.Value);
        }

        public int Value
        {
            get { return bar.Value; }
            set { bar.Value = Math.Max(bar.Minimum, Math.Min(bar.Maximum, value)); lbl.Text = fmt(bar.Value); }
        }
    }

    class ColorButton : Button
    {
        Color c = Color.White;
        public event EventHandler ValueChanged;

        public ColorButton()
        {
            Size = new Size(ZoneApp.I.S(90), ZoneApp.I.S(26));
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderColor = Color.Gray;
            Value = c;
        }

        public Color Value
        {
            get { return c; }
            set
            {
                c = value;
                BackColor = value;
                FlatAppearance.MouseOverBackColor = value;
                FlatAppearance.MouseDownBackColor = value;
                ForeColor = (value.R * 0.3 + value.G * 0.59 + value.B * 0.11) > 140 ? Color.Black : Color.White;
                Text = ZoneStyle.Hex(value);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            using (var d = new ColorDialog { FullOpen = true, AnyColor = true, Color = c })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                Value = d.Color;
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
    }

    class FontButton : Button
    {
        string spec = "Segoe UI|9|0";
        public event EventHandler ValueChanged;

        public FontButton()
        {
            Size = new Size(ZoneApp.I.S(260), ZoneApp.I.S(28));
            TextAlign = ContentAlignment.MiddleLeft;
            AutoEllipsis = true;
        }

        public string Value
        {
            get { return spec; }
            set { spec = value; Text = ZoneStyle.Describe(value); }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            using (var f = ZoneStyle.MakeFont(spec))
            using (var d = new FontDialog { Font = f, ShowEffects = false, FontMustExist = true, AllowVerticalFonts = false, MinSize = 6, MaxSize = 48 })
            {
                try
                {
                    if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                    Value = ZoneStyle.FontSpec(d.Font);
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
                catch (ArgumentException)
                {
                    MessageBox.Show(FindForm(), "Cette police n'est pas prise en charge (polices TrueType/OpenType uniquement).", "Logicapp");
                }
            }
        }
    }

    // Petite zone de saisie posée sur le nom d'une icône (F2).
    class RenameBox : Form
    {
        public event Action<string> Committed;
        readonly TextBox tb;
        bool done;

        public RenameBox(string text, Font font, Rectangle screenRect)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Bounds = screenRect;
            tb = new TextBox
            {
                Text = text, Font = font, Multiline = true, WordWrap = true, Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center
            };
            Controls.Add(tb);
            tb.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Finish(true); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false); }
            };
            Deactivate += delegate { Finish(true); };
            Shown += delegate { Activate(); tb.Focus(); tb.SelectAll(); };
        }

        void Finish(bool ok)
        {
            if (done) return;
            done = true;
            string t = tb.Text.Replace("\r", "").Replace("\n", "");
            BeginInvoke(new Action(Close));
            if (ok && Committed != null) Committed(t);
        }
    }

    static class Dialogs
    {
        // Les zones sont collées au bureau : les boîtes de dialogue ont besoin d'un propriétaire au premier plan.
        static Form TopOwner()
        {
            var f = new Form
            {
                TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000),
                Size = new Size(1, 1), Opacity = 0
            };
            f.Show();
            f.Activate();
            return f;
        }

        public static bool Confirm(string text)
        {
            using (var o = TopOwner())
                return MessageBox.Show(o, text, "Logicapp", MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) == DialogResult.OK;
        }

        public static void Error(string text)
        {
            using (var o = TopOwner())
                MessageBox.Show(o, text, "Logicapp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static string Input(string caption, string prompt, string initial)
        {
            var app = ZoneApp.I;
            using (var f = new Form
            {
                Text = caption, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
                StartPosition = FormStartPosition.CenterScreen, TopMost = true, ShowInTaskbar = false,
                ClientSize = new Size(app.S(340), app.S(112)), Font = new Font("Segoe UI", 9f)
            })
            {
                var lbl = new Label { Text = prompt, AutoSize = true, Location = new Point(app.S(12), app.S(12)) };
                var tb = new TextBox { Text = initial, Location = new Point(app.S(12), app.S(36)), Width = app.S(316) };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK,
                    Location = new Point(app.S(170), app.S(74)), Size = new Size(app.S(76), app.S(28)) };
                var cancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel,
                    Location = new Point(app.S(252), app.S(74)), Size = new Size(app.S(76), app.S(28)) };
                f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Shown += delegate { f.Activate(); tb.Focus(); tb.SelectAll(); };
                return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
            }
        }
    }
}
