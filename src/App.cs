using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ClearDesk
{
    public static class Theme
    {
        public static Brush Brush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        public static Brush Gray(double opacity) { return new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), 46, 46, 46)); }
        public static TextBlock Text(string value, double size, string color)
        { return new TextBlock { Text = value, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap }; }
        public static Button Button(string text, Action action, bool primary)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 9, 14, 9), Background = Brush(primary ? "#6875E8" : "#242A3C"), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            b.Click += delegate { action(); }; return b;
        }
        public static ResourceDictionary Resources()
        {
            return (ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<Style TargetType='Button'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' CornerRadius='8' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Opacity' Value='0.8'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='surface' Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='TextBox'><Setter Property='Background' Value='#202638'/><Setter Property='Foreground' Value='#EFF1FF'/><Setter Property='CaretBrush' Value='White'/><Setter Property='BorderBrush' Value='#39415C'/><Setter Property='Padding' Value='12,10'/><Setter Property='FontSize' Value='14'/></Style>
<Style TargetType='ToolTip'><Setter Property='Background' Value='#242A3C'/><Setter Property='Foreground' Value='White'/></Style>
<Style TargetType='MenuItem'><Setter Property='Padding' Value='12,7'/></Style>
</ResourceDictionary>");
        }
    }

    public class DeskApp : Application
    {
        public Settings State;
        public SettingsStore Store;
        public DesktopOrganizer Organizer;
        public string OrganizerWarning;
        public ManagerWindow Manager;
        readonly List<ZoneWindow> widgets = new List<ZoneWindow>();
        readonly List<DesktopItemWindow> desktopItems = new List<DesktopItemWindow>();
        Forms.NotifyIcon tray;
        bool quitting;
        public bool SaveFailed;
        public string StartupWarning;
        public IStartupRegistration StartupRegistration;
        public bool StartupSettingsAvailable;
        public string DesktopRoot = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string testDesktop;
        IEnumerable<string> DesktopRoots() { return testDesktop == null ? new[] { DesktopRoot, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) } : new[] { DesktopRoot }; }
        public IEnumerable<string> ScanDesktopPaths() { return Catalog.DesktopPaths(DesktopRoots()); }
        static Mutex instance;
        readonly DesktopIconVisibility desktopIcons = new DesktopIconVisibility();
        readonly List<FileSystemWatcher> desktopWatchers = new List<FileSystemWatcher>();
        DispatcherTimer desktopTimer;
        int desktopDirty;
        bool desktopInitialized;

        [STAThread]
        public static void Main(string[] args)
        {
            LaunchOptions options;
            try { options = LaunchOptions.Parse(args); }
            catch (Exception ex) { MessageBox.Show("无法启动清桌：" + ex.Message, "清桌"); return; }
            bool first;
            instance = new Mutex(true, "Local\\ClearDesk-" + Environment.UserName, out first);
            if (!first) { if (!options.AutoStart) MessageBox.Show("清桌已经在运行，请从系统托盘打开管理窗口。", "清桌"); instance.Dispose(); return; }
            DeskApp app = null;
            try
            {
                app = new DeskApp();
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                { MessageBox.Show("操作未完成：" + e.Exception.Message, "清桌", MessageBoxButton.OK, MessageBoxImage.Warning); e.Handled = true; };
                string profile = options.Profile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClearDesk");
                if (options.Desktop != null) app.DesktopRoot = app.testDesktop = options.Desktop;
                app.Initialize(profile);
                app.InitializeDesktop();
                app.Manager = new ManagerWindow(app); app.MainWindow = app.Manager;
                app.SetupTray();
                if (app.State.WidgetsVisible) app.RebuildWidgets();
                if (app.StartupWarning != null) MessageBox.Show(app.StartupWarning, "清桌 · 配置恢复");
                if (app.OrganizerWarning != null) MessageBox.Show(app.OrganizerWarning, "清桌 · 整理记录");
                if (options.AutoStart) app.Run(); else app.Run(app.Manager);
            }
            catch (Exception ex) { MessageBox.Show("无法启动清桌：" + ex.Message, "清桌"); }
            finally { if (app != null) app.DisposeDesktop(); instance.ReleaseMutex(); instance.Dispose(); }
        }
        public void Initialize(string directory)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Resources = Theme.Resources();
            StartupRegistration = new StartupRegistration(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string defaultProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClearDesk");
            StartupSettingsAvailable = testDesktop == null && string.Equals(Catalog.Normalize(directory), Catalog.Normalize(defaultProfile), StringComparison.OrdinalIgnoreCase);
            Store = new SettingsStore(Path.Combine(directory, "settings.json"));
            try { State = Store.Load(); }
            catch (Exception ex)
            {
                // Preserve unreadable data before any future save can replace it.
                string damaged = Store.FilePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
                File.Copy(Store.FilePath, damaged, false);
                try { State = SettingsStore.Read(Store.FilePath + ".bak"); StartupWarning = "配置读取失败，已从上一次备份恢复。原配置保存在：" + damaged; }
                catch { State = Settings.Default(); StartupWarning = "配置读取失败，已使用默认布局。原配置保存在：" + damaged + "\n原因：" + ex.Message; }
            }
            try
            {
                Organizer = new DesktopOrganizer(Path.Combine(directory, "moves.json"));
                var messages = Organizer.Recover(State, delegate { Store.Save(State); });
                if (messages.Count > 0) OrganizerWarning = string.Join("\n", messages.Take(5));
            }
            catch (Exception ex) { Organizer = null; OrganizerWarning = "整理历史无法读取或恢复，已停用文件移动和撤销。历史文件已保留。\n" + ex.Message; }
        }
        void SetupTray()
        {
            tray = new Forms.NotifyIcon { Text = "清桌 ClearDesk", Icon = AppBrand.TrayIcon, Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开管理窗口", null, delegate { Dispatcher.Invoke(new Action(ShowManager)); });
            menu.Items.Add("显示 / 隐藏分区", null, delegate { Dispatcher.Invoke(new Action(ToggleWidgets)); });
            menu.Items.Add("全部折叠", null, delegate { Dispatcher.Invoke(new Action(delegate { SetAllCollapsed(true); })); });
            menu.Items.Add("全部展开", null, delegate { Dispatcher.Invoke(new Action(delegate { SetAllCollapsed(false); })); });
            menu.Items.Add("退出清桌", null, delegate { Dispatcher.Invoke(new Action(Quit)); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(ShowManager)); };
        }
        public void ShowManager() { Manager.Show(); Manager.WindowState = WindowState.Normal; Manager.Activate(); }
        public bool Save()
        {
            try { Store.Save(State); SaveFailed = false; return true; }
            catch (Exception ex) { SaveFailed = true; MessageBox.Show("配置未保存，请检查磁盘空间和写入权限。\n" + ex.Message, "清桌", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        }
        public void Changed(bool rebuild)
        {
            Save(); if (Manager != null) Manager.Refresh();
            if (rebuild) RebuildWidgets(); else { foreach (ZoneWindow w in widgets) w.RefreshItems(); RefreshDesktopItems(); ApplyDesktopVisibility(); }
        }
        public void ToggleWidgets() { State.WidgetsVisible = !State.WidgetsVisible; Changed(true); }
        public void RebuildWidgets()
        {
            desktopIcons.Apply(false);
            foreach (ZoneWindow w in widgets) w.DisposeWindow(); widgets.Clear();
            foreach (DesktopItemWindow w in desktopItems) w.DisposeWindow(); desktopItems.Clear();
            if (!State.WidgetsVisible) return;
            try
            {
                foreach (Zone z in Catalog.VisibleZones(State)) { var w = new ZoneWindow(this, z); widgets.Add(w); w.Show(); }
                ApplyDesktopVisibility();
                RefreshDesktopItems();
            }
            catch { desktopIcons.Apply(false); throw; }
        }
        public void InitializeDesktop()
        {
            desktopInitialized = true;
            try { if (State.AutoClassifyDesktop) Catalog.Classify(State, ScanDesktopPaths()); }
            catch (Exception ex) { StartupWarning = (StartupWarning ?? "") + "\n桌面自动分类未完成：" + ex.Message; }
            Catalog.AddSystemEntries(State);
            Rect area = SystemParameters.WorkArea;
            ZoneLayout.PrepareForStartup(State, area.Left, area.Top, area.Width, area.Height);
            Save();
            foreach (string root in DesktopRoots().Distinct())
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    var watcher = new FileSystemWatcher(root) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName, IncludeSubdirectories = false };
                    FileSystemEventHandler mark = delegate { Interlocked.Exchange(ref desktopDirty, 1); };
                    watcher.Created += mark; watcher.Deleted += mark;
                    watcher.Renamed += delegate { Interlocked.Exchange(ref desktopDirty, 1); };
                    watcher.Error += delegate { Interlocked.Exchange(ref desktopDirty, 1); };
                    watcher.EnableRaisingEvents = true; desktopWatchers.Add(watcher);
                }
                catch { }
            }
            desktopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            desktopTimer.Tick += delegate
            {
                if (State.AutoClassifyDesktop && Interlocked.Exchange(ref desktopDirty, 0) != 0)
                {
                    try
                    {
                        var visibleBefore = Catalog.VisibleZones(State).Select(z => z.Id).ToArray();
                        int added = Catalog.Classify(State, ScanDesktopPaths());
                        if (added > 0)
                        {
                            bool layoutChanged = !visibleBefore.SequenceEqual(Catalog.VisibleZones(State).Select(z => z.Id));
                            if (layoutChanged && State.AutoArrangeZones) ArrangeZones(false);
                            Changed(true);
                        }
                    }
                    catch (Exception ex) { if (Manager != null) Manager.SetStatus("自动分类暂未完成：" + ex.Message); Interlocked.Exchange(ref desktopDirty, 1); }
                }
                ApplyDesktopVisibility();
            };
            desktopTimer.Start();
        }
        void ApplyDesktopVisibility()
        {
            if (!desktopInitialized) return;
            desktopIcons.Apply(State.HideDesktopIcons && State.WidgetsVisible && (widgets.Any(w => w.IsVisible) || State.DesktopItems.Count > 0));
            if (desktopIcons.Warning != null && Manager != null) Manager.SetStatus(desktopIcons.Warning);
        }
        void DisposeDesktop()
        {
            if (desktopTimer != null) desktopTimer.Stop();
            foreach (FileSystemWatcher watcher in desktopWatchers) watcher.Dispose(); desktopWatchers.Clear();
            desktopIcons.Dispose();
        }
        protected override void OnExit(ExitEventArgs e) { DisposeDesktop(); base.OnExit(e); }
        public void ArrangeZones(bool update)
        {
            Rect area = SystemParameters.WorkArea;
            ZoneLayout.Arrange(Catalog.VisibleZones(State), area.Left, area.Top, area.Width, area.Height);
            if (update) { foreach (ZoneWindow window in widgets) window.ApplyModelLayout(); Save(); }
        }
        public void OnZoneFoldChanged()
        {
            if (State.AutoArrangeZones) ArrangeZones(true); else Save();
            if (Manager != null) Manager.Refresh();
        }
        public void SetAllCollapsed(bool collapsed)
        {
            foreach (Zone zone in State.Zones) zone.Collapsed = collapsed;
            if (State.AutoArrangeZones) ArrangeZones(false);
            Changed(true);
        }
        public void RenameZone(Zone zone, Window owner)
        {
            var dialog = new RenameDialog(State, zone); dialog.Owner = owner;
            if (dialog.ShowDialog() == true) Changed(true);
        }
        public void Quit()
        {
            if (quitting) return;
            if (State.RestoreOnExit && !RestoreDesktop(false)) { ShowManager(); return; }
            if (!Save() && MessageBox.Show("配置尚未保存，仍然退出？", "清桌", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            quitting = true;
            DisposeDesktop();
            foreach (ZoneWindow w in widgets) w.DisposeWindow();
            foreach (DesktopItemWindow w in desktopItems) w.DisposeWindow();
            if (tray != null) tray.Dispose();
            Shutdown();
        }
        public bool Confirm(string text) { return MessageBox.Show(text, "清桌", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; }
        public bool RestoreDesktop(bool notify)
        {
            try
            {
                if (Organizer == null) { if (OrganizerWarning != null) throw new IOException(OrganizerWarning); return true; }
                List<string> errors = Organizer.UndoAll(State, delegate { Store.Save(State); });
                Changed(true);
                if (errors.Count > 0) { MessageBox.Show("部分文件暂未恢复，文件仍保留在收纳目录。请解决同名冲突后重试：\n\n" + string.Join("\n", errors.Take(8)), "恢复桌面未完成"); return false; }
                if (notify) MessageBox.Show("已整理的文件已恢复到原桌面位置，未覆盖其他文件。", "清桌");
                return true;
            }
            catch (Exception ex) { MessageBox.Show("恢复未完成，程序将保持打开。\n" + ex.Message, "清桌"); return false; }
        }
        void RefreshDesktopItems()
        {
            foreach (DesktopItemWindow w in desktopItems) w.DisposeWindow(); desktopItems.Clear();
            if (!desktopInitialized || !State.WidgetsVisible || !State.HideDesktopIcons) return;
            foreach (DesktopEntry item in State.DesktopItems) { var w = new DesktopItemWindow(this, item); desktopItems.Add(w); w.Show(); }
        }
        public void ExtractToDesktop(Zone zone, Entry entry, Point point)
        {
            try
            {
                DesktopTransfer.Extract(State, Organizer, zone, entry, DesktopRoot, point.X - 45, point.Y - 30, delegate { Store.Save(State); });
                Changed(true);
                if (Manager != null) Manager.SetStatus("已放回桌面；此入口不再自动收进分区。");
            }
            catch (Exception ex) { MessageBox.Show("未能拖出，原文件已保留。\n" + ex.Message, "清桌"); }
        }
        public void Open(Entry entry)
        {
            try
            {
                if (!entry.Exists) { MessageBox.Show("原文件已移动或删除。可以右键移除入口，再重新添加。\n" + entry.Path, "入口失效"); return; }
                if (entry.IsShell) { Process.Start("explorer.exe", entry.Path); return; }
                Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("无法打开：" + ex.Message, "清桌"); }
        }
        public void Reveal(Entry entry)
        {
            if (entry.IsShell) { Open(entry); return; }
            try { Process.Start("explorer.exe", "/select,\"" + entry.Path + "\""); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "清桌"); }
        }
        public ContextMenu EntryMenu(Zone zone, Entry entry)
        {
            var menu = new ContextMenu();
            AddMenu(menu, "打开", delegate { Open(entry); });
            AddMenu(menu, "在文件夹中显示", delegate { Reveal(entry); });
            AddMenu(menu, "复制路径", delegate { Clipboard.SetText(entry.Path); });
            var move = new MenuItem { Header = "移到分区" };
            foreach (Zone other in State.Zones.Where(z => z != zone))
            {
                Zone target = other;
                var item = new MenuItem { Header = other.Name };
                item.Click += delegate
                {
                    if (!target.Items.Any(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase))) target.Items.Add(entry);
                    if (zone != null) zone.Items.Remove(entry); else State.DesktopItems.RemoveAll(d => d.Entry == entry); Changed(false);
                };
                move.Items.Add(item);
            }
            menu.Items.Add(move);
            if (zone != null)
            {
                AddMenu(menu, "前移一位", delegate { int index = zone.Items.IndexOf(entry); if (index > 0) { Catalog.Place(zone, zone, entry, zone.Items[index - 1]); Changed(false); } });
                AddMenu(menu, "后移一位", delegate { int index = zone.Items.IndexOf(entry); if (index >= 0 && index < zone.Items.Count - 1) { Entry before = index + 2 < zone.Items.Count ? zone.Items[index + 2] : null; Catalog.Place(zone, zone, entry, before); Changed(false); } });
                AddMenu(menu, "放回桌面", delegate { ExtractToDesktop(zone, entry, new Point(SystemParameters.WorkArea.Left + 60, SystemParameters.WorkArea.Top + 60)); });
            }
            AddMenu(menu, "移除入口（保留原文件）", delegate { if (zone != null) zone.Items.Remove(entry); else State.DesktopItems.RemoveAll(d => d.Entry == entry); Changed(false); });
            return menu;
        }
        static void AddMenu(ContextMenu menu, string text, Action action)
        { var item = new MenuItem { Header = text }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        public void AddFiles(Zone zone)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "添加文件、快捷方式或程序", Filter = "所有文件|*.*", DereferenceLinks = false };
            if (dialog.ShowDialog() == true) { Catalog.Add(zone, dialog.FileNames); Changed(false); }
        }
        public void AddFolder(Zone zone)
        {
            using (var dialog = new Forms.FolderBrowserDialog { Description = "选择要加入分区的文件夹" })
                if (dialog.ShowDialog() == Forms.DialogResult.OK) { Catalog.Add(zone, new[] { dialog.SelectedPath }); Changed(false); }
        }
        public void Drop(Zone zone, DragEventArgs e, Entry before = null)
        {
            if (e.Data.GetDataPresent("ClearDesk.Entry"))
            {
                var data = (DraggedEntry)e.Data.GetData("ClearDesk.Entry");
                Catalog.Place(data.Zone, zone, data.Entry, before);
                State.DesktopItems.RemoveAll(d => d.Entry == data.Entry);
                data.InternalHandled = true; e.Effects = DragDropEffects.Move;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) Catalog.Add(zone, (string[])e.Data.GetData(DataFormats.FileDrop));
            else return;
            e.Handled = true; Changed(false);
        }
        public static void DragOver(object sender, DragEventArgs e)
        { e.Effects = e.Data.GetDataPresent("ClearDesk.Entry") ? DragDropEffects.Move : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; }
        public void EditZone(Zone zone, bool create)
        {
            var dialog = new ZoneDialog(zone, State); dialog.Owner = Manager;
            if (dialog.ShowDialog() != true) return;
            Catalog.Rename(State, zone, dialog.ZoneName); zone.Color = dialog.ZoneColor; zone.BackgroundOpacity = dialog.ZoneOpacity;
            if (create) State.Zones.Add(zone);
            if (State.AutoArrangeZones) ArrangeZones(false);
            Changed(true);
        }
    }

    public class DraggedEntry { public Zone Zone; public Entry Entry; public bool InternalHandled; }

    public static class ShellIcons
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        { public IntPtr hIcon; public int iIcon; public uint dwAttributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string display; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attr, out SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW")] static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint attr, out SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr pidl, uint flags, out uint attributes);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        public static ImageSource Get(string path)
        {
            ImageSource result;
            if (cache.TryGetValue(path, out result)) return result;
            SHFILEINFO info;
            try
            {
                IntPtr pidl = IntPtr.Zero; uint attributes;
                try
                {
                    if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out attributes) == 0 && pidl != IntPtr.Zero)
                        SHGetFileInfoPidl(pidl, 0, out info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x108);
                    else SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x100);
                }
                finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
                if (info.hIcon == IntPtr.Zero) return null;
                try { result = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); result.Freeze(); }
                finally { DestroyIcon(info.hIcon); }
                if (cache.Count > 2000) cache.Clear();
                cache[path] = result; return result;
            }
            catch { return null; }
        }
    }

    public class ItemTile : Border
    {
        Point start;
        bool dragging;
        bool pressed;
        public ItemTile(DeskApp app, Zone zone, Entry entry, bool compact)
        {
            Tag = entry;
            Width = compact ? 82 : 100; Height = 100; Margin = new Thickness(4); CornerRadius = new CornerRadius(10); Background = compact ? Brushes.Transparent : Theme.Brush("#202638");
            ToolTip = entry.Path + (entry.Exists ? "" : "\n原文件不存在"); Cursor = Cursors.Hand;
            ContextMenu = app.EntryMenu(zone, entry);
            AllowDrop = true;
            DragOver += DeskApp.DragOver;
            Drop += delegate(object sender, DragEventArgs e) { if (zone != null) app.Drop(zone, e, entry); };
            var panel = new StackPanel { Margin = new Thickness(8, 12, 8, 6) };
            ImageSource icon = ShellIcons.Get(entry.Path);
            if (icon != null) panel.Children.Add(new Image { Source = icon, Width = 32, Height = 32, Opacity = entry.Exists ? 1 : 0.4 });
            else panel.Children.Add(Theme.Text(entry.IsShell ? (entry.Path == "shell:RecycleBinFolder" ? "♲" : "▣") : entry.Exists ? "◇" : "!", 26, "#CCD4E9"));
            panel.Children.Add(new TextBlock { Text = entry.Name, FontSize = 11, Foreground = Theme.Brush(entry.Exists ? "#E4E8F8" : "#ECA6A6"), TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxHeight = 32, Margin = new Thickness(0, 8, 0, 0) });
            Child = panel;
            MouseEnter += delegate { Background = Theme.Brush(compact ? "#28FFFFFF" : "#303953"); };
            MouseLeave += delegate { Background = compact ? Brushes.Transparent : Theme.Brush("#202638"); };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                start = e.GetPosition(this); dragging = false; pressed = true;
                if (e.ClickCount == 2) { app.Open(entry); e.Handled = true; }
            };
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                Point p = e.GetPosition(this);
                if (pressed && e.LeftButton == MouseButtonState.Pressed && !dragging && (Math.Abs(p.X - start.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - start.Y) > SystemParameters.MinimumVerticalDragDistance))
                {
                    dragging = true;
                    var item = new DraggedEntry { Zone = zone, Entry = entry }; bool desktopDrop = false; Point dropPoint = new Point();
                    QueryContinueDragEventHandler query = delegate(object origin, QueryContinueDragEventArgs args)
                    {
                        Point point;
                        if (!args.EscapePressed && (args.KeyStates & DragDropKeyStates.LeftMouseButton) == 0 && DesktopWindows.CursorOnDesktop(out point))
                        { desktopDrop = true; dropPoint = DesktopWindows.LogicalPoint(this, point); args.Action = DragAction.Cancel; args.Handled = true; }
                    };
                    GiveFeedbackEventHandler feedback = delegate(object origin, GiveFeedbackEventArgs args)
                    { Point point; if (DesktopWindows.CursorOnDesktop(out point)) { args.UseDefaultCursors = false; Mouse.SetCursor(Cursors.Arrow); args.Handled = true; } };
                    QueryContinueDrag += query; GiveFeedback += feedback;
                    try
                    {
                        DragDropEffects result = DragDrop.DoDragDrop(this, EntryDrag.Create(item), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                        if (desktopDrop) app.ExtractToDesktop(zone, entry, dropPoint);
                        else if (!item.InternalHandled && result == DragDropEffects.Move && !entry.Exists)
                        {
                            if (app.Organizer != null) app.Organizer.RecordExternalMove(entry.Path);
                            foreach (Zone other in app.State.Zones) other.Items.RemoveAll(value => value.Path == entry.Path);
                            app.State.DesktopItems.RemoveAll(d => d.Entry.Path == entry.Path); app.Changed(false);
                        }
                    }
                    finally { QueryContinueDrag -= query; GiveFeedback -= feedback; pressed = false; dragging = false; Mouse.SetCursor(null); }
                }
            };
            MouseLeftButtonUp += delegate { pressed = false; };
        }
    }

    public class ManagerWindow : Window
    {
        readonly DeskApp app;
        readonly StackPanel navigation = new StackPanel();
        readonly WrapPanel entries = new WrapPanel();
        readonly WrapPanel toolbar = new WrapPanel();
        readonly TextBlock zoneTitle = Theme.Text("", 26, "#F1F3FF");
        readonly TextBlock zoneSubtitle = Theme.Text("", 13, "#A0ABC6");
        readonly TextBlock status = Theme.Text("", 12, "#A0ABC6");
        readonly TextBox search = new TextBox();
        readonly Button widgetButton;
        Zone selected;
        public ManagerWindow(DeskApp application)
        {
            app = application; Title = "清桌 ClearDesk · 桌面整理"; Width = 1120; Height = 740; MinWidth = 940; MinHeight = 620;
            Icon = AppBrand.WindowIcon;
            Background = Theme.Brush("#121725"); Foreground = Brushes.White; FontFamily = new FontFamily("Microsoft YaHei UI"); WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(228) }); root.ColumnDefinitions.Add(new ColumnDefinition()); Content = root;
            var sidebar = new Border { Background = Theme.Brush("#191F30"), Padding = new Thickness(22, 26, 22, 20) }; root.Children.Add(sidebar);
            var side = new DockPanel(); sidebar.Child = side;
            var brand = new StackPanel(); DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
            var brandRow = new StackPanel { Orientation = Orientation.Horizontal };
            brandRow.Children.Add(new Image { Source = AppBrand.WindowIcon, Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0) });
            brandRow.Children.Add(Theme.Text("清桌", 28, "#F1F3FF")); brand.Children.Add(brandRow);
            brand.Children.Add(new TextBlock { Text = "C L E A R D E S K", FontSize = 10, Foreground = Theme.Brush("#919CBF"), Margin = new Thickness(2, 8, 0, 32) });
            var foot = new StackPanel(); DockPanel.SetDock(foot, Dock.Bottom); side.Children.Add(foot);
            foot.Children.Add(Theme.Button("＋  新建分区", delegate { app.EditZone(new Zone { Name = "新分区", X = 100, Y = 100 }, true); }, false));
            foot.Children.Add(new TextBlock { Text = "免费开源 · 无广告\n预览后整理，可撤销移动", FontSize = 11, Foreground = Theme.Brush("#929DBA"), Margin = new Thickness(0, 22, 0, 0), LineHeight = 20 });
            side.Children.Add(new ScrollViewer { Content = navigation, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var main = new Grid { Margin = new Thickness(32, 28, 28, 20) }; Grid.SetColumn(main, 1); root.Children.Add(main);
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new DockPanel(); main.Children.Add(heading);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(actions, Dock.Right); heading.Children.Add(actions);
            widgetButton = Theme.Button("显示桌面分区", app.ToggleWidgets, true); actions.Children.Add(widgetButton);
            actions.Children.Add(Theme.Button("更多 ⋯", ShowMore, false));
            var title = new StackPanel(); title.Children.Add(Theme.Text("让桌面，井井有条。", 24, "#F1F3FF")); title.Children.Add(new TextBlock { Text = "分好类，随手找到。", Foreground = Theme.Brush("#A0ABC6"), FontSize = 13, Margin = new Thickness(0, 8, 0, 0) }); heading.Children.Add(title);
            var searchBox = new Grid { Margin = new Thickness(0, 26, 0, 22) };
            search.ToolTip = "搜索所有分区：文件名或路径"; searchBox.Children.Add(search);
            var searchHint = Theme.Text("搜索文件名或路径…", 14, "#8F9BBA"); searchHint.Margin = new Thickness(13, 11, 0, 0); searchHint.IsHitTestVisible = false; searchBox.Children.Add(searchHint);
            search.TextChanged += delegate { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RefreshEntries(); }; Grid.SetRow(searchBox, 1); main.Children.Add(searchBox);
            var section = new StackPanel(); Grid.SetRow(section, 2); main.Children.Add(section);
            section.Children.Add(zoneTitle); zoneSubtitle.Margin = new Thickness(0, 8, 0, 14); section.Children.Add(zoneSubtitle); toolbar.Margin = new Thickness(0, 0, 0, 18); section.Children.Add(toolbar);
            var listBorder = new Border { Background = Theme.Brush("#171D2D"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), AllowDrop = true };
            listBorder.DragOver += DeskApp.DragOver;
            listBorder.Drop += delegate(object sender, DragEventArgs e)
            {
                if (selected != null) app.Drop(selected, e);
                else if (e.Data.GetDataPresent(DataFormats.FileDrop)) { int n = Catalog.Classify(app.State, (string[])e.Data.GetData(DataFormats.FileDrop)); app.Changed(true); SetStatus("已自动分类 " + n + " 个入口"); e.Handled = true; }
                else SetStatus("请先选择目标分区，再拖入入口。");
            };
            listBorder.Child = new ScrollViewer { Content = entries, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(listBorder, 3); main.Children.Add(listBorder);
            var bottom = new DockPanel { Margin = new Thickness(0, 18, 0, 0) }; Grid.SetRow(bottom, 4); main.Children.Add(bottom);
            var hide = Theme.Button("收起到托盘", Hide, false); DockPanel.SetDock(hide, Dock.Right); bottom.Children.Add(hide); bottom.Children.Add(status);
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = true; Hide(); };
            Refresh(); SetStatus("提示：拖入文件自动分类；双击打开；右键管理入口。");
        }
        public void SetStatus(string message) { status.Text = app.SaveFailed ? "配置未保存，请检查写入权限。" : message; }
        public void Refresh()
        {
            if (selected != null && !app.State.Zones.Contains(selected)) selected = null;
            navigation.Children.Clear();
            navigation.Children.Add(new TextBlock { Text = "我的分区", Foreground = Theme.Brush("#929DBA"), FontSize = 11, Margin = new Thickness(0, 0, 0, 12) });
            AddNavigation("全部入口", null, app.State.Zones.Sum(z => z.Items.Count), "#A0ABC6");
            foreach (Zone zone in app.State.Zones) AddNavigation(zone.Name, zone, zone.Items.Count, zone.Color);
            widgetButton.Content = app.State.WidgetsVisible ? "隐藏桌面分区" : "显示桌面分区";
            toolbar.Children.Clear();
            if (selected == null)
            {
                toolbar.Children.Add(Theme.Button("整理桌面（移动文件）", OrganizeDesktop, true));
                toolbar.Children.Add(Theme.Button("仅扫描入口", ScanDesktop, false));
                toolbar.Children.Add(Theme.Button("撤销上次整理", UndoOrganization, false));
                toolbar.Children.Add(Theme.Button("恢复全部到桌面", delegate { app.RestoreDesktop(true); }, false));
                toolbar.Children.Add(Theme.Button("全部折叠", delegate { app.SetAllCollapsed(true); }, false));
            }
            else
            {
                toolbar.Children.Add(Theme.Button("＋ 添加文件", delegate { app.AddFiles(selected); }, true));
                toolbar.Children.Add(Theme.Button("添加文件夹", delegate { app.AddFolder(selected); }, false));
                toolbar.Children.Add(Theme.Button("重命名", delegate { app.RenameZone(selected, this); }, false));
                toolbar.Children.Add(Theme.Button("外观设置", delegate { app.EditZone(selected, false); }, false));
                toolbar.Children.Add(Theme.Button(selected.Locked ? "解锁分区" : "锁定分区", delegate { selected.Locked = !selected.Locked; app.Changed(true); }, false));
                toolbar.Children.Add(Theme.Button("删除分区", DeleteZone, false));
            }
            RefreshEntries();
            SetStatus("共 " + app.State.Zones.Count + " 个分区 · " + app.State.Zones.Sum(z => z.Items.Count) + " 个入口 · 拖入文件即可添加");
        }
        void AddNavigation(string name, Zone zone, int count, string color)
        {
            var b = Theme.Button("●  " + name + "     " + count, delegate { selected = zone; Refresh(); }, false);
            b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 0, 0, 7); b.Foreground = Theme.Brush(color);
            b.Background = Theme.Brush(selected == zone ? "#303953" : "#191F30"); navigation.Children.Add(b);
            if (zone != null)
            {
                var menu = new ContextMenu();
                Add(menu, "重命名分区…", delegate { app.RenameZone(zone, this); });
                Add(menu, "外观设置…", delegate { app.EditZone(zone, false); });
                Add(menu, "删除分区", delegate { selected = zone; DeleteZone(); });
                b.ContextMenu = menu;
            }
        }
        void RefreshEntries()
        {
            if (app.State == null) return;
            string query = search.Text.Trim();
            zoneTitle.Text = query.Length > 0 ? "搜索结果" : selected == null ? "全部入口" : selected.Name;
            entries.Children.Clear(); int count = 0;
            foreach (Zone z in app.State.Zones.Where(z => query.Length > 0 || selected == null || z == selected))
                foreach (Entry entry in z.Items)
                {
                    if (query.Length > 0 && entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 && entry.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    entries.Children.Add(new ItemTile(app, z, entry, false)); count++;
                }
            zoneSubtitle.Text = count + " 个入口  /  " + (query.Length > 0 ? "按名称和路径搜索全部分区" : "拖入文件或快捷方式 · 双击打开 · 右键管理");
            if (count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(24, 45, 24, 40), Width = 500 };
                empty.Children.Add(Theme.Text(query.Length > 0 ? "没有找到匹配的入口" : "从一个整洁的桌面开始", 22, "#CED5EF"));
                empty.Children.Add(new TextBlock { Text = query.Length > 0 ? "试试其他名称，或清空搜索。" : "点击“整理桌面（移动文件）”预览并整理，\n或点击“仅扫描入口”导入已有桌面文件。\n\n拖入文件可添加入口；实际移动请使用整理按钮。", FontSize = 14, Foreground = Theme.Brush("#939FBE"), Margin = new Thickness(0, 16, 0, 0), LineHeight = 26 });
                entries.Children.Add(empty);
            }
        }
        void ScanDesktop()
        {
            try { int count = Catalog.Classify(app.State, app.ScanDesktopPaths()); if (app.State.AutoArrangeZones) app.ArrangeZones(false); app.Changed(true); SetStatus("已添加 " + count + " 个入口，重复入口已跳过。原文件未移动。"); }
            catch (Exception ex) { MessageBox.Show("桌面扫描未完成：" + ex.Message, "清桌"); }
        }
        void OrganizeDesktop()
        {
            if (app.Organizer == null) { MessageBox.Show(app.OrganizerWarning ?? "文件整理当前不可用。", "清桌"); return; }
            try
            {
                string desktop = Catalog.Normalize(app.DesktopRoot);
                string library = app.State.LibraryPath;
                if (string.IsNullOrWhiteSpace(library)) library = Path.Combine(Path.GetDirectoryName(desktop), "ClearDesk Library");
                // Desktop imports are metadata only. Actual movement happens after this dialog's confirmation.
                Catalog.Classify(app.State, Catalog.DesktopPaths()); app.Changed(false);
                while (true)
                {
                    MoveBatch batch = app.Organizer.Plan(app.State, desktop, library, AppDomain.CurrentDomain.BaseDirectory);
                    var preview = new MovePreviewDialog(app.State, batch, false); preview.Owner = this;
                    if (preview.ShowDialog() != true) return;
                    if (preview.ChangeFolder)
                    {
                        using (var picker = new Forms.FolderBrowserDialog { Description = "选择桌面之外、同一个盘上的收纳目录", SelectedPath = library })
                        { if (picker.ShowDialog() != Forms.DialogResult.OK) return; library = picker.SelectedPath; }
                        continue;
                    }
                    if (batch.Moves.Count == 0) return;
                    app.State.LibraryPath = library;
                    RunFileOperation(delegate
                    {
                        app.Store.Save(app.State);
                        var errors = app.Organizer.Execute(app.State, batch, delegate { app.Store.Save(app.State); });
                        app.State.WidgetsVisible = true; app.Changed(true);
                        int moved = batch.Moves.Count(m => m.Status == "moved");
                        SetStatus("已移动 " + moved + " 项，原位置的图标已移除。收纳目录：" + library);
                        if (errors.Count > 0) MessageBox.Show("已移动 " + moved + " 项；" + errors.Count + " 项未移动，原文件保留。\n\n" + string.Join("\n", errors.Take(8)), "清桌 · 整理结果");
                    });
                    return;
                }
            }
            catch (Exception ex) { MessageBox.Show("无法准备整理：" + ex.Message, "清桌"); }
        }
        void UndoOrganization()
        {
            if (app.Organizer == null) { MessageBox.Show(app.OrganizerWarning ?? "撤销当前不可用。", "清桌"); return; }
            var batch = app.Organizer.UndoBatch;
            if (batch == null) { MessageBox.Show("没有可以撤销的整理记录。", "清桌"); return; }
            var preview = new MovePreviewDialog(app.State, batch, true); preview.Owner = this;
            if (preview.ShowDialog() != true) return;
            RunFileOperation(delegate
            {
                var errors = app.Organizer.Undo(app.State, delegate { app.Store.Save(app.State); });
                app.Changed(false); SetStatus(errors.Count == 0 ? "已撤销上次整理，文件恢复到原桌面位置。" : "部分文件无法撤销，移动记录已保留。");
                if (errors.Count > 0) MessageBox.Show("以下文件尚未恢复；没有覆盖任何文件。\n\n" + string.Join("\n", errors.Take(8)), "清桌 · 撤销结果");
            });
        }
        void RunFileOperation(Action action)
        {
            SetStatus("正在处理文件，请稍候…"); Cursor = Cursors.Wait; IsEnabled = false;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try { action(); }
                catch (Exception ex) { Refresh(); MessageBox.Show("操作已停止，已保存的移动记录可用于恢复。\n" + ex.Message, "清桌"); }
                finally { Cursor = null; IsEnabled = true; }
            }), DispatcherPriority.Background);
        }
        void ShowWallpaperInfo()
        {
            bool running = new[] { "wallpaper32", "wallpaper64" }.Any(name => Process.GetProcessesByName(name).Length > 0);
            MessageBox.Show((running ? "检测到 Wallpaper Engine 正在运行。" : "当前没有检测到 Wallpaper Engine 主进程。") + "\n\n分区使用真实半透明灰色背景，图标与文字保持清晰。\n支持分区设置中的背景浓度滑杆。\n\n本版采用独立透明窗口，不更换壁纸、不修改 Wallpaper Engine 的窗口或设置。动态壁纸会透过分区显示。\n\n如果壁纸被设置为遇到其他窗口就暂停，请在 Wallpaper Engine 中为 ClearDesk.exe 添加继续播放规则。具体壁纸类型、多屏和混合 DPI 仍需实际验证。", "清桌 · 动态壁纸兼容");
        }
        void DeleteZone()
        {
            if (selected == null || !app.Confirm("删除“" + selected.Name + "”分区及其中的入口？\n原文件会保留。")) return;
            app.State.Zones.Remove(selected); selected = null; app.Changed(true);
        }
        void ShowMore()
        {
            var menu = CreateMoreMenu();
            menu.Placement = PlacementMode.MousePoint; menu.IsOpen = true;
        }
        internal ContextMenu CreateMoreMenu()
        {
            var menu = new ContextMenu();
            Add(menu, "整理桌面（预览后移动）…", OrganizeDesktop);
            Add(menu, "撤销上次整理…", UndoOrganization);
            Add(menu, "扫描桌面并自动分类", ScanDesktop);
            Add(menu, "全部折叠", delegate { app.SetAllCollapsed(true); });
            Add(menu, "全部展开", delegate { app.SetAllCollapsed(false); });
            Add(menu, "自动排好分区", delegate { app.State.AutoArrangeZones = true; app.ArrangeZones(true); });
            AddCheck(menu, "自动分类桌面和新文件", app.State.AutoClassifyDesktop, delegate { app.State.AutoClassifyDesktop = !app.State.AutoClassifyDesktop; if (app.State.AutoClassifyDesktop) ScanDesktop(); else app.Save(); });
            AddCheck(menu, "启动时默认折叠", app.State.StartCollapsed, delegate { app.State.StartCollapsed = !app.State.StartCollapsed; app.Save(); });
            AddStartupOption(menu);
            AddCheck(menu, "退出时恢复已整理文件", app.State.RestoreOnExit, delegate { app.State.RestoreOnExit = !app.State.RestoreOnExit; app.Save(); });
            AddCheck(menu, "折叠展开时自动排布", app.State.AutoArrangeZones, delegate { app.State.AutoArrangeZones = !app.State.AutoArrangeZones; if (app.State.AutoArrangeZones) app.ArrangeZones(true); else app.Save(); });
            AddCheck(menu, "显示分区时收起原桌面图标", app.State.HideDesktopIcons, delegate { app.State.HideDesktopIcons = !app.State.HideDesktopIcons; app.Changed(false); });
            AddCheck(menu, "隐藏空的默认分类", app.State.HideEmptyZones, delegate { app.State.HideEmptyZones = !app.State.HideEmptyZones; if (app.State.AutoArrangeZones) app.ArrangeZones(false); app.Changed(true); });
            Add(menu, "导出配置备份…", Export);
            Add(menu, "导入配置备份…", Import);
            Add(menu, "查看本地配置文件夹", delegate { Directory.CreateDirectory(Path.GetDirectoryName(app.Store.FilePath)); Process.Start("explorer.exe", Path.GetDirectoryName(app.Store.FilePath)); });
            Add(menu, "Wallpaper Engine 兼容说明", ShowWallpaperInfo);
            Add(menu, "关于清桌", delegate { MessageBox.Show("清桌 ClearDesk " + AppBrand.Version + "\n免费开源 · MIT 许可 · 无广告 · 无遥测\n\n支持锁定分区、拖出到桌面、手动排序和边缘缩放。\n分区不会出现在 Alt+Tab 列表。\n正常退出时默认恢复已整理文件；冲突时保留文件并提示。", "关于清桌"); });
            Add(menu, "退出", app.Quit);
            return menu;
        }
        void AddStartupOption(ContextMenu menu)
        {
            var item = new MenuItem { Header = "开机自启", IsCheckable = true, IsEnabled = app.StartupSettingsAvailable, ToolTip = "登录 Windows 后自动显示分区，管理窗口收起到托盘。" };
            if (!app.StartupSettingsAvailable) item.ToolTip = "隔离配置不修改当前用户的开机自启设置。";
            else
            {
                try { item.IsChecked = app.StartupRegistration.IsEnabled; }
                catch (Exception ex) { item.IsEnabled = false; item.ToolTip = "无法读取开机自启设置：" + ex.Message; }
            }
            item.Click += delegate
            {
                try
                {
                    app.StartupRegistration.SetEnabled(item.IsChecked);
                    SetStatus(item.IsChecked ? "已开启开机自启，下次登录 Windows 后自动显示分区。" : "已关闭开机自启。");
                }
                catch (Exception ex)
                {
                    try { item.IsChecked = app.StartupRegistration.IsEnabled; } catch { item.IsEnabled = false; }
                    MessageBox.Show("无法修改开机自启设置：\n" + ex.Message, "清桌", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            menu.Items.Add(item);
        }
        static void Add(ContextMenu menu, string name, Action action)
        { var m = new MenuItem { Header = name }; m.Click += delegate { action(); }; menu.Items.Add(m); }
        static void AddCheck(ContextMenu menu, string name, bool value, Action action)
        { var m = new MenuItem { Header = name, IsCheckable = true, IsChecked = value }; m.Click += delegate { action(); }; menu.Items.Add(m); }
        void Export()
        {
            var d = new Microsoft.Win32.SaveFileDialog { FileName = "ClearDesk-backup.json", Filter = "清桌配置|*.json" };
            if (d.ShowDialog() != true) return;
            try { new SettingsStore(d.FileName).Save(app.State); SetStatus("配置备份已导出（不包含原文件）。"); }
            catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message, "清桌"); }
        }
        void Import()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "清桌配置|*.json" };
            if (d.ShowDialog() != true) return;
            try
            {
                Settings imported = SettingsStore.Read(d.FileName);
                if (!app.Confirm("导入会替换当前分区、入口和布局，是否继续？\n当前配置会保留为 settings.json.bak。")) return;
                Settings old = app.State; app.State = imported;
                if (!app.Save()) { app.State = old; return; }
                selected = null; Refresh(); app.RebuildWidgets(); SetStatus("配置已导入，未打开任何文件。");
            }
            catch (Exception ex) { MessageBox.Show("无法导入配置：" + ex.Message, "清桌"); }
        }
    }

    public class ZoneDialog : Window
    {
        public string ZoneName; public string ZoneColor; public double ZoneOpacity;
        public ZoneDialog(Zone zone, Settings state = null)
        {
            Title = "分区设置"; Width = 400; Height = 425; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Brush("#191F30"); Foreground = Brushes.White; FontFamily = new FontFamily("Microsoft YaHei UI");
            var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
            panel.Children.Add(Theme.Text("分区名称", 14, "#CCD4EC"));
            var name = new TextBox { Text = zone.Name, Margin = new Thickness(0, 10, 0, 20), MaxLength = 30 }; panel.Children.Add(name);
            panel.Children.Add(Theme.Text("标识颜色", 14, "#CCD4EC"));
            var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 22) }; panel.Children.Add(colors);
            ZoneColor = zone.Color;
            var choices = new List<Button>();
            foreach (string hex in new[] { "#6875E8", "#37AA91", "#D990BC", "#CC9851", "#9081CD", "#5A9FCA", "#8895AC" })
            {
                string color = hex;
                var b = Theme.Button(hex == ZoneColor ? "✓" : " ", delegate { }, false); b.Width = 36; b.Height = 36; b.Padding = new Thickness(0); b.Background = Theme.Brush(hex);
                b.Click += delegate { ZoneColor = color; foreach (Button choice in choices) choice.Content = " "; b.Content = "✓"; };
                choices.Add(b); colors.Children.Add(b);
            }
            ZoneOpacity = zone.BackgroundOpacity;
            var opacityLabel = Theme.Text("灰色背景浓度：" + Math.Round(ZoneOpacity * 100) + "%", 13, "#CCD4EC"); panel.Children.Add(opacityLabel);
            var opacitySlider = new Slider { Minimum = 10, Maximum = 90, Value = ZoneOpacity * 100, Margin = new Thickness(0, 12, 0, 22), TickFrequency = 10, IsSnapToTickEnabled = false };
            opacitySlider.ValueChanged += delegate { ZoneOpacity = opacitySlider.Value / 100; opacityLabel.Text = "灰色背景浓度：" + Math.Round(opacitySlider.Value) + "%（越低越透明）"; }; panel.Children.Add(opacitySlider);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
            buttons.Children.Add(Theme.Button("取消", delegate { DialogResult = false; }, false));
            var error = Theme.Text("", 12, "#EEAAAA"); error.Margin = new Thickness(0, 10, 0, 0); panel.Children.Add(error);
            var save = Theme.Button("保存分区", delegate
            {
                if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "请填写分区名称。"; name.Focus(); return; }
                if (state != null && state.Zones.Any(z => z != zone && string.Equals(z.Name, name.Text.Trim(), StringComparison.OrdinalIgnoreCase))) { error.Text = "已有同名分区，请换一个名称。"; name.Focus(); return; }
                ZoneName = name.Text.Trim(); DialogResult = true;
            }, true); save.IsDefault = true; buttons.Children.Add(save);
        }
    }

    public class ZoneWindow : Window
    {
        readonly DeskApp app;
        readonly Zone zone;
        readonly WrapPanel items = new WrapPanel();
        readonly TextBlock counter = Theme.Text("", 11, "#9EAAC8");
        readonly ScrollViewer scroll;
        readonly Thumb resizeGrip;
        readonly Button foldButton;
        readonly Button lockButton;
        readonly List<Thumb> edgeGrips = new List<Thumb>();
        readonly DispatcherTimer saveTimer;
        bool ready;
        bool disposing;
        public ZoneWindow(DeskApp application, Zone model)
        {
            app = application; zone = model;
            Title = "清桌 · " + zone.Name; Width = zone.Width; Height = zone.Collapsed ? ZoneLayout.CollapsedHeight : zone.Height;
            MinWidth = 240; MinHeight = zone.Collapsed ? ZoneLayout.CollapsedHeight : 160; MaxWidth = 2000; MaxHeight = 2000;
            if (zone.Collapsed) MaxHeight = ZoneLayout.CollapsedHeight;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = zone.Pinned;
            ShowActivated = false;
            SourceInitialized += delegate { DesktopWindows.ToolWindow(this); };
            Background = Brushes.Transparent; Foreground = Brushes.White; FontFamily = new FontFamily("Microsoft YaHei UI");
            Rect area = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Left = Math.Max(area.Left, Math.Min(zone.X, area.Right - Width)); Top = Math.Max(area.Top, Math.Min(zone.Y, area.Bottom - Height));
            if (!Forms.Screen.AllScreens.Any(s => new Rect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height).IntersectsWith(new Rect(Left, Top, Width, Height)))) { Left = SystemParameters.WorkArea.Left + 20; Top = SystemParameters.WorkArea.Top + 20; }
            var border = new Border { Background = Theme.Gray(zone.BackgroundOpacity), BorderBrush = Theme.Brush("#66FFFFFF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true }; Content = border;
            var frame = new Grid(); border.Child = frame;
            var root = new DockPanel(); frame.Children.Add(root);
            var header = new Border { Background = Theme.Gray(Math.Min(0.9, zone.BackgroundOpacity + 0.1)), Padding = new Thickness(12, 10, 6, 10), Height = ZoneLayout.CollapsedHeight - 2, CornerRadius = new CornerRadius(9, 9, 0, 0) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            var bar = new DockPanel(); header.Child = bar;
            var menu = Theme.Button("⋯", ShowMenu, false); menu.Background = Brushes.Transparent; menu.Padding = new Thickness(8, 4, 8, 4); menu.Margin = new Thickness(0); DockPanel.SetDock(menu, Dock.Right); bar.Children.Add(menu);
            foldButton = Theme.Button(zone.Collapsed ? "▸" : "▾", ToggleCollapse, false); foldButton.Background = Brushes.Transparent; foldButton.Margin = new Thickness(0); foldButton.Padding = new Thickness(8, 4, 8, 4); foldButton.ToolTip = zone.Collapsed ? "展开分区" : "折叠分区"; DockPanel.SetDock(foldButton, Dock.Right); bar.Children.Add(foldButton);
            lockButton = Theme.Button(zone.Locked ? "已锁" : "锁定", delegate { SetLocked(!zone.Locked); }, false);
            lockButton.FontSize = 11; lockButton.Margin = new Thickness(0); lockButton.Padding = new Thickness(5, 4, 5, 4);
            DockPanel.SetDock(lockButton, Dock.Right); bar.Children.Add(lockButton);
            counter.Margin = new Thickness(8, 6, 8, 0); DockPanel.SetDock(counter, Dock.Right); bar.Children.Add(counter);
            var title = Theme.Text("●  " + zone.Name, 14, zone.Color); title.TextTrimming = TextTrimming.CharacterEllipsis; title.VerticalAlignment = VerticalAlignment.Center; bar.Children.Add(title);
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) { ToggleCollapse(); e.Handled = true; }
                else if (!zone.Locked) { try { Point before = new Point(Left, Top); DragMove(); if (Math.Abs(before.X - Left) > 1 || Math.Abs(before.Y - Top) > 1) { app.State.AutoArrangeZones = false; app.Save(); } } catch (InvalidOperationException) { } }
            };
            scroll = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(6), Visibility = zone.Collapsed ? Visibility.Collapsed : Visibility.Visible };
            root.Children.Add(scroll); AllowDrop = true; DragOver += DeskApp.DragOver;
            resizeGrip = new Thumb { Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Opacity = 0.9, ToolTip = "拖动调整分区大小", Visibility = zone.Collapsed ? Visibility.Collapsed : Visibility.Visible };
            resizeGrip.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Border Background='Transparent'><TextBlock Text='◢' Foreground='White' HorizontalAlignment='Right' VerticalAlignment='Bottom'/></Border></ControlTemplate>");
            ConfigureResize(resizeGrip, 1, 1);
            frame.Children.Add(resizeGrip);
            AddEdge(frame, HorizontalAlignment.Left, VerticalAlignment.Stretch, 7, double.NaN, -1, 0, Cursors.SizeWE);
            AddEdge(frame, HorizontalAlignment.Right, VerticalAlignment.Stretch, 7, double.NaN, 1, 0, Cursors.SizeWE);
            AddEdge(frame, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, 7, 0, -1, Cursors.SizeNS);
            AddEdge(frame, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, 7, 0, 1, Cursors.SizeNS);
            AddEdge(frame, HorizontalAlignment.Left, VerticalAlignment.Top, 16, 16, -1, -1, Cursors.SizeNWSE);
            AddEdge(frame, HorizontalAlignment.Right, VerticalAlignment.Top, 16, 16, 1, -1, Cursors.SizeNESW);
            AddEdge(frame, HorizontalAlignment.Left, VerticalAlignment.Bottom, 16, 16, -1, 1, Cursors.SizeNESW);
            Panel.SetZIndex(resizeGrip, 2);
            Drop += delegate(object sender, DragEventArgs e) { app.Drop(zone, e); };
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; saveTimer.Tick += delegate { saveTimer.Stop(); app.Save(); };
            Loaded += delegate { ready = true; RecordLayout(); };
            LocationChanged += delegate { RecordLayout(); }; SizeChanged += delegate { RecordLayout(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!disposing) { e.Cancel = true; app.ToggleWidgets(); } };
            ContextMenu = BuildMenu();
            UpdateLockControls();
            RefreshItems();
        }
        void RecordLayout()
        {
            if (!ready || disposing) return;
            zone.X = Left; zone.Y = Top; zone.Width = Width; if (!zone.Collapsed) zone.Height = Height;
            saveTimer.Stop(); saveTimer.Start();
        }
        void AddEdge(Grid frame, HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height, int dx, int dy, Cursor cursor)
        {
            var grip = new Thumb { HorizontalAlignment = horizontal, VerticalAlignment = vertical, Width = width, Height = height, Cursor = cursor, Visibility = zone.Collapsed ? Visibility.Collapsed : Visibility.Visible };
            grip.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Border Background='Transparent'/></ControlTemplate>");
            ConfigureResize(grip, dx, dy);
            edgeGrips.Add(grip); frame.Children.Add(grip);
        }
        void ConfigureResize(Thumb grip, int dx, int dy)
        {
            Point origin = new Point(); double originalWidth = 0, originalHeight = 0;
            grip.DragStarted += delegate { origin = DesktopWindows.LogicalPoint(this, DesktopWindows.CursorPosition()); originalWidth = Width; originalHeight = Height; };
            grip.DragDelta += delegate
            {
                Point cursor = DesktopWindows.LogicalPoint(this, DesktopWindows.CursorPosition());
                double width = Math.Max(MinWidth, Math.Min(MaxWidth, originalWidth + (cursor.X - origin.X) * dx));
                double height = Math.Max(MinHeight, Math.Min(MaxHeight, originalHeight + (cursor.Y - origin.Y) * dy));
                ResizeBy(dx == 0 ? 0 : width - Width, dy == 0 ? 0 : height - Height, dx < 0, dy < 0);
            };
        }
        public void ResizeBy(double horizontal, double vertical, bool fromLeft, bool fromTop)
        {
            if (zone.Locked) return;
            app.State.AutoArrangeZones = false;
            double previousWidth = Width, previousHeight = Height;
            Width = Math.Max(MinWidth, Math.Min(MaxWidth, Width + horizontal));
            if (!zone.Collapsed) Height = Math.Max(MinHeight, Math.Min(MaxHeight, Height + vertical));
            if (fromLeft) Left -= Width - previousWidth;
            if (fromTop && !zone.Collapsed) Top -= Height - previousHeight;
            zone.X = Left; zone.Y = Top; zone.Width = Width; if (!zone.Collapsed) zone.Height = Height;
            if (saveTimer != null) { saveTimer.Stop(); saveTimer.Start(); }
        }
        public void DisposeWindow() { disposing = true; saveTimer.Stop(); Close(); }
        public void RefreshItems()
        {
            items.Children.Clear(); counter.Text = zone.Items.Count.ToString();
            if (zone.Collapsed) return;
            foreach (Entry entry in zone.Items) items.Children.Add(new ItemTile(app, zone, entry, true));
            if (zone.Items.Count == 0) { var t = Theme.Text("把文件或软件快捷方式\n拖到这个分区", 13, "#A3AECA"); t.Margin = new Thickness(18, 24, 10, 10); items.Children.Add(t); }
        }
        public bool IsContentCollapsed { get { return scroll.Visibility == Visibility.Collapsed && resizeGrip.Visibility == Visibility.Collapsed && items.Children.Count == 0; } }
        public void ApplyModelLayout()
        {
            bool wasReady = ready; ready = false;
            saveTimer.Stop();
            MinHeight = 0; MaxHeight = 2000;
            MinHeight = zone.Collapsed ? ZoneLayout.CollapsedHeight : 160;
            Height = zone.Collapsed ? ZoneLayout.CollapsedHeight : zone.Height;
            if (zone.Collapsed) MaxHeight = ZoneLayout.CollapsedHeight;
            ResizeMode = ResizeMode.NoResize;
            Width = zone.Width; Left = zone.X; Top = zone.Y;
            scroll.Visibility = zone.Collapsed ? Visibility.Collapsed : Visibility.Visible;
            UpdateLockControls();
            foldButton.Content = zone.Collapsed ? "▸" : "▾"; foldButton.ToolTip = zone.Collapsed ? "展开分区" : "折叠分区";
            RefreshItems(); ContextMenu = BuildMenu();
            ready = wasReady;
        }
        public void SetCollapsed(bool collapsed)
        {
            if (zone.Collapsed == collapsed) return;
            if (!zone.Collapsed) zone.Height = Height;
            zone.Collapsed = collapsed; ApplyModelLayout(); app.OnZoneFoldChanged();
        }
        void ToggleCollapse() { SetCollapsed(!zone.Collapsed); }
        public void SetLocked(bool locked)
        {
            zone.Locked = locked; UpdateLockControls(); ContextMenu = BuildMenu(); app.Save();
            if (app.Manager != null) app.Manager.Refresh();
        }
        void UpdateLockControls()
        {
            resizeGrip.Visibility = zone.Collapsed || zone.Locked ? Visibility.Collapsed : Visibility.Visible;
            foreach (Thumb grip in edgeGrips) grip.Visibility = resizeGrip.Visibility;
            lockButton.Content = zone.Locked ? "已锁" : "锁定";
            lockButton.Background = zone.Locked ? Theme.Brush("#365CC8B1") : Brushes.Transparent;
            lockButton.ToolTip = zone.Locked ? "位置和大小已锁定，点击解锁" : "锁定位置和大小，仍可展开和使用文件";
            System.Windows.Automation.AutomationProperties.SetName(lockButton, zone.Locked ? "解锁分区" : "锁定分区");
        }
        void ShowMenu()
        { var menu = BuildMenu(); menu.Placement = PlacementMode.MousePoint; menu.IsOpen = true; }
        ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            Add(menu, "重命名分区…", delegate { app.RenameZone(zone, this); });
            Add(menu, zone.Locked ? "解锁分区" : "锁定分区", delegate { SetLocked(!zone.Locked); });
            Add(menu, "添加文件…", delegate { app.AddFiles(zone); });
            Add(menu, "添加文件夹…", delegate { app.AddFolder(zone); });
            Add(menu, "编辑分区…", delegate { app.ShowManager(); app.EditZone(zone, false); });
            Add(menu, "按名称排序", delegate { zone.Items = zone.Items.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList(); app.Changed(false); });
            Add(menu, zone.Collapsed ? "展开分区" : "折叠分区", ToggleCollapse);
            Add(menu, "全部折叠", delegate { app.SetAllCollapsed(true); });
            Add(menu, "全部展开", delegate { app.SetAllCollapsed(false); });
            Add(menu, zone.Pinned ? "取消置顶" : "置顶显示", delegate { zone.Pinned = !zone.Pinned; Topmost = zone.Pinned; app.Save(); });
            Add(menu, "打开管理窗口", app.ShowManager);
            Add(menu, "隐藏所有分区", app.ToggleWidgets);
            return menu;
        }
        static void Add(ContextMenu menu, string name, Action action) { var m = new MenuItem { Header = name }; m.Click += delegate { action(); }; menu.Items.Add(m); }
    }
}
