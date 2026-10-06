using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ClearDesk
{
    public static class ZoneTests
    {
        public static void Run(Action<bool, string> check, string root)
        {
            Settings state = Settings.Default();
            check(state.Zones.All(z => z.Collapsed) && state.StartCollapsed, "new zones and default startup use folded title bars");
            check(state.AutoClassifyDesktop && state.AutoArrangeZones && state.HideEmptyZones && state.HideDesktopIcons, "automatic grouping and desktop presentation defaults enabled");
            Zone apps = state.Zones.First(z => z.CategoryKey == "应用"); string originalId = apps.Id;
            Catalog.Rename(state, apps, "常用工具");
            string shortcut = Path.Combine(root, "new-shortcut.lnk"); File.WriteAllText(shortcut, "fixture");
            int originalCount = state.Zones.Count; Catalog.Classify(state, new[] { shortcut });
            check(apps.Name == "常用工具" && apps.Id == originalId && apps.Items.Any(e => e.Path == shortcut) && state.Zones.Count == originalCount, "renaming a category preserves identity and automatic routing");
            bool rejected = false; try { Catalog.Rename(state, apps, "文档"); } catch (ArgumentException) { rejected = true; }
            check(rejected && apps.Name == "常用工具", "duplicate names rejected without changing current name");
            rejected = false; try { Catalog.Rename(state, apps, "   "); } catch (ArgumentException) { rejected = true; }
            check(rejected, "empty zone names rejected");
            Catalog.AddSystemEntries(state); Catalog.AddSystemEntries(state);
            check(state.Zones.SelectMany(z => z.Items).Count(e => e.IsShell) == 2 && state.Zones.SelectMany(z => z.Items).Where(e => e.IsShell).All(e => e.Exists), "system entries are available without duplicate creation");
            var store = new SettingsStore(Path.Combine(root, "zone-state", "settings.json")); store.Save(state); Settings loaded = store.Load();
            check(loaded.Zones.First(z => z.Id == originalId).CategoryKey == "应用" && loaded.Zones.SelectMany(z => z.Items).Count(e => e.IsShell) == 2, "renamed category keys and shell entries survive reload");
            loaded.Zones[0].Items.Add(new Entry { Path = "shell:UnknownCommand", Name = "invalid" });
            rejected = false; try { SettingsStore.Validate(loaded); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "configuration cannot add arbitrary shell commands");
            check(Catalog.VisibleZones(state).Count() == 2, "unused default categories omitted from desktop");
            var manual = new Zone { Name = "我的分区" }; state.Zones.Add(manual);
            check(Catalog.VisibleZones(state).Contains(manual), "empty custom zones remain visible for adding files");
            foreach (Zone z in state.Zones) z.Collapsed = false;
            ZoneLayout.PrepareForStartup(state, -1280, 0, 1280, 720);
            check(state.Zones.All(z => z.Collapsed), "old expanded layouts fold at startup by default");
            check(Catalog.VisibleZones(state).All(z => z.X >= -1280 && z.Y >= 0 && z.X + z.Width <= 0 && z.Y + ZoneLayout.CollapsedHeight <= 720), "automatic folded layout fits a monitor with negative coordinates");
            var all = Settings.Default(); foreach (Zone z in all.Zones) z.Collapsed = false;
            ZoneLayout.Arrange(all.Zones, 0, 0, 1280, 720);
            check(all.Zones.All(z => z.X >= 0 && z.Y >= 0 && z.X + z.Width <= 1280 && z.Y + z.Height <= 720), "expanded default zones stay inside work area");
            bool overlap = false;
            for (int i = 0; i < all.Zones.Count; i++) for (int j = i + 1; j < all.Zones.Count; j++)
                if (new Rect(all.Zones[i].X, all.Zones[i].Y, all.Zones[i].Width, all.Zones[i].Height).IntersectsWith(new Rect(all.Zones[j].X, all.Zones[j].Y, all.Zones[j].Width, all.Zones[j].Height))) overlap = true;
            check(!overlap, "expanded default zones do not overlap");
            state.StartCollapsed = false; state.AutoArrangeZones = false; state.Zones[0].Collapsed = false;
            state.Zones[0].X = 321; state.LayoutRevision = 3; ZoneLayout.PrepareForStartup(state, 0, 0, 1280, 720);
            check(!state.Zones[0].Collapsed && state.Zones[0].X == 321, "users can keep an expanded state and manual positions");
            store.Save(state); loaded = store.Load();
            check(!loaded.StartCollapsed && !loaded.AutoArrangeZones, "explicit false preferences preserved on reload");
            var fixedZone = state.Zones[0]; fixedZone.Locked = true; fixedZone.X = 600; fixedZone.Y = 40; fixedZone.Width = 330; fixedZone.Height = 390;
            var fixedBounds = new Rect(fixedZone.X, fixedZone.Y, fixedZone.Width, fixedZone.Height);
            state.AutoArrangeZones = true; ZoneLayout.PrepareForStartup(state, 0, 0, 1280, 720);
            check(new Rect(fixedZone.X, fixedZone.Y, fixedZone.Width, fixedZone.Height) == fixedBounds, "locked zone geometry survives startup and automatic arrangement");
            check(Catalog.VisibleZones(state).Where(z => !z.Locked).All(z => !new Rect(z.X, z.Y, z.Width, z.Collapsed ? ZoneLayout.CollapsedHeight : z.Height).IntersectsWith(new Rect(fixedZone.X, fixedZone.Y, fixedZone.Width, ZoneLayout.CollapsedHeight))), "automatic arrangement avoids a locked title bar");
            store.Save(state); loaded = store.Load();
            check(loaded.Zones[0].Locked && !loaded.Zones[1].Locked, "per-zone lock preferences survive reload");
            string legacyLock = Path.Combine(root, "legacy-lock.json"); File.WriteAllText(legacyLock, File.ReadAllText(store.FilePath).Replace("\"Locked\":true,", "").Replace("\"Locked\":false,", ""));
            check(SettingsStore.Read(legacyLock).Zones.All(z => !z.Locked), "old configurations default to unlocked without losing entries");
            Interaction(check, root);
        }
        static void Interaction(Action<bool, string> check, string root)
        {
            var state = Settings.Default();
            check(state.RestoreOnExit, "normal exit defaults to restoring organized files");
            Zone zone = state.Zones.First(z => z.CategoryKey == "文档");
            var first = new Entry { Name = "z.txt", Path = Path.Combine(root, "z.txt") };
            var second = new Entry { Name = "a.txt", Path = Path.Combine(root, "a.txt") };
            var third = new Entry { Name = "m.txt", Path = Path.Combine(root, "m.txt") };
            foreach (Entry e in new[] { first, second, third }) { File.WriteAllText(e.Path, e.Name); zone.Items.Add(e); }
            Catalog.Place(zone, zone, third, first);
            check(zone.Items.SequenceEqual(new[] { third, first, second }), "drag insertion preserves chosen order rather than alphabetizing");
            Catalog.Place(zone, zone, third, third);
            check(zone.Items[0] == third, "dropping onto itself keeps its position");
            var target = state.Zones[0]; target.Items.Add(second); Catalog.Place(zone, target, first, second);
            check(!zone.Items.Contains(first) && target.Items.SequenceEqual(new[] { first, second }), "cross-zone drag inserts before target without duplication");
            var orderStore = new SettingsStore(Path.Combine(root, "order-state", "settings.json")); orderStore.Save(state);
            check(orderStore.Load().Zones.First(z => z.Id == target.Id).Items.Select(e => e.Name).SequenceEqual(new[] { "z.txt", "a.txt" }), "user ordering survives saving and reloading");
            var data = EntryDrag.Create(new DraggedEntry { Entry = first, Zone = target });
            check(data.GetDataPresent(DataFormats.FileDrop) && ((string[])data.GetData(DataFormats.FileDrop))[0] == first.Path && data.GetDataPresent("ClearDesk.Entry"), "drag payload supports Explorer files and internal zone moves");
            var shell = new Entry { Name = "回收站", Path = "shell:RecycleBinFolder" };
            check(!EntryDrag.Create(new DraggedEntry { Entry = shell }).GetDataPresent(DataFormats.FileDrop), "virtual system entries are never exported as fake filesystem paths");
            check(ShellIcons.Get("shell:RecycleBinFolder") != null && ShellIcons.Get("shell:MyComputerFolder") != null, "shell namespace entries resolve real Windows system icons");
            string desktop = Path.Combine(root, "interaction-desktop"), library = Path.Combine(root, "interaction-library"); Directory.CreateDirectory(desktop);
            string a = Path.Combine(desktop, "one.txt"), b = Path.Combine(desktop, "two.txt"); File.WriteAllText(a, "one"); File.WriteAllText(b, "two");
            var store = new SettingsStore(Path.Combine(root, "interaction-state", "settings.json"));
            var organizer = new DesktopOrganizer(Path.Combine(root, "interaction-state", "moves.json"));
            state = Settings.Default(); Catalog.Classify(state, new[] { a, b }); zone = state.Zones.First(z => z.CategoryKey == "文档");
            var batch = organizer.Plan(state, desktop, library, null); organizer.Execute(state, batch, delegate { store.Save(state); });
            Entry one = zone.Items.First(e => e.Name == "one.txt");
            DesktopTransfer.Extract(state, organizer, zone, one, desktop, 123, 234, delegate { store.Save(state); });
            check(File.Exists(a) && !File.Exists(batch.Moves.First(m => m.Source == a).Destination) && !zone.Items.Contains(one), "drag out restores the recorded file and removes it from its zone");
            check(Catalog.Classify(state, new[] { a }) == 0 && state.DesktopItems.Single().Entry.Path == a, "extracted desktop entry is not immediately recaptured by automatic classification");
            var loaded = store.Load(); check(loaded.DesktopItems.Single().X == 123 && loaded.DesktopItems.Single().Y == 234, "extracted item position and exclusion survive restart");
            File.WriteAllText(b, "conflict");
            check(organizer.UndoAll(state, delegate { store.Save(state); }).Count == 1 && File.ReadAllText(b) == "conflict", "restore-all reports a conflict and never overwrites desktop contents");
            File.Delete(b); check(organizer.UndoAll(state, delegate { store.Save(state); }).Count == 0 && File.ReadAllText(b) == "two" && organizer.UndoBatch == null, "restore-all retries safely and restores every remaining organized file");
            var outside = new Entry { Name = "z.txt", Path = first.Path }; zone.Items.Add(outside); File.WriteAllText(Path.Combine(desktop, "z.txt"), "keep");
            DesktopTransfer.Extract(state, organizer, zone, outside, desktop, 300, 200, delegate { store.Save(state); });
            check(File.Exists(first.Path) && Path.GetFileName(outside.Path) == "z (2).txt" && File.ReadAllText(Path.Combine(desktop, "z.txt")) == "keep", "dragging a referenced external file copies it with a unique name and preserves both originals");
            state.RestoreOnExit = false; store.Save(state);
            check(!store.Load().RestoreOnExit, "explicit keep-files-in-library preference survives reload");
            string c = Path.Combine(desktop, "external-move.txt"); File.WriteAllText(c, "external"); Catalog.Classify(state, new[] { c });
            var nextBatch = organizer.Plan(state, desktop, library, null); organizer.Execute(state, nextBatch, delegate { store.Save(state); });
            FileMove exported = nextBatch.Moves.First(m => m.Source == c); string externalDestination = Path.Combine(root, "external-destination.txt");
            File.Move(exported.Destination, externalDestination); organizer.RecordExternalMove(exported.Destination);
            organizer = new DesktopOrganizer(organizer.HistoryPath);
            check(organizer.Recover(state, delegate { store.Save(state); }).Count == 0 && organizer.History.Batches.Last().Moves.First(m => m.Source == c).Status == "exported", "an external Explorer move is retained in history without creating a false recovery error");
            check(organizer.UndoAll(state, delegate { store.Save(state); }).Count == 0 && File.ReadAllText(externalDestination) == "external", "restore-all restores earlier batches while preserving externally moved files");
        }
        public static void Ui(Action<bool, string> check, DeskApp app, Zone zone)
        {
            app.State.AutoArrangeZones = false; zone.Collapsed = true; zone.Height = 420;
            var window = new ZoneWindow(app, zone);
            check(window.Height == ZoneLayout.CollapsedHeight && window.MinHeight == window.MaxHeight && window.IsContentCollapsed, "folded window exactly matches header and contains no tiles or resize grip");
            window.SetCollapsed(false);
            check(window.Height == 420 && !window.IsContentCollapsed, "unfold restores saved height and content");
            app.State.AutoArrangeZones = true; double oldWidth = window.Width;
            window.ResizeBy(80, 60, false, false);
            check(window.Width == oldWidth + 80 && window.Height == 480 && zone.Height == 480 && !app.State.AutoArrangeZones, "manual resizing changes stored geometry and disables automatic layout resets");
            window.ResizeBy(-80, -60, false, false);
            window.SetLocked(true); app.State.AutoArrangeZones = true;
            window.ResizeBy(100, 100, true, true);
            check(window.Width == oldWidth && window.Height == 420 && app.State.AutoArrangeZones, "locked resize is ignored without changing geometry or layout preferences");
            window.SetCollapsed(true); window.SetCollapsed(false);
            check(zone.Locked && window.Height == 420, "folding a locked zone preserves its lock and expanded height");
            window.SetLocked(false); app.State.AutoArrangeZones = false;
            window.ResizeBy(20, 20, false, false);
            check(window.Width == oldWidth + 20 && window.Height == 440, "unlocking restores manual resizing");
            window.ResizeBy(-20, -20, false, false);
            for (int i = 0; i < 5; i++) { window.SetCollapsed(true); window.SetCollapsed(false); }
            check(window.Height == 420 && zone.Height == 420, "repeated folding does not overwrite expanded height");
            window.SetCollapsed(true); window.RefreshItems();
            check(window.IsContentCollapsed, "adding or refreshing entries cannot reveal folded content");
            bool visible = app.State.WidgetsVisible; app.State.WidgetsVisible = false;
            app.SetAllCollapsed(false); check(app.State.Zones.All(z => !z.Collapsed), "expand-all updates every zone");
            app.SetAllCollapsed(true); check(app.State.Zones.All(z => z.Collapsed), "fold-all updates every zone");
            app.State.WidgetsVisible = visible; window.DisposeWindow();
        }
    }
}
