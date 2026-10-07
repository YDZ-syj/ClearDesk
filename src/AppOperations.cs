using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ClearDesk
{
    public partial class DeskApp
    {
        public bool FileOperationBusy { get; private set; }
        CancellationTokenSource fileCancellation;
        TaskCompletionSource<bool> fileCompletion;
        bool quitAfterOperation;
        int modalDepth;
        public T ShowModal<T>(Func<T> show)
        { modalDepth++; try { return show(); } finally { modalDepth--; } }
        public void ProtectMenu(System.Windows.Controls.ContextMenu menu)
        {
            bool opened = false;
            menu.Opened += delegate { if (!opened) { opened = true; modalDepth++; } };
            menu.Closed += delegate { if (opened) { opened = false; modalDepth--; } };
        }
        public void CancelFileOperation() { if (fileCancellation != null) fileCancellation.Cancel(); }
        internal async Task<T> RunBackground<T>(string label, Func<Settings, Func<bool>, Action<int, int, string>, T> action, bool quiet = false)
        {
            if (FileOperationBusy) throw new InvalidOperationException("当前操作尚未完成，请稍候。");
            Settings working = SettingsStore.Clone(State);
            FileOperationBusy = true; fileCancellation = new CancellationTokenSource();
            fileCompletion = new TaskCompletionSource<bool>();
            if (Manager != null) { Manager.SetFileBusy(true, quiet); if (!quiet) Manager.SetStatus(label); }
            foreach (var window in widgets) window.IsEnabled = false;
            foreach (var window in desktopItems) window.IsEnabled = false;
            var progress = new Progress<FileProgress>(p => { if (Manager != null && FileOperationBusy && !quiet) Manager.ReportFileProgress(p.Current, p.Total, p.Name); });
            IProgress<FileProgress> reports = progress;
            try
            {
                return await Task.Run(delegate
                {
                    try
                    {
                        T result = action(working, () => fileCancellation.IsCancellationRequested, (current, total, name) => reports.Report(new FileProgress { Current = current, Total = total, Name = name }));
                        if (!SettingsStore.Equivalent(State, working) || !File.Exists(Store.FilePath)) Store.Save(working);
                        return result;
                    }
                    catch
                    {
                        // A failed metadata save must leave the journal available for recovery.
                        try { if (Organizer != null) Organizer.Recover(working, delegate { Store.Save(working); }); } catch { }
                        throw;
                    }
                });
            }
            finally
            {
                bool changed = !SettingsStore.Equivalent(State, working);
                bool layoutChanged = !Catalog.VisibleZones(State).Select(z => z.Id).SequenceEqual(Catalog.VisibleZones(working).Select(z => z.Id));
                if (changed) State = working;
                FileOperationBusy = false; fileCancellation.Dispose(); fileCancellation = null;
                try
                {
                    if (Manager != null) { Manager.SetFileBusy(false, quiet); if (changed || !quiet) Manager.Refresh(); }
                    if (layoutChanged && State.AutoArrangeZones) { ArrangeZones(false); Save(); }
                    if (desktopInitialized && changed) RebuildWidgets();
                    else { foreach (var window in widgets) window.IsEnabled = true; foreach (var window in desktopItems) window.IsEnabled = true; }
                }
                finally
                {
                    fileCompletion.TrySetResult(true);
                    if (quitAfterOperation) { quitAfterOperation = false; var scheduledQuit = Dispatcher.BeginInvoke(new Action(Quit)); }
                }
            }
        }
        sealed class FileProgress { public int Current, Total; public string Name; }
        public async Task<bool> RestoreDesktopAsync(bool notify)
        {
            try
            {
                if (Organizer == null) { if (OrganizerWarning != null) throw new IOException(OrganizerWarning); return true; }
                var errors = await RunBackground("正在恢复桌面…", delegate(Settings state, Func<bool> cancelled, Action<int, int, string> progress)
                {
                    EntryTracking.Reconcile(state, Organizer);
                    return Organizer.UndoAll(state, delegate { Store.Save(state); }, cancelled, progress);
                });
                bool pending = Organizer.UndoBatch != null;
                if (errors.Count > 0 || pending)
                {
                    if (Manager != null) Manager.SetStatus("恢复尚未完成，未恢复项目仍保留在收纳目录，可继续重试。");
                    if (errors.Count > 0) MessageBox.Show("以下项目尚未恢复，未覆盖任何文件：\n" + string.Join("\n", errors.Take(8)), "清桌 · 恢复结果");
                    return false;
                }
                if (Manager != null && notify) Manager.SetStatus("已恢复全部可恢复文件到原桌面。");
                return true;
            }
            catch (Exception ex) { MessageBox.Show("恢复未完成，程序保持打开。\n" + ex.Message, "清桌"); return false; }
        }
    }
}
