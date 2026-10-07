using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32.SafeHandles;
using System.Text;
using System.ComponentModel;

namespace ClearDesk
{
    [DataContract]
    public class FileMove
    {
        [DataMember] public string Source;
        [DataMember] public string Destination;
        [DataMember] public string ZoneId;
        [DataMember] public string Identity;
        [DataMember] public bool Directory;
        [DataMember] public string Status;
        [DataMember] public string Error;
        [DataMember] public bool SettingsApplied;
        [DataMember] public bool UndoApplied;
    }
    [DataContract]
    public class MoveBatch
    {
        [DataMember] public string Id;
        [DataMember] public string Desktop;
        [DataMember] public string Library;
        [DataMember] public List<FileMove> Moves;
        public List<string> Skipped = new List<string>();
    }
    [DataContract]
    public class MoveHistory
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<MoveBatch> Batches = new List<MoveBatch>();
    }

    // Stable Windows file identity survives same-volume renames, including directory moves.
    // Undo will refuse to move a different object that later appears at the saved path.
    public static class FileIdentity
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Information
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information info);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, IntPtr data, uint size);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RenameInformation { public uint Replace; public IntPtr Root; public uint Length; public char Name; }
        static string FromHandle(SafeFileHandle handle)
        {
            Information info;
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out info)) throw new IOException("无法读取文件标识。", new Win32Exception(Marshal.GetLastWin32Error()));
            if (info.IndexHigh == 0 && info.IndexLow == 0) throw new IOException("当前文件系统不提供稳定文件标识。");
            if ((info.Attributes & 0x400) != 0) throw new IOException("不能移动重解析链接或云端占位文件。");
            return info.Volume.ToString("X8") + ":" + info.IndexHigh.ToString("X8") + info.IndexLow.ToString("X8");
        }
        public static void MoveVerified(string source, string destination, string expected)
        {
            // Deny concurrent delete/rename; verify and rename the same open object.
            using (var handle = CreateFile(source, 0x10080, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
            {
                if (FromHandle(handle) != expected) throw new IOException("文件已被替换，已停止移动：" + source);
                byte[] name = Encoding.Unicode.GetBytes(Catalog.Normalize(destination));
                int offset = Marshal.OffsetOf(typeof(RenameInformation), "Name").ToInt32();
                int length = offset + name.Length + 2; IntPtr buffer = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.Copy(new byte[length], 0, buffer, length);
                    Marshal.WriteInt32(buffer, Marshal.OffsetOf(typeof(RenameInformation), "Length").ToInt32(), name.Length);
                    Marshal.Copy(name, 0, IntPtr.Add(buffer, offset), name.Length);
                    if (!SetFileInformationByHandle(handle, 3, buffer, (uint)length)) throw new IOException("文件无法移动，原文件和同名目标均保留。", new Win32Exception(Marshal.GetLastWin32Error()));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        public static string Get(string path)
        {
            using (var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                Information info;
                if (handle.IsInvalid || !GetFileInformationByHandle(handle, out info)) throw new IOException("无法读取文件标识：" + path);
                if (info.IndexHigh == 0 && info.IndexLow == 0) throw new IOException("当前文件系统不提供稳定文件标识，无法进行可恢复的整理：" + path);
                return info.Volume.ToString("X8") + ":" + info.IndexHigh.ToString("X8") + info.IndexLow.ToString("X8");
            }
        }
    }

    public class DesktopOrganizer
    {
        public string HistoryPath { get; private set; }
        public MoveHistory History { get; private set; }
        public DesktopOrganizer(string historyPath)
        {
            HistoryPath = historyPath;
            if (!File.Exists(historyPath)) { History = new MoveHistory(); return; }
            // Never overwrite an unreadable history: it may be the only record of real file moves.
            using (var stream = File.OpenRead(historyPath)) History = (MoveHistory)new DataContractJsonSerializer(typeof(MoveHistory)).ReadObject(stream);
            ValidateHistory();
        }
        static bool Equal(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        static bool Exists(string path) { return File.Exists(path) || System.IO.Directory.Exists(path); }
        static bool Inside(string path, string root) { return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        static void NoLinks(string path)
        {
            string current = Catalog.Normalize(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("整理路径包含链接或云端占位目录，请选择普通本地文件夹：" + current);
                string parent = Path.GetDirectoryName(current); if (Equal(parent, current)) break; current = parent;
            }
        }
        static void ValidateRoots(string desktop, string library)
        {
            if (!Path.IsPathRooted(desktop) || !Path.IsPathRooted(library) || !Equal(Catalog.Normalize(desktop), desktop) || !Equal(Catalog.Normalize(library), library)) throw new InvalidDataException("整理路径必须是完整的规范路径。");
            if (Equal(desktop, library) || Inside(library, desktop) || Inside(desktop, library)) throw new IOException("收纳目录需要在桌面之外，且不能包含桌面。");
            if (!Equal(Path.GetPathRoot(desktop), Path.GetPathRoot(library))) throw new IOException("请选择与桌面在同一个盘上的收纳目录，以支持可靠的移动和撤销。");
        }
        static void ValidateMove(MoveBatch batch, FileMove move)
        {
            if (move == null || string.IsNullOrEmpty(move.Identity) || string.IsNullOrEmpty(move.ZoneId) || !Path.IsPathRooted(move.Source ?? "") || !Path.IsPathRooted(move.Destination ?? "")) throw new InvalidDataException("移动记录不完整。");
            if (!Equal(Catalog.Normalize(move.Source), move.Source) || !Equal(Catalog.Normalize(move.Destination), move.Destination) || !Equal(Path.GetDirectoryName(move.Source), batch.Desktop) || !Inside(move.Destination, batch.Library)) throw new InvalidDataException("移动记录超出了指定目录。");
            if (!new[] { "pending", "moved", "failed", "undoing", "undone", "blocked", "exported" }.Contains(move.Status)) throw new InvalidDataException("移动记录状态不受支持。");
        }
        void ValidateHistory()
        {
            if (History == null || History.Version != 1 || History.Batches == null || History.Batches.Count > 1000) throw new InvalidDataException("整理历史格式不受支持。");
            foreach (MoveBatch batch in History.Batches)
            {
                if (batch == null || batch.Moves == null || batch.Moves.Count > 10000 || string.IsNullOrEmpty(batch.Id)) throw new InvalidDataException("整理批次不完整。");
                ValidateRoots(batch.Desktop, batch.Library);
                foreach (FileMove move in batch.Moves) ValidateMove(batch, move);
            }
        }
        void SaveHistory()
        {
            ValidateHistory();
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath));
            string temp = HistoryPath + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { new DataContractJsonSerializer(typeof(MoveHistory)).WriteObject(stream, History); stream.Flush(true); }
            AtomicFile.Commit(temp, HistoryPath, HistoryPath + ".bak");
        }
        public MoveBatch Plan(Settings state, string desktop, string library, string runningDirectory)
        {
            desktop = Catalog.Normalize(desktop); library = Catalog.Normalize(library);
            ValidateRoots(desktop, library); NoLinks(desktop); NoLinks(library);
            var batch = new MoveBatch { Id = Guid.NewGuid().ToString("N"), Desktop = desktop, Library = library, Moves = new List<FileMove>() };
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in System.IO.Directory.EnumerateFileSystemEntries(desktop).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
            {
                string source = Catalog.Normalize(raw); string name = Path.GetFileName(source);
                var attr = File.GetAttributes(source); string extension = Path.GetExtension(source).ToLowerInvariant();
                // Move software shortcuts, not executable installations that rely on adjacent files.
                if ((attr & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0 || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
                    || new[] { ".exe", ".dll", ".msi", ".bat", ".cmd", ".ps1" }.Contains(extension)
                    || (!string.IsNullOrEmpty(runningDirectory) && (Equal(source, Catalog.Normalize(runningDirectory)) || Inside(Catalog.Normalize(runningDirectory), source))))
                { batch.Skipped.Add(name); continue; }
                Zone zone = state.Zones.FirstOrDefault(z => z.Items.Any(e => Equal(e.Path, source))) ?? state.Zones.FirstOrDefault(z => z.CategoryKey == Catalog.Category(source));
                if (zone == null) { batch.Skipped.Add(name + "（没有匹配分区，请先扫描分类）"); continue; }
                string label = new string(zone.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).Take(30).ToArray()).Trim().TrimEnd('.');
                string destinationFolder = Path.Combine(library, "分区-" + label + "-" + SafeName(zone.Id));
                NoLinks(destinationFolder);
                string destination = Path.Combine(destinationFolder, name); int suffix = 2;
                while (Exists(destination) || reserved.Contains(destination))
                {
                    string stem = System.IO.Directory.Exists(source) ? name : Path.GetFileNameWithoutExtension(name);
                    destination = Path.Combine(destinationFolder, stem + " (" + suffix++ + ")" + (System.IO.Directory.Exists(source) ? "" : extension));
                }
                reserved.Add(destination);
                batch.Moves.Add(new FileMove { Source = source, Destination = destination, ZoneId = zone.Id, Identity = FileIdentity.Get(source), Directory = System.IO.Directory.Exists(source), Status = "pending" });
            }
            return batch;
        }
        static string SafeName(string name)
        {
            if (name.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-zA-Z0-9_-]+$")) throw new InvalidDataException("分区标识不能用作收纳目录。");
            return name;
        }
        static void Remap(Settings state, string source, string destination, string identity)
        {
            foreach (Entry entry in state.Zones.SelectMany(z => z.Items).Concat(state.DesktopItems.Select(d => d.Entry)))
            {
                if (Equal(entry.Path, source))
                { if (!string.IsNullOrEmpty(entry.Identity) && entry.Identity != identity) continue; entry.Path = destination; entry.Name = Path.GetFileName(destination); entry.Identity = identity; }
                else if (Inside(entry.Path, source))
                {
                    string target = destination + entry.Path.Substring(source.Length);
                    if (!string.IsNullOrEmpty(entry.Identity)) { try { if (FileIdentity.Get(target) != entry.Identity) continue; } catch { continue; } }
                    entry.Path = target;
                }
            }
        }
        static void Move(FileMove move, bool undo)
        {
            string from = undo ? move.Destination : move.Source, to = undo ? move.Source : move.Destination;
            NoLinks(from); NoLinks(Path.GetDirectoryName(to));
            if (!Exists(from) || FileIdentity.Get(from) != move.Identity) throw new IOException("文件不存在或已被替换：" + from);
            if (Exists(to)) throw new IOException("目标位置已有同名文件，已保留两份：" + to);
            if (!System.IO.Directory.Exists(Path.GetDirectoryName(to)))
            {
                if (undo) throw new IOException("原桌面目录不存在，无法撤销。");
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(to));
            }
            FileIdentity.MoveVerified(from, to, move.Identity);
        }
        public List<string> Execute(Settings state, MoveBatch batch, Action persistSettings, Func<bool> cancelled = null, Action<int, int, string> progress = null)
        {
            ValidateRoots(batch.Desktop, batch.Library);
            if (History.Batches.Any(b => b.Moves.Any(m => m.Status == "pending" || m.Status == "undoing"))) throw new IOException("有尚未恢复的整理操作，请重新启动清桌。");
            if (History.Batches.Count >= 900) ArchiveCompleted();
            if (History.Batches.Count >= 1000) throw new IOException("有过多待恢复批次，请先恢复或处理这些文件。");
            if (History.Batches.Any(b => b.Id == batch.Id)) throw new IOException("这份预览已经执行过，请重新预览。");
            foreach (FileMove move in batch.Moves)
            { ValidateMove(batch, move); if (!state.Zones.Any(z => z.Id == move.ZoneId)) throw new IOException("分区已变化，请重新预览。"); }
            if (cancelled != null && cancelled()) return new List<string>();
            History.Batches.Add(batch); SaveHistory(); // Record intent durably before touching any source.
            var errors = new List<string>();
            int completed = 0;
            foreach (FileMove move in batch.Moves)
            {
                if (cancelled != null && cancelled())
                {
                    foreach (FileMove unstarted in batch.Moves.Where(m => m.Status == "pending")) { unstarted.Status = "failed"; unstarted.Error = "用户取消，原文件未移动。"; }
                    SaveHistory(); break;
                }
                try { Move(move, false); }
                catch (Exception ex) { move.Status = "failed"; move.Error = ex.Message; errors.Add(ex.Message); SaveHistory(); if (progress != null) progress(++completed, batch.Moves.Count, Path.GetFileName(move.Source)); continue; }
                move.Status = "moved"; move.Error = null; SaveHistory();
                Remap(state, move.Source, move.Destination, move.Identity);
                Catalog.Add(state.Zones.First(z => z.Id == move.ZoneId), new[] { move.Destination });
                persistSettings(); // If saving fails, stop. Startup recovery uses the durable journal.
                move.SettingsApplied = true; SaveHistory();
                if (progress != null) progress(++completed, batch.Moves.Count, Path.GetFileName(move.Source));
            }
            return errors;
        }
        public MoveBatch UndoBatch { get { return History.Batches.LastOrDefault(b => b.Moves.Any(m => m.Status == "moved" || m.Status == "undoing" || m.Status == "blocked")); } }
        public bool RestoreEntry(Settings state, Entry entry, Action persistSettings)
        {
            FileMove move = History.Batches.SelectMany(b => b.Moves).LastOrDefault(m => Equal(m.Destination, entry.Path) && (m.Status == "moved" || m.Status == "blocked"));
            if (move == null) return false;
            move.Status = "undoing"; SaveHistory();
            try { Move(move, true); }
            catch (Exception ex) { move.Status = "blocked"; move.Error = ex.Message; SaveHistory(); throw; }
            move.Status = "undone"; move.Error = null; SaveHistory();
            Remap(state, move.Destination, move.Source, move.Identity); entry.Path = move.Source;
            persistSettings(); move.UndoApplied = true; SaveHistory(); return true;
        }
        public void RecordExternalMove(string path)
        {
            foreach (FileMove move in History.Batches.SelectMany(b => b.Moves).Where(m => Equal(m.Destination, path) && m.Status == "moved")) move.Status = "exported";
            SaveHistory();
        }
        public List<string> UndoAll(Settings state, Action persistSettings, Func<bool> cancelled = null, Action<int, int, string> progress = null)
        {
            var errors = new List<string>();
            foreach (MoveBatch batch in History.Batches.AsEnumerable().Reverse().ToList())
            {
                if (cancelled != null && cancelled()) break;
                if (batch.Moves.Any(m => m.Status == "moved" || m.Status == "blocked" || m.Status == "undoing")) errors.AddRange(UndoBatchFiles(state, batch, persistSettings, null, cancelled, progress));
            }
            return errors;
        }
        public List<string> Undo(Settings state, Action persistSettings, Func<bool> cancelled = null, Action<int, int, string> progress = null)
        {
            MoveBatch batch = UndoBatch;
            return batch == null ? new List<string>() : UndoBatchFiles(state, batch, persistSettings, null, cancelled, progress);
        }
        public List<string> UndoSelected(Settings state, MoveBatch batch, IEnumerable<FileMove> selected, Action persistSettings, Func<bool> cancelled = null, Action<int, int, string> progress = null)
        {
            if (!History.Batches.Contains(batch)) throw new InvalidDataException("整理记录已变化，请重新预览。");
            var selection = new HashSet<FileMove>(selected);
            if (selection.Any(m => !batch.Moves.Contains(m))) throw new InvalidDataException("恢复选择不属于此批次。");
            return UndoBatchFiles(state, batch, persistSettings, selection, cancelled, progress);
        }
        List<string> UndoBatchFiles(Settings state, MoveBatch batch, Action persistSettings, HashSet<FileMove> selected, Func<bool> cancelled, Action<int, int, string> progress)
        {
            var errors = new List<string>();
            var moves = batch.Moves.AsEnumerable().Reverse().Where(m => (selected == null || selected.Contains(m)) && (m.Status == "moved" || m.Status == "undoing" || m.Status == "blocked")).ToList();
            int completed = 0;
            foreach (FileMove move in moves)
            {
                if (cancelled != null && cancelled()) break;
                move.Status = "undoing"; SaveHistory();
                try { Move(move, true); }
                catch (Exception ex) { move.Status = "blocked"; move.Error = ex.Message; errors.Add(ex.Message); SaveHistory(); if (progress != null) progress(++completed, moves.Count, Path.GetFileName(move.Source)); continue; }
                move.Status = "undone"; move.Error = null; SaveHistory();
                Remap(state, move.Destination, move.Source, move.Identity); persistSettings();
                move.UndoApplied = true; SaveHistory();
                if (progress != null) progress(++completed, moves.Count, Path.GetFileName(move.Source));
            }
            return errors;
        }
        public void UpdateRenamedDestination(string previous, string current, string identity)
        {
            bool changed = false;
            foreach (FileMove move in History.Batches.SelectMany(b => b.Moves).Where(m => m.Status == "moved" || m.Status == "blocked"))
                if (Equal(move.Destination, previous) && move.Identity == identity) { move.Destination = current; changed = true; }
            if (changed) SaveHistory();
        }
        public int ArchiveCompleted()
        {
            var completed = History.Batches.Where(b => b.Moves.All(m => m.Status == "undone" || m.Status == "failed" || m.Status == "exported")).ToList();
            if (completed.Count == 0) return 0;
            string directory = Path.Combine(Path.GetDirectoryName(HistoryPath), "history-archives"); Directory.CreateDirectory(directory);
            string archive = Path.Combine(directory, "history-" + Guid.NewGuid().ToString("N") + ".json");
            using (var stream = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { new DataContractJsonSerializer(typeof(MoveHistory)).WriteObject(stream, new MoveHistory { Batches = completed }); stream.Flush(true); }
            var original = History.Batches; History.Batches = original.Except(completed).ToList();
            try { SaveHistory(); } catch { History.Batches = original; throw; }
            return completed.Count;
        }
        public List<string> Recover(Settings state, Action persistSettings)
        {
            var errors = new List<string>(); bool changed = false;
            foreach (MoveBatch batch in History.Batches)
                foreach (FileMove move in batch.Moves)
                {
                    if (move.Status == "failed" || move.Status == "exported") continue;
                    try
                    {
                        NoLinks(batch.Desktop); NoLinks(batch.Library);
                        bool atSource = Exists(move.Source) && FileIdentity.Get(move.Source) == move.Identity;
                        bool atDestination = Exists(move.Destination) && FileIdentity.Get(move.Destination) == move.Identity;
                        if (move.Status == "undone")
                        {
                            if (atSource && !move.UndoApplied) { Remap(state, move.Destination, move.Source, move.Identity); changed = true; }
                            continue;
                        }
                        if (atDestination && !atSource)
                        {
                            move.Status = "moved";
                            if (!move.SettingsApplied) Remap(state, move.Source, move.Destination, move.Identity);
                            Zone zone = state.Zones.FirstOrDefault(z => z.Id == move.ZoneId);
                            if (zone != null && !move.SettingsApplied) Catalog.Add(zone, new[] { move.Destination });
                            changed = true;
                        }
                        else if (atSource && !atDestination)
                        {
                            if (move.Status == "pending") move.Status = "failed"; else move.Status = "undone";
                            Remap(state, move.Destination, move.Source, move.Identity); changed = true;
                        }
                        else if (move.Status != "undone")
                        { move.Status = "blocked"; move.Error = "无法确认文件位置，请检查整理记录：" + move.Source; errors.Add(move.Error); changed = true; }
                    }
                    catch (Exception ex) { errors.Add(ex.Message); }
                }
            if (changed)
            {
                SaveHistory(); persistSettings();
                foreach (FileMove move in History.Batches.SelectMany(b => b.Moves))
                { if (move.Status == "moved") move.SettingsApplied = true; else if (move.Status == "undone") move.UndoApplied = true; }
                SaveHistory();
            }
            return errors;
        }
    }
}
