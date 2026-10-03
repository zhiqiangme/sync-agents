# 回归检查

从仓库根目录运行。检查直接调用生产辅助类，文件操作仅发生在新建的临时目录中，不运行同步程序入口或改写真实工具配置。

## Windows 文件系统

需要 .NET 10 SDK、PowerShell 7，以及管理员权限或已启用的 Windows 开发者模式（用于创建测试符号链接）。无需第三方测试框架。

```powershell
dotnet run --project tests/SetupTool.Tests -c Release
```

覆盖只读文件链接、空源保护、悬空链接、自有文件保留、junction、非链接重解析文件和未安装工具。非链接重解析文件通过临时文件上的自定义标签构造，不依赖真实云同步目录。
