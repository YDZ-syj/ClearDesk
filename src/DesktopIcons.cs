using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ClearDesk
{
    public static class DesktopIconNative
    {
        delegate bool EnumCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        public static IntPtr Find()
        {
            IntPtr defView = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
                EnumWindows(delegate(IntPtr window, IntPtr unused)
                { defView = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null); return defView == IntPtr.Zero; }, IntPtr.Zero);
            return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }
        public static bool IsIconView(IntPtr window)
        {
            if (!IsWindow(window)) return false;
            var name = new StringBuilder(128); GetClassName(window, name, name.Capacity);
            return name.ToString() == "SysListView32";
        }
        public static void Hide(IntPtr window) { if (IsIconView(window)) ShowWindow(window, 0); }
        public static void Restore(IntPtr window)
        {
            // Respect a user's persistent Explorer preference. No registry values are written.
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced"))
                    if (key != null && Convert.ToInt32(key.GetValue("HideIcons", 0)) == 1) return;
            }
            catch { }
            if (!IsIconView(window)) window = Find();
            if (IsIconView(window)) ShowWindow(window, 4);
        }
    }

    public sealed class DesktopIconVisibility : IDisposable
    {
        IntPtr ownedWindow;
        EventWaitHandle stop;
        Process guard;
        public string Warning { get; private set; }
        public void Apply(bool hide)
        {
            if (!hide) { Release(); return; }
            IntPtr window = DesktopIconNative.Find();
            if (window == IntPtr.Zero) { Release(); Warning = "桌面图标层暂不可用，已保留原桌面图标。"; return; }
            if (ownedWindow != IntPtr.Zero && (window != ownedWindow || guard == null || guard.HasExited)) Release();
            if (ownedWindow == IntPtr.Zero)
            {
                if (!DesktopIconNative.IsWindowVisible(window)) { Warning = null; return; }
                string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClearDesk.IconGuard.exe");
                if (!File.Exists(helper)) { Warning = "缺少图标恢复保护程序，请使用完整发布包。原桌面图标仍显示。"; return; }
                string token = "Local\\ClearDesk.IconGuard." + Process.GetCurrentProcess().Id + "." + Guid.NewGuid().ToString("N");
                EventWaitHandle ready = null;
                try
                {
                    ready = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".ready");
                    stop = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".stop");
                    var parent = Process.GetCurrentProcess();
                    guard = Process.Start(new ProcessStartInfo(helper, parent.Id + " " + parent.StartTime.ToUniversalTime().Ticks + " " + window.ToInt64() + " " + token)
                    { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory });
                    if (!ready.WaitOne(2000) || guard == null || guard.HasExited) throw new IOException("图标恢复保护程序没有启动。");
                    ownedWindow = window; Warning = null;
                }
                catch (Exception ex) { Release(); Warning = ex.Message + " 已保留原桌面图标。"; return; }
                finally { if (ready != null) ready.Dispose(); }
            }
            DesktopIconNative.Hide(ownedWindow);
        }
        void Release()
        {
            if (ownedWindow != IntPtr.Zero) { DesktopIconNative.Restore(ownedWindow); ownedWindow = IntPtr.Zero; }
            if (stop != null) { stop.Set(); stop.Dispose(); stop = null; }
            if (guard != null) { guard.Dispose(); guard = null; }
        }
        public void Dispose() { Release(); }
    }

    public static class IconGuard
    {
        // This helper never hides anything. If the main process crashes, restore the original
        // icon view. A normal release signals .stop after restoring the view itself.
        public static void Main(string[] args)
        {
            if (args.Length != 4) return;
            IntPtr window = IntPtr.Zero; bool watching = false;
            try
            {
                int id = int.Parse(args[0]); long ticks = long.Parse(args[1]); window = new IntPtr(long.Parse(args[2]));
                if (!args[3].StartsWith("Local\\ClearDesk.IconGuard." + id + ".", StringComparison.Ordinal)) return;
                using (var parent = Process.GetProcessById(id))
                using (var ready = EventWaitHandle.OpenExisting(args[3] + ".ready"))
                using (var stop = EventWaitHandle.OpenExisting(args[3] + ".stop"))
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != ticks || !DesktopIconNative.IsIconView(window)) return;
                    watching = true; ready.Set();
                    while (!stop.WaitOne(500))
                    { if (parent.HasExited) { DesktopIconNative.Restore(window); watching = false; return; } }
                    watching = false;
                }
            }
            catch { if (watching) DesktopIconNative.Restore(window); }
        }
    }
}
