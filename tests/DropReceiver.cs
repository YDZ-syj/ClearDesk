using System;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace ClearDesk
{
    public static class DropReceiver
    {
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [STAThread] public static void Main(string[] args)
        {
            var form = new Form { Text = "ClearDesk external drop test", Width = 280, Height = 180, Left = 30, Top = 30, StartPosition = FormStartPosition.Manual, AllowDrop = true };
            form.Shown += delegate { ShowWindow(form.Handle, 4); };
            form.DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            form.DragDrop += delegate(object sender, DragEventArgs e)
            {
                foreach (string path in (string[])e.Data.GetData(DataFormats.FileDrop)) File.Copy(path, Path.Combine(args[0], Path.GetFileName(path)), false);
            };
            Application.Run(form);
        }
    }
}
