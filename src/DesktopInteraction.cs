using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClearDesk
{
    public static class DesktopWindows
    {
        [StructLayout(LayoutKind.Sequential)] public struct NativePoint { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLong64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetLong64(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetLong32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetLong32(IntPtr window, int index, int value);
        public static long ExtendedStyle(IntPtr window) { return IntPtr.Size == 8 ? GetLong64(window, -20).ToInt64() : GetLong32(window, -20); }
        public static void ToolWindow(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            long style = (ExtendedStyle(handle) | 0x80L) & ~0x40000L;
            if (IntPtr.Size == 8) SetLong64(handle, -20, new IntPtr(style)); else SetLong32(handle, -20, (int)style);
        }
        static string ClassName(IntPtr handle) { var name = new StringBuilder(256); GetClassName(handle, name, name.Capacity); return name.ToString(); }
        public static Point CursorPosition() { NativePoint point; GetCursorPos(out point); return new Point(point.X, point.Y); }
        public static bool CursorOnDesktop(out Point point)
        {
            NativePoint cursor; GetCursorPos(out cursor); point = new Point(cursor.X, cursor.Y);
            return IsDesktopPoint(point);
        }
        public static bool IsDesktopPoint(Point point)
        {
            IntPtr handle = WindowFromPoint(new NativePoint { X = (int)point.X, Y = (int)point.Y }); string root = ClassName(GetAncestor(handle, 2));
            return root == "Progman" || root == "WorkerW" || ClassName(handle) == "SHELLDLL_DefView";
        }
        public static Point LogicalPoint(Visual visual, Point native)
        {
            var source = PresentationSource.FromVisual(visual);
            return source == null || source.CompositionTarget == null ? native : source.CompositionTarget.TransformFromDevice.Transform(native);
        }
    }

    public static class EntryDrag
    {
        public static DataObject Create(DraggedEntry item)
        {
            var data = new DataObject(); data.SetData("ClearDesk.Entry", item, false);
            if (!item.Entry.IsShell && item.Entry.Exists)
            {
                data.SetData(DataFormats.FileDrop, new[] { item.Entry.Path });
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1))); // External default: copy.
            }
            return data;
        }
    }

    public static class DesktopTransfer
    {
        static bool Equal(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        public static void Extract(Settings state, DesktopOrganizer organizer, Zone source, Entry entry, string desktop, double x, double y, Action persist)
        {
            if (!entry.Exists) throw new IOException("文件已移动或删除，无法拖出。");
            if (!entry.IsShell)
            {
                bool restored = organizer != null && organizer.RestoreEntry(state, entry, persist);
                if (!restored && !Equal(Path.GetDirectoryName(entry.Path), Catalog.Normalize(desktop)))
                {
                    string destination = Path.Combine(desktop, Path.GetFileName(entry.Path));
                    if (File.Exists(destination) || Directory.Exists(destination))
                    {
                        int suffix = 2; string stem = Path.GetFileNameWithoutExtension(destination), ext = Path.GetExtension(destination);
                        do { destination = Path.Combine(desktop, stem + " (" + suffix++ + ")" + ext); } while (File.Exists(destination) || Directory.Exists(destination));
                    }
                    CheckLinks(entry.Path); Copy(entry.Path, destination); entry.Path = destination;
                }
            }
            foreach (Zone zone in state.Zones) zone.Items.RemoveAll(e => e == entry || Equal(e.Path, entry.Path));
            state.DesktopItems.RemoveAll(d => Equal(d.Entry.Path, entry.Path));
            state.DesktopItems.Add(new DesktopEntry { Entry = entry, X = x, Y = y });
            persist();
        }
        static void CheckLinks(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("链接或云端占位文件请先下载到本地再拖出。");
            if (Directory.Exists(path)) foreach (string child in Directory.EnumerateFileSystemEntries(path)) CheckLinks(child);
        }
        static void Copy(string source, string destination)
        {
            if (Directory.Exists(source))
            {
                Directory.CreateDirectory(destination);
                foreach (string child in Directory.EnumerateFileSystemEntries(source)) Copy(child, Path.Combine(destination, Path.GetFileName(child)));
            }
            else File.Copy(source, destination, false);
        }
    }

    public class DesktopItemWindow : Window
    {
        bool disposing;
        public DesktopItemWindow(DeskApp app, DesktopEntry model)
        {
            Title = "清桌桌面入口 · " + model.Entry.Name; Width = 90; Height = 108;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent;
            Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(model.X, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(model.Y, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
            Content = new ItemTile(app, null, model.Entry, true);
            SourceInitialized += delegate { DesktopWindows.ToolWindow(this); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!disposing) e.Cancel = true; };
        }
        public void DisposeWindow() { disposing = true; Close(); }
    }
}
