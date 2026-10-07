using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Collections.Generic;

namespace ClearDesk
{
    public class MovePreviewDialog : Window
    {
        public bool ChangeFolder;
        public sealed class Selection
        {
            public bool Selected { get; set; }
            public FileMove Move { get; set; }
            public string Name { get; set; }
            public string Zone { get; set; }
            public string From { get; set; }
            public string To { get; set; }
        }
        public readonly List<Selection> Rows = new List<Selection>();
        public List<FileMove> SelectedMoves { get { return Rows.Where(r => r.Selected).Select(r => r.Move).ToList(); } }
        public MovePreviewDialog(Settings state, MoveBatch batch, bool undo)
        {
            Title = undo ? "撤销整理 · 预览" : "整理桌面 · 移动预览"; Width = 940; Height = 620; MinWidth = 740; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Theme.Brush("#191F30"); Foreground = Brushes.White; FontFamily = new FontFamily("Microsoft YaHei UI");
            var panel = new DockPanel { Margin = new Thickness(24) }; Content = panel;
            var summary = new StackPanel(); DockPanel.SetDock(summary, Dock.Top); panel.Children.Add(summary);
            summary.Children.Add(Theme.Text(undo ? "恢复到原桌面位置" : "确认这些文件的去向", 24, "#F1F3FF"));
            var moves = batch.Moves.Where(m => !undo || m.Status == "moved" || m.Status == "undoing" || m.Status == "blocked").ToList();
            var description = Theme.Text(undo ? "将尝试恢复 " + moves.Count + " 项；原位置已有同名文件时会跳过，保留两份。" : "将移动 " + moves.Count + " 项个人桌面文件或快捷方式，移动成功后原图标消失。\n公共桌面和系统图标保留；程序本体、脚本、隐藏文件和链接跳过。", 13, "#A0ABC6");
            description.Margin = new Thickness(0, 12, 0, 12); summary.Children.Add(description);
            summary.Children.Add(Theme.Text("收纳目录：" + batch.Library, 12, "#C7D0EB"));
            if (!undo && batch.Skipped.Count > 0)
            {
                var skipped = new Expander { Header = "跳过 " + batch.Skipped.Count + " 项（展开查看）", Margin = new Thickness(0, 12, 0, 0), Foreground = Theme.Brush("#C7D0EB") };
                skipped.Content = new ScrollViewer { Content = Theme.Text(string.Join("\n", batch.Skipped), 12, "#A0ABC6"), MaxHeight = 90 }; summary.Children.Add(skipped);
            }
            var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
            footer.Children.Add(Theme.Text(undo ? "文件内容以当前版本为准。撤销移动不会撤销编辑；已被其他文件替换的项目不会恢复。" : (state.RestoreOnExit ? "正常退出时会搬回原桌面；异常结束后，文件仍在收纳目录，可重新打开后恢复。" : "退出后文件继续保存在收纳目录；要搬回桌面，请点击“恢复全部到桌面”。"), 12, "#A0ABC6"));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; footer.Children.Add(buttons);
            if (!undo) buttons.Children.Add(Theme.Button("更换收纳目录…", delegate { ChangeFolder = true; DialogResult = true; }, false));
            buttons.Children.Add(Theme.Button("取消", delegate { DialogResult = false; }, false));
            var confirm = Theme.Button(undo ? "确认撤销" : "确认移动 " + moves.Count + " 项", delegate { DialogResult = true; }, true); confirm.IsEnabled = moves.Count > 0; buttons.Children.Add(confirm);
            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, Background = Theme.Brush("#22283A"), Foreground = Theme.Brush("#1B2235"), RowBackground = Theme.Brush("#E2E5ED"), AlternatingRowBackground = Theme.Brush("#F0F2F7"), GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, Margin = new Thickness(0, 18, 0, 0), HeadersVisibility = DataGridHeadersVisibility.Column };
            var selectionTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><CheckBox IsChecked='{Binding Selected, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}' HorizontalAlignment='Center' VerticalAlignment='Center'/></DataTemplate>");
            grid.Columns.Add(new DataGridTemplateColumn { Header = "选择", CellTemplate = selectionTemplate, Width = 55 });
            grid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding("Name"), Width = 160 });
            grid.Columns.Add(new DataGridTextColumn { Header = "分区", Binding = new Binding("Zone"), Width = 70 });
            grid.Columns.Add(new DataGridTextColumn { Header = "当前位置", Binding = new Binding("From"), Width = 285 });
            grid.Columns.Add(new DataGridTextColumn { Header = "移动到", Binding = new Binding("To"), Width = 285 });
            foreach (DataGridTextColumn column in grid.Columns.OfType<DataGridTextColumn>())
            {
                column.IsReadOnly = true;
                var cell = new Style(typeof(TextBlock));
                cell.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
                cell.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4)));
                cell.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(((Binding)column.Binding).Path.Path)));
                column.ElementStyle = cell;
            }
            Rows.AddRange(moves.Select(m => new Selection { Selected = true, Move = m, Name = System.IO.Path.GetFileName(m.Source), Zone = state.Zones.Where(z => z.Id == m.ZoneId).Select(z => z.Name).FirstOrDefault() ?? "已删除分区", From = undo ? m.Destination : m.Source, To = undo ? m.Source : m.Destination }));
            grid.ItemsSource = Rows;
            confirm.Content = undo ? "恢复勾选项目" : "移动勾选项目";
            description.Text += "\n取消勾选可保留某些项目；仅处理勾选项。";
            panel.Children.Add(grid);
        }
    }
}
