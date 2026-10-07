using System;
using System.IO;
using Microsoft.Win32;

namespace ClearDesk
{
    public sealed class LaunchOptions
    {
        public bool AutoStart;
        public bool Demo;
        public string Profile;
        public string Desktop;
        public static LaunchOptions Parse(string[] args)
        {
            var options = new LaunchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--autostart") { options.AutoStart = true; continue; }
                if (args[i] == "--demo") { options.Demo = true; continue; }
                if (args[i] != "--profile" && args[i] != "--desktop") throw new ArgumentException("未知启动参数：" + args[i]);
                string flag = args[i];
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--")) throw new ArgumentException("启动参数缺少路径。");
                string path = Path.GetFullPath(args[i]);
                if (flag == "--profile") options.Profile = path; else options.Desktop = path;
            }
            if (options.Demo && (options.Profile == null || options.Desktop == null)) throw new ArgumentException("演示模式需要指定独立的 --profile 和 --desktop。");
            return options;
        }
    }

    public interface IStartupRegistration
    {
        bool IsEnabled { get; }
        void SetEnabled(bool enabled);
    }

    public sealed class StartupRegistration : IStartupRegistration
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "ClearDesk";
        readonly RegistryKey root;
        readonly string keyPath;
        readonly string executable;
        public string Command { get; private set; }
        public StartupRegistration(string executable) : this(executable, Registry.CurrentUser, RunKey) { }
        internal StartupRegistration(string executable, RegistryKey root, string keyPath)
        {
            if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) || executable.IndexOf('"') >= 0 || executable.IndexOf('\n') >= 0 || executable.IndexOf('\r') >= 0) throw new ArgumentException("自启程序路径无效。");
            this.executable = Path.GetFullPath(executable); this.root = root; this.keyPath = keyPath;
            Command = "\"" + this.executable + "\" --autostart";
        }
        public bool IsEnabled
        {
            get
            {
                using (var key = root.OpenSubKey(keyPath, false))
                    return key != null && string.Equals(key.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
        }
        public void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (!File.Exists(executable)) throw new FileNotFoundException("自启程序不存在，请重新解压发布包。", executable);
                if (Command.Length > 260) throw new IOException("程序路径过长，无法设置开机自启。请将软件放到较短的路径后重试。");
                using (var key = root.CreateSubKey(keyPath)) key.SetValue(ValueName, Command, RegistryValueKind.String);
            }
            else
            {
                using (var key = root.OpenSubKey(keyPath, true)) { if (key != null) key.DeleteValue(ValueName, false); }
            }
            if (IsEnabled != enabled) throw new IOException("开机自启设置未生效，请检查当前用户的权限。");
        }
    }
}
