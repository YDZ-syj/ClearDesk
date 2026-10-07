using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ClearDesk
{
    public static class StandaloneTests
    {
        [DllImport("comctl32.dll")] static extern void InitCommonControls();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint style, string name, string title, uint windowStyle, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
        public static void Run(Action<bool, string> check, string root, string build)
        {
            string isolated = Path.Combine(root, "standalone-executable"); Directory.CreateDirectory(isolated);
            string executable = Path.Combine(isolated, "Standalone.exe"); File.Copy(Path.Combine(build, "ClearDesk.exe"), executable);
            InitCommonControls(); IntPtr view = CreateWindowEx(0x80, "SysListView32", "ClearDesk private guard test", 0x90000000, -20000, -20000, 20, 20, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (view == IntPtr.Zero) throw new Exception("Cannot create the private guard-test window.");
            Process guard = null, parent = null;
            try
            {
                parent = Process.GetCurrentProcess();
                string token = "Local\\ClearDesk.IconGuard." + parent.Id + "." + Guid.NewGuid().ToString("N");
                using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".ready"))
                using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".stop"))
                {
                    guard = StartGuard(executable, parent, view, token);
                    check(ready.WaitOne(10000) && !guard.HasExited && Directory.GetFiles(isolated).Length == 1, "one renamed EXE starts its independent guard without adjacent files or a normal app instance");
                    DesktopIconNative.Hide(view);
                    check(!DesktopIconNative.IsWindowVisible(view) && DesktopIconNative.Restore(view) && DesktopIconNative.IsWindowVisible(view), "restoration confirms visibility using a private test view rather than Explorer");
                    stop.Set(); if (!guard.WaitForExit(5000)) throw new Exception("Guard release timeout.");
                    check(guard.ExitCode == 0, "single-file guard exits cleanly after normal ownership release"); guard.Dispose(); guard = null;
                }
                parent.Dispose(); parent = null;
                parent = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep -Seconds 30") { UseShellExecute = false, CreateNoWindow = true });
                token = "Local\\ClearDesk.IconGuard." + parent.Id + "." + Guid.NewGuid().ToString("N");
                using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".ready"))
                using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".stop"))
                {
                    guard = StartGuard(executable, parent, view, token);
                    if (!ready.WaitOne(10000)) throw new Exception("Crash-test guard readiness timeout.");
                    DesktopIconNative.Hide(view); parent.Kill(); parent.WaitForExit();
                    if (!guard.WaitForExit(10000)) throw new Exception("Crash-test guard exit timeout.");
                    check(DesktopIconNative.IsWindowVisible(view) && guard.ExitCode == 0, "packaged single-file guard restores its owned private view after parent termination");
                }
            }
            finally
            {
                if (guard != null) { if (!guard.HasExited) guard.Kill(); guard.Dispose(); }
                if (parent != null) { if (parent.Id != Process.GetCurrentProcess().Id && !parent.HasExited) parent.Kill(); parent.Dispose(); }
                DestroyWindow(view);
            }
        }
        static Process StartGuard(string executable, Process parent, IntPtr view, string token)
        {
            return Process.Start(new ProcessStartInfo(executable, "--icon-guard " + parent.Id + " " + parent.StartTime.ToUniversalTime().Ticks + " " + view.ToInt64() + " " + token) { UseShellExecute = false, CreateNoWindow = true });
        }
    }
}
