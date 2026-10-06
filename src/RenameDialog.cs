using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClearDesk
{
    public class RenameDialog : Window
    {
        public RenameDialog(Settings state, Zone zone)
        {
            Title = "重命名分区"; Width = 410; Height = 260; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Brush("#191F30"); Foreground = Brushes.White; FontFamily = new FontFamily("Microsoft YaHei UI");
            var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
            panel.Children.Add(Theme.Text("新的分区名称", 16, "#E2E7F6"));
            var name = new TextBox { Text = zone.Name, MaxLength = 30, Margin = new Thickness(0, 12, 0, 8) }; panel.Children.Add(name);
            panel.Children.Add(Theme.Text(string.IsNullOrEmpty(zone.CategoryKey) ? "自定义分区" : "自动归类：" + zone.CategoryKey + "（改名后继续生效）", 12, "#A0ABC6"));
            var error = Theme.Text("", 12, "#EEAAAA"); error.Margin = new Thickness(0, 8, 0, 8); panel.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
            buttons.Children.Add(Theme.Button("取消", delegate { DialogResult = false; }, false));
            var confirm = Theme.Button("确认重命名", delegate
            {
                try { Catalog.Rename(state, zone, name.Text); DialogResult = true; }
                catch (ArgumentException ex) { error.Text = ex.Message; name.Focus(); }
            }, true); confirm.IsDefault = true; buttons.Children.Add(confirm);
            Loaded += delegate { name.Focus(); name.SelectAll(); };
        }
    }
}
