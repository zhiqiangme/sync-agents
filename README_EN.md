<div align="right">

[![中文](https://img.shields.io/badge/中文-切换-FF6B6B?style=for-the-badge)](README.md)
[![English](https://img.shields.io/badge/English-Current-1E90FF?style=for-the-badge)](README_EN.md)

</div>

# AI Agent Configuration Manager

Unified management of agent configuration files and skills for DeepSeek Harness (dsh), Codex, OpenCode, Gemini, Claude, and more.

## The Problem

Codex wants an `AGENTS.md`, OpenCode wants one too, and Claude Code requires a separate `CLAUDE.md` — yet they should all contain the same content. Every update means manually copying the file to each location, tedious and error-prone.

This project eliminates that duplication. Maintain a single `AGENTS.md` and distribute it to every tool's config directory via symbolic links. Edit once, and every tool picks up the latest version automatically.

## How It Works

`%USERPROFILE%\.agents\AGENTS.md` is the single master (canonical source); the config file in every tool directory is a Windows symbolic link pointing to it. Editing the config through any tool writes to the master itself, so there is no "which one is newest" problem.

## Repository Layout

```
agents-config/
├── setup.ps1      # PowerShell setup script
├── README.md      # Chinese documentation
├── README_EN.md   # English documentation
└── LICENSE        # MIT license
```

Note: this repository does **not** contain `AGENTS.md`. The configuration file should live at the canonical source location `%USERPROFILE%\.agents\AGENTS.md` (e.g. `C:\Users\<your-username>\.agents\AGENTS.md`).

## Sync Targets

After running the script, symbolic links are created at the following locations. Tools whose config directory is missing are skipped automatically.

### AGENTS configuration file

Links are created in the following priority order, all pointing to the canonical source:

| Priority | Tool | Target path |
|----------|------|-------------|
| 1 | DeepSeek Harness | `%DSH_HOME%\AGENTS.md` (defaults to `%USERPROFILE%\.dsh\AGENTS.md` when unset) |
| 2 | Codex | `%USERPROFILE%\.codex\AGENTS.md` |
| 3 | OpenCode | `%USERPROFILE%\.config\opencode\AGENTS.md` |
| 4 | Gemini | `%USERPROFILE%\.gemini\config\AGENTS.md` |
| 5 | Claude | `%USERPROFILE%\.claude\CLAUDE.md` |

> DeepSeek Harness prefers the `DSH_HOME` environment variable and falls back to `~\.dsh`, matching dsh's own home-directory resolution.

### Skills directory

When `%USERPROFILE%\.agents\skills` exists, the script creates a separate symbolic link for each first-level subfolder at the following locations. Each tool's `skills` directory remains a regular directory:

| Tool | Target path |
|------|-------------|
| WorkBuddy | `%USERPROFILE%\.workbuddy\skills` |
| WorkBuddy International | `%USERPROFILE%\.workbuddy-ai\skills` |
| Trae-CN | `%USERPROFILE%\.trae-cn\skills` |
| Claude | `%USERPROFILE%\.claude\skills` |
| QoderWork | `%USERPROFILE%\.qoderworkcn\skills` |
| Marvis | `%APPDATA%\Tencent\Marvis\User\<user-id>\skills\custom` (user ID detected automatically) |

> DeepSeek Harness and Codex read `%USERPROFILE%\.agents\skills` directly and need no sync.
> Broken first-level symbolic links in each target `skills` directory are removed automatically. If the source directory does not exist, Skills sync is skipped without affecting the main flow.

### Other rule files

| Target | Behavior |
|--------|----------|
| Trae Work CN | Detects `%USERPROFILE%\.trae-cn`; creates `user_rules` when missing, deletes all `rule-*.md` there, then creates a `rule-agents.md` symlink to the canonical source |
| Qoder Work CN | Replaces `%USERPROFILE%\.qoderworkcn\awareness\main\AGENTS.md` with a symlink to the canonical source |
| WSL | Copies (not symlinks) `AGENTS.md` and Skills into the OpenCode / Codex config directories (`~/.config/opencode`, `~/.codex`) of the WSL `Ubuntu-26.04` distro |

## Usage

1. Create the configuration file at `%USERPROFILE%\.agents\AGENTS.md` (the script requires the master to exist and be non-empty)
2. Run `setup.ps1` (the script requests administrator privileges via UAC; click "Yes"). You can also run it with `pwsh -ExecutionPolicy Bypass -File setup.ps1`.

## Sync Flow

1. Verify the canonical source exists and is non-empty; otherwise print a message and exit
2. Clean up existing `AGENTS.md` / `CLAUDE.md` in each tool directory: symbolic links are deleted, real files are moved to the Recycle Bin (recoverable)
3. Create symlinks to the canonical source in the priority order above (DeepSeek Harness first)
4. Sync the Trae Work CN and Qoder Work CN rule files
5. Sync the Skills directory and WSL configs

The canonical source is the single master: the script never scans, compares, or picks the "newest" file among tool directories — edits made through any tool always modify the canonical source itself.

## Notes

- The canonical source path is `%USERPROFILE%\.agents\AGENTS.md` (uppercase)
- The script prefers PowerShell 7 (`pwsh`) and falls back to the built-in Windows PowerShell 5.x if unavailable
- No need to run as administrator manually; the script auto-elevates via UAC (the same interpreter version is preserved after elevation)
- Supports Windows 10/11
- The script detects whether each tool is installed (via the presence of its config directory) and skips tools that are not installed
- To add a new AGENTS.md sync target, edit the `$targets` array in `setup.ps1`; for Skills targets, edit `$skillTargets`

## License

MIT License
