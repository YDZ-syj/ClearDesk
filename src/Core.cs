using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ClearDesk
{
    [DataContract]
    public class Entry
    {
        [DataMember] public string Path { get; set; }
        [DataMember] public string Name { get; set; }
        public bool IsShell { get { return Path == "shell:RecycleBinFolder" || Path == "shell:MyComputerFolder"; } }
        public bool Exists { get { return IsShell || File.Exists(Path) || Directory.Exists(Path); } }
    }

    [DataContract]
    public class Zone
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Color { get; set; }
        [DataMember] public List<Entry> Items { get; set; }
        [DataMember] public double X { get; set; }
        [DataMember] public double Y { get; set; }
        [DataMember] public double Width { get; set; }
        [DataMember] public double Height { get; set; }
        [DataMember] public bool Pinned { get; set; }
        [DataMember] public bool Locked { get; set; }
        [DataMember] public bool Collapsed { get; set; }
        [DataMember] public double BackgroundOpacity { get; set; }
        [DataMember] public string CategoryKey { get; set; }
        [OnDeserializing] void BeforeRead(StreamingContext context) { BackgroundOpacity = 0.48; }
        public Zone() { Id = Guid.NewGuid().ToString("N"); CategoryKey = ""; Items = new List<Entry>(); Width = 310; Height = 360; Color = "#6875E8"; BackgroundOpacity = 0.48; Collapsed = true; }
    }

    [DataContract]
    public class DesktopEntry
    {
        [DataMember] public Entry Entry { get; set; }
        [DataMember] public double X { get; set; }
        [DataMember] public double Y { get; set; }
    }

    [DataContract]
    public class Settings
    {
        [DataMember] public int Version { get; set; }
        [DataMember] public List<Zone> Zones { get; set; }
        [DataMember] public bool WidgetsVisible { get; set; }
        [DataMember] public string LibraryPath { get; set; }
        [DataMember] public bool AutoClassifyDesktop { get; set; }
        [DataMember] public bool AutoArrangeZones { get; set; }
        [DataMember] public bool StartCollapsed { get; set; }
        [DataMember] public bool HideDesktopIcons { get; set; }
        [DataMember] public bool HideEmptyZones { get; set; }
        [DataMember] public int LayoutRevision { get; set; }
        [DataMember] public List<DesktopEntry> DesktopItems { get; set; }
        [DataMember] public bool RestoreOnExit { get; set; }
        [OnDeserializing] void BeforeRead(StreamingContext context)
        { AutoClassifyDesktop = true; AutoArrangeZones = true; StartCollapsed = true; HideDesktopIcons = true; HideEmptyZones = true; RestoreOnExit = true; DesktopItems = new List<DesktopEntry>(); }
        public Settings() { Version = 1; WidgetsVisible = true; Zones = new List<Zone>(); DesktopItems = new List<DesktopEntry>(); RestoreOnExit = true; AutoClassifyDesktop = true; AutoArrangeZones = true; StartCollapsed = true; HideDesktopIcons = true; HideEmptyZones = true; }
        public static Settings Default()
        {
            var s = new Settings();
            string[] names = { "应用", "文档", "图片", "影音", "压缩包", "文件夹", "其他", "系统" };
            string[] colors = { "#6875E8", "#37AA91", "#D990BC", "#CC9851", "#9081CD", "#5A9FCA", "#8895AC", "#A4ADB8" };
            for (int i = 0; i < names.Length; i++) s.Zones.Add(new Zone { Name = names[i], CategoryKey = names[i], Color = colors[i], X = 60 + (i % 4) * 325, Y = 80 + (i / 4) * 375 });
            return s;
        }
    }

    public static class Catalog
    {
        public static readonly string[] Categories = { "应用", "文档", "图片", "影音", "压缩包", "文件夹", "其他", "系统" };
        public static Zone FindCategoryZone(Settings state, string category)
        {
            Zone zone = state.Zones.FirstOrDefault(z => z.CategoryKey == category);
            if (zone == null) { zone = new Zone { Name = UniqueName(state, category), CategoryKey = category }; state.Zones.Add(zone); }
            return zone;
        }
        static string UniqueName(Settings state, string name)
        { string candidate = name; int i = 2; while (state.Zones.Any(z => string.Equals(z.Name, candidate, StringComparison.OrdinalIgnoreCase))) candidate = name + " " + i++; return candidate; }
        public static void Rename(Settings state, Zone zone, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 30) throw new ArgumentException("名称需要包含1～30个字符。");
            if (state.Zones.Any(z => z != zone && string.Equals(z.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("已有同名分区，请换一个名称。");
            zone.Name = name;
        }
        public static void AddSystemEntries(Settings state)
        {
            Zone zone = FindCategoryZone(state, "系统");
            foreach (Entry entry in new[] { new Entry { Name = "回收站", Path = "shell:RecycleBinFolder" }, new Entry { Name = "此电脑", Path = "shell:MyComputerFolder" } })
                if (!state.Zones.Any(z => z.Items.Any(e => e.Path == entry.Path)) && !state.DesktopItems.Any(d => d.Entry.Path == entry.Path)) zone.Items.Add(entry);
        }
        public static IEnumerable<Zone> VisibleZones(Settings state)
        { return state.Zones.Where(z => !state.HideEmptyZones || z.Items.Count > 0 || string.IsNullOrEmpty(z.CategoryKey)); }
        public static void Place(Zone source, Zone target, Entry entry, Entry before)
        {
            if (before == entry && source == target) return;
            if (source != null) source.Items.Remove(entry);
            target.Items.RemoveAll(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
            int index = before == null ? -1 : target.Items.IndexOf(before);
            target.Items.Insert(index < 0 ? target.Items.Count : index, entry);
        }
        public static string Category(string path)
        {
            if (Directory.Exists(path)) return "文件夹";
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (new[] { ".lnk", ".url", ".exe", ".appref-ms" }.Contains(ext)) return "应用";
            if (new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".csv", ".rtf", ".odt", ".epub" }.Contains(ext)) return "文档";
            if (new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".heic", ".psd" }.Contains(ext)) return "图片";
            if (new[] { ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".mp3", ".wav", ".flac", ".aac", ".ogg" }.Contains(ext)) return "影音";
            if (new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2" }.Contains(ext)) return "压缩包";
            return "其他";
        }
        public static string Normalize(string path)
        {
            string full = System.IO.Path.GetFullPath(path);
            return full.Length > System.IO.Path.GetPathRoot(full).Length ? full.TrimEnd(System.IO.Path.DirectorySeparatorChar) : full;
        }
        public static int Add(Zone zone, IEnumerable<string> paths)
        {
            int count = 0;
            foreach (string value in paths)
            {
                string path;
                try { path = Normalize(value); } catch (Exception) { continue; }
                if (!File.Exists(path) && !Directory.Exists(path)) continue;
                if (zone.Items.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                string name = System.IO.Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) name = path;
                zone.Items.Add(new Entry { Path = path, Name = name }); count++;
            }
            return count;
        }
        public static int Classify(Settings settings, IEnumerable<string> paths)
        {
            int count = 0;
            foreach (string raw in paths)
            {
                string path;
                try { path = Normalize(raw); } catch (Exception) { continue; }
                if (!File.Exists(path) && !Directory.Exists(path)) continue;
                if (settings.Zones.Any(z => z.Items.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))) || settings.DesktopItems.Any(d => string.Equals(d.Entry.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                string category = Category(path);
                Zone zone = FindCategoryZone(settings, category);
                count += Add(zone, new[] { path });
            }
            return count;
        }
        public static IEnumerable<string> DesktopPaths()
        { return DesktopPaths(new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }); }
        public static IEnumerable<string> DesktopPaths(IEnumerable<string> roots)
        {
            var list = new List<string>();
            foreach (string root in roots.Distinct())
            {
                if (!Directory.Exists(root)) continue;
                foreach (string path in Directory.EnumerateFileSystemEntries(root))
                {
                    if (System.IO.Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if ((File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    list.Add(path);
                }
            }
            return list;
        }
    }

    public class SettingsStore
    {
        public string FilePath { get; private set; }
        public SettingsStore(string path) { FilePath = path; }
        public Settings Load()
        {
            return File.Exists(FilePath) ? Read(FilePath) : Settings.Default();
        }
        public static Settings Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var s = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                Validate(s); return s;
            }
        }
        public static void Validate(Settings s)
        {
            if (s == null || s.Version != 1 || s.Zones == null || s.Zones.Count > 100) throw new InvalidDataException("配置格式或版本不受支持。");
            var ids = new HashSet<string>();
            if (s.DesktopItems == null) s.DesktopItems = new List<DesktopEntry>();
            if (s.DesktopItems.Count > 10000 || s.DesktopItems.Any(d => d == null || d.Entry == null || string.IsNullOrWhiteSpace(d.Entry.Name) || string.IsNullOrWhiteSpace(d.Entry.Path) || (!d.Entry.IsShell && !Path.IsPathRooted(d.Entry.Path)) || double.IsNaN(d.X) || double.IsInfinity(d.X) || double.IsNaN(d.Y) || double.IsInfinity(d.Y))) throw new InvalidDataException("桌面入口数据不完整。");
            var categories = new HashSet<string>();
            foreach (Zone z in s.Zones)
            {
                if (z == null || string.IsNullOrWhiteSpace(z.Name) || string.IsNullOrEmpty(z.Id) || !ids.Add(z.Id) || z.Items == null || z.Items.Count > 10000)
                    throw new InvalidDataException("分区数据不完整。");
                if (z.CategoryKey == null && Catalog.Categories.Contains(z.Name) && !categories.Contains(z.Name)) z.CategoryKey = z.Name;
                if (!string.IsNullOrEmpty(z.CategoryKey) && (!Catalog.Categories.Contains(z.CategoryKey) || !categories.Add(z.CategoryKey))) throw new InvalidDataException("自动分类规则重复或不受支持。");
                if (double.IsNaN(z.X) || double.IsInfinity(z.X) || double.IsNaN(z.Y) || double.IsInfinity(z.Y)) { z.X = 80; z.Y = 80; }
                if (double.IsNaN(z.Width) || double.IsInfinity(z.Width) || z.Width < 240 || z.Width > 2000) z.Width = 310;
                if (double.IsNaN(z.Height) || double.IsInfinity(z.Height) || z.Height < 160 || z.Height > 2000) z.Height = 360;
                if (z.Color == null || !System.Text.RegularExpressions.Regex.IsMatch(z.Color, "^#[0-9A-Fa-f]{6}$")) z.Color = "#6875E8";
                if (double.IsNaN(z.BackgroundOpacity) || double.IsInfinity(z.BackgroundOpacity) || z.BackgroundOpacity < 0.1 || z.BackgroundOpacity > 0.9) z.BackgroundOpacity = 0.48;
                foreach (Entry entry in z.Items)
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Path) || (!entry.IsShell && !System.IO.Path.IsPathRooted(entry.Path)) || string.IsNullOrWhiteSpace(entry.Name)) throw new InvalidDataException("文件入口数据不完整。");
            }
        }
        public void Save(Settings s)
        {
            Validate(s);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, s); stream.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
    }
}
