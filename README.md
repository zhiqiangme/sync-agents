<div align="right">

[![中文](https://img.shields.io/badge/中文-当前阅读-FF6B6B?style=for-the-badge)](README.md)
[![English](https://img.shields.io/badge/English-Switch-1E90FF?style=for-the-badge)](README_EN.md)

</div>

# AI 代理配置管理

统一管理 DeepSeek Harness（dsh）、Codex、OpenCode、Gemini、Claude、MiMo Code 等 AI 工具的代理配置文件与 Skills。

## 痛点

Codex 中放一份 `AGENTS.md`，OpenCode 中又要放一份，Claude Code 还得专门放一个 `CLAUDE.md`，而它们承载的内容本应完全相同。每次规范更新都要逐个文件手动复制、反复同步，繁琐又容易遗漏。

本项目的目的正是消除这种重复：只维护一份 `AGENTS.md`，通过软链接分发到各个 AI 工具的配置目录，所有工具都自动识别同一份内容——编辑一处，全部生效。

## 快速开始

1. 前往 [Releases 页面](https://github.com/zhiqiangme/sync-agents/releases/latest) 下载最新版 `sync-agents.exe`
2. 在 `%USERPROFILE%\.agents\AGENTS.md` 写入你的规范（必须非空）；如需同步 Skills，将各 Skill 文件夹放入 `%USERPROFILE%\.agents\skills`
3. 双击运行 `sync-agents.exe`，按提示在 UAC 弹窗中点「是」（已开启 Windows 开发者模式时无需提权）

## 原理

`%USERPROFILE%\.agents\AGENTS.md` 是唯一的母版（规范源），各工具目录中的配置文件都是指向它的 Windows 软链接（Symbolic Link）。通过任意工具编辑配置时，实际写入的都是母版本身，因此不存在"哪个最新"的问题。

## 目录结构

```
sync-agents/
├── SetupTool/     # C# 源码，发布为单文件原生 exe（sync-agents.exe）
├── tests/         # 临时目录中的回归检查，不运行真实配置同步
├── README.md      # 中文说明
├── README_EN.md   # 英文说明
└── LICENSE        # MIT 许可证
```

注意：本仓库**不**包含 `AGENTS.md`。配置文件应放在规范源位置 `%USERPROFILE%\.agents\AGENTS.md`（即 `C:\Users\<你的用户名>\.agents\AGENTS.md`）。

## 同步目标

运行程序后，会自动创建软链接到以下位置。工具对应的配置目录不存在时自动跳过。

### AGENTS 配置文件

为以下工具创建软链接，全部指向规范源（序号仅表示处理顺序，不影响生效内容）：

| 序号 | 工具 | 目标路径 |
|------|------|----------|
| 1 | DeepSeek Harness | `%DSH_HOME%\AGENTS.md`（未设置时默认为 `%USERPROFILE%\.dsh\AGENTS.md`） |
| 2 | Codex | `%CODEX_HOME%\AGENTS.md`（未设置时默认为 `%USERPROFILE%\.codex\AGENTS.md`） |
| 3 | OpenCode | `%USERPROFILE%\.config\opencode\AGENTS.md` |
| 4 | Gemini | `%USERPROFILE%\.gemini\config\AGENTS.md` |
| 5 | Claude | `%CLAUDE_CONFIG_DIR%\CLAUDE.md`（未设置时默认为 `%USERPROFILE%\.claude\CLAUDE.md`） |
| 6 | ZCode | `%USERPROFILE%\.zcode\AGENTS.md` |
| 7 | Qoder CN | `%USERPROFILE%\.qoder-cn\AGENTS.md` |
| 8 | Qoder | `%USERPROFILE%\.qoder\AGENTS.md` |
| 9 | MiMo Code | `%USERPROFILE%\.config\mimocode\AGENTS.md` |

> MiMo Code 仅同步 `AGENTS.md`，不同步 Skills。

> DeepSeek Harness、Codex、Claude 分别优先读取 `DSH_HOME`、`CODEX_HOME`、`CLAUDE_CONFIG_DIR` 环境变量，未设置时回退到用户主目录下的默认目录，与各工具自身的目录解析规则一致。

### Skills 目录

当 `%USERPROFILE%\.agents\skills` 存在时，程序会将其中每个包含 `SKILL.md` 的一级子文件夹分别软链接到以下位置（跳过隐藏目录与目标不存在的链接）。各工具的 `skills` 目录本身保持为普通目录：

| 工具 | 目标路径 |
|------|----------|
| WorkBuddy | `%USERPROFILE%\.workbuddy\skills` |
| WorkBuddy 国际版 | `%USERPROFILE%\.workbuddy-ai\skills` |
| Trae-CN | `%USERPROFILE%\.trae-cn\skills` |
| Claude | `%CLAUDE_CONFIG_DIR%\skills`（未设置时默认为 `%USERPROFILE%\.claude\skills`） |
| QoderWork | `%USERPROFILE%\.qoderworkcn\skills` |

> DeepSeek Harness 与 Codex 直接读取 `%USERPROFILE%\.agents\skills`，无需同步。
> 同步时会自动删除各目标 `skills` 目录下目标已不存在的一级软链接。源目录不存在或没有可同步的 Skill 时，跳过 Windows 本机 Skills 同步，保留各工具现有目录与内容，不影响主流程。

### 其他规则文件

| 目标 | 说明 |
|------|------|
| Trae Work CN | 检测 `%USERPROFILE%\.trae-cn`；自动创建 `user_rules` 目录，删除其中所有 `rule-*.md`，再创建指向规范源的 `rule-agents.md` 软链接 |
| Qoder Work CN | 将 `%USERPROFILE%\.qoderworkcn\awareness\main\AGENTS.md` 替换为指向规范源的软链接 |

## 使用方法

1. 从源码构建：在 `SetupTool` 目录执行 `dotnet publish -c Release -o publish`，生成 `publish\sync-agents.exe`（构建需要 .NET 10 SDK 与 MSVC 工具链）
2. 在 `%USERPROFILE%\.agents\AGENTS.md` 创建配置文件（程序要求母版存在且非空）
3. 运行 `SetupTool\publish\sync-agents.exe`（程序会自动请求管理员权限，UAC 弹窗点「是」即可；已开启 Windows 开发者模式时无需提权）

更新源码后，重新执行上述发布命令即可更新同一路径的 exe。构建与发布产物由 `.gitignore` 忽略，不提交到仓库；使用已发布的 exe 无需安装 SDK 或 MSVC。

开发验证方法见 [tests/README.md](tests/README.md)。

## 同步流程

1. 校验规范源存在且非空，不满足则提示并退出
2. 逐个工具处理：未检测到配置目录时跳过且不改动任何文件；否则清理该工具目录中原有的 `AGENTS.md` / `CLAUDE.md`（软链接直接删除，真实文件移入回收站，可恢复），随后立即创建指向规范源的软链接
3. 同步 Trae Work CN、Qoder Work CN 规则文件
4. 同步 Skills 目录

清理前会确认目标路径不是规范源本身（例如 `DSH_HOME` 指向 `%USERPROFILE%\.agents`），命中时报错退出，不删除任何文件。被清理的真实文件若内容与规范源不同（常见于工具保存时以普通文件替换了软链接），程序会给出醒目警告，提示从回收站恢复后合并到规范源。

提权时若 UAC 换成了其他账户，程序会停止运行，避免把链接建到其他账户的配置目录。

规范源是唯一母版：程序不会扫描、比较或挑选工具目录中的"最新"文件。即使通过某个工具修改配置，实际改动的也是规范源本身。

## 注意事项

- 规范源路径为 `%USERPROFILE%\.agents\AGENTS.md`，全大写
- 发布产物为 Native AOT 原生单文件 exe：无需安装 .NET 运行时或 PowerShell，运行时也不解包临时文件
- 无需手动以管理员身份运行，程序会通过 UAC 自动提权（已开启 Windows 开发者模式时普通权限即可创建软链接）
- 支持 Windows 10/11
- 程序会检测工具是否已安装（通过配置目录是否存在判断），未装的工具自动跳过
- 如需新增 AGENTS.md 同步工具，编辑 `SetupTool/Program.cs` 中的 `targets` 数组；新增 Skills 同步工具编辑其中的工具列表

## 许可证

MIT License
