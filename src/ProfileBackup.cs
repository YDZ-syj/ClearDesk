using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Runtime.Serialization.Json;

namespace ClearDesk
{
    public static class ProfileBackup
    {
        public static void Export(string destination, SettingsStore store, DesktopOrganizer organizer)
        {
            if (organizer == null) throw new IOException("移动记录不可读，无法创建完整备份，请保留原配置目录。");
            if (!File.Exists(store.FilePath)) throw new IOException("请先保存当前配置。");
            string parent = Path.GetDirectoryName(store.FilePath);
            string[] files = new[] { store.FilePath, organizer.HistoryPath }.Where(File.Exists).ToArray();
            string archives = Path.Combine(parent, "history-archives");
            if (Directory.Exists(archives)) files = files.Concat(Directory.GetFiles(archives, "history-*.json")).ToArray();
            if (files.Any(f => string.Equals(Catalog.Normalize(f), Catalog.Normalize(destination), StringComparison.OrdinalIgnoreCase))) throw new IOException("备份不能覆盖当前配置或移动记录。");
            string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    {
                        foreach (string file in files)
                        {
                            string name = Path.GetDirectoryName(file) == archives ? "history-archives/" + Path.GetFileName(file) : Path.GetFileName(file);
                            zip.CreateEntryFromFile(file, name);
                        }
                        if (!File.Exists(organizer.HistoryPath))
                            using (var history = zip.CreateEntry("moves.json").Open()) new DataContractJsonSerializer(typeof(MoveHistory)).WriteObject(history, organizer.History);
                        var note = zip.CreateEntry("BACKUP.txt");
                        using (var writer = new StreamWriter(note.Open(), new UTF8Encoding(false)))
                            writer.Write("ClearDesk profile backup: settings and move history only.\nNo file contents are included. Keep the library and desktop files separately.\nRestore these files to the profile directory only while ClearDesk is closed.\nThis backup contains private file paths; do not upload it publicly.\n");
                    }
                    stream.Flush(true);
                }
                AtomicFile.Commit(temp, destination, null);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
