using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClearDesk
{
    public static class CoreTests
    {
        static int count;
        static void Check(bool condition, string name)
        { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
        [STAThread]
        public static int Main(string[] args)
        {
            string root = Path.Combine(Path.GetTempPath(), "ClearDeskTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string doc = Path.Combine(root, "方案.PDF"), image = Path.Combine(root, "image.png"), appPath = Path.Combine(root, "editor.lnk"), folder = Path.Combine(root, "project");
                File.WriteAllText(doc, "test"); File.WriteAllText(image, "test"); File.WriteAllText(appPath, "test"); Directory.CreateDirectory(folder);
                Check(Catalog.Category(doc) == "文档", "case insensitive extension classification");
                Check(Catalog.Category(folder) == "文件夹", "directory classification");
                Check(Catalog.Category(appPath) == "应用", "software shortcut classification");
                Check(Catalog.Category("music.flac") == "影音" && Catalog.Category("archive.7z") == "压缩包", "media and archive classification");
                Check(Catalog.Normalize("C:\\") == "C:\\", "drive root preserved");
                var s = Settings.Default();
                Check(Catalog.Classify(s, new[] { doc, image, appPath, folder }) == 4, "automatic import into four categories");
                Check(Catalog.Classify(s, new[] { doc, doc.ToUpperInvariant() }) == 0, "duplicates across all zones skipped");
                Zone docs = s.Zones.First(z => z.Name == "文档");
                Check(Catalog.Add(docs, new[] { Path.Combine(root, "missing.txt"), doc }) == 0, "missing paths and duplicate entries skipped");
                Check(File.ReadAllText(doc) == "test" && Directory.Exists(folder), "classification preserves original files");
                var store = new SettingsStore(Path.Combine(root, "state", "settings.json")); store.Save(s);
                var restored = store.Load();
                Check(restored.Zones.Sum(z => z.Items.Count) == 4 && restored.Zones.First(z => z.Name == "文档").Items[0].Path == doc, "unicode JSON round trip");
                s.Zones[0].Pinned = true; s.Zones[0].Collapsed = true; s.Zones[0].X = -800; store.Save(s);
                Check(!SettingsStore.Read(store.FilePath + ".bak").Zones[0].Pinned, "previous configuration backup retained");
                restored = store.Load(); Check(restored.Zones[0].Pinned && restored.Zones[0].Collapsed && restored.Zones[0].X == -800, "layout options round trip");
                string legacyPath = Path.Combine(root, "legacy.json");
                File.WriteAllText(legacyPath, File.ReadAllText(store.FilePath).Replace("\"BackgroundOpacity\":0.48,", "").Replace(",\"BackgroundOpacity\":0.48", ""));
                Check(SettingsStore.Read(legacyPath).Zones.All(z => z.BackgroundOpacity == 0.48), "old configurations receive transparent gray default");
                File.Delete(doc); Check(!docs.Items[0].Exists, "deleted files detected without deleting entry");
                File.WriteAllText(doc, "test");
                File.WriteAllText(store.FilePath, "{not json");
                bool rejected = false; try { store.Load(); } catch { rejected = true; }
                Check(rejected, "invalid JSON rejected");
                var invalid = Settings.Default(); invalid.Zones[0].Items.Add(new Entry { Path = "relative.txt", Name = "bad" });
                rejected = false; try { SettingsStore.Validate(invalid); } catch { rejected = true; }
                Check(rejected, "relative configuration paths rejected");
                invalid = Settings.Default(); invalid.Zones[0].Color = "invalid"; invalid.Zones[0].Width = double.NaN; SettingsStore.Validate(invalid);
                Check(invalid.Zones[0].Color == "#6875E8" && invalid.Zones[0].Width == 310, "invalid visual values repaired");
                OrganizerTests.Run(Check, root);
                ZoneTests.Run(Check, root);
                var ui = new DeskApp(); ui.Initialize(Path.GetDirectoryName(store.FilePath));
                Check(ui.StartupWarning != null && ui.State.Zones.Sum(z => z.Items.Count) == 4, "damaged configuration recovered from backup");
                Check(Directory.GetFiles(Path.GetDirectoryName(store.FilePath), "*.unreadable-*").Length == 1, "damaged original preserved");
                ui.State = s;
                var manager = new ManagerWindow(ui); ui.Manager = manager;
                Check(AppBrand.WindowIcon != null && AppBrand.TrayIcon.Width == 32 && manager.Icon != null, "embedded app icon loads for the window and tray");
                ZoneTests.Ui(Check, ui, docs);
                Render((FrameworkElement)manager.Content, 1120, 700, Path.Combine(args[0], "manager-preview.png"), manager.Background);
                Check(manager.Content != null, "management UI constructed and rendered without opening desktop windows");
                docs.Collapsed = false;
                var widget = new ZoneWindow(ui, docs);
                Check(widget.AllowsTransparency && widget.Opacity == 1 && ((SolidColorBrush)widget.Background).Color.A == 0, "real background transparency preserves opaque icons and text");
                Render((FrameworkElement)widget.Content, 310, 360, Path.Combine(args[0], "zone-preview.png"), widget.Background);
                var grayImage = new BitmapImage(new Uri(Path.Combine(args[0], "zone-preview.png")));
                byte[] grayPixel = new byte[4]; grayImage.CopyPixels(new Int32Rect(100, 220, 1, 1), grayPixel, 4, 0);
                Check(grayPixel[3] > 0 && grayPixel[3] < 255, "rendered widget body has a genuine alpha channel");
                widget.SetCollapsed(true);
                Render((FrameworkElement)widget.Content, 310, (int)ZoneLayout.CollapsedHeight, Path.Combine(args[0], "collapsed-preview.png"), widget.Background);
                widget.DisposeWindow();
                var dialog = new ZoneDialog(docs);
                Render((FrameworkElement)dialog.Content, 400, 400, Path.Combine(args[0], "settings-preview.png"), dialog.Background);
                var renameDialog = new RenameDialog(s, docs);
                Render((FrameworkElement)renameDialog.Content, 410, 230, Path.Combine(args[0], "rename-preview.png"), renameDialog.Background);
                Check(File.Exists(Path.Combine(args[0], "zone-preview.png")), "widget and settings dialog rendered");
                var previewBatch = new MoveBatch { Desktop = "C:\\Users\\Demo\\Desktop", Library = "C:\\Users\\Demo\\ClearDesk Library", Moves = new System.Collections.Generic.List<FileMove> { new FileMove { Source = "C:\\Users\\Demo\\Desktop\\方案.PDF", Destination = "C:\\Users\\Demo\\ClearDesk Library\\文档\\方案.PDF", ZoneId = docs.Id } } };
                var previewDialog = new MovePreviewDialog(s, previewBatch, false);
                Render((FrameworkElement)previewDialog.Content, 940, 580, Path.Combine(args[0], "move-preview.png"), previewDialog.Background);
                Check(File.Exists(Path.Combine(args[0], "move-preview.png")), "move confirmation dialog rendered");
                Console.WriteLine("All " + count + " checks passed."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                // Only remove this test's verified, randomly named temporary directory.
                if (Path.GetDirectoryName(root) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(root).StartsWith("ClearDeskTests-")) Directory.Delete(root, true);
            }
        }
        static void Render(FrameworkElement element, int width, int height, string path, Brush background)
        {
            element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var backdrop = new DrawingVisual(); using (DrawingContext dc = backdrop.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            bitmap.Render(backdrop); bitmap.Render(element);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
