<div align="right">

[![中文](https://img.shields.io/badge/中文-切换-FF6B6B?style=for-the-badge)](README.md)
[![English](https://img.shields.io/badge/English-Current-1E90FF?style=for-the-badge)](README_EN.md)

</div>

# AI Agent Configuration Manager

Unified management of agent configuration files and skills for DeepSeek Harness (dsh), Codex, OpenCode, Gemini, Claude, MiMo Code, and more.

## The Problem

Codex wants an `AGENTS.md`, OpenCode wants one too, and Claude Code requires a separate `CLAUDE.md` — yet they should all contain the same content. Every update means manually copying the file to each location, tedious and error-prone.

This project eliminates that duplication. Maintain a single `AGENTS.md` and distribute it to every tool's config directory via symbolic links. Edit once, and every tool picks up the latest version automatically.

## How It Works

`%USERPROFILE%\.agents\AGENTS.md` is the single master (canonical source); the config file in every tool directory is a Windows symbolic link pointing to it. Editing the config through any tool writes to the master itself, so there is no "which one is newest" problem.

## Repository Layout

```
agents-config/
├── SetupTool/     # C# source, published as a single-file native exe (sync-agents.exe)
├── archive/       # Retired legacy scripts (setup.ps1 etc.), kept for reference only
├── tests/         # Regression checks in temporary directories; no real config sync
├── README.md      # Chinese documentation
├── README_EN.md   # English documentation
└── LICENSE        # MIT license
```

Note: this repository does **not** contain `AGENTS.md`. The configuration file should live at the canonical source location `%USERPROFILE%\.agents\AGENTS.md` (e.g. `C:\Users\<your-username>\.agents\AGENTS.md`).

## Sync Targets

After running the program, symbolic links are created at the following locations. Tools whose config directory is missing are skipped automatically.

### AGENTS configuration file

Links are created in the following priority order, all pointing to the canonical source:

| Priority | Tool | Target path |
|----------|------|-------------|
| 1 | DeepSeek Harness | `%DSH_HOME%\AGENTS.md` (defaults to `%USERPROFILE%\.dsh\AGENTS.md` when unset) |
| 2 | Codex | `%USERPROFILE%\.codex\AGENTS.md` |
| 3 | OpenCode | `%USERPROFILE%\.config\opencode\AGENTS.md` |
| 4 | Gemini | `%USERPROFILE%\.gemini\config\AGENTS.md` |
| 5 | Claude | `%USERPROFILE%\.claude\CLAUDE.md` |
| 6 | ZCode | `%USERPROFILE%\.zcode\AGENTS.md` |
| 7 | Qoder CN | `%USERPROFILE%\.qoder-cn\AGENTS.md` |
| 8 | Qoder | `%USERPROFILE%\.qoder\AGENTS.md` |
| 9 | MiMo Code | `%USERPROFILE%\.config\mimocode\AGENTS.md` |

> DeepSeek Harness prefers the `DSH_HOME` environment variable and falls back to `~\.dsh`, matching dsh's own home-directory resolution.
> MiMo Code only syncs `AGENTS.md` and does not sync Skills.

### Skills directory

When `%USERPROFILE%\.agents\skills` exists, the program creates a separate symbolic link for each first-level subfolder at the following locations. Each tool's `skills` directory remains a regular directory:

| Tool | Target path |
|------|-------------|
| WorkBuddy | `%USERPROFILE%\.workbuddy\skills` |
| WorkBuddy International | `%USERPROFILE%\.workbuddy-ai\skills` |
| Trae-CN | `%USERPROFILE%\.trae-cn\skills` |
| Claude | `%USERPROFILE%\.claude\skills` |
| QoderWork | `%USERPROFILE%\.qoderworkcn\skills` |
| Marvis | `%APPDATA%\Tencent\Marvis\User\<user-id>\skills\custom` (user ID detected automatically) |

> DeepSeek Harness and Codex read `%USERPROFILE%\.agents\skills` directly and need no sync.
> Broken first-level symbolic links in each target `skills` directory are removed automatically. If the source directory is missing or contains no first-level subfolders, Windows Skills sync is skipped, preserving each tool's existing directory and contents without affecting the main flow.

### Other rule files

| Target | Behavior |
|--------|----------|
| Trae Work CN | Detects `%USERPROFILE%\.trae-cn`; creates `user_rules` when missing, deletes all `rule-*.md` there, then creates a `rule-agents.md` symlink to the canonical source |
| Qoder Work CN | Replaces `%USERPROFILE%\.qoderworkcn\awareness\main\AGENTS.md` with a symlink to the canonical source |

## Usage

1. Build from source: run `dotnet publish -c Release -o publish` inside `SetupTool` to generate `publish\sync-agents.exe` (building requires the .NET 10 SDK and MSVC toolchain)
2. Create the configuration file at `%USERPROFILE%\.agents\AGENTS.md` (the program requires the master to exist and be non-empty)
3. Run `SetupTool\publish\sync-agents.exe` (the program requests administrator privileges via UAC; click "Yes"). With Windows Developer Mode enabled, no elevation is needed

After updating the source, rerun the publish command to update the exe at the same path. Build and publish outputs are ignored by `.gitignore` and are not committed to the repository. Running the published exe requires neither the SDK nor MSVC.

See [tests/README.md](tests/README.md) for development checks.

## Sync Flow

1. Verify the canonical source exists and is non-empty; otherwise print a message and exit
2. Clean up existing `AGENTS.md` / `CLAUDE.md` in each tool directory: symbolic links are deleted, real files are moved to the Recycle Bin (recoverable)
3. Create symlinks to the canonical source in the priority order above (DeepSeek Harness first)
4. Sync the Trae Work CN and Qoder Work CN rule files
5. Sync the Skills directory

The canonical source is the single master: the program never scans, compares, or picks the "newest" file among tool directories — edits made through any tool always modify the canonical source itself.

## Notes

- The canonical source path is `%USERPROFILE%\.agents\AGENTS.md` (uppercase)
- The published binary is a Native AOT single-file exe: no .NET runtime or PowerShell required, and no temp-file extraction at runtime
- No need to run as administrator manually; the program auto-elevates via UAC (with Windows Developer Mode enabled, regular privileges suffice for creating symlinks)
- Supports Windows 10/11
- The program detects whether each tool is installed (via the presence of its config directory) and skips tools that are not installed
- To add a new AGENTS.md sync target, edit the `targets` array in `SetupTool/Program.cs`; for Skills targets, edit the tool list there

## License

MIT License
