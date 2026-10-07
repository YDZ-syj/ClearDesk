using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClearDesk
{
    public static class EntryTracking
    {
        // Search only each tracked object's parent. Never crawl drives or follow links.
        public static bool Reconcile(Settings state, DesktopOrganizer organizer = null)
        {
            bool changed = false;
            var entries = state.Zones.SelectMany(z => z.Items).Concat(state.DesktopItems.Select(d => d.Entry)).Where(e => !e.IsShell).ToList();
            if (organizer != null)
                foreach (FileMove move in organizer.History.Batches.SelectMany(b => b.Moves).Where(m => m.Status == "moved" || m.Status == "blocked" || m.Status == "pending"))
                {
                    foreach (Entry entry in entries.Where(e => string.Equals(e.Path, move.Destination, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(e.Identity))) { entry.Identity = move.Identity; changed = true; }
                    if (!entries.Any(e => e.Path == move.Destination && e.Identity == move.Identity)) entries.Add(new Entry { Path = move.Destination, Identity = move.Identity });
                }
            var parents = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in entries)
            {
                string current = null;
                try { if (File.Exists(entry.Path) || Directory.Exists(entry.Path)) current = FileIdentity.Get(entry.Path); } catch { }
                if (string.IsNullOrEmpty(entry.Identity))
                { if (current != null) { entry.Identity = current; changed = true; } continue; }
                if (current == entry.Identity) continue;
                string parent = Path.GetDirectoryName(entry.Path);
                Dictionary<string, List<string>> candidates;
                if (!parents.TryGetValue(parent, out candidates))
                {
                    candidates = new Dictionary<string, List<string>>(); parents.Add(parent, candidates);
                    try
                    {
                        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) == 0)
                            foreach (string path in Directory.EnumerateFileSystemEntries(parent))
                            {
                                try
                                {
                                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                                    string id = FileIdentity.Get(path); List<string> paths;
                                    if (!candidates.TryGetValue(id, out paths)) candidates.Add(id, paths = new List<string>());
                                    paths.Add(path);
                                }
                                catch { }
                            }
                    }
                    catch { }
                }
                List<string> matches;
                if (!candidates.TryGetValue(entry.Identity, out matches) || matches.Count != 1) continue;
                string previous = entry.Path, destination = matches[0];
                if (organizer != null) organizer.UpdateRenamedDestination(previous, destination, entry.Identity);
                entry.Path = destination; entry.Name = Path.GetFileName(destination); changed = true;
                // Update nested references only when they still identify the same child.
                foreach (Entry child in entries.Where(e => e != entry && e.Path.StartsWith(previous.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    string target = destination + child.Path.Substring(previous.Length);
                    try
                    {
                        if (!string.IsNullOrEmpty(child.Identity) && FileIdentity.Get(target) == child.Identity) { child.Path = target; changed = true; }
                    }
                    catch { }
                }
            }
            return changed;
        }
    }
}
