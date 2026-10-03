# 回归检查

从仓库根目录运行。检查直接调用生产辅助类或 Bash 脚本，文件操作仅发生在新建的临时目录中，不运行同步程序入口或改写真实工具配置。

## Windows 文件系统

需要 .NET 10 SDK、PowerShell 7，以及管理员权限或已启用的 Windows 开发者模式（用于创建测试符号链接）。无需第三方测试框架。

```powershell
dotnet run --project tests/SetupTool.Tests -c Release
```

覆盖只读文件链接、空源保护、悬空链接、自有文件保留、junction、非链接重解析文件和未安装工具。非链接重解析文件通过临时文件上的自定义标签构造，不依赖真实云同步目录。

## WSL Skills 复制脚本

在 Git Bash 或具备 GNU `cp`、`mv`、`mktemp` 的 Linux Bash 中运行：

```bash
bash tests/test-wsl-skills.sh
```

默认提取 C# 中的生产脚本。也可由 C# 检查程序导出脚本，再把文件路径作为第一个参数传入，验证编译后的实际脚本：

```powershell
dotnet run --project tests/SetupTool.Tests -c Release -- --print-wsl-script > "$env:TEMP\agents-config-wsl-test.sh"
```

覆盖特殊字符路径、复制中途失败、替换失败恢复、恢复失败保留备份、链接目标保护、悬空链接和缺失目标。Git Bash 中通过这些检查不等于已验证实际 WSL 发行版；替换包含两次重命名，断电或强制结束进程时仍可能需要从 `.sync-skills.*` 中恢复备份。
