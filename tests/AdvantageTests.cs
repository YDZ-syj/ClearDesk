using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ClearDesk
{
    public static class AdvantageTests
    {
        public static void Run(Action<bool, string> check, string root)
        {
            string desktop = Path.Combine(root, "advanced-desktop"), library = Path.Combine(root, "advanced-library"), profile = Path.Combine(root, "advanced-profile");
            Directory.CreateDirectory(desktop);
            string one = Path.Combine(desktop, "one.txt"), two = Path.Combine(desktop, "two.txt"), three = Path.Combine(desktop, "three.txt");
            foreach (string path in new[] { one, two, three }) File.WriteAllText(path, Path.GetFileName(path));
            var state = Settings.Default(); Catalog.Classify(state, new[] { one, two, three });
            var store = new SettingsStore(Path.Combine(profile, "settings.json")); store.Save(state);
            var organizer = new DesktopOrganizer(Path.Combine(profile, "moves.json"));
            Zone zone = state.Zones.First(z => z.CategoryKey == "文档"); Entry original = zone.Items[0];
            string renamed = Path.Combine(desktop, "renamed.txt"); File.Move(one, renamed);
            check(EntryTracking.Reconcile(state, organizer) && zone.Items[0] == original && original.Path == renamed && original.Name == "renamed.txt", "external rename preserves entry and manual ordering");
            File.WriteAllText(one, "different file"); Catalog.Classify(state, new[] { one });
            check(zone.Items.Count == 4 && zone.Items[0].Identity != zone.Items.Last().Identity, "replacement at a reused path becomes a separate file object");
            store.Save(state); var reload = store.Load();
            check(reload.Zones.First(z => z.Id == zone.Id).Items[0].Identity == original.Identity, "entry identities survive save and reload");
            var plan = organizer.Plan(state, desktop, library, null);
            var preview = new MovePreviewDialog(state, plan, false); preview.Rows.ForEach(r => r.Selected = r.Move.Source == renamed); var chosen = preview.SelectedMoves;
            check(chosen.Count == 1 && chosen[0].Source == renamed, "move preview supports selecting individual files");
            plan.Moves = chosen; organizer.Execute(state, plan, delegate { store.Save(state); });
            check(File.Exists(two) && File.Exists(three) && File.Exists(one) && !File.Exists(renamed), "unselected desktop files remain untouched");
            var move = plan.Moves[0]; string changed = Path.Combine(Path.GetDirectoryName(move.Destination), "changed.txt"); File.Move(move.Destination, changed);
            EntryTracking.Reconcile(state, organizer);
            check(move.Destination == changed && original.Path == changed, "renaming an organized file also updates its recovery destination");
            organizer.UndoSelected(state, plan, chosen, delegate { store.Save(state); });
            check(File.Exists(renamed) && File.ReadAllText(renamed) == "one.txt" && original.Path == renamed, "organized files remain recoverable after external rename");
            check(organizer.ArchiveCompleted() == 1 && organizer.History.Batches.Count == 0, "completed history can be archived without deleting its evidence");
            check(Directory.GetFiles(Path.Combine(profile, "history-archives"), "*.json").Length == 1, "archive is durably retained in a separate file");
            plan = organizer.Plan(state, desktop, library, null); int progressCount = 0; bool cancel = false;
            organizer.Execute(state, plan, delegate { store.Save(state); }, () => cancel, delegate(int current, int total, string name) { progressCount++; cancel = true; });
            check(plan.Moves.Count(m => m.Status == "moved") == 1 && plan.Moves.Count(m => m.Status == "pending") == 0 && progressCount == 1, "cancel stops between files and leaves no misleading pending intents");
            check(plan.Moves.Where(m => m.Status == "failed").All(m => File.Exists(m.Source)), "canceled unstarted files remain at their original paths");
            check(organizer.ArchiveCompleted() == 0 && organizer.UndoBatch == plan, "archiving never removes a partially executed recoverable batch");
            organizer = new DesktopOrganizer(organizer.HistoryPath); state = store.Load(); organizer.Recover(state, delegate { store.Save(state); });
            check(organizer.UndoAll(state, delegate { store.Save(state); }).Count == 0 && new[] { one, two, three, renamed }.All(File.Exists), "partial cancellation remains recoverable after process restart");
            plan = organizer.Plan(state, desktop, library, null); organizer.Execute(state, plan, delegate { store.Save(state); });
            var undoPreview = new MovePreviewDialog(state, plan, true); undoPreview.Rows.ForEach(r => r.Selected = r.Move == plan.Moves[0]);
            organizer.UndoSelected(state, plan, undoPreview.SelectedMoves, delegate { store.Save(state); });
            check(File.Exists(plan.Moves[0].Source) && plan.Moves.Skip(1).All(m => File.Exists(m.Destination)), "selective restore leaves unselected organized files in the library");
            cancel = false; organizer.UndoAll(state, delegate { store.Save(state); }, () => cancel, delegate { cancel = true; });
            check(organizer.UndoBatch != null, "canceling restore preserves the remaining recovery records");
            organizer.UndoAll(state, delegate { store.Save(state); });
            check(organizer.UndoBatch == null && plan.Moves.All(m => File.Exists(m.Source)), "remaining files can be restored after canceling a restore");
            store.Save(state); string backup = Path.Combine(root, "profile-backup.zip"); ProfileBackup.Export(backup, store, organizer);
            using (var zip = ZipFile.OpenRead(backup))
            {
                check(zip.GetEntry("settings.json") != null && zip.GetEntry("moves.json") != null && zip.Entries.Any(e => e.FullName.StartsWith("history-archives/")), "complete backup includes settings, move history and archived records");
                check(zip.Entries.All(e => e.FullName == "settings.json" || e.FullName == "moves.json" || e.FullName == "BACKUP.txt" || e.FullName.StartsWith("history-archives/")), "profile backups never include actual desktop or library file contents");
            }
            bool rejected = false; try { ProfileBackup.Export(store.FilePath, store, organizer); } catch (IOException) { rejected = true; }
            check(rejected && store.Load().Zones.Count == state.Zones.Count, "backup cannot overwrite the active configuration");
            plan = organizer.Plan(state, desktop, library, null); organizer.Execute(state, plan, delegate { store.Save(state); });
            string reused = plan.Moves[0].Source; File.WriteAllText(reused, "new object"); Catalog.Classify(state, new[] { reused }); store.Save(state);
            organizer.Recover(state, delegate { store.Save(state); });
            check(state.Zones.SelectMany(z => z.Items).Any(e => e.Path == reused && e.Exists), "recovery never remaps a new file at a previously used source pathname");
            string nestedRoot = Path.Combine(desktop, "nested"), child; Directory.CreateDirectory(nestedRoot); File.WriteAllText(child = Path.Combine(nestedRoot, "child.txt"), "child");
            Catalog.Add(state.Zones[0], new[] { nestedRoot, child }); Entry parentEntry = state.Zones[0].Items.First(e => e.Path == nestedRoot); Entry childEntry = state.Zones[0].Items.First(e => e.Path == child);
            string nestedRename = Path.Combine(desktop, "nested-renamed"); Directory.Move(nestedRoot, nestedRename); EntryTracking.Reconcile(state, organizer);
            check(parentEntry.Path == nestedRename && childEntry.Path == Path.Combine(nestedRename, "child.txt") && childEntry.Exists, "folder rename updates tracked children without recreating entries");
            string external = Path.Combine(root, "external-large.bin"); File.WriteAllBytes(external, new byte[2 * 1024 * 1024]); var externalEntry = new Entry { Path = external, Name = "external-large.bin", Identity = FileIdentity.Get(external) }; state.Zones[0].Items.Add(externalEntry);
            int cancellationChecks = 0; rejected = false;
            try { DesktopTransfer.Extract(state, organizer, state.Zones[0], externalEntry, desktop, 0, 0, delegate { store.Save(state); }, () => ++cancellationChecks >= 3); } catch (OperationCanceledException) { rejected = true; }
            check(rejected && File.Exists(external) && !File.Exists(Path.Combine(desktop, "external-large.bin")) && !Directory.GetDirectories(desktop, ".cleardesk-copy-*").Any(), "canceled copy removes its partial staging area and preserves the original");
            string guardFile = Path.Combine(root, "leased.txt"), guardTarget = Path.Combine(root, "leased-target.txt"); File.WriteAllText(guardFile, "preserve");
            using (var lease = new FileStream(guardFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            { rejected = false; try { FileIdentity.MoveVerified(guardFile, guardTarget, FileIdentity.Get(guardFile)); } catch (IOException) { rejected = true; } check(rejected && File.Exists(guardFile) && !File.Exists(guardTarget), "a conflicting open file lease blocks movement without losing the original"); }
            Rect accessible = DesktopWindows.AccessibleBounds(new Rect(1600, -1000, 310, 360), new[] { new Rect(0, 0, 1920, 1080) });
            check(accessible.Top >= 0 && accessible.Left >= 0, "unreachable title bars return to an available screen");
            Rect secondScreen = new Rect(-1200, 100, 310, 360);
            check(DesktopWindows.AccessibleBounds(secondScreen, new[] { new Rect(0, 0, 1920, 1080), new Rect(-1920, 0, 1920, 1080) }) == secondScreen, "accessible layouts on negative-coordinate monitors are preserved");
            string legacyDesk = Path.Combine(root, "legacy-rename-desktop"), legacyLibrary = Path.Combine(root, "legacy-rename-library"); Directory.CreateDirectory(legacyDesk);
            string legacySource = Path.Combine(legacyDesk, "legacy.txt"); File.WriteAllText(legacySource, "legacy content");
            var legacyState = Settings.Default(); Catalog.Classify(legacyState, new[] { legacySource });
            var legacyStore = new SettingsStore(Path.Combine(root, "legacy-rename-profile", "settings.json"));
            var legacyOrganizer = new DesktopOrganizer(Path.Combine(root, "legacy-rename-profile", "moves.json"));
            var legacyPlan = legacyOrganizer.Plan(legacyState, legacyDesk, legacyLibrary, null); legacyOrganizer.Execute(legacyState, legacyPlan, delegate { legacyStore.Save(legacyState); });
            Entry legacyEntry = legacyState.Zones.SelectMany(z => z.Items).First(); legacyEntry.Identity = null; legacyStore.Save(legacyState);
            string legacyRename = Path.Combine(Path.GetDirectoryName(legacyPlan.Moves[0].Destination), "renamed-legacy.txt"); File.Move(legacyPlan.Moves[0].Destination, legacyRename);
            legacyState = legacyStore.Load(); legacyOrganizer = new DesktopOrganizer(legacyOrganizer.HistoryPath);
            EntryTracking.Reconcile(legacyState, legacyOrganizer);
            check(legacyState.Zones.SelectMany(z => z.Items).First().Path == legacyRename, "old configurations recover file identity from existing move history during migration");
            check(legacyOrganizer.Recover(legacyState, delegate { legacyStore.Save(legacyState); }).Count == 0, "startup recognizes renamed old-version organized files before reporting recovery errors");
            foreach (Zone legacyZone in legacyState.Zones) legacyZone.Items.Clear();
            string removedRename = Path.Combine(Path.GetDirectoryName(legacyRename), "removed-entry-renamed.txt"); File.Move(legacyRename, removedRename); EntryTracking.Reconcile(legacyState, legacyOrganizer);
            check(legacyOrganizer.UndoAll(legacyState, delegate { legacyStore.Save(legacyState); }).Count == 0 && File.ReadAllText(legacySource) == "legacy content", "renamed organized files remain recoverable even after their entry is removed");
            var emptyStore = new SettingsStore(Path.Combine(root, "empty-backup-profile", "settings.json")); emptyStore.Save(Settings.Default());
            var emptyOrganizer = new DesktopOrganizer(Path.Combine(root, "empty-backup-profile", "moves.json"));
            string emptyBackup = Path.Combine(root, "empty-backup.zip"); ProfileBackup.Export(emptyBackup, emptyStore, emptyOrganizer);
            using (var zip = ZipFile.OpenRead(emptyBackup)) check(zip.GetEntry("moves.json") != null, "a profile without prior moves still exports an explicit empty recovery history");
            string atomic = Path.Combine(root, "atomic-profile.json"), temp = atomic + ".tmp"; File.WriteAllText(atomic, "original"); File.WriteAllText(temp, "new");
            var readLease = new FileStream(atomic, FileMode.Open, FileAccess.Read, FileShare.Read);
            var releaseLease = Task.Run(delegate { Thread.Sleep(90); readLease.Dispose(); });
            AtomicFile.Commit(temp, atomic, atomic + ".bak"); releaseLease.GetAwaiter().GetResult();
            check(File.ReadAllText(atomic) == "new" && File.ReadAllText(atomic + ".bak") == "original", "temporary metadata sharing conflicts retry without corrupting the saved profile or its backup");
        }
        public static void Ui(Action<bool, string> check, DeskApp app)
        {
            var original = app.State; var context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) }; int ticks = 0;
            try
            {
                timer.Tick += delegate { ticks++; if (ticks >= 4) app.CancelFileOperation(); }; timer.Start();
                int uiThread = Thread.CurrentThread.ManagedThreadId;
                var job = app.RunBackground("test background work", delegate(Settings state, Func<bool> cancelled, Action<int, int, string> progress)
                {
                    int workerThread = Thread.CurrentThread.ManagedThreadId;
                    while (!cancelled()) Thread.Sleep(5);
                    state.RestoreOnExit = !state.RestoreOnExit; progress(1, 1, "test"); return workerThread;
                });
                bool secondRejected = false;
                var second = app.RunBackground("second work", delegate { return true; });
                Pump(second); try { second.GetAwaiter().GetResult(); } catch (InvalidOperationException) { secondRejected = true; }
                Pump(job); timer.Stop(); int thread = job.GetAwaiter().GetResult();
                check(thread != uiThread && ticks >= 4, "background file work keeps the actual WPF event loop responsive");
                check(secondRejected, "concurrent file operations cannot write the profile simultaneously");
                check(!app.FileOperationBusy && app.State.RestoreOnExit != original.RestoreOnExit && app.Store.Load().RestoreOnExit == app.State.RestoreOnExit, "cancel finishes the operation cleanly and persists completed metadata");
                bool startupAvailable = app.StartupSettingsAvailable;
                check(app.Manager.CreateMoreMenu().Items.OfType<System.Windows.Controls.MenuItem>().Any(m => (string)m.Header == "检查更新") && app.StartupSettingsAvailable == startupAvailable, "background work preserves the startup and update settings interface");
            }
            finally { timer.Stop(); app.State = original; app.Store.Save(original); app.Manager.Refresh(); SynchronizationContext.SetSynchronizationContext(context); }
        }
        static void Pump(Task task)
        {
            var frame = new DispatcherFrame(); var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) }; var deadline = DateTime.UtcNow.AddSeconds(10);
            poll.Tick += delegate { if (task.IsCompleted || DateTime.UtcNow > deadline) frame.Continue = false; }; poll.Start(); Dispatcher.PushFrame(frame); poll.Stop();
            if (!task.IsCompleted) throw new Exception("Background operation timeout.");
        }
    }
}
