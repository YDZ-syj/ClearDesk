using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ClearDesk
{
    public static class StartupTests
    {
        public static void Run(Action<bool, string> check, string root, DeskApp app, ManagerWindow manager)
        {
            check(!LaunchOptions.Parse(new string[0]).AutoStart, "manual launch keeps the management window visible");
            check(LaunchOptions.Parse(new[] { "--demo", "--profile", root, "--desktop", root }).Demo, "demo launch requires and preserves isolated paths");
            bool invalidDemo = false; try { LaunchOptions.Parse(new[] { "--demo" }); } catch (ArgumentException) { invalidDemo = true; }
            check(invalidDemo, "demo launch cannot accidentally fall back to the real profile or desktop");
            var options = LaunchOptions.Parse(new[] { "--profile", root, "--autostart", "--desktop", root });
            check(options.AutoStart && options.Profile == Path.GetFullPath(root) && options.Desktop == Path.GetFullPath(root), "autostart flag does not consume profile or desktop arguments");
            bool rejected = false;
            try { LaunchOptions.Parse(new[] { "--profile", "--autostart" }); } catch (ArgumentException) { rejected = true; }
            check(rejected, "missing profile paths are rejected before launch");
            rejected = false;
            try { LaunchOptions.Parse(new[] { "--unknown" }); } catch (ArgumentException) { rejected = true; }
            check(rejected, "unknown launch flags remain rejected");
            var disabled = manager.CreateMoreMenu().Items.OfType<MenuItem>().First(m => (string)m.Header == "开机自启");
            check(!disabled.IsEnabled && !disabled.IsChecked, "isolated profiles cannot alter the real Windows startup preference");

            // Use a random, non-startup registry location; never touch the real Run key.
            string registryPath = @"Software\ClearDesk.Tests\" + Guid.NewGuid().ToString("N");
            IStartupRegistration original = app.StartupRegistration; bool originalAvailable = app.StartupSettingsAvailable;
            try
            {
                string executable = Path.Combine(root, "有空格 startup", "ClearDesk.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(executable)); File.WriteAllText(executable, "fixture");
                var startup = new StartupRegistration(executable, Registry.CurrentUser, registryPath);
                check(!startup.IsEnabled, "startup registration defaults to disabled without writing a key");
                using (var key = Registry.CurrentUser.CreateSubKey(registryPath)) key.SetValue("OtherApp", "unchanged");
                startup.SetEnabled(true);
                using (var key = Registry.CurrentUser.OpenSubKey(registryPath))
                    check((string)key.GetValue("ClearDesk") == "\"" + executable + "\" --autostart", "Windows startup registration quotes executable paths containing spaces and Unicode");
                check(new StartupRegistration(executable, Registry.CurrentUser, registryPath).IsEnabled, "startup choice persists across registration instances");
                startup.SetEnabled(false); startup.SetEnabled(false);
                using (var key = Registry.CurrentUser.OpenSubKey(registryPath))
                    check(key.GetValue("ClearDesk") == null && (string)key.GetValue("OtherApp") == "unchanged", "disabling startup removes only ClearDesk and preserves other applications");

                app.StartupRegistration = startup; app.StartupSettingsAvailable = true;
                var item = manager.CreateMoreMenu().Items.OfType<MenuItem>().First(m => (string)m.Header == "开机自启");
                check(item.IsEnabled && item.IsCheckable && !item.IsChecked, "startup option is available and unchecked for a new registration");
                item.IsChecked = true; item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                check(startup.IsEnabled && manager.CreateMoreMenu().Items.OfType<MenuItem>().First(m => (string)m.Header == "开机自启").IsChecked, "enabling through the menu persists and reopens as checked");
                item.IsChecked = false; item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                check(!startup.IsEnabled, "disabling through the menu cancels Windows startup registration");

                var missing = new StartupRegistration(Path.Combine(root, "missing.exe"), Registry.CurrentUser, registryPath);
                rejected = false; try { missing.SetEnabled(true); } catch (FileNotFoundException) { rejected = true; }
                check(rejected && !startup.IsEnabled, "missing executables are not registered for startup");
                startup.SetEnabled(true);
                string moved = Path.Combine(root, "new-location", "ClearDesk.exe"); Directory.CreateDirectory(Path.GetDirectoryName(moved)); File.WriteAllText(moved, "fixture");
                var relocated = new StartupRegistration(moved, Registry.CurrentUser, registryPath);
                check(!relocated.IsEnabled, "a moved executable does not falsely report an old startup path as enabled");
                relocated.SetEnabled(true);
                check(relocated.IsEnabled && !startup.IsEnabled, "re-enabling after relocation updates the startup path");
                relocated.SetEnabled(false);
            }
            finally
            {
                app.StartupRegistration = original; app.StartupSettingsAvailable = originalAvailable;
                Registry.CurrentUser.DeleteSubKeyTree(registryPath, false);
            }
        }
    }
}
