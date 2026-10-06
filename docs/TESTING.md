# 验证记录

版本：0.5.0。记录日期：2026-10-06。

| 验证 | 结果与范围 |
| --- | --- |
| 自动检查 | 98 项通过，覆盖分类、配置迁移、移动与撤销、冲突及文件替换、中断恢复、布局、锁定、Shell 图标和透明渲染。 |
| 实际窗口交互 | 使用模拟桌面和隔离配置验证自动分区、默认折叠、完整收起、Alt+Tab 排除、拖动排序、缩放、锁定保存、外部文件拖放及桌面拖出。 |
| 退出与恢复 | 模拟文件在正常退出后恢复；正常退出和强制结束后的原桌面图标恢复通过。 |
| Wallpaper Engine | 窗口交互测试期间原壁纸进程保持运行。 |
| 作者手动验收 | 作者反馈混合 DPI、Explorer 重启测试无异常；此项为本机手动结果，未记录具体缩放组合。 |

窗口测试的结构化结果见 [desktop-smoke-0.5.0.json](desktop-smoke-0.5.0.json)。截图使用模拟文件，不包含真实桌面或个人文件。

## 重复验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\desktop-smoke.ps1 -ReleaseDirectory dist
```

自动检查只操作随机临时目录。窗口测试通过 `--profile` 和 `--desktop` 使用隔离配置及模拟文件；会短暂隐藏原桌面图标，并可能收起其他窗口，结束后恢复。需要交互式 Windows 会话，不适合无桌面的 CI。

## 待扩展的兼容测试

- 不同 Windows 版本与多屏缩放组合，包括拔插显示器、休眠和恢复。
- Wallpaper Engine 视频、场景、Web 壁纸及播放列表切换。
- 虚拟桌面、Win+D、全屏应用及长期运行。
- 云同步桌面、占位文件和权限受限目录；当前可能跳过不适合移动的项目。

以上本机结果不代表全部硬件、桌面配置或壁纸类型均已验证。报告兼容问题时请提供系统版本、缩放设置和复现步骤，并去除私人路径。
