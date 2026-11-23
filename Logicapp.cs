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
using System.Reflection;

// Informations affichées dans les propriétés du fichier et la colonne « Éditeur » du Gestionnaire des tâches.
[assembly: AssemblyTitle("Logicapp")]
[assembly: AssemblyDescription("Logicapp — zones repliables pour le bureau")]
[assembly: AssemblyCompany("OnTrack Software")]
[assembly: AssemblyProduct("Logicapp")]
[assembly: AssemblyCopyright("© 2026 OnTrack Software")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

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

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }
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
                0x4000 /*SYSICONINDEX*/ | 0x100 /*ICON*/ | 0x800 /*ATTRIBUTES*/ | 0x200 /*DISPLAYNAME*/);
            displayName = sfi.szDisplayName;
            if (sfi.hIcon != IntPtr.Zero) Native.DestroyIcon(sfi.hIcon);
            int idx = sfi.iIcon & 0xFFFFFF;
            bool isLink = (sfi.dwAttributes & 0x10000 /*SFGAO_LINK*/) != 0;
            Bitmap r = Load(idx, size);
            if (r == null) return null;
            // Flèche des raccourcis : dessinée à la même taille que sur le bureau Windows
            // (la flèche intégrée aux grandes icônes est beaucoup trop grosse).
            if (isLink)
            {
                Bitmap o = LinkOverlay(OverlaySize(size));
                if (o != null)
                    using (var g = Graphics.FromImage(r))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(o, new Rectangle(0, size - o.Height, o.Width, o.Height));
                    }
            }
            return r;
        }

        // Icône (sans superposition) d'index idx dans la liste système adaptée, mise à la taille voulue.
        static Bitmap Load(int idx, int size)
        {
            int shil = size <= 16 ? 1 : size <= 32 ? 0 : size <= 48 ? 2 : 4; // SMALL, LARGE, EXTRALARGE, JUMBO
            Bitmap b = FromList(shil, idx, 0);
            // Pas de vraie version 256 px : Windows renvoie une petite icône dans un coin -> on prend la 48 px.
            if (b != null && shil == 4 && Extent(b) <= 48) { b.Dispose(); b = FromList(2, idx, 0); }
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

        // Comme l'Explorateur : flèche 16 px pour les petites icônes, 32 px jusqu'à 48 px, puis 2/3 de l'icône.
        static int OverlaySize(int size) { return size <= 32 ? size : Math.Max(32, size * 2 / 3); }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHSTOCKICONINFO
        {
            public uint cbSize; public IntPtr hIcon; public int iSysImageIndex, iIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
        }
        [DllImport("shell32.dll")]
        static extern int SHGetStockIconInfo(uint siid, uint flags, ref SHSTOCKICONINFO info);

        static int linkIndex = -2;
        static readonly Dictionary<int, Bitmap> overlays = new Dictionary<int, Bitmap>();

        static Bitmap LinkOverlay(int size)
        {
            Bitmap b;
            if (overlays.TryGetValue(size, out b)) return b;
            if (linkIndex == -2)
            {
                var info = new SHSTOCKICONINFO();
                info.cbSize = (uint)Marshal.SizeOf(typeof(SHSTOCKICONINFO));
                linkIndex = SHGetStockIconInfo(29 /*SIID_LINK*/, 0x4000 /*SHGSI_SYSICONINDEX*/, ref info) == 0 ? info.iSysImageIndex : -1;
            }
            b = linkIndex >= 0 ? Load(linkIndex, size) : null;
            overlays[size] = b;
            return b;
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
        // Valeurs par défaut = preset « Néon RGB »
        public int Theme = 1;         // 0 Classique, 1 Néon RGB, 2 Night City, 3 Studio, 4+ presets illustrés (voir PresetNames)
        public int RgbAnim = 1;       // 0 Désactivée, 1 Au survol, 2 Toujours (lente)
        public int IconSize = 48, Spacing = 0, CornerRadius = 14, BorderWidth = 2;
        public int BgAlpha = 215, BgImageAlpha = 255, TitleAlpha = 0, BorderAlpha = 255;
        public int BgImageMode = 0;   // 0 Remplir, 1 Ajuster, 2 Étirer, 3 Mosaïque, 4 Centrer
        public int TitleAlign = 0;    // 0 Gauche, 1 Centre, 2 Droite
        public bool LabelShadow = true, ShowCount = true;
        public int IdleOpacity = 40;  // opacité de toute la zone (%) quand la souris n'est pas dessus
        public int SortMode = 0;      // 0 Nom, 1 Date de création, 2 Libre (rangement à la souris)
        public bool SortDesc = false; // ordre inverse (Nom / Date)
        public string BgImage = "";
        public string LabelFont = "Segoe UI|9|0", TitleFont = "Bahnschrift SemiBold|10.5|0";
        public Color LabelColor = Color.White;
        public Color BgColor = Color.FromArgb(10, 9, 20);
        public Color TitleColor = Color.FromArgb(20, 18, 40);
        public Color TitleTextColor = Color.FromArgb(240, 240, 255);
        public Color BorderColor = Color.White;

        public static readonly string[] PresetNames = { "Classique", "Néon RGB", "Night City", "Studio", "Rallye Stellaire", "Friche", "Sanguine", "Veillée", "Circuit", "Exil" };
        public static int MaxTheme { get { return PresetNames.Length - 1; } }

        public ZoneStyle Clone() { return (ZoneStyle)MemberwiseClone(); }

        // Preset : couleurs, polices et forme ; garde le comportement (taille d'icônes, tri, opacité au repos, image…).
        public static ZoneStyle Preset(int theme, ZoneStyle keep)
        {
            var s = new ZoneStyle();
            if (keep != null)
            {
                s.IconSize = keep.IconSize; s.Spacing = keep.Spacing; s.IdleOpacity = keep.IdleOpacity;
                s.SortMode = keep.SortMode; s.SortDesc = keep.SortDesc; s.ShowCount = keep.ShowCount;
                s.LabelShadow = keep.LabelShadow; s.RgbAnim = keep.RgbAnim; s.TitleAlign = keep.TitleAlign;
                s.BgImage = keep.BgImage; s.BgImageMode = keep.BgImageMode; s.BgImageAlpha = keep.BgImageAlpha;
            }
            s.Theme = Math.Max(0, Math.Min(MaxTheme, theme));
            switch (s.Theme)
            {
                case 0: // Classique (style d'origine)
                    s.BgColor = Color.FromArgb(30, 32, 38); s.BgAlpha = 200;
                    s.TitleColor = Color.FromArgb(50, 54, 66); s.TitleAlpha = 230;
                    s.TitleTextColor = Color.FromArgb(236, 238, 244); s.TitleFont = "Segoe UI Semibold|9.5|0";
                    s.CornerRadius = 8; s.BorderWidth = 0; s.BorderColor = Color.White; s.BorderAlpha = 60;
                    s.LabelFont = "Segoe UI|9|0";
                    break;
                case 2: // Night City : onglet cyan, accents magenta
                    s.BgColor = Color.FromArgb(6, 14, 22); s.BgAlpha = 205;
                    s.TitleColor = Color.FromArgb(0, 240, 255); s.TitleAlpha = 255;
                    s.TitleTextColor = Color.FromArgb(4, 16, 20); s.TitleFont = "Bahnschrift SemiBold|10|0";
                    s.CornerRadius = 18; s.BorderWidth = 1; s.BorderColor = Color.FromArgb(255, 43, 214); s.BorderAlpha = 255;
                    s.LabelFont = "Bahnschrift|9.5|0";
                    break;
                case 3: // Studio : sobre, liseré qui change lentement de couleur
                    s.BgColor = Color.FromArgb(22, 22, 28); s.BgAlpha = 228;
                    s.TitleColor = Color.FromArgb(0, 229, 255); s.TitleAlpha = 255;
                    s.TitleTextColor = Color.FromArgb(217, 217, 227); s.TitleFont = "Bahnschrift SemiBold|10|0";
                    s.CornerRadius = 12; s.BorderWidth = 1; s.BorderColor = Color.White; s.BorderAlpha = 22;
                    s.LabelFont = "Segoe UI|9|0";
                    break;
                case 4: // Rallye Stellaire : métal brossé, chevrons de course orange, lame lumineuse cyan
                    s.BgColor = Color.FromArgb(27, 33, 38); s.BgAlpha = 236;
                    s.TitleColor = Color.FromArgb(217, 130, 43); s.TitleAlpha = 255;
                    s.TitleTextColor = Color.FromArgb(239, 230, 214); s.TitleFont = "Bahnschrift SemiBold|10.5|0";
                    s.CornerRadius = 14; s.BorderWidth = 1; s.BorderColor = Color.FromArgb(230, 197, 138); s.BorderAlpha = 150;
                    s.LabelFont = "Bahnschrift|9.5|0"; s.LabelColor = Color.FromArgb(239, 230, 214);
                    break;
                case 5: // Friche : béton taché, mousse, ruban adhésif écrit à la main
                    s.BgColor = Color.FromArgb(36, 39, 31); s.BgAlpha = 235;
                    s.TitleColor = Color.FromArgb(214, 201, 164); s.TitleAlpha = 245;
                    s.TitleTextColor = Color.FromArgb(42, 38, 32); s.TitleFont = "Ink Free|13|1";
                    s.CornerRadius = 3; s.BorderWidth = 1; s.BorderColor = Color.FromArgb(214, 201, 164); s.BorderAlpha = 50;
                    s.LabelFont = "Segoe UI|9|0"; s.LabelColor = Color.FromArgb(221, 216, 200);
                    break;
                case 6: // Sanguine : nuit gothique, cadre bronze, rouge sang
                    s.BgColor = Color.FromArgb(19, 12, 14); s.BgAlpha = 240;
                    s.TitleColor = Color.FromArgb(155, 28, 36); s.TitleAlpha = 255;
                    s.TitleTextColor = Color.FromArgb(217, 207, 191); s.TitleFont = "Palatino Linotype|10.5|1";
                    s.CornerRadius = 0; s.BorderWidth = 1; s.BorderColor = Color.FromArgb(140, 115, 80); s.BorderAlpha = 210;
                    s.LabelFont = "Palatino Linotype|9.5|0"; s.LabelColor = Color.FromArgb(217, 207, 191);
                    break;
                case 7: // Veillée : tome de pierre, onglet doré, cases biseautées
                    s.BgColor = Color.FromArgb(28, 27, 26); s.BgAlpha = 242;
                    s.TitleColor = Color.FromArgb(200, 179, 138); s.TitleAlpha = 255;
                    s.TitleTextColor = Color.FromArgb(200, 179, 138); s.TitleFont = "Palatino Linotype|14|0";
                    s.CornerRadius = 0; s.BorderWidth = 2; s.BorderColor = Color.FromArgb(94, 91, 85); s.BorderAlpha = 255;
                    s.LabelFont = "Palatino Linotype|9.5|0"; s.LabelColor = Color.FromArgb(217, 212, 201);
                    break;
                case 8: // Circuit : papier crème, bandes de course rétro, cartes bleu canard
                    s.BgColor = Color.FromArgb(235, 231, 220); s.BgAlpha = 245;
                    s.TitleColor = Color.FromArgb(212, 87, 59); s.TitleAlpha = 255;
                    s.TitleTextColor = Color.FromArgb(30, 42, 48); s.TitleFont = "Bahnschrift|12|1";
                    s.CornerRadius = 12; s.BorderWidth = 0; s.BorderColor = Color.White; s.BorderAlpha = 60;
                    s.LabelFont = "Bahnschrift SemiBold|8.5|0"; s.LabelColor = Color.FromArgb(243, 241, 234);
                    break;
                case 9: // Exil : anthracite, orbites cuivrées, pastilles rondes
                    s.BgColor = Color.FromArgb(21, 21, 23); s.BgAlpha = 230;
                    s.TitleColor = Color.FromArgb(200, 116, 58); s.TitleAlpha = 255;
                    s.TitleTextColor = Color.FromArgb(244, 242, 238); s.TitleFont = "Segoe UI Semibold|9.5|0";
                    s.CornerRadius = 4; s.BorderWidth = 1; s.BorderColor = Color.White; s.BorderAlpha = 14;
                    s.LabelFont = "Segoe UI|8.5|0"; s.LabelColor = Color.FromArgb(163, 163, 166);
                    break;
                // [presets]
                // 1 = Néon RGB : valeurs par défaut de la classe
            }
            return s;
        }

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
            W(sb, "IdleOpacity", IdleOpacity); W(sb, "SortMode", SortMode); W(sb, "SortDesc", SortDesc ? 1 : 0);
            W(sb, "Theme", Theme); W(sb, "RgbAnim", RgbAnim);
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
                case "IdleOpacity": IdleOpacity = I(v, IdleOpacity, 10, 100); return true;
                case "Theme": Theme = I(v, Theme, 0, MaxTheme); return true;
                case "RgbAnim": RgbAnim = I(v, RgbAnim, 0, 2); return true;
                case "SortMode": SortMode = I(v, SortMode, 0, 2); return true;
                case "SortDesc": SortDesc = v == "1"; return true;
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

        class ZoneRec { public string Name, Order = ""; public int X, Y, W = 320, H = 220; public bool Collapsed, HasTheme; public ZoneStyle Style; }

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
            bool defaultHasTheme = false;
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
                    else if (section == "Default") { DefaultStyle.Read(k, v); if (k == "Theme") defaultHasTheme = true; }
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
                            case "Order": cur.Order = v; break;
                            default: cur.Style.Read(k, v); if (k == "Theme") cur.HasTheme = true; break;
                        }
                    }
                }
            }
            // Configuration d'avant les thèmes : on passe au nouveau design par défaut (Néon RGB),
            // en gardant le comportement de chaque zone (taille d'icônes, tri, opacité au repos, image…).
            if (!defaultHasTheme) DefaultStyle = ZoneStyle.Preset(1, DefaultStyle);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in recs)
            {
                if (!r.HasTheme) r.Style = ZoneStyle.Preset(1, r.Style);
                string name = Sanitize(r.Name ?? "");
                if (name.Length == 0 || !seen.Add(name)) continue;
                AddZone(name, new Rectangle(r.X, r.Y, r.W, r.H), r.Collapsed, r.Style, r.Order);
            }
            // Dossiers ajoutés à la main dans « Zones du bureau » : on en fait des zones aussi.
            foreach (string dir in Directory.GetDirectories(Root))
            {
                string n = Path.GetFileName(dir);
                if (seen.Contains(n) || n.StartsWith(".")) continue;
                if ((File.GetAttributes(dir) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                seen.Add(n);
                AddZone(n, DefaultBounds(Zones.Count), false, DefaultStyle.Clone(), "");
            }
            if (Zones.Count == 0 && createDefault) NewZone(false);
            miLock.Checked = Locked;
            miPeek.Checked = PeekOnHover;
            Save();
        }

        void AddZone(string name, Rectangle r, bool collapsed, ZoneStyle style, string order)
        {
            var z = new ZoneForm(name, EnsureVisible(r), collapsed, style, order);
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
            AddZone(n, Clamp(r), false, DefaultStyle.Clone(), "");
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
                sb.AppendLine("Order=" + z.OrderString); // ordre libre (noms de fichiers séparés par |)
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
                }
                using (var a = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                    if (a != null) a.DeleteValue(OldAppName, false);
                return true;
            }
            catch { return false; }
        }

        // Windows ne lance une entrée « Run » que si elle est approuvée dans la liste des applis de démarrage
        // (StartupApproved\Run : 02 = activée, 03 = désactivée par l'utilisateur dans le Gestionnaire des tâches).
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        static bool IsDisabledInTaskManager()
        {
            using (var a = Registry.CurrentUser.OpenSubKey(ApprovedKey))
            {
                var b = a == null ? null : a.GetValue(AppName) as byte[];
                return b != null && b.Length > 0 && (b[0] & 1) != 0;
            }
        }

        static bool IsStartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                if (k == null || k.GetValue(AppName) == null) return false;
            return !IsDisabledInTaskManager();
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
                using (var a = Registry.CurrentUser.CreateSubKey(ApprovedKey))
                {
                    if (on) a.SetValue(AppName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                    else a.DeleteValue(AppName, false);
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
        // Renvoie les chemins de destination des éléments effectivement déplacés/copiés.
        public List<string> MoveInto(IEnumerable<string> items, string destDir, bool copy)
        {
            var errors = new List<string>();
            var done = new List<string>();
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
                    if (File.Exists(dest) || Directory.Exists(dest)) done.Add(dest);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { errors.Add(Path.GetFileName(src) + " : " + ex.Message); }
            }
            if (errors.Count > 0)
                Dialogs.Error("Certains éléments n'ont pas pu être déplacés :\n\n" + string.Join("\n", errors.ToArray()));
            return done;
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

        class Item { public string FullPath, Name; public bool IsDir; public DateTime Created; public Bitmap Icon; public Rectangle Cell; }
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
        Bitmap layer, bgCache;               // couche fixe (fond, titre, icônes)
        IntPtr dib, dibBits, dibDC, dibOld;  // image affichée par Windows (on dessine directement dedans)
        Bitmap dibBmp;
        int dibW, dibH;
        readonly Timer animTimer;            // animations RGB (uniquement quand elles sont visibles)
        float animPhase;
        // Glitch Night City : rafales courtes et aléatoires
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        readonly Random glitchRnd = new Random();
        long glitchUntil, nextGlitch, activeSince;
        bool animWasActive;
        static readonly ImageAttributes GhostMagenta = MakeGhost(true), GhostCyan = MakeGhost(false);

        static ImageAttributes MakeGhost(bool magenta)
        {
            var m = new ColorMatrix(new[]
            {
                new float[] { magenta ? 1 : 0, 0, 0, 0, 0 },
                new float[] { 0, magenta ? 0 : 1, 0, 0, 0 },
                new float[] { 0, 0, 1, 0, 0 },
                new float[] { 0, 0, 0, 0.5f, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            });
            var ia = new ImageAttributes();
            ia.SetColorMatrix(m);
            return ia;
        }
        string tipText = "";
        SettingsForm settingsForm;

        // Opacité globale (fondu rapide au survol)
        readonly Timer fadeTimer;
        int curAlpha = 255;
        bool renaming, selfDragging;

        // Ordre libre : noms de fichiers dans l'ordre choisi à la souris
        List<string> order = new List<string>();
        int lastCols = 1, lastCw = 1, lastCellH = 1, dropIndex = -1;
        public string OrderString { get { return string.Join("|", order.ToArray()); } }

        public ZoneForm(string name, Rectangle bounds, bool collapsed, ZoneStyle style, string orderStr)
        {
            foreach (string n in (orderStr ?? "").Split('|')) if (n.Length > 0) order.Add(n);
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
            fadeTimer = new Timer { Interval = 15 };
            fadeTimer.Tick += delegate { FadeStep(); };
            animTimer = new Timer { Interval = 33 };
            animTimer.Tick += delegate { AnimStep(); };
            menu.Closed += delegate { RefreshMouseInside(); };
            curAlpha = IdleAlpha;

            Reload();
            BuildBgCache();
        }

        // ---------- Opacité au survol ----------

        int IdleAlpha { get { return (int)Math.Round(Style.IdleOpacity * 2.55); } }

        // La zone est « active » (opacité normale) quand on interagit avec elle.
        bool IsActive
        {
            get
            {
                return mouseInside || drag != DragMode.None || dropHover || selfDragging || renaming || menu.Visible ||
                       (settingsForm != null && !settingsForm.IsDisposed);
            }
        }

        public void UpdateFade()
        {
            int target = IsActive ? 255 : IdleAlpha;
            if (target != curAlpha) fadeTimer.Start();
            UpdateAnim();
        }

        void FadeStep()
        {
            int target = IsActive ? 255 : IdleAlpha;
            int step = 36; // ~100-120 ms pour un fondu complet
            if (Math.Abs(target - curAlpha) <= step) { curAlpha = target; fadeTimer.Stop(); }
            else curAlpha += target > curAlpha ? step : -step;
            if (IsHandleCreated) PushLayered();
        }

        // ---------- Animations RGB ----------
        // Fluides (30 i/s) quand la souris est sur la zone, très lentes (10 i/s) en mode « Toujours »,
        // arrêtées sinon : au repos l'app reste à ~0 % de processeur.

        void UpdateAnim()
        {
            int mode = Style.Theme == 0 ? 0 : Style.RgbAnim;
            bool run = IsHandleCreated && (mode == 2 || (mode == 1 && IsActive));
            bool active = IsActive;
            if (active && !animWasActive)
            {
                activeSince = Clock.ElapsedMilliseconds;
                if (Style.Theme == 2) nextGlitch = activeSince; // petit glitch à l'arrivée de la souris
            }
            animWasActive = active;
            if (!run)
            {
                if (animTimer.Enabled) { animTimer.Stop(); if (Style.Theme >= 2) Compose(); } // retire le glitch / les effets en cours
                return;
            }
            int interval = IsActive ? 33 : 100;
            if (animTimer.Interval != interval) animTimer.Interval = interval;
            if (!animTimer.Enabled) animTimer.Start();
        }

        void AnimStep()
        {
            float period = Style.Theme == 1 ? 4f : Style.Theme == 2 ? 2.6f : 8f; // secondes par cycle
            if (!IsActive) period *= 4f;
            animPhase += animTimer.Interval / 1000f / period;
            if (animPhase >= 1f) animPhase -= (float)Math.Floor(animPhase);
            if (Style.Theme == 2)
            {
                long now = Clock.ElapsedMilliseconds;
                if (now >= nextGlitch)
                {
                    glitchUntil = now + 160 + glitchRnd.Next(180);          // rafale de 0,16 à 0,34 s
                    nextGlitch = now + 2800 + glitchRnd.Next(3500);         // la suivante dans 3 à 6 s
                }
            }
            Compose();
        }

        void RefreshMouseInside()
        {
            if (IsDisposed) return;
            bool inside = Bounds.Contains(Cursor.Position);
            if (inside != mouseInside)
            {
                mouseInside = inside;
                if (!inside) { hover = -1; hoverGear = false; SetTip(""); }
                Render();
            }
            UpdateFade();
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
            titleH = (int)Math.Ceiling(titleFont.GetHeight()) + app.S(12) + TitleExtra;
            lineH = (int)Math.Ceiling(labelFont.GetHeight());
        }

        // Applique un style (aperçu en direct depuis la fenêtre de réglages).
        public void ApplyStyle(ZoneStyle s)
        {
            bool icons = Style.IconSize != s.IconSize;
            bool bg = Style.BgImage != s.BgImage || Style.BgImageMode != s.BgImageMode || Style.BgImageAlpha != s.BgImageAlpha;
            bool sort = Style.SortMode != s.SortMode || Style.SortDesc != s.SortDesc;
            if (s.SortMode == 2 && Style.SortMode != 2)
            {
                order.Clear(); // passage en libre : on part de l'ordre affiché
                foreach (var it in items) order.Add(Path.GetFileName(it.FullPath));
            }
            Style = s;
            CreateFonts();
            MinimumSize = new Size(ZoneApp.I.S(140), titleH);
            if (ExpandedHeight < titleH + ZoneApp.I.S(60)) ExpandedHeight = titleH + ZoneApp.I.S(60);
            if (!IsOpen) Height = titleH;
            if (icons) Reload();
            else if (sort) SortItems();
            if (bg) BuildBgCache();
            LayoutItems();
            Render();
            UpdateFade();
        }

        // ---------- Tri ----------

        void SortItems()
        {
            var cmpName = StringComparer.CurrentCultureIgnoreCase;
            if (Style.SortMode == 2)
            {
                // Libre : ordre enregistré ; les nouveaux éléments vont à la fin (par nom).
                var pos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < order.Count; i++) if (!pos.ContainsKey(order[i])) pos[order[i]] = i;
                items.Sort((x, y) =>
                {
                    int px, py;
                    bool hx = pos.TryGetValue(Path.GetFileName(x.FullPath), out px), hy = pos.TryGetValue(Path.GetFileName(y.FullPath), out py);
                    if (hx && hy) return px.CompareTo(py);
                    if (hx != hy) return hx ? -1 : 1;
                    return cmpName.Compare(x.Name, y.Name);
                });
                var newOrder = new List<string>();
                foreach (var it in items) newOrder.Add(Path.GetFileName(it.FullPath));
                bool changed = string.Join("|", newOrder.ToArray()) != OrderString;
                order = newOrder;
                if (changed && ZoneApp.I.Zones.Contains(this)) ZoneApp.I.Save();
                return;
            }
            if (Style.SortMode == 1)
                items.Sort((x, y) => x.Created != y.Created ? y.Created.CompareTo(x.Created) : cmpName.Compare(x.Name, y.Name)); // récents d'abord
            else
                items.Sort((x, y) => x.IsDir != y.IsDir ? (x.IsDir ? -1 : 1) : cmpName.Compare(x.Name, y.Name));
            if (Style.SortDesc) items.Reverse();
        }

        public void SetSort(int mode, bool desc)
        {
            if (mode == 2 && Style.SortMode != 2)
            {
                // Passage en libre : on part de l'ordre affiché actuellement.
                order.Clear();
                foreach (var it in items) order.Add(Path.GetFileName(it.FullPath));
            }
            Style.SortMode = mode;
            Style.SortDesc = desc;
            SortItems();
            LayoutItems();
            Render();
            ZoneApp.I.Save();
        }

        // Rang d'insertion (dans la grille) correspondant à un point de la zone.
        int DropIndexAt(Point p)
        {
            if (items.Count == 0) return 0;
            Rectangle cr = ContentRect;
            int x = Math.Max(0, p.X - cr.X), y = Math.Max(0, p.Y - cr.Y + scrollY);
            int row = y / Math.Max(1, lastCellH);
            int col = Math.Min(lastCols - 1, x / Math.Max(1, lastCw));
            bool after = (x - col * lastCw) > lastCw / 2;
            return Math.Max(0, Math.Min(items.Count, row * lastCols + col + (after ? 1 : 0)));
        }

        // Range les fichiers déplacés (déjà dans la zone) à la position voulue, en mode libre.
        void ReorderTo(IList<string> paths, int index)
        {
            if (Style.SortMode != 2) SetSort(2, false);
            var moving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in paths) moving.Add(Path.GetFileName(p.TrimEnd('\\')));
            var movingOrdered = new List<string>();
            var rest = new List<string>();
            int insertAt = 0;
            for (int k = 0; k < items.Count; k++)
            {
                string n = Path.GetFileName(items[k].FullPath);
                if (moving.Contains(n)) movingOrdered.Add(n);
                else { rest.Add(n); if (k < index) insertAt++; }
            }
            foreach (string n in moving) if (!movingOrdered.Contains(n)) movingOrdered.Add(n); // éléments tout juste arrivés
            rest.InsertRange(Math.Min(insertAt, rest.Count), movingOrdered);
            order = rest;
            SortItems();
            LayoutItems();
            Render();
            ZoneApp.I.Save();
        }

        // ---------- Fenêtre ----------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero) Native.SetOwner(Handle, progman);
            Render();
            UpdateAnim();
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
            animTimer.Stop();
            fadeTimer.Stop();
            if (watcher != null) { watcher.Dispose(); watcher = null; }
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
            try { Close(); Dispose(); } catch { }
            foreach (var it in items) if (it.Icon != null) it.Icon.Dispose();
            if (layer != null) layer.Dispose();
            FreeDib();
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
                int top = titleH + p / 2 + ContentTopExtra;
                return new Rectangle(p, top, Math.Max(0, Width - 2 * p), Math.Max(0, Height - top - p));
            }
        }

        Size CellSize()
        {
            var app = ZoneApp.I;
            int isz = app.S(Style.IconSize), sp = app.S(Style.Spacing);
            return new Size(Math.Max(isz + app.S(22), app.S(76)) + sp, app.S(6) + isz + app.S(4) + 2 * lineH + app.S(8) + sp + 2 * IconInset);
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
            lastCols = cols; lastCw = cw; lastCellH = cs.Height;
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

        // ---------- Formes et styles ----------

        // Polygone à coins coupés (style cyberpunk) : tl/tr/br/bl = coins à couper.
        static GraphicsPath Chamfer(RectangleF r, float c, bool tl, bool tr, bool br, bool bl)
        {
            c = Math.Max(0, Math.Min(c, Math.Min(r.Width, r.Height) / 2f));
            var pts = new List<PointF>();
            if (tl) { pts.Add(new PointF(r.X, r.Y + c)); pts.Add(new PointF(r.X + c, r.Y)); } else pts.Add(new PointF(r.X, r.Y));
            if (tr) { pts.Add(new PointF(r.Right - c, r.Y)); pts.Add(new PointF(r.Right, r.Y + c)); } else pts.Add(new PointF(r.Right, r.Y));
            if (br) { pts.Add(new PointF(r.Right, r.Bottom - c)); pts.Add(new PointF(r.Right - c, r.Bottom)); } else pts.Add(new PointF(r.Right, r.Bottom));
            if (bl) { pts.Add(new PointF(r.X + c, r.Bottom)); pts.Add(new PointF(r.X, r.Bottom - c)); } else pts.Add(new PointF(r.X, r.Bottom));
            var p = new GraphicsPath();
            p.AddPolygon(pts.ToArray());
            return p;
        }

        // Forme de la zone selon le thème. « full » = avec le bas (faux pour la seule barre de titre d'une zone ouverte).
        GraphicsPath ShapePath(RectangleF r, float cut, bool full)
        {
            switch (Style.Theme)
            {
                case 1: return Chamfer(r, cut, true, false, full, false);  // Néon : haut-gauche + bas-droite
                case 2: return Chamfer(r, cut, false, false, full, false); // Night City : bas-droite
                case 3: return Chamfer(r, cut, false, true, false, false); // Studio : haut-droite
                case 4: return Chamfer(r, cut, false, true, false, full);  // Rallye Stellaire : haut-droite + bas-gauche
                case 5: return RoundRect(r, cut, full);                // Friche : coins à peine arrondis
                case 6: return RoundRect(r, cut, full);                // Sanguine : rectangle (coins réglables)
                case 7: return RoundRect(r, cut, full);                // Veillée : rectangle (onglet de titre à part)
                case 8: return RoundRect(r, cut, full);                // Circuit : carte arrondie
                case 9: return RoundRect(r, cut, full);                // Exil : coins à peine arrondis
                // [shape]
                default: return RoundRect(r, cut, full);
            }
        }

        static readonly Color[] Rainbow =
        {
            Color.FromArgb(255, 0, 76), Color.FromArgb(255, 184, 0), Color.FromArgb(61, 255, 110),
            Color.FromArgb(0, 229, 255), Color.FromArgb(122, 92, 255), Color.FromArgb(255, 43, 214), Color.FromArgb(255, 0, 76)
        };

        static ColorBlend RainbowBlend(int alpha)
        {
            var cb = new ColorBlend(Rainbow.Length);
            for (int i = 0; i < Rainbow.Length; i++)
            {
                cb.Colors[i] = Color.FromArgb(alpha, Rainbow[i]);
                cb.Positions[i] = i / (float)(Rainbow.Length - 1);
            }
            return cb;
        }

        static Color HueShift(Color c, float degrees)
        {
            float h = (c.GetHue() + degrees) % 360f, s = c.GetSaturation(), l = c.GetBrightness();
            float q = l < 0.5f ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            Func<float, float> f = t =>
            {
                if (t < 0) t += 1; if (t > 1) t -= 1;
                if (t < 1f / 6) return p + (q - p) * 6 * t;
                if (t < 0.5f) return q;
                if (t < 2f / 3) return p + (q - p) * (2f / 3 - t) * 6;
                return p;
            };
            float hk = h / 360f;
            return Color.FromArgb(c.A, (int)(f(hk + 1f / 3) * 255), (int)(f(hk) * 255), (int)(f(hk - 1f / 3) * 255));
        }

        Color AccentColor { get { return Style.Theme == 1 ? Color.FromArgb(0, 229, 255) : Style.TitleColor; } }

        // Texte espacé (lettres écartées), avec « … » s'il ne tient pas. Renvoie la largeur (mesure seule si brush == null).
        static float DrawTracked(Graphics g, string s, Font f, Brush b, float x, float yMid, float track, float maxRight)
        {
            using (var fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                float hgt = f.GetHeight(g), cx = x;
                float ell = g.MeasureString("…", f, PointF.Empty, fmt).Width;
                // Largeur totale : si tout tient, on dessine sans jamais couper.
                float total = 0;
                for (int i = 0; i < s.Length; i++)
                    total += g.MeasureString(s[i].ToString(), f, PointF.Empty, fmt).Width + (i == s.Length - 1 ? 0 : track);
                if (x + total <= maxRight + 0.5f)
                {
                    if (b != null)
                        for (int i = 0; i < s.Length; i++)
                        {
                            string ch = s[i].ToString();
                            g.DrawString(ch, f, b, cx, yMid - hgt / 2, fmt);
                            cx += g.MeasureString(ch, f, PointF.Empty, fmt).Width + track;
                        }
                    return total;
                }
                for (int i = 0; i < s.Length; i++)
                {
                    string ch = s[i].ToString();
                    float cw = g.MeasureString(ch, f, PointF.Empty, fmt).Width;
                    bool last = i == s.Length - 1;
                    if (cx + cw + (last ? 0 : ell) > maxRight && !(last && cx + cw <= maxRight))
                    {
                        if (b != null) g.DrawString("…", f, b, cx, yMid - hgt / 2, fmt);
                        cx += ell;
                        break;
                    }
                    if (b != null) g.DrawString(ch, f, b, cx, yMid - hgt / 2, fmt);
                    cx += cw + (last ? 0 : track);
                }
                return cx - x;
            }
        }

        void DrawChevron(Graphics g, float x, float yMid, bool open, Color c)
        {
            float s = ZoneApp.I.S(5);
            PointF[] pts = open
                ? new[] { new PointF(x, yMid - s * 0.5f), new PointF(x + s * 1.6f, yMid - s * 0.5f), new PointF(x + s * 0.8f, yMid + s * 0.6f) }
                : new[] { new PointF(x + s * 0.2f, yMid - s * 0.8f), new PointF(x + s * 1.2f, yMid), new PointF(x + s * 0.2f, yMid + s * 0.8f) };
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }

        // ---------- Presets illustrés (thème 4 et suivants) ----------
        // Chaque preset dessine : un décor de fond (couche fixe, sous les icônes), son titre, ses icônes,
        // un décor de premier plan (cadres, ornements) et des effets animés qui ne tournent qu'au survol.

        // Marge autour de l'icône pour les presets qui la posent dans une case, une carte ou une pastille.
        int IconInset
        {
            get
            {
                switch (Style.Theme)
                {
                    case 7: return ZoneApp.I.S(8);
                    case 8: return ZoneApp.I.S(8);
                    case 9: return ZoneApp.I.S(8);
                    // [inset]
                    default: return 0;
                }
            }
        }

        // Hauteur ajoutée à la barre de titre (ruban, filet, soulignement…).
        int TitleExtra
        {
            get
            {
                switch (Style.Theme)
                {
                    case 5: return ZoneApp.I.S(8);
                    case 6: return ZoneApp.I.S(12);
                    case 7: return ZoneApp.I.S(6);
                    case 8: return ZoneApp.I.S(8);
                    case 9: return ZoneApp.I.S(8);
                    // [titleextra]
                    default: return 0;
                }
            }
        }

        // Espace ajouté au-dessus des icônes (filet décoratif en haut du panneau).
        int ContentTopExtra
        {
            get
            {
                switch (Style.Theme)
                {
                    case 7: return ZoneApp.I.S(10);
                    // [topextra]
                    default: return 0;
                }
            }
        }

        static Bitmap noiseTex;

        // Grain fin (points clairs et sombres) généré une seule fois, posé en mosaïque sur les fonds « matière ».
        static Bitmap NoiseTexture
        {
            get
            {
                if (noiseTex != null) return noiseTex;
                var rnd = new Random(7);
                var b = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        int v = rnd.Next(100);
                        Color c = v < 18 ? Color.FromArgb(rnd.Next(6, 16), 255, 255, 255)
                                : v < 36 ? Color.FromArgb(rnd.Next(8, 26), 0, 0, 0) : Color.Transparent;
                        b.SetPixel(x, y, c);
                    }
                noiseTex = b;
                return b;
            }
        }

        static void FillNoise(Graphics g, GraphicsPath shape)
        {
            if (shape == null) return;
            using (var tb = new TextureBrush(NoiseTexture, WrapMode.Tile)) g.FillPath(tb, shape);
        }

        // Halo elliptique : couleur au centre, transparent au bord.
        static void FillGlow(Graphics g, RectangleF r, Color c)
        {
            if (r.Width < 1 || r.Height < 1 || c.A == 0) return;
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (var b = new PathGradientBrush(p))
                {
                    b.CenterColor = c;
                    b.SurroundColors = new[] { Color.FromArgb(0, c) };
                    g.FillPath(b, p);
                }
            }
        }

        static float EaseOut(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        // Avancement (0 → 1) de l'animation d'arrivée de la souris ; 0 quand rien n'est animé.
        float HoverProgress(float ms)
        {
            if (!animTimer.Enabled || !IsActive) return 0f;
            return Math.Min(1f, (Clock.ElapsedMilliseconds - activeSince) / ms);
        }

        // Pseudo-aléatoire stable (particules) : même valeur à chaque image pour un même k.
        static float Hash(int k, int salt)
        {
            unchecked
            {
                int x = k * 374761393 + salt * 668265263;
                x = (x ^ (x >> 13)) * 1274126177;
                x ^= x >> 16;
                return (x & 0xFFFFFF) / (float)0x1000000;
            }
        }

        Rectangle IconRect(Rectangle cell, int isz)
        {
            return new Rectangle(cell.X + (cell.Width - isz) / 2, cell.Y + ZoneApp.I.S(6) + IconInset, isz, isz);
        }

        RectangleF LabelRect(Rectangle cell, Rectangle ir)
        {
            var app = ZoneApp.I;
            return new RectangleF(cell.X + app.S(4), ir.Bottom + app.S(4) + IconInset, cell.Width - app.S(8), 2 * lineH + 2);
        }

        void DrawItemLabel(Graphics g, string name, RectangleF lr, Brush lb, Brush sb, StringFormat sf)
        {
            if (Style.LabelShadow) { var sr = lr; sr.Offset(1, 1); g.DrawString(name, labelFont, sb, sr, sf); }
            g.DrawString(name, labelFont, lb, lr, sf);
        }

        // Icône simple avec surbrillance au survol (fond teinté + liseré).
        void DrawSimpleItem(Graphics g, int i, Rectangle cell, bool sel, int isz, Brush lb, Brush sb, StringFormat sf, Color fill, Color edge)
        {
            var app = ZoneApp.I;
            bool hov = i == hover;
            if (sel || hov)
                using (var hp = RoundRect(HitRect(i), app.S(3), true))
                {
                    using (var hb = new SolidBrush(Color.FromArgb(sel ? (hov ? 80 : 60) : 30, fill))) g.FillPath(hb, hp);
                    using (var pen = new Pen(Color.FromArgb(sel ? 230 : 150, edge))) g.DrawPath(pen, hp);
                }
            var ir = IconRect(cell, isz);
            if (items[i].Icon != null) g.DrawImage(items[i].Icon, ir);
            DrawItemLabel(g, items[i].Name, LabelRect(cell, ir), lb, sb, sf);
        }

        void DrawTitleThemed(Graphics g, int w, bool open, Rectangle gr)
        {
            float right = gr.X - ZoneApp.I.S(4);
            switch (Style.Theme)
            {
                case 4: TitleRallye(g, w, open, right); break;
                case 5: TitleFriche(g, w, open, right); break;
                case 6: TitleSanguine(g, w, open, right); break;
                case 7: TitleVeillee(g, w, open, right); break;
                case 8: TitleCircuit(g, w, open, right); break;
                case 9: TitleExil(g, w, open, right); break;
                // [title]
            }
        }

        void DrawDecorBack(Graphics g, int w, int h, GraphicsPath shape, bool open)
        {
            switch (Style.Theme)
            {
                case 4: BackRallye(g, w, h, shape, open); break;
                case 5: BackFriche(g, w, h, shape, open); break;
                case 6: BackSanguine(g, w, h, shape, open); break;
                case 7: BackVeillee(g, w, h, shape, open); break;
                case 8: BackCircuit(g, w, h, shape, open); break;
                case 9: BackExil(g, w, h, shape, open); break;
                // [back]
            }
        }

        void DrawItemThemed(Graphics g, int i, Rectangle cell, bool sel, int isz, Brush lb, Brush sb, StringFormat sf)
        {
            switch (Style.Theme)
            {
                case 4: DrawSimpleItem(g, i, cell, sel, isz, lb, sb, sf, Holo, Holo); break;
                case 5: DrawSimpleItem(g, i, cell, sel, isz, lb, sb, sf, Color.FromArgb(255, 238, 196), Style.TitleColor); break;
                case 6: DrawSimpleItem(g, i, cell, sel, isz, lb, sb, sf, Style.TitleColor, Style.BorderColor); break;
                case 7: ItemVeillee(g, i, cell, sel, isz, lb, sb, sf); break;
                case 8: ItemCircuit(g, i, cell, sel, isz, lb, sb, sf); break;
                case 9: ItemExil(g, i, cell, sel, isz, lb, sb, sf); break;
                // [item]
            }
        }

        void DrawDecorFront(Graphics g, int w, int h, bool open, float cut)
        {
            switch (Style.Theme)
            {
                case 4: FrontRallye(g, w, h, cut); break;
                case 5: FrontFriche(g, w, h, cut); break;
                case 6: FrontSanguine(g, w, h); break;
                case 7: FrontVeillee(g, w, h); break;
                case 8: FrontCircuit(g, w, h, cut); break;
                case 9: FrontExil(g, w, h, cut); break;
                // [front]
            }
        }

        void DrawAnimatedThemed(Graphics g, int w, int h, bool open, float cut)
        {
            if (!animTimer.Enabled) return; // au repos : seule la couche fixe est affichée
            switch (Style.Theme)
            {
                case 4: AnimRallye(g, w, h, open, cut); break;
                case 5: AnimFriche(g, w, h, open, cut); break;
                case 6: AnimSanguine(g, w, h, open, cut); break;
                case 7: AnimVeillee(g, w, h, open, cut); break;
                case 8: AnimCircuit(g, w, h, open, cut); break;
                case 9: AnimExil(g, w, h, open, cut); break;
                // [anim]
            }
        }

        // Traînées de vitesse horizontales qui filent de droite à gauche dans la barre de titre.
        void DrawSpeedStreaks(Graphics g, int w, Color c, int periodMs)
        {
            var app = ZoneApp.I;
            long now = Clock.ElapsedMilliseconds;
            float[] ys = { 0.28f, 0.55f, 0.8f };
            int[] lens = { 70, 110, 50 };
            int[] offs = { 0, periodMs * 2 / 5, periodMs * 7 / 10 };
            g.SetClip(new Rectangle(0, 0, w, titleH));
            for (int k = 0; k < 3; k++)
            {
                float len = app.S(lens[k]), span = w + len + app.S(20);
                float x = w - ((now + offs[k]) % periodMs) / (float)periodMs * span;
                float y = titleH * ys[k];
                using (var b = new LinearGradientBrush(new RectangleF(x - 1, y, len + 2, 2), Color.FromArgb(0, c), c, 0f))
                    g.FillRectangle(b, x, y, len, Math.Max(1, app.S(1)));
            }
            g.ResetClip();
        }

        // Bande lumineuse qui descend sur le panneau (balayage holographique).
        void DrawScanBand(Graphics g, int w, int h, float cut, Color c, int periodMs, int alpha)
        {
            var app = ZoneApp.I;
            if (h <= titleH + app.S(10)) return;
            using (var shape = ShapePath(new RectangleF(0, 0, w, h), cut, true))
            {
                g.SetClip(shape);
                g.IntersectClip(new Rectangle(0, titleH, w, h - titleH));
                float band = app.S(46), ph = (Clock.ElapsedMilliseconds % periodMs) / (float)periodMs;
                float by = titleH - band + ph * (h - titleH + band);
                using (var lb = new LinearGradientBrush(new RectangleF(0, by - 1, w, band + 2), Color.Transparent, Color.Transparent, 90f))
                {
                    var cb = new ColorBlend(4);
                    cb.Colors = new[] { Color.FromArgb(0, c), Color.FromArgb(alpha / 3, c), Color.FromArgb(alpha, Color.FromArgb(Math.Min(255, c.R + 60), Math.Min(255, c.G + 30), Math.Min(255, c.B + 25))), Color.FromArgb(0, c) };
                    cb.Positions = new[] { 0f, 0.7f, 0.96f, 1f };
                    lb.InterpolationColors = cb;
                    g.FillRectangle(lb, 0, by, w, band);
                }
                g.ResetClip();
            }
        }

        // ===== Rallye Stellaire : métal brossé, chevrons de course, numéro, lame lumineuse cyan =====

        static readonly Color Holo = Color.FromArgb(95, 212, 230);

        void TitleRallye(Graphics g, int w, bool open, float right)
        {
            var app = ZoneApp.I;
            float yMid = titleH / 2f - app.S(1);
            float track = Math.Max(1f, titleFont.Size * 0.18f) * app.Scale;
            // Chevrons de course à gauche, qui s'estompent vers la droite
            float cw = app.S(44), step = app.S(12), lean = titleH * 0.62f;
            g.SetClip(new RectangleF(0, 0, cw, titleH));
            for (float x = -lean; x < cw; x += step)
            {
                int a = (int)(Style.TitleAlpha * 0.9f * Math.Max(0f, 1f - Math.Max(0f, x) / cw));
                using (var b = new SolidBrush(Color.FromArgb(a, Style.TitleColor)))
                    g.FillPolygon(b, new[] { new PointF(x, titleH), new PointF(x + app.S(6), titleH), new PointF(x + app.S(6) + lean, 0), new PointF(x + lean, 0) });
            }
            g.ResetClip();
            // Numéro « Nº 08 »
            if (Style.ShowCount)
                using (var cf = new Font(titleFont.FontFamily, Math.Max(7f, titleFont.SizeInPoints * 0.8f), FontStyle.Regular, GraphicsUnit.Point))
                {
                    string num = "Nº " + items.Count.ToString("00");
                    float nw = DrawTracked(g, num, cf, null, 0, yMid, track, float.MaxValue);
                    using (var b = new SolidBrush(Color.FromArgb(Style.TitleAlpha, Style.TitleColor))) DrawTracked(g, num, cf, b, right - nw, yMid, track, right);
                    right -= nw + app.S(10);
                }
            using (var b = new SolidBrush(Style.TitleTextColor))
                DrawTracked(g, ZoneName.ToUpperInvariant(), titleFont, b, cw + app.S(6), yMid, track * 1.6f, right);
            // Lame éteinte sous le titre (elle s'allume au survol)
            if (open)
                using (var b = new SolidBrush(Color.FromArgb(45, Holo))) g.FillRectangle(b, app.S(10), titleH - app.S(3), w - app.S(20), app.S(2));
        }

        void BackRallye(Graphics g, int w, int h, GraphicsPath shape, bool open)
        {
            if (shape == null) return;
            var app = ZoneApp.I;
            g.SetClip(shape);
            using (var p = new Pen(Color.FromArgb(5, 255, 255, 255)))
                for (int y = 1; y < h; y += Math.Max(2, app.S(3))) g.DrawLine(p, 0, y, w, y);
            FillNoise(g, shape);
            using (var top = new LinearGradientBrush(new RectangleF(0, 0, w, titleH + 1), Color.FromArgb(14, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                g.FillRectangle(top, 0, 0, w, titleH);
            if (open && h > titleH + app.S(40))
            {
                // Règle de télémétrie en bas à gauche
                float y = h - app.S(9), x0 = app.S(Style.CornerRadius) + app.S(6), x1 = w * 0.55f;
                for (float x = x0; x < x1; x += app.S(8))
                    using (var p = new Pen(Color.FromArgb((int)(90 * (1 - (x - x0) / (x1 - x0))), Style.BorderColor)))
                        g.DrawLine(p, x, y, x, y + app.S(4));
            }
            g.ResetClip();
        }

        void FrontRallye(Graphics g, int w, int h, float cut)
        {
            if (Style.BorderWidth <= 0) return;
            float bw = ZoneApp.I.S(Style.BorderWidth);
            using (var bp = ShapePath(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), Math.Max(0, cut - bw / 2), true))
            using (var lb = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, w), Math.Max(1, h)), Color.Black, Color.Black, 35f))
            {
                var cb = new ColorBlend(3);
                cb.Colors = new[] { Color.FromArgb(Style.BorderAlpha, Style.BorderColor), Color.FromArgb(Style.BorderAlpha * 2 / 3, Style.TitleColor), Color.FromArgb(Style.BorderAlpha / 2, Holo) };
                cb.Positions = new[] { 0f, 0.45f, 1f };
                lb.InterpolationColors = cb;
                using (var pen = new Pen(lb, bw)) g.DrawPath(pen, bp);
            }
        }

        void AnimRallye(Graphics g, int w, int h, bool open, float cut)
        {
            var app = ZoneApp.I;
            if (!IsActive) return;
            long now = Clock.ElapsedMilliseconds;
            float t = EaseOut(HoverProgress(300));
            DrawSpeedStreaks(g, w, Color.FromArgb(140, 255, 214, 160), 900);
            if (!open) return;
            // Lame qui s'allume de gauche à droite puis grésille
            float x0 = app.S(10), len = (w - app.S(20)) * t, y = titleH - app.S(3), bh = app.S(2);
            if (len > 0)
            {
                int hum = (now / 60) % 2 == 0 ? 255 : 210;
                using (var b = new SolidBrush(Color.FromArgb(hum * 18 / 255, Holo))) g.FillRectangle(b, x0, y - app.S(5), len, bh + app.S(10));
                using (var b = new SolidBrush(Color.FromArgb(hum * 50 / 255, Holo))) g.FillRectangle(b, x0, y - app.S(2), len, bh + app.S(4));
                using (var b = new SolidBrush(Color.FromArgb(hum, 201, 246, 255))) g.FillRectangle(b, x0, y, len, bh);
            }
            // Grille holographique + balayage
            if (h > titleH + app.S(10))
            {
                using (var shape = ShapePath(new RectangleF(0, 0, w, h), cut, true))
                {
                    g.SetClip(shape);
                    g.IntersectClip(new Rectangle(0, titleH, w, h - titleH));
                    int step = app.S(14), ds = Math.Max(1, app.S(1));
                    using (var b = new SolidBrush(Color.FromArgb((int)(60 * t), Holo)))
                        for (int yy = titleH + step / 2; yy < h; yy += step)
                            for (int xx = step / 2; xx < w; xx += step) g.FillRectangle(b, xx, yy, ds, ds);
                    g.ResetClip();
                }
                DrawScanBand(g, w, h, cut, Holo, 2600, 90);
            }
        }

        // ===== Friche : béton taché, mousse, liane, ruban adhésif écrit à la main, lampe torche =====

        static readonly Color Moss = Color.FromArgb(95, 112, 55), Moss2 = Color.FromArgb(138, 154, 78), MossDark = Color.FromArgb(76, 90, 44);

        static int StableHash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s ?? "") h = h * 31 + c;
                return h;
            }
        }

        static Color Lighten(Color c, int d)
        {
            return Color.FromArgb(c.A, Math.Min(255, c.R + d), Math.Min(255, c.G + d), Math.Min(255, c.B + d));
        }

        static void Leaf(Graphics g, float x, float y, float rx, float ry, float angle, Brush b)
        {
            var st = g.Save();
            g.TranslateTransform(x, y);
            g.RotateTransform(angle);
            g.FillEllipse(b, -rx, -ry, 2 * rx, 2 * ry);
            g.Restore(st);
        }

        // Morceau de ruban aux bouts déchirés, centré sur l'origine.
        static PointF[] TornTape(float w, float h)
        {
            float x = w / 2, y = h / 2, j = Math.Max(2f, h * 0.14f);
            return new[]
            {
                new PointF(-x + j * 0.5f, -y), new PointF(x, -y + j * 0.3f), new PointF(x - j, -y + h * 0.3f), new PointF(x, -y + h * 0.52f),
                new PointF(x - j * 0.6f, -y + h * 0.78f), new PointF(x, y), new PointF(-x, y - j * 0.3f), new PointF(-x + j, y - h * 0.3f),
                new PointF(-x, -y + h * 0.46f), new PointF(-x + j * 0.6f, -y + h * 0.22f)
            };
        }

        void TitleFriche(Graphics g, int w, bool open, float right)
        {
            var app = ZoneApp.I;
            if (Style.ShowCount)
                using (var cf = new Font(titleFont.FontFamily, Math.Max(7f, titleFont.SizeInPoints * 0.85f), FontStyle.Regular, GraphicsUnit.Point))
                {
                    int n = items.Count;
                    string cnt = n + (n > 1 ? " trucs" : " truc");
                    float cw = DrawTracked(g, cnt, cf, null, 0, titleH / 2f, 0, float.MaxValue);
                    using (var b = new SolidBrush(Color.FromArgb(170, Style.TitleColor))) DrawTracked(g, cnt, cf, b, right - cw, titleH / 2f, 0, right);
                    right -= cw + app.S(12);
                }
            // Ruban adhésif un peu de travers, nom écrit à la main
            float x0 = app.S(12), pad = app.S(14);
            float tw = DrawTracked(g, ZoneName, titleFont, null, 0, 0, 0, float.MaxValue);
            float tapeW = Math.Max(app.S(40), Math.Min(tw + 2 * pad, right - x0));
            float tapeH = Math.Max(app.S(16), titleH - app.S(14));
            var st = g.Save();
            g.TranslateTransform(x0 + tapeW / 2, titleH / 2f);
            g.RotateTransform(-2.2f);
            PointF[] tape = TornTape(tapeW, tapeH);
            g.TranslateTransform(1, 2);
            using (var sh = new SolidBrush(Color.FromArgb(80, 0, 0, 0))) g.FillPolygon(sh, tape);
            g.TranslateTransform(-1, -2);
            using (var tb = new LinearGradientBrush(new RectangleF(-tapeW / 2, -tapeH / 2 - 1, tapeW, tapeH + 2),
                Color.FromArgb(Style.TitleAlpha, Lighten(Style.TitleColor, 14)), Color.FromArgb(Style.TitleAlpha, Style.TitleColor), 90f))
                g.FillPolygon(tb, tape);
            using (var b = new SolidBrush(Style.TitleTextColor))
                DrawTracked(g, ZoneName, titleFont, b, -tapeW / 2 + pad, app.S(1), 0, tapeW / 2 - app.S(6));
            g.Restore(st);
        }

        void BackFriche(Graphics g, int w, int h, GraphicsPath shape, bool open)
        {
            if (shape == null) return;
            var app = ZoneApp.I;
            g.SetClip(shape);
            FillNoise(g, shape);
            int seed = StableHash(ZoneName);
            // Taches d'humidité
            for (int k = 0; k < 3; k++)
            {
                float cx = w * (0.1f + 0.8f * Hash(k, seed)), cy = h * Hash(k, seed + 1), r = app.S(50) + app.S(60) * Hash(k, seed + 2);
                Color c = k % 2 == 0 ? Color.FromArgb(48, 110, 95, 55) : Color.FromArgb(70, 30, 40, 22);
                FillGlow(g, new RectangleF(cx - r, cy - r * 0.8f, 2 * r, 1.6f * r), c);
            }
            // Mousse qui déborde du coin bas-gauche
            float[,] blobs = { { 0.18f, 0.70f, 14, 0 }, { 0.34f, 0.85f, 11, 1 }, { 0.48f, 0.95f, 13, 0 }, { 0.08f, 0.40f, 10, 2 }, { 0.62f, 1.0f, 9, 2 }, { 0.26f, 0.55f, 6, 1 } };
            float bx = -app.S(10), bw2 = app.S(150), bh2 = app.S(60), by = h + app.S(12) - bh2;
            for (int k = 0; k < blobs.GetLength(0); k++)
            {
                float r = app.S((int)blobs[k, 2]);
                float cx = bx + bw2 * blobs[k, 0], cy = by + bh2 * blobs[k, 1];
                Color c = blobs[k, 3] == 0 ? Moss : blobs[k, 3] == 1 ? Moss2 : MossDark;
                using (var b = new SolidBrush(Color.FromArgb(205, c))) g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
            }
            // Liane qui pend du haut (zone ouverte)
            if (open && h > titleH + app.S(80))
            {
                float vx = w * 0.52f;
                Func<float, float, PointF> P = (x, y) => new PointF(vx + app.S((int)x), app.S((int)y));
                using (var vp = new Pen(Color.FromArgb(220, 86, 101, 58), Math.Max(1.5f, app.S(2))))
                {
                    g.DrawBezier(vp, P(8, 0), P(12, 18), P(2, 30), P(12, 46));
                    g.DrawBezier(vp, P(12, 46), P(22, 62), P(32, 68), P(28, 86));
                }
                using (var vp = new Pen(Color.FromArgb(200, MossDark), Math.Max(1.2f, app.S(1))))
                    g.DrawBezier(vp, P(40, 0), P(38, 12), P(48, 20), P(44, 34));
                using (var lb = new SolidBrush(Color.FromArgb(230, 108, 125, 64)))
                {
                    float[,] leaves = { { 14, 14, 30 }, { 4, 30, -30 }, { 18, 50, 25 }, { 26, 70, -20 }, { 46, 16, 40 }, { 40, 30, -35 } };
                    for (int k = 0; k < leaves.GetLength(0); k++)
                    {
                        PointF q = P(leaves[k, 0], leaves[k, 1]);
                        Leaf(g, q.X, q.Y, app.S(5), app.S(3), leaves[k, 2], lb);
                    }
                }
            }
            g.ResetClip();
        }

        void FrontFriche(Graphics g, int w, int h, float cut)
        {
            if (Style.BorderWidth <= 0) return;
            float bw = ZoneApp.I.S(Style.BorderWidth);
            using (var pen = new Pen(Color.FromArgb(Style.BorderAlpha, Style.BorderColor), bw))
            using (var bp = ShapePath(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), Math.Max(0, cut - bw / 2), true))
                g.DrawPath(pen, bp);
        }

        void AnimFriche(Graphics g, int w, int h, bool open, float cut)
        {
            var app = ZoneApp.I;
            if (!open || !IsActive || h <= titleH) return;
            float t = EaseOut(HoverProgress(250));
            float now = Clock.ElapsedMilliseconds % 1000000L;
            using (var shape = ShapePath(new RectangleF(0, 0, w, h), cut, true))
            {
                g.SetClip(shape);
                // Lampe torche qui suit la souris, avec un vacillement de temps en temps
                Point m = PointToClient(Cursor.Position);
                float f = now % 5000;
                float flick = f > 4450 && f < 4500 ? 0.35f : f > 4600 && f < 4650 ? 0.55f : 1f;
                float r = app.S(130);
                FillGlow(g, new RectangleF(m.X - r, m.Y - r, 2 * r, 2 * r), Color.FromArgb((int)(52 * t * flick), 255, 238, 196));
                // Spores lumineuses qui montent lentement
                float d = Math.Max(2f, app.S(3));
                for (int k = 0; k < 12; k++)
                {
                    float dur = 5000 + 4000 * Hash(k, 11), p = ((now + Hash(k, 12) * 8000) % dur) / dur;
                    float x = w * (0.06f + 0.88f * Hash(k, 13)) + (Hash(k, 14) - 0.5f) * app.S(30) * p;
                    float y = titleH + (h - titleH) * (0.55f + 0.45f * Hash(k, 15)) - app.S(140) * p;
                    float a = p < 0.15f ? p / 0.15f : p > 0.85f ? (1 - p) / 0.15f : 1f;
                    int al = (int)(200 * a * t);
                    using (var b = new SolidBrush(Color.FromArgb(al / 3, 225, 232, 180))) g.FillEllipse(b, x - d, y - d, 2 * d, 2 * d);
                    using (var b = new SolidBrush(Color.FromArgb(al, 225, 232, 180))) g.FillEllipse(b, x - d / 2, y - d / 2, d, d);
                }
                g.ResetClip();
            }
        }

        // ===== Sanguine : nuit gothique, cadre bronze à ornements, capitales gravées, filet de sang, bougie =====

        static string Roman(int n)
        {
            if (n <= 0) return "0";
            if (n > 3999) return n.ToString();
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] r = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(r[i]); n -= v[i]; }
            return sb.ToString();
        }

        float SanguineRuleY { get { return titleH - ZoneApp.I.S(6); } }

        void TitleSanguine(Graphics g, int w, bool open, float right)
        {
            var app = ZoneApp.I;
            Color bronze = Style.BorderColor;
            float textMid = (titleH - app.S(10)) / 2f + app.S(2);
            float track = titleFont.Size * 0.3f * app.Scale;
            string name = ZoneName.ToUpperInvariant();
            string cnt = Collapsed && Style.ShowCount ? "·  " + Roman(items.Count) + "  ·" : null;
            float maxR = Math.Min(right, w - app.S(30));
            float tw = DrawTracked(g, name, titleFont, null, 0, textMid, track, float.MaxValue);
            Font cf = null;
            float cw = 0;
            if (cnt != null)
            {
                cf = new Font(titleFont.FontFamily, Math.Max(7f, titleFont.SizeInPoints * 0.85f), FontStyle.Regular, GraphicsUnit.Point);
                cw = DrawTracked(g, cnt, cf, null, 0, textMid, track * 0.5f, float.MaxValue) + app.S(10);
            }
            float x = Math.Max(app.S(30), (w - tw - cw) / 2f);
            using (var b = new SolidBrush(Style.TitleTextColor)) x += DrawTracked(g, name, titleFont, b, x, textMid, track, maxR - cw) + app.S(10);
            if (cf != null)
            {
                using (var b = new SolidBrush(Lighten(Style.TitleColor, 25))) DrawTracked(g, cnt, cf, b, x, textMid, track * 0.5f, maxR);
                cf.Dispose();
            }
            // Filet bronze avec losange central
            float ry = SanguineRuleY, rx0 = app.S(24), rx1 = w - app.S(24);
            if (rx1 > rx0 + 10)
                using (var lb = new LinearGradientBrush(new RectangleF(rx0 - 1, ry, rx1 - rx0 + 2, 2), Color.Black, Color.Black, 0f))
                {
                    var cb = new ColorBlend(4);
                    cb.Colors = new[] { Color.FromArgb(0, bronze), Color.FromArgb(180, bronze), Color.FromArgb(180, bronze), Color.FromArgb(0, bronze) };
                    cb.Positions = new[] { 0f, 0.25f, 0.75f, 1f };
                    lb.InterpolationColors = cb;
                    g.FillRectangle(lb, rx0, ry, rx1 - rx0, Math.Max(1, app.S(1)));
                }
            float d = app.S(4), cx = w / 2f, dy = ry + 0.5f;
            var dia = new[] { new PointF(cx, dy - d), new PointF(cx + d, dy), new PointF(cx, dy + d), new PointF(cx - d, dy) };
            using (var p = new Pen(Color.FromArgb(Math.Max(1, Style.BgAlpha), Style.BgColor), app.S(2))) g.DrawPolygon(p, dia);
            using (var b = new SolidBrush(bronze)) g.FillPolygon(b, dia);
        }

        void BackSanguine(Graphics g, int w, int h, GraphicsPath shape, bool open)
        {
            if (shape == null) return;
            g.SetClip(shape);
            using (var lb = new LinearGradientBrush(new RectangleF(0, -1, w, h + 2), Color.Black, Color.Black, 90f))
            {
                Color wine = Color.FromArgb(46, 11, 16);
                var cb = new ColorBlend(4);
                cb.Colors = new[] { Color.FromArgb(0, wine), Color.FromArgb(0, wine), Color.FromArgb(150, wine), Color.FromArgb(230, 52, 10, 16) };
                cb.Positions = new[] { 0f, 0.35f, 0.7f, 1f };
                lb.InterpolationColors = cb;
                g.FillRectangle(lb, 0, 0, w, h);
            }
            g.ResetClip();
        }

        // Ornement d'angle (dessiné pour le coin haut-gauche dans une boîte de 30 × 30, puis retourné).
        static void Ornament(Graphics g, float x, float y, float sx, float sy, float size, Color c)
        {
            var st = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(sx * size / 30f, sy * size / 30f);
            using (var p = new Pen(c, 1.2f))
            {
                g.DrawLine(p, 3, 27, 3, 9); g.DrawArc(p, 3, 3, 12, 12, 180, 90); g.DrawLine(p, 9, 3, 27, 3);
            }
            using (var p = new Pen(Color.FromArgb(c.A * 7 / 10, c), 0.8f))
            {
                g.DrawLine(p, 7, 23, 7, 12); g.DrawArc(p, 7, 7, 10, 10, 180, 90); g.DrawLine(p, 12, 7, 23, 7);
            }
            using (var p = new Pen(c, 1f)) g.DrawBezier(p, 10, 10, 13, 10, 14, 12, 12, 14);
            using (var b = new SolidBrush(c))
            {
                g.FillEllipse(b, 1.6f, 25.6f, 2.8f, 2.8f);
                g.FillEllipse(b, 25.6f, 1.6f, 2.8f, 2.8f);
                g.FillPolygon(b, new[] { new PointF(16, 1), new PointF(18, 3), new PointF(16, 5), new PointF(14, 3) });
                g.FillPolygon(b, new[] { new PointF(1, 16), new PointF(3, 14), new PointF(5, 16), new PointF(3, 18) });
            }
            g.Restore(st);
        }

        void FrontSanguine(Graphics g, int w, int h)
        {
            var app = ZoneApp.I;
            Color c = Color.FromArgb(Style.BorderAlpha, Style.BorderColor);
            if (Style.BorderWidth > 0)
            {
                float bw = app.S(Style.BorderWidth);
                using (var pen = new Pen(c, bw)) g.DrawRectangle(pen, bw / 2, bw / 2, w - bw, h - bw);
                float ins = app.S(4) + bw / 2;
                if (h > 2 * ins + 2)
                    using (var pen = new Pen(Color.FromArgb(Style.BorderAlpha * 2 / 5, Style.BorderColor))) g.DrawRectangle(pen, ins, ins, w - 2 * ins, h - 2 * ins);
            }
            float size = Math.Min(app.S(28), h / 2f - app.S(2)), o = app.S(2);
            if (size < app.S(10)) return;
            Ornament(g, o, o, 1, 1, size, c);
            Ornament(g, w - o, o, -1, 1, size, c);
            Ornament(g, o, h - o, 1, -1, size, c);
            Ornament(g, w - o, h - o, -1, -1, size, c);
        }

        static readonly int[] CandleAt = { 0, 11, 23, 31, 47, 58, 71, 84 };
        static readonly float[] CandleLevel = { 0.9f, 1f, 0.75f, 0.95f, 0.82f, 1f, 0.7f, 0.92f };

        // Intensité d'une flamme qui vacille par paliers irréguliers.
        static float CandleFlicker(float now, float periodMs)
        {
            float ph = (now % periodMs) / periodMs * 100f;
            float lv = CandleLevel[0];
            for (int k = 0; k < CandleAt.Length; k++) if (ph >= CandleAt[k]) lv = CandleLevel[k];
            return lv;
        }

        // Voile de brume qui dérive lentement au ras du bas.
        void DrawMist(Graphics g, int w, int h, float now, float t, int alpha)
        {
            var app = ZoneApp.I;
            if (h <= titleH + app.S(30)) return;
            g.SetClip(new Rectangle(0, titleH, w, h - titleH));
            float shift = (float)Math.Sin(now / 9000.0 * 2 * Math.PI) * w * 0.09f, ry = app.S(42);
            FillGlow(g, new RectangleF(w * 0.25f + shift - w * 0.4f, h - ry, w * 0.8f, 2 * ry), Color.FromArgb((int)(alpha * t), 200, 192, 198));
            FillGlow(g, new RectangleF(w * 0.7f - shift * 0.7f - w * 0.35f, h - ry * 0.8f, w * 0.7f, 1.6f * ry), Color.FromArgb((int)(alpha * 0.8f * t), 200, 192, 198));
            g.ResetClip();
        }

        void AnimSanguine(Graphics g, int w, int h, bool open, float cut)
        {
            var app = ZoneApp.I;
            if (!IsActive) return;
            float t = EaseOut(HoverProgress(500));
            float now = Clock.ElapsedMilliseconds % 1000000L;
            long el = Clock.ElapsedMilliseconds - activeSince;
            // Lueur de bougie qui vacille en haut de la zone
            FillGlow(g, new RectangleF(w / 2f - w * 0.8f, -h * 0.55f, w * 1.6f, h * 1.1f), Color.FromArgb((int)(46 * t * CandleFlicker(now, 1700)), 255, 179, 107));
            // Filet de sang qui se répand depuis le centre du séparateur
            float ry = SanguineRuleY, full = (w - app.S(48)) * 0.8f, len = full * t, cx = w / 2f;
            if (len > 2)
            {
                Color cr = Style.TitleColor;
                using (var lb = new LinearGradientBrush(new RectangleF(cx - len / 2 - 1, ry, len + 2, 2), Color.Black, Color.Black, 0f))
                {
                    var cb = new ColorBlend(5);
                    cb.Colors = new[] { Color.FromArgb(0, cr), Color.FromArgb(255, cr), Color.FromArgb(255, Lighten(cr, 40)), Color.FromArgb(255, cr), Color.FromArgb(0, cr) };
                    cb.Positions = new[] { 0f, 0.3f, 0.5f, 0.7f, 1f };
                    lb.InterpolationColors = cb;
                    g.FillRectangle(lb, cx - len / 2, ry - app.S(1), len, app.S(3));
                }
                FillGlow(g, new RectangleF(cx - len / 2, ry - app.S(5), len, app.S(11)), Color.FromArgb(70, Lighten(cr, 40)));
            }
            if (!open) return;
            // Une goutte se forme sous le losange puis tombe, toutes les 4,2 s
            if (el > 600)
            {
                float p = ((el - 600) % 4200) / 4200f, dl, dy = 0;
                int a = 255;
                if (p < 0.35f) dl = app.S(12) * (p / 0.35f);
                else if (p < 0.55f) { float q = (p - 0.35f) / 0.2f; dl = app.S(9); dy = app.S(60) * q * q; }
                else if (p < 0.62f) { float q = (p - 0.55f) / 0.07f; dl = app.S(9); dy = app.S(60) + app.S(20) * q; a = (int)(255 * (1 - q)); }
                else dl = 0;
                if (dl > 1)
                {
                    float dw = Math.Max(2f, app.S(3)), y0 = ry + app.S(5) + dy;
                    using (var lb = new LinearGradientBrush(new RectangleF(cx - dw / 2, y0 - 1, dw, dl + 2), Color.FromArgb(a, 125, 20, 27), Color.FromArgb(a, 195, 38, 47), 90f))
                    using (var dp = RoundRect(new RectangleF(cx - dw / 2, y0, dw, dl), dw / 2, true))
                        g.FillPath(lb, dp);
                }
            }
            DrawMist(g, w, h, now, t, 36);
        }

        // ===== Veillée : tome de pierre, onglet doré, cases biseautées, filets à volutes =====

        static ImageAttributes sepiaAttr;

        // Icônes un peu désaturées et virées au sépia, comme des objets anciens.
        static ImageAttributes SepiaAttr
        {
            get
            {
                if (sepiaAttr != null) return sepiaAttr;
                float s = 0.35f;
                float[] lum = { 0.3086f, 0.6094f, 0.0820f };
                float[] tint = { 1.02f * 0.88f, 0.96f * 0.88f, 0.84f * 0.88f };
                var m = new float[5][];
                for (int i = 0; i < 3; i++)
                {
                    m[i] = new float[5];
                    for (int j = 0; j < 3; j++) m[i][j] = ((1 - s) * lum[i] + (i == j ? s : 0)) * tint[j];
                }
                m[3] = new float[] { 0, 0, 0, 1, 0 };
                m[4] = new float[] { 0, 0, 0, 0, 1 };
                var ia = new ImageAttributes();
                ia.SetColorMatrix(new ColorMatrix(m));
                sepiaAttr = ia;
                return ia;
            }
        }

        // Volute en bout de filet (dessinée dans une boîte de 26 × 10, pointe vers la gauche).
        static void Curl(Graphics g, float x, float y, float scale, bool mirror, Color c)
        {
            var st = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(mirror ? -scale : scale, scale);
            g.TranslateTransform(0, -5);
            using (var p = new Pen(c, 1f))
            {
                g.DrawLine(p, 26, 5, 12, 5);
                g.DrawBezier(p, 12, 5, 9, 5, 8, 2, 5, 2);
                g.DrawBezier(p, 5, 2, 2, 2, 0.5f, 4, 2, 6);
                g.DrawBezier(p, 2, 6, 3.5f, 8, 6, 8, 6, 6);
                g.DrawBezier(p, 6, 6, 6, 4.5f, 4, 4.5f, 4, 5);
            }
            using (var b = new SolidBrush(c)) g.FillEllipse(b, 8, 4, 2, 2);
            g.Restore(st);
        }

        void FiligreeRule(Graphics g, float x0, float x1, float y, Color c)
        {
            float sc = ZoneApp.I.S(26) / 26f, cw = 26 * sc;
            if (x1 - x0 < 2 * cw + 10) return;
            Curl(g, x0, y, sc, false, c);
            Curl(g, x1, y, sc, true, c);
            using (var p = new Pen(Color.FromArgb(c.A * 3 / 4, c))) g.DrawLine(p, x0 + cw, y, x1 - cw, y);
        }

        void TitleVeillee(Graphics g, int w, bool open, float right)
        {
            var app = ZoneApp.I;
            float top = app.S(4), ym = top + (titleH - top) / 2f;
            if (Style.ShowCount)
                using (var cf = new Font(titleFont.FontFamily, Math.Max(7f, titleFont.SizeInPoints * 0.72f), FontStyle.Regular, GraphicsUnit.Point))
                {
                    string cnt = items.Count.ToString();
                    float cw = DrawTracked(g, cnt, cf, null, 0, 0, 0, float.MaxValue);
                    using (var b = new SolidBrush(Color.FromArgb(143, 138, 128))) DrawTracked(g, cnt, cf, b, right - cw, ym + app.S(2), 0, right);
                    right -= cw + app.S(10);
                }
            // Onglet de pierre qui dépasse du panneau
            float tw = DrawTracked(g, ZoneName, titleFont, null, 0, 0, 0, float.MaxValue);
            float tabW = Math.Max(app.S(60), Math.Min(tw + app.S(34), right));
            var tab = new RectangleF(0, top, tabW, titleH - top);
            using (var lb = new LinearGradientBrush(new RectangleF(0, top - 1, tabW, titleH - top + 2), Color.FromArgb(242, 44, 42, 39), Color.FromArgb(242, 30, 29, 27), 90f))
                g.FillRectangle(lb, tab);
            using (var tb = new TextureBrush(NoiseTexture, WrapMode.Tile)) g.FillRectangle(tb, tab);
            using (var p = new Pen(Color.FromArgb(Style.BorderAlpha * 4 / 5, Style.BorderColor)))
                g.DrawLines(p, new[] { new PointF(0.5f, titleH), new PointF(0.5f, top + 0.5f), new PointF(tabW - 0.5f, top + 0.5f), new PointF(tabW - 0.5f, titleH) });
            using (var b = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) DrawTracked(g, ZoneName, titleFont, b, app.S(15) + 1, ym + 1, 0, tabW - app.S(8));
            using (var b = new SolidBrush(Style.TitleTextColor)) DrawTracked(g, ZoneName, titleFont, b, app.S(15), ym, 0, tabW - app.S(8));
            // Repliée : le bord du cadre sous l'onglet
            if (!open)
            {
                float bw = Math.Max(1, app.S(Style.BorderWidth));
                using (var p = new Pen(Color.FromArgb(Style.BorderAlpha, Style.BorderColor), bw)) g.DrawLine(p, 0, titleH - bw / 2, w, titleH - bw / 2);
            }
        }

        void BackVeillee(Graphics g, int w, int h, GraphicsPath shape, bool open)
        {
            if (shape == null) return;
            var app = ZoneApp.I;
            g.SetClip(shape);
            FillNoise(g, shape);
            // Ombre intérieure : le panneau paraît creusé
            int st = Math.Max(1, app.S(3));
            for (int k = 0; k < 5; k++)
            {
                float x = k * st, y = titleH + k * st, rw = w - 2 * k * st, rh = h - titleH - 2 * k * st;
                if (rw <= 0 || rh <= 0) break;
                using (var p = new Pen(Color.FromArgb(70 - k * 13, 0, 0, 0), st)) g.DrawRectangle(p, x, y, rw, rh);
            }
            if (open) FiligreeRule(g, app.S(10), w - app.S(10), titleH + app.S(9), Color.FromArgb(160, 119, 115, 107));
            g.ResetClip();
        }

        void ItemVeillee(Graphics g, int i, Rectangle cell, bool sel, int isz, Brush lb, Brush sb, StringFormat sf)
        {
            var app = ZoneApp.I;
            bool hov = i == hover;
            var ir = IconRect(cell, isz);
            var slot = Rectangle.Inflate(ir, IconInset, IconInset);
            using (var bg = new LinearGradientBrush(new Rectangle(slot.X, slot.Y - 1, slot.Width, slot.Height + 2), Color.FromArgb(47, 45, 42), Color.FromArgb(20, 19, 18), 90f))
                g.FillRectangle(bg, slot);
            int s2 = app.S(2), s3 = Math.Max(1, app.S(3));
            using (var p = new Pen(Color.FromArgb(150, 0, 0, 0), s3)) g.DrawRectangle(p, slot.X + s2, slot.Y + s2, slot.Width - 2 * s2, slot.Height - 2 * s2);
            if (hov || sel)
                using (var p = new Pen(Color.FromArgb(sel ? 90 : 60, 225, 214, 190), app.S(4))) g.DrawRectangle(p, Rectangle.Inflate(slot, s2, s2));
            using (var p = new Pen(Color.Black)) g.DrawRectangle(p, slot.X + 1, slot.Y + 1, slot.Width - 3, slot.Height - 3);
            Color edge = sel ? Style.TitleColor : hov ? Color.FromArgb(220, 212, 194) : Color.FromArgb(103, 100, 93);
            using (var p = new Pen(edge)) g.DrawRectangle(p, slot.X, slot.Y, slot.Width - 1, slot.Height - 1);
            var ic = items[i].Icon;
            if (ic != null) g.DrawImage(ic, ir, 0, 0, ic.Width, ic.Height, GraphicsUnit.Pixel, SepiaAttr);
            DrawItemLabel(g, items[i].Name, LabelRect(cell, ir), lb, sb, sf);
        }

        void FrontVeillee(Graphics g, int w, int h)
        {
            var app = ZoneApp.I;
            if (h <= titleH + 2 || Style.BorderWidth <= 0) return;
            float bw = app.S(Style.BorderWidth);
            using (var p = new Pen(Color.FromArgb(Style.BorderAlpha, Style.BorderColor), bw)) g.DrawRectangle(p, bw / 2, titleH + bw / 2, w - bw, h - titleH - bw);
            using (var p = new Pen(Color.FromArgb(220, 11, 10, 10))) g.DrawRectangle(p, bw + 0.5f, titleH + bw + 0.5f, w - 2 * bw - 1, h - titleH - 2 * bw - 1);
        }

        void AnimVeillee(Graphics g, int w, int h, bool open, float cut)
        {
            var app = ZoneApp.I;
            if (!IsActive) return;
            float t = EaseOut(HoverProgress(400));
            float now = Clock.ElapsedMilliseconds % 1000000L;
            long el = Clock.ElapsedMilliseconds - activeSince;
            if (open && h > titleH)
            {
                // Lueur de bougie dans le coin haut-gauche
                g.SetClip(new Rectangle(0, titleH, w, h - titleH));
                FillGlow(g, new RectangleF(w * 0.18f - w * 0.7f, titleH - h * 0.5f, w * 1.4f, h), Color.FromArgb((int)(36 * t * CandleFlicker(now, 1900)), 255, 176, 100));
                g.ResetClip();
                DrawMist(g, w, h, now, t, 26);
                // La case survolée respire doucement
                if (hover >= 0 && hover < items.Count)
                {
                    var slot = Rectangle.Inflate(IconRect(CellRect(hover), app.S(Style.IconSize)), IconInset, IconInset);
                    float pulse = 0.5f + 0.5f * (float)Math.Sin(now / 1600.0 * 2 * Math.PI);
                    g.SetClip(ContentRect);
                    using (var p = new Pen(Color.FromArgb((int)(70 * pulse), 235, 225, 200), Math.Max(1, app.S(2)))) g.DrawRectangle(p, Rectangle.Inflate(slot, app.S(3), app.S(3)));
                    g.ResetClip();
                }
            }
            // Éclat qui parcourt le bord du cadre, une seule fois à l'arrivée de la souris
            float sp = el / 1400f;
            if (sp < 1f)
            {
                float y = open ? titleH : titleH - app.S(2), seg = w * 0.3f, x = -seg + sp * (w + seg);
                int a = (int)(230 * (sp > 0.85f ? (1 - sp) / 0.15f : 1f));
                using (var lb = new LinearGradientBrush(new RectangleF(x - 1, y, seg + 2, 2), Color.Black, Color.Black, 0f))
                {
                    var cb = new ColorBlend(3);
                    cb.Colors = new[] { Color.FromArgb(0, 255, 250, 235), Color.FromArgb(a, 255, 250, 235), Color.FromArgb(0, 255, 250, 235) };
                    cb.Positions = new[] { 0f, 0.5f, 1f };
                    lb.InterpolationColors = cb;
                    g.FillRectangle(lb, x, y, seg, Math.Max(1, app.S(2)));
                }
            }
        }

        // ===== Circuit : papier crème, « /TITRE », bandes de course rétro, cartes bleu canard, pilule sombre =====

        static readonly Color RaceNavy = Color.FromArgb(34, 52, 62), RaceTeal = Color.FromArgb(52, 82, 95),
                              RaceSky = Color.FromArgb(169, 195, 198), RaceOrange = Color.FromArgb(228, 138, 46);

        void TitleCircuit(Graphics g, int w, bool open, float right)
        {
            var app = ZoneApp.I;
            float ym = titleH / 2f;
            float track = Math.Max(1f, titleFont.Size * 0.12f) * app.Scale;
            Color cream = Color.FromArgb(241, 239, 232);
            // Compteur dans une pilule sombre, avec un losange
            if (Style.ShowCount)
                using (var cf = new Font(titleFont.FontFamily, Math.Max(7f, titleFont.SizeInPoints * 0.62f), FontStyle.Bold, GraphicsUnit.Point))
                {
                    string cnt = items.Count.ToString("00");
                    float cw = DrawTracked(g, cnt, cf, null, 0, ym, 0, float.MaxValue);
                    float ph = Math.Max(app.S(16), titleH * 0.48f), pw = cw + app.S(30);
                    var pr = new RectangleF(right - pw, ym - ph / 2, pw, ph);
                    using (var pp = RoundRect(pr, ph / 2, true))
                    using (var b = new SolidBrush(RaceNavy)) g.FillPath(b, pp);
                    float d = ph * 0.2f, cx = pr.X + app.S(12);
                    using (var p = new Pen(cream, Math.Max(1.5f, app.S(2))))
                        g.DrawPolygon(p, new[] { new PointF(cx, ym - d), new PointF(cx + d, ym), new PointF(cx, ym + d), new PointF(cx - d, ym) });
                    using (var b = new SolidBrush(cream)) DrawTracked(g, cnt, cf, b, pr.X + app.S(20), ym, 0, pr.Right);
                    right = pr.X - app.S(8);
                }
            // « / » rouge puis le nom en capitales larges
            float x = app.S(14);
            Color red = Color.FromArgb(Style.TitleAlpha, Style.TitleColor);
            using (var b = new SolidBrush(red)) x += DrawTracked(g, "/", titleFont, b, x, ym, 0, right) + track;
            using (var b = new SolidBrush(Style.TitleTextColor)) x += DrawTracked(g, ZoneName.ToUpperInvariant(), titleFont, b, x, ym, track, right - app.S(44));
            // Petit badge rayé
            float bx = x + app.S(10), bwid = app.S(30), bh = app.S(12);
            if (bx + bwid <= right)
            {
                using (var p = new Pen(red, Math.Max(1.5f, app.S(2)))) g.DrawRectangle(p, bx, ym - bh / 2, bwid, bh);
                using (var b = new SolidBrush(red))
                    for (int k = 0; k < 3; k++) g.FillRectangle(b, bx + app.S(5) + k * app.S(7), ym - app.S(2), app.S(3), app.S(4));
            }
        }

        void BackCircuit(Graphics g, int w, int h, GraphicsPath shape, bool open)
        {
            if (shape == null) return;
            var app = ZoneApp.I;
            g.SetClip(shape);
            FillNoise(g, shape);
            if (!open) { g.ResetClip(); return; } // repliée : la pilule et la roue restent lisibles
            // Bandes de course qui traversent le coin bas-droit
            var st = g.Save();
            g.TranslateTransform(w - app.S(5), Math.Max(h - app.S(70), titleH + app.S(30)));
            g.RotateTransform(36f);
            float L = app.S(600);
            int[,] bands = { { -75, -51 }, { -43, -23 }, { -15, 7 }, { 17, 25 } };
            Color[] cols = { RaceTeal, RaceSky, Style.TitleColor, RaceOrange };
            for (int k = 0; k < 4; k++)
                using (var b = new SolidBrush(Color.FromArgb(240, cols[k])))
                    g.FillRectangle(b, app.S(bands[k, 0]), -L, app.S(bands[k, 1] - bands[k, 0]), 2 * L);
            g.Restore(st);
            g.ResetClip();
        }

        void ItemCircuit(Graphics g, int i, Rectangle cell, bool sel, int isz, Brush lb, Brush sb, StringFormat sf)
        {
            var app = ZoneApp.I;
            bool hov = i == hover;
            int lift = hov ? app.S(2) : 0;
            var card = new RectangleF(cell.X + app.S(4), cell.Y + app.S(4) - lift, cell.Width - app.S(8), cell.Height - app.S(8));
            float rad = app.S(9);
            // La carte survolée se soulève (ombre portée) et prend un contour blanc et un point rouge
            if (hov)
                using (var shp = RoundRect(new RectangleF(card.X, card.Y + app.S(4), card.Width, card.Height), rad, true))
                using (var b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillPath(b, shp);
            using (var cp = RoundRect(card, rad, true))
            {
                using (var bg = new LinearGradientBrush(new RectangleF(card.X, card.Y - 1, card.Width, card.Height + 2), Color.FromArgb(58, 90, 104), RaceTeal, 90f))
                    g.FillPath(bg, cp);
                using (var p = new Pen(Color.FromArgb(36, 255, 255, 255))) g.DrawPath(p, cp);
            }
            if (hov || sel)
                using (var p = new Pen(sel ? Color.FromArgb(Style.TitleAlpha, Style.TitleColor) : Color.White, Math.Max(1.5f, app.S(2))))
                using (var op = RoundRect(RectangleF.Inflate(card, app.S(1), app.S(1)), rad + app.S(1), true))
                    g.DrawPath(p, op);
            if (hov)
                using (var b = new SolidBrush(Style.TitleColor))
                {
                    float d = app.S(6);
                    g.FillEllipse(b, card.Right - app.S(6) - d, card.Y + app.S(6), d, d);
                }
            var ir = IconRect(cell, isz);
            ir.Offset(0, -lift);
            if (items[i].Icon != null) g.DrawImage(items[i].Icon, ir);
            DrawItemLabel(g, items[i].Name, LabelRect(cell, ir), lb, sb, sf);
        }

        void FrontCircuit(Graphics g, int w, int h, float cut)
        {
            if (Style.BorderWidth <= 0) return;
            float bw = ZoneApp.I.S(Style.BorderWidth);
            using (var pen = new Pen(Color.FromArgb(Style.BorderAlpha, Style.BorderColor), bw))
            using (var bp = ShapePath(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), Math.Max(0, cut - bw / 2), true))
                g.DrawPath(pen, bp);
        }

        void AnimCircuit(Graphics g, int w, int h, bool open, float cut)
        {
            var app = ZoneApp.I;
            if (!IsActive) return;
            float t = EaseOut(HoverProgress(300));
            DrawSpeedStreaks(g, w, Color.FromArgb(150, Style.TitleColor), 1100);
            if (!open || h <= titleH) return;
            // Quelques grains de sable emportés par le vent
            float now = Clock.ElapsedMilliseconds % 1000000L;
            using (var shape = ShapePath(new RectangleF(0, 0, w, h), cut, true))
            {
                g.SetClip(shape);
                float gw = Math.Max(2f, app.S(3)), gh = Math.Max(1f, app.S(3) / 2f);
                for (int k = 0; k < 10; k++)
                {
                    float dur = 1800 + 1400 * Hash(k, 21), p = ((now + Hash(k, 22) * 3000) % dur) / dur;
                    float x = w * (0.6f + 0.45f * Hash(k, 23)) - app.S(220) * p;
                    float y = titleH + (h - titleH) * (0.15f + 0.8f * Hash(k, 24)) + app.S(18) * p;
                    float a = p < 0.15f ? p / 0.15f : 1f - (p - 0.15f) / 0.85f;
                    using (var b = new SolidBrush(Color.FromArgb((int)(150 * a * t), 160, 100, 50))) g.FillRectangle(b, x, y, gw, gh);
                }
                g.ResetClip();
            }
        }

        // ===== Exil : anthracite, orbites cuivrées, capitales très espacées soulignées, pastilles rondes =====

        float exilTextX, exilTextW;

        float ExilUnderlineY { get { return titleH - ZoneApp.I.S(9); } }

        PointF ExilOrbitCenter(int w, int h) { return new PointF(w * 0.62f, titleH + (h - titleH) * 0.5f); }

        void DrawExilUnderline(Graphics g, float k)
        {
            var app = ZoneApp.I;
            float full = Math.Max(app.S(20), exilTextW * 0.6f), uw = full * k, uh = Math.Max(2, app.S(3));
            if (uw < uh) return;
            float cx = exilTextX + exilTextW / 2f;
            using (var p = RoundRect(new RectangleF(cx - uw / 2, ExilUnderlineY, uw, uh), uh / 2, true))
            using (var b = new SolidBrush(Color.FromArgb(Style.TitleAlpha, Style.TitleColor))) g.FillPath(b, p);
        }

        void TitleExil(Graphics g, int w, bool open, float right)
        {
            var app = ZoneApp.I;
            Color white = Style.TitleTextColor;
            float mid = titleH / 2f - app.S(2);
            // Compteur et jauge cuivrée (pleine à 12 éléments)
            if (Style.ShowCount)
                using (var cf = new Font(titleFont.FontFamily, titleFont.SizeInPoints * 1.15f, FontStyle.Bold, GraphicsUnit.Point))
                {
                    float barW = app.S(70), barH = Math.Max(3, app.S(4)), by = mid - barH / 2, bx = right - barW - app.S(6);
                    using (var p = new Pen(Color.FromArgb(90, white)))
                    {
                        g.DrawLine(p, bx - app.S(5), mid - app.S(6), bx - app.S(5), mid + app.S(6));
                        g.DrawLine(p, bx + barW + app.S(5), mid - app.S(6), bx + barW + app.S(5), mid + app.S(6));
                    }
                    using (var tp = RoundRect(new RectangleF(bx, by, barW, barH), barH / 2, true))
                    using (var b = new SolidBrush(Color.FromArgb(22, white))) g.FillPath(b, tp);
                    float fw = barW * Math.Min(1f, items.Count / 12f);
                    if (fw >= barH)
                        using (var fp = RoundRect(new RectangleF(bx, by, fw, barH), barH / 2, true))
                        using (var b = new LinearGradientBrush(new RectangleF(bx - 1, by, fw + 2, barH), Color.FromArgb(184, 101, 47), Lighten(Style.TitleColor, 30), 0f))
                            g.FillPath(b, fp);
                    string cnt = items.Count.ToString("00");
                    float cw = DrawTracked(g, cnt, cf, null, 0, 0, 0, float.MaxValue);
                    using (var b = new SolidBrush(white)) DrawTracked(g, cnt, cf, b, bx - app.S(12) - cw, mid, 0, bx);
                    right = bx - app.S(24) - cw;
                }
            float track = titleFont.Size * 0.32f * app.Scale;
            exilTextX = app.S(18);
            using (var b = new SolidBrush(white))
                exilTextW = DrawTracked(g, ZoneName.ToUpperInvariant(), titleFont, b, exilTextX, mid, track, right);
            DrawExilUnderline(g, 0.35f); // au repos ; il s'étire au survol
        }

        void BackExil(Graphics g, int w, int h, GraphicsPath shape, bool open)
        {
            if (shape == null) return;
            var app = ZoneApp.I;
            if (!open || h <= titleH + app.S(40)) return;
            g.SetClip(shape);
            // Titre répété en filigrane géant
            using (var gf = new Font(titleFont.FontFamily, titleFont.SizeInPoints * 6.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (var b = new SolidBrush(Color.FromArgb(9, 255, 255, 255)))
                g.DrawString(ZoneName.ToUpperInvariant(), gf, b, -app.S(6), titleH + app.S(2));
            // Orbites et réticule de carte holographique
            Color o = Style.TitleColor;
            PointF c = ExilOrbitCenter(w, h);
            float r1 = app.S(250), r2 = app.S(210);
            using (var p = new Pen(Color.FromArgb(48, o))) g.DrawEllipse(p, c.X - r1, c.Y - r1, 2 * r1, 2 * r1);
            using (var p = new Pen(Color.FromArgb(40, o))) g.DrawEllipse(p, c.X - r2, c.Y - r2, 2 * r2, 2 * r2);
            DrawExilDashedOrbit(g, c, 0f, 34);
            using (var p = new Pen(Color.FromArgb(26, o)))
            {
                g.DrawLine(p, 0, c.Y, w, c.Y);
                g.DrawLine(p, c.X, titleH, c.X, h);
            }
            float tk = app.S(200);
            using (var p = new Pen(Color.FromArgb(90, o)))
            {
                g.DrawLine(p, c.X - tk, c.Y - app.S(6), c.X - tk, c.Y + app.S(6));
                g.DrawLine(p, c.X + tk, c.Y - app.S(6), c.X + tk, c.Y + app.S(6));
            }
            g.ResetClip();
        }

        void DrawExilDashedOrbit(Graphics g, PointF c, float offset, int alpha)
        {
            float r3 = ZoneApp.I.S(170);
            using (var p = new Pen(Color.FromArgb(alpha, Style.TitleColor)) { DashPattern = new float[] { 2, 6 }, DashOffset = offset })
                g.DrawEllipse(p, c.X - r3, c.Y - r3, 2 * r3, 2 * r3);
        }

        void ItemExil(Graphics g, int i, Rectangle cell, bool sel, int isz, Brush lb, Brush sb, StringFormat sf)
        {
            var app = ZoneApp.I;
            bool hov = i == hover;
            var ir = IconRect(cell, isz);
            float d = isz + 2 * IconInset;
            var disc = new RectangleF(ir.X + isz / 2f - d / 2, ir.Y + isz / 2f - d / 2, d, d);
            if (hov) disc.Inflate(app.S(1), app.S(1));
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(disc);
                using (var b = new PathGradientBrush(p))
                {
                    b.CenterPoint = new PointF(disc.X + disc.Width * 0.4f, disc.Y + disc.Height * 0.35f);
                    b.CenterColor = Color.FromArgb(51, 51, 54);
                    b.SurroundColors = new[] { Color.FromArgb(31, 31, 33) };
                    g.FillPath(b, p);
                }
            }
            float rw = Math.Max(1.5f, app.S(2));
            if (hov)
                using (var p = new Pen(Color.FromArgb(46, 255, 255, 255), app.S(6))) g.DrawEllipse(p, RectangleF.Inflate(disc, app.S(2), app.S(2)));
            Color ring = sel ? Color.FromArgb(Style.TitleAlpha, Style.TitleColor) : hov ? Color.FromArgb(244, 242, 238) : Color.FromArgb(59, 59, 62);
            using (var p = new Pen(ring, rw)) g.DrawEllipse(p, RectangleF.Inflate(disc, -rw / 2, -rw / 2));
            if (items[i].Icon != null) g.DrawImage(items[i].Icon, ir);
            var lr = LabelRect(cell, ir);
            if (!hov) { DrawItemLabel(g, items[i].Name, lr, lb, sb, sf); return; }
            // Le nom survolé devient une pilule blanche
            using (var one = new StringFormat(sf))
            {
                one.FormatFlags |= StringFormatFlags.NoWrap;
                one.Trimming = StringTrimming.EllipsisCharacter;
                one.LineAlignment = StringAlignment.Center;
                SizeF ts = g.MeasureString(items[i].Name, labelFont, new SizeF(cell.Width - app.S(18), lineH * 1.5f), one);
                float pw = Math.Min(cell.Width - app.S(4), ts.Width + app.S(14)), ph = lineH + app.S(4);
                var pr = new RectangleF(cell.X + (cell.Width - pw) / 2f, lr.Y - app.S(2), pw, ph);
                using (var pp = RoundRect(pr, ph / 2, true))
                using (var b = new SolidBrush(Color.FromArgb(244, 242, 238))) g.FillPath(b, pp);
                using (var b = new SolidBrush(Color.FromArgb(18, 18, 20)))
                    g.DrawString(items[i].Name, labelFont, b, new RectangleF(pr.X + app.S(6), pr.Y, pr.Width - app.S(12), pr.Height), one);
            }
        }

        void FrontExil(Graphics g, int w, int h, float cut)
        {
            if (Style.BorderWidth <= 0) return;
            float bw = ZoneApp.I.S(Style.BorderWidth);
            using (var pen = new Pen(Color.FromArgb(Style.BorderAlpha, Style.BorderColor), bw))
            using (var bp = ShapePath(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), Math.Max(0, cut - bw / 2), true))
                g.DrawPath(pen, bp);
        }

        void AnimExil(Graphics g, int w, int h, bool open, float cut)
        {
            var app = ZoneApp.I;
            if (!IsActive) return;
            float t = EaseOut(HoverProgress(350));
            float now = Clock.ElapsedMilliseconds % 1000000L;
            // Le soulignement s'étire depuis le centre
            DrawExilUnderline(g, 0.35f + 0.65f * t);
            if (!open || h <= titleH + app.S(40)) return;
            using (var shape = ShapePath(new RectangleF(0, 0, w, h), cut, true))
            {
                g.SetClip(shape);
                g.IntersectClip(new Rectangle(0, titleH, w, h - titleH));
                PointF c = ExilOrbitCenter(w, h);
                Color o = Style.TitleColor;
                // Orbites qui tournent très lentement, en sens opposés
                DrawExilDashedOrbit(g, c, (now / 110000f * 2 * (float)Math.PI * app.S(170)) % 8f, (int)(34 + 40 * t));
                float r1 = app.S(250), r2 = app.S(210);
                float a1 = (float)(now / 70000.0 * 2 * Math.PI), a2 = -(float)(now / 110000.0 * 2 * Math.PI);
                Action<float, float, float, Color> dot = (r, a, size, col) =>
                {
                    float x = c.X + r * (float)Math.Cos(a), y = c.Y + r * (float)Math.Sin(a);
                    using (var b = new SolidBrush(col)) g.FillEllipse(b, x - size, y - size, 2 * size, 2 * size);
                };
                dot(r1, a1, app.S(4), Color.FromArgb((int)(220 * t), o));
                dot(r1, a1 + (float)Math.PI, app.S(3), Color.FromArgb((int)(160 * t), o));
                dot(r2, a2 - (float)Math.PI / 2, app.S(5), Color.FromArgb((int)(200 * t), o));
                dot(r2, a2 + (float)Math.PI / 4, Math.Max(2f, app.S(5) / 2f), Color.FromArgb((int)(150 * t), 232, 226, 216));
                g.ResetClip();
            }
            // Bande cuivrée qui balaie la zone, comme une carte holographique
            DrawScanBand(g, w, h, cut, Style.TitleColor, 4500, 70);
        }

        // ---------- Rendu ----------

        public void Render()
        {
            if (!IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0) return;
            RenderStatic();
            Compose();
        }

        // Couche fixe : redessinée seulement quand quelque chose change (survol d'une icône, contenu, réglages…).
        void RenderStatic()
        {
            var app = ZoneApp.I;
            int w = Width, h = Height;
            if (layer == null || layer.Width != w || layer.Height != h)
            {
                if (layer != null) layer.Dispose();
                layer = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            }
            int theme = Style.Theme;
            bool open = IsOpen;
            float cut = app.S(Style.CornerRadius);
            using (var g = Graphics.FromImage(layer))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                // --- Fond ---
                GraphicsPath shape;
                if (theme == 2)
                {
                    // Night City : rangée de titre invisible (mais cliquable) + panneau sous l'onglet
                    using (var hb = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(hb, 0, 0, w, titleH);
                    shape = h > titleH ? Chamfer(new RectangleF(0, titleH, w, h - titleH), cut, false, false, true, false) : null;
                }
                else if (theme == 7)
                {
                    // Veillée : onglet de titre posé sur le panneau (rangée de titre cliquable mais invisible)
                    using (var hb = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(hb, 0, 0, w, titleH);
                    shape = h > titleH ? ShapePath(new RectangleF(0, titleH, w, h - titleH), cut, true) : null;
                }
                else shape = ShapePath(new RectangleF(0, 0, w, h), cut, true);
                if (shape != null)
                {
                    using (var b = new SolidBrush(Color.FromArgb(Math.Max(1, Style.BgAlpha), Style.BgColor))) g.FillPath(b, shape);
                    if (bgCache != null)
                        using (var tb = new TextureBrush(bgCache, WrapMode.Clamp))
                        {
                            int th = open ? h : ExpandedHeight;
                            if (bgCache.Width != w || bgCache.Height != th) tb.ScaleTransform((float)w / bgCache.Width, (float)th / bgCache.Height);
                            g.FillPath(tb, shape);
                        }
                }

                if (theme >= 4) DrawDecorBack(g, w, h, shape, open);

                // --- Barre de titre ---
                float yMid = titleH / 2f;
                Rectangle gr = GearRect;
                float right = gr.X - app.S(4);
                if (theme == 0)
                {
                    using (var tp = RoundRect(new RectangleF(0, 0, w, titleH), cut, !open))
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
                    }
                }
                else if (theme >= 4) DrawTitleThemed(g, w, open, gr);
                else
                {
                    string name = ZoneName.ToUpperInvariant();
                    float track = Math.Max(1f, titleFont.Size * 0.18f) * app.Scale;
                    if (theme == 1 && Style.TitleAlpha > 0)
                        using (var tp = ShapePath(new RectangleF(0, 0, w, titleH), cut, !open))
                        using (var b = new SolidBrush(Color.FromArgb(Style.TitleAlpha, Style.TitleColor))) g.FillPath(b, tp);

                    // Compteur à droite (Night City : « // 08 ITEMS » ; Studio : « 8 éléments » ; Néon : seulement replié)
                    string count = null;
                    if (theme == 2) count = "// " + items.Count.ToString("00") + (items.Count > 1 ? " ITEMS" : " ITEM");
                    else if (theme == 3) count = items.Count + (items.Count > 1 ? " éléments" : " élément");
                    else if (Collapsed && Style.ShowCount) count = "(" + items.Count + ")";
                    if (count != null)
                    {
                        using (var cf = new Font(labelFont.FontFamily, Math.Max(7f, titleFont.SizeInPoints * 0.82f), FontStyle.Regular, GraphicsUnit.Point))
                        {
                            Color cc = theme == 2 ? Style.BorderColor : Color.FromArgb(150, Style.TitleTextColor);
                            float cw = DrawTracked(g, count, cf, null, 0, yMid, theme == 2 ? track : 0, float.MaxValue);
                            float cx = right - cw;
                            using (var cb = new SolidBrush(cc)) DrawTracked(g, count, cf, cb, cx, yMid, theme == 2 ? track : 0, right);
                            right = cx - app.S(8);
                        }
                    }

                    float x0 = app.S(12) + (theme == 1 ? cut * 0.35f : 0);
                    Color txt = Style.TitleTextColor;
                    if (theme == 2)
                    {
                        // Onglet cyan biseauté
                        float slant = titleH * 0.75f;
                        float tw = DrawTracked(g, name, titleFont, null, 0, yMid, track, float.MaxValue);
                        float tabW = Math.Min(x0 + app.S(16) + tw + slant + app.S(6), right - app.S(4));
                        tabW = Math.Max(tabW, x0 + app.S(16) + slant + app.S(20));
                        using (var tab = new GraphicsPath())
                        {
                            tab.AddPolygon(new[] { new PointF(0, 0), new PointF(tabW - slant, 0), new PointF(tabW, titleH), new PointF(0, titleH) });
                            using (var b = new SolidBrush(Color.FromArgb(Style.TitleAlpha, Style.TitleColor))) g.FillPath(b, tab);
                        }
                        DrawChevron(g, x0, yMid, open, txt);
                        using (var b = new SolidBrush(txt)) DrawTracked(g, name, titleFont, b, x0 + app.S(16), yMid, track, tabW - slant);
                    }
                    else
                    {
                        float avail = right - x0 - app.S(16);
                        float tw = Math.Min(DrawTracked(g, name, titleFont, null, 0, yMid, track, float.MaxValue), avail);
                        float tx = x0 + app.S(16);
                        if (Style.TitleAlign == 1) tx = Math.Max(tx, (w - tw) / 2f);
                        else if (Style.TitleAlign == 2) tx = Math.Max(tx, right - tw);
                        DrawChevron(g, tx - app.S(16), yMid, open, Color.FromArgb(200, txt));
                        using (var b = new SolidBrush(txt)) DrawTracked(g, name, titleFont, b, tx, yMid, track, right);
                    }
                }

                // --- Roue crantée ---
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    Color gc = theme == 2 ? Style.TitleColor : Style.TitleTextColor;
                    if (hoverGear)
                    {
                        int d = (int)(titleH * 0.72);
                        using (var hb = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
                            g.FillEllipse(hb, gr.X + (gr.Width - d) / 2, gr.Y + (gr.Height - d) / 2, d, d);
                    }
                    int ga = hoverGear ? 255 : mouseInside ? 190 : 110;
                    using (var gb = new SolidBrush(Color.FromArgb(ga, gc)))
                        g.DrawString(gearGlyph, gearFont, gb, new RectangleF(gr.X, gr.Y + 1, gr.Width, gr.Height), sf);
                }

                // --- Décor fixe Night City (lignes de balayage, contour, crochets) ---
                if (theme == 2 && shape != null)
                {
                    g.SetClip(shape);
                    using (var sp = new Pen(Color.FromArgb(14, Style.TitleColor)))
                        for (int y = titleH + 2; y < h; y += Math.Max(2, app.S(3))) g.DrawLine(sp, 0, y, w, y);
                    g.ResetClip();
                    using (var op = new Pen(Color.FromArgb(150, Style.TitleColor), Math.Max(1, app.S(Style.BorderWidth))))
                    using (var ip = Chamfer(new RectangleF(0.5f, titleH, w - 1, h - titleH - 0.5f), cut, false, false, true, false))
                        g.DrawPath(op, ip);
                    using (var tp = new Pen(Style.TitleColor, app.S(2))) g.DrawLine(tp, 0, titleH + app.S(1), w, titleH + app.S(1));
                    float bl = app.S(10), bwid = app.S(2);
                    using (var mp = new Pen(Color.FromArgb(Style.BorderAlpha, Style.BorderColor), bwid))
                    {
                        g.DrawLines(mp, new[] { new PointF(bwid / 2, h - bl), new PointF(bwid / 2, h - bwid / 2), new PointF(bl, h - bwid / 2) });
                        float c2 = Math.Min(cut, Math.Min(w, h - titleH) / 2f);
                        g.DrawLine(mp, w - c2 + bwid, h - bwid / 2, w - bwid / 2, h - c2 + bwid);
                    }
                }
                if (theme == 2)
                    using (var mp = new Pen(Color.FromArgb(Style.BorderAlpha, Style.BorderColor), app.S(2)))
                    {
                        float bl = app.S(10), o = app.S(1);
                        g.DrawLines(mp, new[] { new PointF(o, bl), new PointF(o, o), new PointF(bl, o) });
                    }

                // --- Icônes ---
                if (open)
                {
                    Rectangle cr = ContentRect;
                    g.SetClip(cr);
                    int isz = app.S(Style.IconSize);
                    bool neonHover = theme == 1 || theme == 2;
                    Color acc = AccentColor;
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
                            if (theme >= 4) { DrawItemThemed(g, i, cell, sel, isz, lb, sb, sf); continue; }
                            var ir = new Rectangle(cell.X + (cell.Width - isz) / 2, cell.Y + app.S(6), isz, isz);
                            if (sel || (i == hover && !neonHover))
                            {
                                using (var hp = RoundRect(HitRect(i), app.S(5), true))
                                {
                                    Color fill = !sel ? Color.FromArgb(32, 255, 255, 255)
                                        : neonHover ? Color.FromArgb(i == hover ? 70 : 50, acc) : Color.FromArgb(i == hover ? 120 : 95, 0, 120, 215);
                                    using (var hb = new SolidBrush(fill)) g.FillPath(hb, hp);
                                    if (sel)
                                        using (var pen = new Pen(neonHover ? Color.FromArgb(210, acc) : Color.FromArgb(150, 90, 170, 255))) g.DrawPath(pen, hp);
                                }
                            }
                            if (i == hover && neonHover)
                            {
                                // Halo néon autour de l'icône survolée
                                var hr = Rectangle.Inflate(ir, app.S(4), app.S(4));
                                using (var hp = RoundRect(hr, app.S(7), true))
                                {
                                    using (var glow = new Pen(Color.FromArgb(70, acc), app.S(6))) g.DrawPath(glow, hp);
                                    using (var line = new Pen(Color.FromArgb(235, 255, 255, 255), app.S(2))) g.DrawPath(line, hp);
                                }
                            }
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
                    // Repère d'insertion (rangement libre par glisser-déposer)
                    if (dropIndex >= 0)
                    {
                        Rectangle refCell;
                        int xLine;
                        if (items.Count == 0) { refCell = new Rectangle(cr.X, cr.Y, lastCw, lastCellH); xLine = cr.X + app.S(2); }
                        else if (dropIndex < items.Count)
                        {
                            refCell = CellRect(dropIndex);
                            xLine = refCell.X;
                            if (dropIndex % lastCols == 0) xLine += app.S(2);
                        }
                        else { refCell = CellRect(items.Count - 1); xLine = refCell.Right - app.S(2); }
                        using (var pen = new Pen(Color.FromArgb(240, 60, 150, 255), app.S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(pen, xLine, refCell.Y + app.S(6), xLine, refCell.Bottom - app.S(6));
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

                // --- Bordures fixes ---
                if (theme >= 4) DrawDecorFront(g, w, h, open, cut);
                if ((theme == 0 || theme == 3) && Style.BorderWidth > 0)
                {
                    float bw = app.S(Style.BorderWidth);
                    using (var pen = new Pen(Color.FromArgb(Style.BorderAlpha, Style.BorderColor), bw))
                    using (var bp = ShapePath(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), Math.Max(0, cut - bw / 2), true))
                        g.DrawPath(pen, bp);
                }
                if (dropHover)
                {
                    float bw = app.S(2);
                    using (var pen = new Pen(Color.FromArgb(230, 0, 120, 215), bw))
                    using (var bp = ShapePath(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), Math.Max(0, cut - bw / 2), true))
                        g.DrawPath(pen, bp);
                }
                if (shape != null) shape.Dispose();
            }
        }

        // Assemble la couche fixe + les éléments animés, directement dans l'image affichée par Windows.
        void Compose()
        {
            if (!IsHandleCreated || IsDisposed || layer == null) return;
            int w = layer.Width, h = layer.Height;
            EnsureDib(w, h);
            if (dibBmp == null) return;
            using (var g = Graphics.FromImage(dibBmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(layer, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                DrawAnimated(g, w, h);
            }
            PushLayered();
        }

        void DrawAnimated(Graphics g, int w, int h)
        {
            var app = ZoneApp.I;
            int theme = Style.Theme;
            bool open = IsOpen;
            float cut = app.S(Style.CornerRadius);
            if (theme == 1)
            {
                // Néon RGB : ligne arc-en-ciel qui défile sous le titre + bordure arc-en-ciel qui tourne
                if (open)
                {
                    float x0 = app.S(12), len = Math.Max(1, w - 2 * x0), y = titleH - app.S(2);
                    using (var lb = new LinearGradientBrush(new RectangleF(x0, y, len, app.S(2)), Color.Black, Color.Black, 0f))
                    {
                        lb.InterpolationColors = RainbowBlend(255);
                        lb.WrapMode = WrapMode.Tile;
                        lb.TranslateTransform(animPhase * len, 0, MatrixOrder.Append);
                        g.FillRectangle(lb, x0, y, len, app.S(2));
                    }
                    using (var gl = new LinearGradientBrush(new RectangleF(x0, y, len, app.S(6)), Color.Black, Color.Black, 0f))
                    {
                        gl.InterpolationColors = RainbowBlend(55);
                        gl.WrapMode = WrapMode.Tile;
                        gl.TranslateTransform(animPhase * len, 0, MatrixOrder.Append);
                        g.FillRectangle(gl, x0, y + app.S(2), len, app.S(4));
                    }
                }
                if (Style.BorderWidth > 0)
                {
                    float bw = app.S(Style.BorderWidth);
                    var rect = new Rectangle(0, 0, w, h);
                    using (var shape = ShapePath(new RectangleF(0, 0, w, h), cut, true))
                    using (var bp = ShapePath(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), Math.Max(0, cut - bw / 2), true))
                    using (var glowB = new LinearGradientBrush(rect, Color.Black, Color.Black, animPhase * 360f))
                    using (var lineB = new LinearGradientBrush(rect, Color.Black, Color.Black, animPhase * 360f))
                    {
                        glowB.InterpolationColors = RainbowBlend(Math.Min(255, Style.BorderAlpha) * 60 / 255);
                        lineB.InterpolationColors = RainbowBlend(Style.BorderAlpha);
                        g.SetClip(shape);
                        using (var gp = new Pen(glowB, bw + app.S(6))) g.DrawPath(gp, bp);
                        g.ResetClip();
                        using (var lp = new Pen(lineB, bw)) g.DrawPath(lp, bp);
                    }
                }
            }
            else if (theme == 2 && open)
            {
                if (animTimer.Enabled && h > titleH + 4)
                {
                    using (var panel = Chamfer(new RectangleF(0, titleH, w, h - titleH), cut, false, false, true, false))
                    {
                        g.SetClip(panel);
                        long now = Clock.ElapsedMilliseconds;
                        // Bande de rafraîchissement (écran CRT) qui descend en continu
                        float ph = (now % 3200) / 3200f, band = app.S(46);
                        float by = titleH - band + ph * (h - titleH + band * 2);
                        using (var rb = new LinearGradientBrush(new RectangleF(0, by - band / 2, w, band), Color.Transparent, Color.Transparent, 90f))
                        {
                            var cb = new ColorBlend(3);
                            cb.Colors = new[] { Color.FromArgb(0, Style.TitleColor), Color.FromArgb(34, Style.TitleColor), Color.FromArgb(0, Style.TitleColor) };
                            cb.Positions = new[] { 0f, 0.5f, 1f };
                            rb.InterpolationColors = cb;
                            g.FillRectangle(rb, 0, by - band / 2, w, band);
                        }
                        using (var lp = new Pen(Color.FromArgb(60, Style.TitleColor))) g.DrawLine(lp, 0, by, w, by);

                        // Rafale de glitch : tranches décalées + fantômes magenta / cyan
                        if (now < glitchUntil && layer != null)
                        {
                            var rnd = glitchRnd;
                            if (rnd.Next(3) == 0)
                                using (var fl = new SolidBrush(Color.FromArgb(16, Style.TitleColor))) g.FillRectangle(fl, 0, titleH, w, h - titleH);
                            int n = 3 + rnd.Next(4);
                            for (int k = 0; k < n; k++)
                            {
                                int sh = app.S(3) + rnd.Next(app.S(14));
                                int sy = titleH + 2 + rnd.Next(Math.Max(1, h - titleH - sh - 2));
                                int dx = (rnd.Next(2) == 0 ? -1 : 1) * (app.S(4) + rnd.Next(app.S(14)));
                                int split = app.S(2) + rnd.Next(app.S(3));
                                g.CompositingMode = CompositingMode.SourceCopy;
                                g.DrawImage(layer, new Rectangle(dx, sy, w, sh), 0, sy, w, sh, GraphicsUnit.Pixel);
                                g.CompositingMode = CompositingMode.SourceOver;
                                g.DrawImage(layer, new Rectangle(dx - split, sy, w, sh), 0, sy, w, sh, GraphicsUnit.Pixel, GhostCyan);
                                g.DrawImage(layer, new Rectangle(dx + split, sy, w, sh), 0, sy, w, sh, GraphicsUnit.Pixel, GhostMagenta);
                            }
                        }
                        g.ResetClip();
                    }
                }
                // Night City : faisceau magenta qui balaie le haut du panneau
                float seg = Math.Max(20, w * 0.3f), x = -seg + animPhase * (w + seg), y = titleH;
                using (var pb = new LinearGradientBrush(new RectangleF(x, y, seg, app.S(2)), Color.Transparent, Color.Transparent, 0f))
                {
                    var cb = new ColorBlend(3);
                    cb.Colors = new[] { Color.FromArgb(0, Style.BorderColor), Color.FromArgb(255, Style.BorderColor), Color.FromArgb(0, Style.BorderColor) };
                    cb.Positions = new[] { 0f, 0.5f, 1f };
                    pb.InterpolationColors = cb;
                    g.FillRectangle(pb, x, y, seg, app.S(2));
                }
            }
            else if (theme >= 4) DrawAnimatedThemed(g, w, h, open, cut);
            else if (theme == 3)
            {
                // Studio : liseré du haut dont la couleur évolue lentement
                Color c = HueShift(Style.TitleColor, animPhase * 360f);
                using (var shape = ShapePath(new RectangleF(0, 0, w, h), cut, true))
                {
                    g.SetClip(shape);
                    using (var b = new SolidBrush(Color.FromArgb(Style.TitleAlpha, c))) g.FillRectangle(b, 0, 0, w, app.S(3));
                    using (var fade = new LinearGradientBrush(new RectangleF(0, app.S(3), w, app.S(10)), Color.FromArgb(Style.TitleAlpha * 70 / 255, c), Color.FromArgb(0, c), 90f))
                        g.FillRectangle(fade, 0, app.S(3), w, app.S(10));
                    g.ResetClip();
                }
            }
        }

        void EnsureDib(int w, int h)
        {
            if (dib != IntPtr.Zero && dibW == w && dibH == h) return;
            FreeDib();
            var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            dibDC = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            dib = Native.CreateDIBSection(dibDC, ref bi, 0, out dibBits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || dibBits == IntPtr.Zero) { Native.DeleteDC(dibDC); dibDC = IntPtr.Zero; dib = IntPtr.Zero; return; }
            dibOld = Native.SelectObject(dibDC, dib);
            dibBmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, dibBits);
            dibW = w; dibH = h;
        }

        void FreeDib()
        {
            if (dibBmp != null) { dibBmp.Dispose(); dibBmp = null; }
            if (dibDC != IntPtr.Zero)
            {
                if (dibOld != IntPtr.Zero) Native.SelectObject(dibDC, dibOld);
                Native.DeleteDC(dibDC);
            }
            if (dib != IntPtr.Zero) Native.DeleteObject(dib);
            dib = dibDC = dibOld = dibBits = IntPtr.Zero;
            dibW = dibH = 0;
        }

        void PushLayered()
        {
            if (dibDC == IntPtr.Zero || !IsHandleCreated) return;
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            try
            {
                var size = new Native.SIZE { cx = dibW, cy = dibH };
                var src = new Native.POINT();
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = (byte)curAlpha, AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, dibDC, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
            }
            finally { Native.ReleaseDC(IntPtr.Zero, screen); }
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
                        Created = SafeCreated(p),
                        Icon = ic
                    });
                }
            }
            catch { }
            SortItems();
            selected.RemoveWhere(s => !items.Exists(it => string.Equals(it.FullPath, s, StringComparison.OrdinalIgnoreCase)));
            hover = -1;
            LayoutItems();
            Render();
        }

        static DateTime SafeCreated(string p)
        {
            try { return File.GetCreationTime(p); } catch { return DateTime.MinValue; }
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
            int top = c.Y + app.S(6) + app.S(Style.IconSize) + app.S(2) + 2 * IconInset;
            var r = new Rectangle(PointToScreen(new Point(c.X + app.S(2), top)), new Size(c.Width - app.S(4), 2 * lineH + app.S(8)));
            string path = items[i].FullPath, shown = items[i].Name;
            var box = new RenameBox(shown, labelFont, r);
            box.Committed += delegate (string newName) { RenameItem(path, shown, newName); };
            box.FormClosed += delegate { renaming = false; RefreshMouseInside(); };
            renaming = true;
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
                int oi = order.FindIndex(n => string.Equals(n, Path.GetFileName(old), StringComparison.OrdinalIgnoreCase));
                if (oi >= 0) order[oi] = Path.GetFileName(np); // garde sa place en mode libre
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
            UpdateFade();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (drag != DragMode.None) return;
            mouseInside = false; hover = -1; hoverGear = false;
            SetTip("");
            Render();
            UpdateFade();
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
            if (!menu.Visible) RefreshMouseInside();
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
            selfDragging = true;
            try { DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            finally { selfDragging = false; dropIndex = -1; }
            QueueReload();
            RefreshMouseInside();
        }

        void UpdateDropIndex(DragEventArgs e)
        {
            int idx = -1;
            if (IsOpen && (selfDragging || Style.SortMode == 2))
                idx = DropIndexAt(PointToClient(new Point(e.X, e.Y)));
            if (idx != dropIndex) { dropIndex = idx; Render(); }
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
            e.Effect = selfDragging ? DragDropEffects.Move : EffectFor(e);
            if (e.Effect == DragDropEffects.None) return;
            StartPeek();
            dropHover = !selfDragging; // bordure bleue seulement pour ce qui vient de l'extérieur
            UpdateDropIndex(e);
            Render();
            UpdateFade();
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            e.Effect = selfDragging ? DragDropEffects.Move : EffectFor(e);
            UpdateDropIndex(e);
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            dropHover = false;
            dropIndex = -1;
            Render();
            UpdateFade();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            int idx = dropIndex >= 0 ? dropIndex : items.Count;
            dropHover = false;
            dropIndex = -1;
            Render();
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            if (selfDragging)
            {
                // Glisser à l'intérieur de la zone : on range l'icône à cet endroit (passe en tri libre).
                ReorderTo(files, idx);
                return;
            }
            bool copy = e.Effect == DragDropEffects.Copy;
            bool free = Style.SortMode == 2;
            BeginInvoke(new Action(delegate // hors du glisser-déposer pour libérer la source
            {
                var dests = ZoneApp.I.MoveInto(files, Folder, copy);
                Reload();
                if (free && dests.Count > 0) ReorderTo(dests, idx);
                UpdateFade();
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
            var sortMenu = new ToolStripMenuItem("Trier les icônes");
            string[] sortNames = { "Par nom", "Par date de création", "Libre (glisser pour ranger)" };
            for (int m = 0; m < 3; m++)
            {
                int mode = m;
                var mi = new ToolStripMenuItem(sortNames[m], null, delegate { SetSort(mode, mode == 2 ? false : Style.SortDesc); });
                mi.Checked = Style.SortMode == m;
                sortMenu.DropDownItems.Add(mi);
            }
            sortMenu.DropDownItems.Add(new ToolStripSeparator());
            var rev = new ToolStripMenuItem("Ordre inverse", null, delegate { SetSort(Style.SortMode, !Style.SortDesc); });
            rev.Checked = Style.SortDesc;
            rev.Enabled = Style.SortMode != 2;
            sortMenu.DropDownItems.Add(rev);
            menu.Items.Add(sortMenu);
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
            settingsForm.FormClosed += delegate { settingsForm = null; RefreshMouseInside(); };
            settingsForm.Show();
            UpdateFade();
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
        ComboBox cmbMode, cmbAlign, cmbPreset, cmbAnim;
        FontButton fbTitle, fbLabel;
        CheckBox chkCount, chkShadow, chkSortDesc;
        Slider slIdle;
        ComboBox cmbSort;
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
            ClientSize = new Size(S(520), S(600));

            var tabs = new TabControl { Location = new Point(S(10), S(10)), Size = new Size(S(500), S(522)) };
            Controls.Add(tabs);

            // --- Général ---
            var t = NewTab(tabs, "Général");
            tbName = new TextBox { Width = S(260) };
            Row(t, "Nom de la zone", tbName);
            cmbPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(180) };
            cmbPreset.Items.AddRange(ZoneStyle.PresetNames);
            Row(t, "Style prédéfini", cmbPreset);
            cmbAnim = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(180) };
            cmbAnim.Items.AddRange(new object[] { "Désactivée", "Au survol", "Toujours (lente)" });
            Row(t, "Animation RGB", cmbAnim);
            Note(t, "Le style prédéfini applique couleurs, polices et forme : Néon RGB, Night City, Studio ou Classique. La taille des icônes, le tri et l'opacité au repos sont conservés, et tout reste personnalisable ensuite.");
            nuRadius = new NumericUpDown { Minimum = 0, Maximum = 40, Width = S(70) };
            Row(t, "Coins : arrondi / coupe (px)", nuRadius);
            nuBorder = new NumericUpDown { Minimum = 0, Maximum = 10, Width = S(70) };
            Row(t, "Épaisseur de bordure", nuBorder);
            cbBorder = new ColorButton();
            Row(t, "Couleur de bordure", cbBorder);
            slBorderAlpha = new Slider(0, 100, Pct);
            Row(t, "Opacité de bordure", slBorderAlpha);
            slIdle = new Slider(10, 100, Pct);
            Row(t, "Opacité sans la souris", slIdle);
            Note(t, "Quand la souris quitte la zone, toute la zone (fond, icônes, textes) passe à ce pourcentage de son opacité normale, avec un fondu rapide. 100 % = pas d'effet.");

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
            Note(t, "Astuce : opacité du fond à 0 % + une image = image seule. L'opacité ne touche que le fond : icônes et textes restent nets.");

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
            cmbSort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(220) };
            cmbSort.Items.AddRange(new object[] { "Par nom (alphabétique)", "Par date de création", "Libre (glisser pour ranger)" });
            Row(t, "Tri des icônes", cmbSort);
            chkSortDesc = new CheckBox { Text = "Ordre inverse (Z → A, ou plus anciens d'abord)", AutoSize = true };
            Row(t, "", chkSortDesc);

            // --- Boutons ---
            int by = S(544);
            var btnAll = new Button { Text = "Appliquer à toutes les zones", AutoSize = true, Location = new Point(S(10), by) };
            var btnReset = new Button { Text = "Réinitialiser", AutoSize = true, Location = new Point(S(200), by) };
            var btnOk = new Button { Text = "OK", Size = new Size(S(80), S(28)), Location = new Point(S(342), by) };
            var btnCancel = new Button { Text = "Annuler", Size = new Size(S(80), S(28)), Location = new Point(S(430), by), DialogResult = DialogResult.Cancel };
            lblInfo = new Label { AutoSize = true, ForeColor = Color.SeaGreen, Location = new Point(S(12), S(578)) };
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
            cmbAnim.SelectedIndexChanged += changed;
            cmbPreset.SelectedIndexChanged += delegate
            {
                if (loading) return;
                ReadControls();
                st = ZoneStyle.Preset(cmbPreset.SelectedIndex, st);
                LoadControls();
                zone.ApplyStyle(st.Clone());
                lblInfo.Text = "";
            };
            foreach (var c in new[] { cbBorder, cbBg, cbTitleText, cbTitleBar, cbLabel }) c.ValueChanged += changed;
            foreach (var s in new[] { slBorderAlpha, slBgAlpha, slImgAlpha, slTitleAlpha, slIcon, slSpacing, slIdle }) s.ValueChanged += changed;
            cmbSort.SelectedIndexChanged += changed; chkSortDesc.CheckedChanged += changed;
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
            cmbPreset.SelectedIndex = st.Theme;
            cmbAnim.SelectedIndex = st.RgbAnim;
            cmbAnim.Enabled = st.Theme != 0;
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
            slIdle.Value = st.IdleOpacity;
            cmbSort.SelectedIndex = st.SortMode;
            chkSortDesc.Checked = st.SortDesc;
            chkSortDesc.Enabled = st.SortMode != 2;
            loading = false;
        }

        void ReadControls()
        {
            st.Theme = Math.Max(0, cmbPreset.SelectedIndex);
            st.RgbAnim = Math.Max(0, cmbAnim.SelectedIndex);
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
            st.IdleOpacity = slIdle.Value;
            st.SortMode = Math.Max(0, cmbSort.SelectedIndex);
            st.SortDesc = st.SortMode != 2 && chkSortDesc.Checked;
            chkSortDesc.Enabled = st.SortMode != 2;
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
            // Retour à la ligne automatique dans la largeur de l'onglet (le texte ne sort jamais du cadre)
            var l = new Label { Text = text, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, S(8), 0, S(6)),
                                MaximumSize = new Size(S(450), 0) };
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
