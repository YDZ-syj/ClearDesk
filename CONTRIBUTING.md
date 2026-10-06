# 参与清桌 ClearDesk

欢迎提交可复现的问题、文档改进和代码贡献。项目采用 MIT 许可。

## 本地开发

需要 Windows 10/11 和 .NET Framework 4.8。克隆源码后执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

代码保持兼容系统自带 C# 编译器，不依赖 NuGet。图标原稿是 `assets/ClearDesk.svg`，调整矩形几何后可运行 `tools/build-icon.ps1` 重新生成 PNG 与多尺寸 ICO。

涉及文件移动的修改必须覆盖同名冲突、文件被替换、保存中断和跨启动恢复。测试使用随机临时目录，勿用真实用户文件验证移动功能。需要交互桌面时可运行 `tests/desktop-smoke.ps1 -ReleaseDirectory dist`；脚本只移动模拟文件，可能临时收起其他窗口并在结束后恢复。

## 提交变更

- 说明触发条件、修改后的行为和验证结果。
- 先运行相关自动检查；交互问题附复现步骤，必要时附不含个人信息的截图。
- 保持改动聚焦，不提交 `dist/`、个人配置、移动历史、备份或真实桌面文件。
- 新功能同时更新使用说明与更新记录。

配置和移动记录包含本机路径。提交 Issue 前请隐去用户名、文件内容和私人路径，不要直接附完整个人配置。
