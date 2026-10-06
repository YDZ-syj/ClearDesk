using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace ClearDesk
{
    public static class OrganizerTests
    {
        static string Write(string folder, string name, string content)
        { string path = Path.Combine(folder, name); File.WriteAllText(path, content); return path; }
        public static void Run(Action<bool, string> check, string root)
        {
            string desk = Path.Combine(root, "Desktop"), library = Path.Combine(root, "Library"); Directory.CreateDirectory(desk);
            string document = Write(desk, "文档.txt", "original"), link = Write(desk, "Editor.lnk", "shortcut"), exe = Write(desk, "Keep.exe", "installation");
            string project = Path.Combine(desk, "Project"); Directory.CreateDirectory(project); string child = Write(project, "child.txt", "nested");
            string own = Path.Combine(desk, "ClearDeskSource"); Directory.CreateDirectory(own);
            var state = Settings.Default(); Catalog.Classify(state, new[] { document, link, exe, project });
            Zone docs = state.Zones.First(z => z.Name == "文档"); Catalog.Add(docs, new[] { child });
            var store = new SettingsStore(Path.Combine(root, "move-state", "settings.json")); store.Save(state);
            var organizer = new DesktopOrganizer(Path.Combine(root, "move-state", "moves.json"));
            var plan = organizer.Plan(state, desk, library, Path.Combine(own, "bin"));
            check(plan.Moves.Count == 3 && plan.Skipped.Contains("Keep.exe") && plan.Skipped.Contains("ClearDeskSource"), "only eligible desktop items planned; application installation preserved");
            check(File.Exists(document) && !Directory.Exists(library) && !File.Exists(organizer.HistoryPath), "preview makes no filesystem changes");
            check(organizer.Execute(state, plan, delegate { store.Save(state); }).Count == 0, "files, shortcuts and directory moved successfully");
            check(!File.Exists(document) && !File.Exists(link) && !Directory.Exists(project) && File.Exists(exe), "original desktop items removed only after successful movement");
            var docMove = plan.Moves.First(m => m.Source == document); var folderMove = plan.Moves.First(m => m.Source == project);
            check(File.ReadAllText(docMove.Destination) == "original" && File.Exists(Path.Combine(folderMove.Destination, "child.txt")), "file contents and nested directory contents preserved");
            check(docs.Items.Any(e => e.Path == docMove.Destination) && docs.Items.Any(e => e.Path == Path.Combine(folderMove.Destination, "child.txt")), "all entries including nested references updated");
            docs.Items.RemoveAll(e => e.Path == docMove.Destination); store.Save(state);
            organizer.Recover(state, delegate { store.Save(state); });
            check(!docs.Items.Any(e => e.Path == docMove.Destination), "startup recovery does not resurrect intentionally removed entries");
            Catalog.Add(docs, new[] { docMove.Destination });
            organizer = new DesktopOrganizer(organizer.HistoryPath);
            check(organizer.UndoBatch != null && organizer.History.Batches.Count == 1, "move history survives process restart");
            File.WriteAllText(docMove.Destination, "edited after move");
            check(organizer.Undo(state, delegate { store.Save(state); }).Count == 0 && File.ReadAllText(document) == "edited after move", "undo restores position and keeps post-move edits");
            check(Directory.Exists(project) && File.Exists(child) && docs.Items.Any(e => e.Path == child) && organizer.UndoBatch == null, "undo restores directory and nested entry paths");

            string occupied = Path.Combine(Path.GetDirectoryName(docMove.Destination), "文档.txt"); File.WriteAllText(occupied, "keep existing");
            plan = organizer.Plan(state, desk, library, own);
            docMove = plan.Moves.First(m => m.Source == document);
            check(docMove.Destination != occupied, "existing destination gets a distinct suffix in preview");
            organizer.Execute(state, plan, delegate { store.Save(state); });
            check(File.ReadAllText(occupied) == "keep existing", "existing destination never overwritten");
            Write(desk, "文档.txt", "new desktop file");
            var errors = organizer.Undo(state, delegate { store.Save(state); });
            check(errors.Count == 1 && File.ReadAllText(document) == "new desktop file" && File.Exists(docMove.Destination), "undo collision preserves both files and remains retryable");
            File.Move(document, Path.Combine(desk, "new-desktop-file.txt"));
            check(organizer.Undo(state, delegate { store.Save(state); }).Count == 0 && File.ReadAllText(document) == "edited after move", "undo retries after collision is resolved");

            plan = organizer.Plan(state, desk, library, own); docMove = plan.Moves.First(m => m.Source == document);
            File.Move(document, Path.Combine(root, "saved-original.txt")); Write(desk, "文档.txt", "replacement");
            errors = organizer.Execute(state, plan, delegate { store.Save(state); });
            check(errors.Count == 1 && File.ReadAllText(document) == "replacement", "source replaced after preview is not moved");
            organizer.Undo(state, delegate { store.Save(state); });

            plan = organizer.Plan(state, desk, library, own); docMove = plan.Moves.First(m => m.Source == document);
            Directory.CreateDirectory(Path.GetDirectoryName(docMove.Destination)); File.WriteAllText(docMove.Destination, "new collision");
            errors = organizer.Execute(state, plan, delegate { store.Save(state); });
            check(errors.Count == 1 && File.Exists(document) && File.ReadAllText(docMove.Destination) == "new collision", "destination created after preview is not overwritten");
            organizer.Undo(state, delegate { store.Save(state); });

            bool rejected = false; try { organizer.Plan(state, desk, Path.Combine(desk, "unsafe"), own); } catch (IOException) { rejected = true; }
            check(rejected, "storage inside desktop rejected");
            rejected = false; try { organizer.Plan(state, desk, Path.GetPathRoot(desk), own); } catch (IOException) { rejected = true; }
            check(rejected, "storage containing desktop rejected");

            // A crash after the rename but before settings save must recover from the journal.
            string recoveryDesk = Path.Combine(root, "RecoveryDesktop"), recoveryLib = Path.Combine(root, "RecoveryLibrary"); Directory.CreateDirectory(recoveryDesk);
            string recoveryFile = Write(recoveryDesk, "recover.txt", "recoverable"); var recoveryState = Settings.Default(); Catalog.Classify(recoveryState, new[] { recoveryFile });
            var recoveryStore = new SettingsStore(Path.Combine(root, "recovery", "settings.json")); recoveryStore.Save(recoveryState);
            var recovery = new DesktopOrganizer(Path.Combine(root, "recovery", "moves.json")); var recoveryPlan = recovery.Plan(recoveryState, recoveryDesk, recoveryLib, null);
            rejected = false; try { recovery.Execute(recoveryState, recoveryPlan, delegate { throw new IOException("simulated settings failure"); }); } catch (IOException) { rejected = true; }
            check(rejected && !File.Exists(recoveryFile), "settings failure stops after a durable move record");
            recoveryState = recoveryStore.Load(); recovery = new DesktopOrganizer(recovery.HistoryPath);
            check(recovery.Recover(recoveryState, delegate { recoveryStore.Save(recoveryState); }).Count == 0 && recoveryState.Zones.SelectMany(z => z.Items).Any(e => e.Path == recoveryPlan.Moves[0].Destination), "restart repairs settings after interrupted move");

            recovery.History.Batches[0].Moves[0].Status = "pending";
            using (var stream = File.Create(recovery.HistoryPath)) new DataContractJsonSerializer(typeof(MoveHistory)).WriteObject(stream, recovery.History);
            recoveryState = recoveryStore.Load(); recovery = new DesktopOrganizer(recovery.HistoryPath); recovery.Recover(recoveryState, delegate { recoveryStore.Save(recoveryState); });
            check(recovery.History.Batches[0].Moves[0].Status == "moved", "interrupted pre-completion journal reconciles by stable file identity");

            rejected = false; try { recovery.Undo(recoveryState, delegate { throw new IOException("simulated undo save failure"); }); } catch (IOException) { rejected = true; }
            recoveryState = recoveryStore.Load(); recovery = new DesktopOrganizer(recovery.HistoryPath); recovery.Recover(recoveryState, delegate { recoveryStore.Save(recoveryState); });
            check(rejected && File.Exists(recoveryFile) && recoveryState.Zones.SelectMany(z => z.Items).Any(e => e.Path == recoveryFile), "restart repairs settings after interrupted undo");
            recoveryPlan = recovery.Plan(recoveryState, recoveryDesk, recoveryLib, null); recovery.Execute(recoveryState, recoveryPlan, delegate { recoveryStore.Save(recoveryState); });
            recovery = new DesktopOrganizer(recovery.HistoryPath); recovery.Recover(recoveryState, delegate { recoveryStore.Save(recoveryState); });
            check(recovery.History.Batches[0].Moves[0].Status == "undone" && recovery.History.Batches[1].Moves[0].Status == "moved", "later moves do not reactivate previously undone batches");

            string movedPath = recoveryPlan.Moves[0].Destination;
            File.Move(movedPath, Path.Combine(root, "keep-moved-original.txt")); File.WriteAllText(movedPath, "unrelated replacement");
            errors = recovery.Undo(recoveryState, delegate { recoveryStore.Save(recoveryState); });
            check(errors.Count == 1 && !File.Exists(recoveryFile) && File.ReadAllText(movedPath) == "unrelated replacement", "undo does not restore an unrelated replacement file");

            string badHistory = Path.Combine(root, "bad-moves.json"); File.WriteAllText(badHistory, "invalid history"); rejected = false;
            try { new DesktopOrganizer(badHistory); } catch { rejected = true; }
            check(rejected && File.ReadAllText(badHistory) == "invalid history", "corrupt history rejected without replacing recovery evidence");
            rejected = false; recovery.History.Batches[1].Moves[0].Destination = Path.Combine(root, "outside-library.txt");
            using (var stream = File.Create(badHistory)) new DataContractJsonSerializer(typeof(MoveHistory)).WriteObject(stream, recovery.History);
            try { new DesktopOrganizer(badHistory); } catch { rejected = true; }
            check(rejected, "history paths outside approved storage rejected");
        }
    }
}
