# 实现与参考资料

## 设计

- WPF 透明工具窗口承载分区；背景单独控制 alpha，图标和文字保持清晰。使用 `WS_EX_TOOLWINDOW` 排除 Alt+Tab，不修改壁纸宿主层级。
- 系统图标通过 Shell PIDL 获取，并释放 PIDL 和 HICON。文件拖放同时提供内部入口格式与 Windows 文件列表。
- 原桌面图标仅临时隐藏 `SysListView32`，不隐藏 `SHELLDLL_DefView` / `WorkerW` 或修改用户的永久设置。独立保护助手负责异常退出后的图标恢复。
- 实际整理采用同盘移动、文件标识检查和写前日志；每项移动均保存结果，启动时核对中断状态。恢复遇到冲突不覆盖原件。
- 分区锁定固定位置、宽度和展开高度；自动布局避让锁定分区，文件使用与排序仍可操作。

## 代码结构

| 文件 | 职责 |
| --- | --- |
| `src/Core.cs` | 分类、数据模型、配置保存与校验 |
| `src/App.cs` | 管理界面、分区窗口、托盘和图标 |
| `src/AppOperations.cs` | 串行后台任务、进度取消与退出恢复 |
| `src/EntryTracking.cs` | 文件身份迁移及原文件夹内改名跟踪 |
| `src/ProfileBackup.cs` | 配置、移动日志和历史归档的完整 ZIP 备份 |
| `src/Organizer.cs` | 移动日志、文件标识、撤销与恢复 |
| `src/DesktopIcons.cs` | 原桌面图标及恢复助手 |
| `src/DesktopInteraction.cs` | 文件拖放与独立桌面入口 |
| `src/ZoneLayout.cs` | 折叠、布局及锁定避让 |
| `src/AppBrand.cs`、`assets/` | 原创图标与品牌资源 |
| `src/StartupRegistration.cs` | 当前用户的 Windows 启动项及静默启动参数 |
| `src/UpdateChecker.cs` | GitHub 公开版本查询、版本比较与提醒节流 |
| `tests/`、`tools/` | 检查、窗口测试与打包 |

## 参考

参考以下资料的设计和系统行为，未引入这些项目的源码依赖。

- [PecoFence](https://github.com/DayuanJiang/PecoFence)：分区、备份及文件整理设计参考。
- [win11-desktop-fences](https://github.com/yuan201644-collab/win11-desktop-fences)：桌面容器定位设计参考。
- Microsoft：[SHParseDisplayName](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shparsedisplayname)、[SHGetFileInfo](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow)、[工具窗口样式](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)、[WPF 拖放](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview)、[透明窗口](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency)。
- Microsoft：[SetFileInformationByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle)、[FILE_RENAME_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info)：通过同一文件句柄校验对象并移动，禁止覆盖目标。
- Wallpaper Engine：[桌面修改软件兼容说明](https://help.wallpaperengine.io/en/noshow/nowallpaper.html)、[应用规则](https://help.wallpaperengine.io/en/functionality/applicationrules.html)。

透明窗口避免了静态壁纸采样，但不同动态壁纸与系统环境的兼容程度仍以实际测试为准，见 [TESTING.md](TESTING.md)。
