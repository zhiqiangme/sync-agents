# 设置 AI 代理配置软链接
# 将 AGENTS.md 同步到 DeepSeek Harness、Codex、OpenCode、Gemini、Claude、ZCode

# 引入 Visual Basic 文件系统 API，用于将旧文件送入回收站而非直接删除
Add-Type -AssemblyName Microsoft.VisualBasic

# 检查是否以管理员身份运行，非管理员则自动触发 UAC 提权后退出当前进程
# 若已开启 Windows 开发者模式，则无需管理员权限也能创建软链接
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$devMode = (Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -Name "AllowDevelopmentWithoutDevLicense" -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense

if (-not $isAdmin -and $devMode -ne 1) {
    Write-Host "当前未以管理员身份运行且未开启开发者模式，正在请求提升权限..." -ForegroundColor Yellow
    # 获取当前 PowerShell 解释器路径，确保提权后仍使用相同版本（PS7 或 PS5）
    $psExe = (Get-Process -Id $PID).Path
    if (-not $psExe) { $psExe = "powershell.exe" }
    # 用数组传递参数，避免含空格路径在单字符串下被错误解析
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($args.Count -gt 0) { $argList += $args }
    try {
        Start-Process -FilePath $psExe -ArgumentList $argList -Verb RunAs -WorkingDirectory $PSScriptRoot
    } catch {
        Write-Host "无法自动获取管理员权限，请右键此脚本选择「以管理员身份运行」" -ForegroundColor Red
        Write-Host "或前往 Windows 设置 -> 隐私和安全性 -> 开发者选项，开启开发者模式" -ForegroundColor Yellow
        Write-Host "按任意键退出..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 1
    }
    exit 0
}

# DeepSeek Harness 配置目录：解析规则与 dsh 自身一致（优先 $env:DSH_HOME，未设置时回退 ~\.dsh）
# dsh 直接读取 ~\.agents\skills，因此 skills 无需为 dsh 单独同步
$dshHome = if ($env:DSH_HOME) { $env:DSH_HOME } else { Join-Path $env:USERPROFILE ".dsh" }

# 同步目标：Tool=工具名称，ConfigDir=用于判断工具是否已安装的目录，TargetFile=软链接目标路径
# dsh 排在最前，其 AGENTS.md 在所有 Harness 软件中优先级最高；未检测到 dsh 配置目录时自动跳过
$targets = @(
    @{ Tool = "DSH";      ConfigDir = $dshHome;                            TargetFile = "$dshHome\AGENTS.md" },
    @{ Tool = "Codex";    ConfigDir = "$env:USERPROFILE\.codex";           TargetFile = "$env:USERPROFILE\.codex\AGENTS.md" },
    @{ Tool = "OpenCode"; ConfigDir = "$env:USERPROFILE\.config\opencode"; TargetFile = "$env:USERPROFILE\.config\opencode\AGENTS.md" },
    @{ Tool = "Gemini";   ConfigDir = "$env:USERPROFILE\.gemini\config";   TargetFile = "$env:USERPROFILE\.gemini\config\AGENTS.md" },
    @{ Tool = "Claude";   ConfigDir = "$env:USERPROFILE\.claude";          TargetFile = "$env:USERPROFILE\.claude\CLAUDE.md" },
    # ZCode：用户级指令文件固定为 ~\.zcode\AGENTS.md；用 cli 子目录判断是否已安装
    # ZCode 与 dsh、Codex 一样直接读取 ~\.agents\skills，因此 skills 无需为其单独同步
    @{ Tool = "ZCode";    ConfigDir = "$env:USERPROFILE\.zcode\cli";       TargetFile = "$env:USERPROFILE\.zcode\AGENTS.md" }
)

# Trae Work CN 规则文件：以 .trae-cn 根目录判断软件是否存在
# user_rules 不存在时自动创建，其中原有 rule-*.md 会被替换为指向规范源的软链接
$traeConfigDir = "$env:USERPROFILE\.trae-cn"
$traeRuleDir = Join-Path $traeConfigDir "user_rules"

# 规范源：唯一真实的配置文件
$canonicalSource = Join-Path $env:USERPROFILE ".agents\AGENTS.md"

Write-Host "正在检查规范源..." -ForegroundColor Cyan

# 规范源是唯一母版：各工具目录的 AGENTS.md / CLAUDE.md 只是指向它的软链接，
# 通过任意工具编辑配置时，实际修改的都是规范源本身，因此无需比较或挑选"最新"文件
if (-not (Test-Path $canonicalSource)) {
    Write-Host "未找到规范源: " $canonicalSource -ForegroundColor Red
    Write-Host "请先在该路径创建 AGENTS.md 后再运行本脚本" -ForegroundColor Yellow
    Write-Host "按任意键退出..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# 规范源为空时不继续，避免用空文件覆盖各工具原有配置
$csContent = $null
try { $csContent = [System.IO.File]::ReadAllText($canonicalSource) } catch { }
if (-not $csContent -or $csContent.Length -eq 0) {
    Write-Host "规范源为空: " $canonicalSource -ForegroundColor Red
    Write-Host "请写入内容后再运行本脚本" -ForegroundColor Yellow
    Write-Host "按任意键退出..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}

# 删除工具目录中原有的 AGENTS.md / CLAUDE.md，为创建新软链接做准备
# 使用回收站而非永久删除，避免误操作丢失用户配置
foreach ($t in $targets) {
    $dir = Split-Path $t.TargetFile -Parent
    foreach ($name in @('AGENTS.md', 'CLAUDE.md')) {
        $p = Join-Path $dir $name
        if (Test-Path $p) {
            $it = Get-Item $p
            if ($it.LinkType -eq 'SymbolicLink') {
                Write-Host "移除现有软链接: " $p -ForegroundColor Yellow
                # 软链接直接删除即可，不会影响规范源
                Remove-Item $p -Force
            } else {
                Write-Host "移入回收站: " $p -ForegroundColor Yellow
                # 普通文件送入回收站，保留恢复可能性
                [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($p, 'OnlyErrorDialogs', 'SendToRecycleBin')
            }
        }
    }
}

# 为每个已安装的工具创建软链接
$created = 0
$skipped = 0
foreach ($t in $targets) {
    if (-not (Test-Path $t.ConfigDir)) {
        Write-Host "[跳过] " $t.Tool ": 未检测到配置目录 " $t.ConfigDir -ForegroundColor DarkGray
        $skipped++
        continue
    }

    $target = $t.TargetFile

    try {
        # 使用 New-Item 创建文件软链接，移除对 cmd /c mklink 的依赖
        # 需管理员权限或已开启 Windows 开发者模式
        New-Item -ItemType SymbolicLink -Path $target -Target $canonicalSource -Force | Out-Null
        Write-Host "[完成] 已创建软链接 [" $t.Tool "]: " $target -ForegroundColor Green
        $created++
    } catch {
        Write-Host "创建软链接失败: $target" -ForegroundColor Red
        Write-Host "原因: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "请确保以管理员身份运行此脚本，或在 Windows 设置中启用开发者模式" -ForegroundColor Yellow
        Write-Host "按任意键退出..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 1
    }
}

# Trae Work CN 规则文件同步：删除 user_rules 目录下所有 rule-*.md，
# 创建一个指向规范源的软链接（命名保持 rule- 前缀，由 Trae 自行管理时间戳）
if (Test-Path -LiteralPath $traeConfigDir -PathType Container) {
    Write-Host ""
    Write-Host "正在同步 Trae Work CN 规则文件..." -ForegroundColor Cyan

    # Trae 已存在但规则目录尚未生成时，先创建完整目录结构
    if (-not (Test-Path -LiteralPath $traeRuleDir -PathType Container)) {
        try {
            New-Item -ItemType Directory -Path $traeRuleDir -Force -ErrorAction Stop | Out-Null
            Write-Host "已创建 Trae Work CN 规则目录: " $traeRuleDir -ForegroundColor Yellow
        } catch {
            Write-Host "创建 Trae Work CN 规则目录失败: $traeRuleDir" -ForegroundColor Red
            Write-Host "原因: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "按任意键退出..." -ForegroundColor Gray
            $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
            exit 1
        }
    }

    # 清理目录中所有 rule-*.md 文件（软链接直接删除，普通文件送入回收站）
    Get-ChildItem -Path $traeRuleDir -Filter "rule-*.md" -File -Force | ForEach-Object {
        $p = $_.FullName
        if ($_.LinkType -eq 'SymbolicLink') {
            Write-Host "移除现有软链接: " $p -ForegroundColor Yellow
            Remove-Item $p -Force
        } else {
            Write-Host "移入回收站: " $p -ForegroundColor Yellow
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($p, 'OnlyErrorDialogs', 'SendToRecycleBin')
        }
    }

    # 创建软链接：用固定名称 rule-agents.md，避免与 Trae 自动生成的时间戳文件混淆
    $traeRuleLink = Join-Path $traeRuleDir "rule-agents.md"
    try {
        New-Item -ItemType SymbolicLink -Path $traeRuleLink -Target $canonicalSource -Force | Out-Null
        Write-Host "[完成] 已创建软链接 [Trae-Work]: " $traeRuleLink -ForegroundColor Green
        $created++
    } catch {
        Write-Host "创建软链接失败: $traeRuleLink" -ForegroundColor Red
        Write-Host "原因: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "请确保以管理员身份运行此脚本，或在 Windows 设置中启用开发者模式" -ForegroundColor Yellow
        Write-Host "按任意键退出..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 1
    }
} else {
    Write-Host "[跳过] Trae-Work: 未检测到配置目录 " $traeConfigDir -ForegroundColor DarkGray
    $skipped++
}

# Qoder Work CN 规则文件同步：将 awareness\main\AGENTS.md 替换为指向规范源的软链接
$qoderRuleDir = "$env:USERPROFILE\.qoderworkcn\awareness\main"
if (Test-Path $qoderRuleDir) {
    Write-Host ""
    Write-Host "正在同步 Qoder Work CN 规则文件..." -ForegroundColor Cyan

    $qoderTarget = Join-Path $qoderRuleDir "AGENTS.md"
    if (Test-Path $qoderTarget) {
        $it = Get-Item $qoderTarget -Force
        if ($it.LinkType -eq 'SymbolicLink') {
            Write-Host "移除现有软链接: " $qoderTarget -ForegroundColor Yellow
            Remove-Item $qoderTarget -Force
        } else {
            Write-Host "移入回收站: " $qoderTarget -ForegroundColor Yellow
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($qoderTarget, 'OnlyErrorDialogs', 'SendToRecycleBin')
        }
    }

    try {
        New-Item -ItemType SymbolicLink -Path $qoderTarget -Target $canonicalSource -Force | Out-Null
        Write-Host "[完成] 已创建软链接 [QoderWork]: " $qoderTarget -ForegroundColor Green
        $created++
    } catch {
        Write-Host "创建软链接失败: $qoderTarget" -ForegroundColor Red
        Write-Host "原因: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "请确保以管理员身份运行此脚本，或在 Windows 设置中启用开发者模式" -ForegroundColor Yellow
        Write-Host "按任意键退出..." -ForegroundColor Gray
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        exit 1
    }
} else {
    Write-Host "[跳过] QoderWork: 未检测到配置目录 " $qoderRuleDir -ForegroundColor DarkGray
    $skipped++
}

# Skills 目录同步：将规范源 .agents\skills 下的一级子目录分别软链接到各工具
$skillsSource = Join-Path $env:USERPROFILE ".agents\skills"

if (-not (Test-Path $skillsSource)) {
    Write-Host "未检测到 Skills 源目录: " $skillsSource "，跳过 Skills 同步。" -ForegroundColor DarkGray
} else {
    Write-Host ""
    Write-Host "正在同步 Skills 目录..." -ForegroundColor Cyan

    # 只同步一级 Skills 子目录，目标 skills 本身保持为普通目录
    $skillSourceDirs = Get-ChildItem -Path $skillsSource -Directory -Force
    if ($skillSourceDirs.Count -eq 0) {
        Write-Host "Skills 源目录下没有一级子目录，跳过 Skills 同步。" -ForegroundColor DarkGray
    }

    if ($skillSourceDirs.Count -gt 0) {
    # 各工具的 skills 目标目录
    # Codex、dsh 与 ZCode 一样直接读取 ~\.agents\skills，无需同步
    $skillTargets = @(
        @{ Tool = "WorkBuddy"; TargetDir = "$env:USERPROFILE\.workbuddy\skills" },
        @{ Tool = "Trae-CN";   TargetDir = "$env:USERPROFILE\.trae-cn\skills" },
        @{ Tool = "Claude";    TargetDir = "$env:USERPROFILE\.claude\skills" },
        @{ Tool = "QoderWork"; TargetDir = "$env:USERPROFILE\.qoderworkcn\skills" }
    )

    # Marvis 用户 ID 每台电脑不同，动态扫描 User 目录，排除 default_user
    $marvisUserDir = Join-Path $env:APPDATA "Tencent\Marvis\User"
    if (Test-Path $marvisUserDir) {
        $marvisUserId = Get-ChildItem -Path $marvisUserDir -Directory |
            Where-Object { $_.Name -ne "default_user" } |
            Select-Object -First 1
        if ($marvisUserId) {
            $marvisCustomDir = Join-Path $marvisUserId.FullName "skills\custom"
            $skillTargets += @{ Tool = "Marvis"; TargetDir = $marvisCustomDir }
        } else {
            Write-Host "未找到 Marvis 用户目录，跳过 Marvis Skills 同步。" -ForegroundColor DarkGray
        }
    }

    foreach ($s in $skillTargets) {
        # 父目录不存在则跳过，不创建不存在的工具目录
        $parentDir = Split-Path $s.TargetDir -Parent
        if (-not (Test-Path $parentDir)) {
            Write-Host "[跳过] " $s.Tool ": 未检测到配置目录 " $parentDir -ForegroundColor DarkGray
            $skipped++
            continue
        }

        # 兼容旧版本：若整个 skills 目录是软链接，先移除并改建为普通目录
        if (Test-Path $s.TargetDir) {
            $it = Get-Item $s.TargetDir -Force
            if ($it.LinkType -eq 'SymbolicLink') {
                Write-Host "移除旧版 Skills 目录软链接: " $s.TargetDir -ForegroundColor Yellow
                Remove-Item $s.TargetDir -Force
            }
        }

        if (-not (Test-Path $s.TargetDir)) {
            New-Item -ItemType Directory -Path $s.TargetDir -Force | Out-Null
        }

        # 清理目标 skills 目录下目标已不存在的一级软链接
        # 注意：PS7 中 Test-Path 对悬空符号链接默认返回 True（仅判断链接本身是否存在），
        # 必须检查链接 Target 指向的路径是否真实存在，才能正确识别失效链接
        Get-ChildItem -LiteralPath $s.TargetDir -Force | ForEach-Object {
            if ($_.LinkType -eq 'SymbolicLink' -and -not (Test-Path -LiteralPath $_.Target)) {
                Write-Host "移除失效的 Skills 软链接: " $_.FullName -ForegroundColor Yellow
                Remove-Item -LiteralPath $_.FullName -Force
            }
        }

        foreach ($skillSourceDir in $skillSourceDirs) {
            $skillTargetDir = Join-Path $s.TargetDir $skillSourceDir.Name

            # 同名旧项需先清理：软链接直接移除，普通目录送入回收站
            if (Test-Path $skillTargetDir) {
                $skillTargetItem = Get-Item $skillTargetDir -Force
                if ($skillTargetItem.LinkType -eq 'SymbolicLink') {
                    Write-Host "移除现有软链接: " $skillTargetDir -ForegroundColor Yellow
                    Remove-Item $skillTargetDir -Force
                } else {
                    Write-Host "移入回收站: " $skillTargetDir -ForegroundColor Yellow
                    [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory(
                        $skillTargetDir,
                        'OnlyErrorDialogs',
                        'SendToRecycleBin'
                    )
                }
            }

            try {
                New-Item -ItemType SymbolicLink -Path $skillTargetDir -Target $skillSourceDir.FullName -Force | Out-Null
                Write-Host "[完成] 已创建 Skills 软链接 [" $s.Tool "]: " $skillTargetDir -ForegroundColor Green
                $created++
            } catch {
                Write-Host "创建软链接失败: $skillTargetDir" -ForegroundColor Red
                Write-Host "原因: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host "请确保以管理员身份运行此脚本，或在 Windows 设置中启用开发者模式" -ForegroundColor Yellow
                Write-Host "按任意键退出..." -ForegroundColor Gray
                $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
                exit 1
            }
        }
    }
    }
}

# WSL 同步：将 AGENTS.md 和 Skills 复制到 WSL 文件系统的 OpenCode / Codex 配置目录
# WSL 内无法使用 Windows 软链接，因此采用文件复制方式同步
$wslDistro = "Ubuntu-26.04"

# 将 Windows 路径转换为 WSL /mnt/x/... 格式（避免依赖 wslpath 子进程）
function ConvertTo-WslPath([string]$winPath) {
    $full = (Resolve-Path $winPath -ErrorAction Stop).ProviderPath
    $drive = $full.Substring(0, 1).ToLower()
    $rest = $full.Substring(2) -replace '\\', '/'
    return "/mnt/$drive$rest"
}

Write-Host ""
Write-Host "正在同步 WSL 配置 (OpenCode / Codex)..." -ForegroundColor Cyan

# 检查 WSL 发行版是否可用
$wslList = (wsl -l -q 2>$null) -replace "`0", "" | ForEach-Object { $_.Trim() } | Where-Object { $_ }
if ($wslList -notcontains $wslDistro) {
    Write-Host "[跳过] WSL: 未检测到发行版 " $wslDistro -ForegroundColor DarkGray
    $skipped++
} else {
    # 获取 WSL 用户主目录
    $wslHome = (wsl -d $wslDistro -- bash -c 'echo $HOME' 2>$null).Trim()
    if (-not $wslHome) {
        Write-Host "[跳过] WSL: 无法获取主目录" -ForegroundColor DarkGray
        $skipped++
    } else {
        # 转换规范源路径为 WSL 格式
        $wslCanonicalSource = ConvertTo-WslPath $canonicalSource
        $wslSkillsSource = $null
        if (Test-Path $skillsSource) {
            $wslSkillsSource = ConvertTo-WslPath $skillsSource
        }

        # WSL 同步目标列表：Tool=工具名，ConfigDir=WSL 内配置目录
        $wslTargets = @(
            @{ Tool = "OpenCode"; ConfigDir = "$wslHome/.config/opencode" },
            @{ Tool = "Codex";    ConfigDir = "$wslHome/.codex" }
        )

        foreach ($wt in $wslTargets) {
            $dir = $wt.ConfigDir

            # 创建目标目录并复制 AGENTS.md
            wsl -d $wslDistro -- mkdir -p "$dir"
            wsl -d $wslDistro -- cp "$wslCanonicalSource" "$dir/AGENTS.md"
            Write-Host "[完成] 已复制 AGENTS.md -> WSL [" $wt.Tool "]: $dir/AGENTS.md" -ForegroundColor Green
            $created++

            # 复制 Skills 目录
            if ($wslSkillsSource) {
                # 移除旧副本后整体复制，确保内容与规范源一致
                wsl -d $wslDistro -- bash -c "rm -rf '$dir/skills' && cp -r '$wslSkillsSource' '$dir/skills'"
                Write-Host "[完成] 已复制 Skills -> WSL [" $wt.Tool "]: $dir/skills/" -ForegroundColor Green
                $created++
            }
        }

        if (-not $wslSkillsSource) {
            Write-Host "[跳过] WSL Skills: 未检测到源目录 " $skillsSource -ForegroundColor DarkGray
        }
    }
}

Write-Host ""
Write-Host "完成！新建 " $created " 个软链接，跳过 " $skipped " 个未安装的工具。" -ForegroundColor Cyan
Write-Host "规范源: " $canonicalSource -ForegroundColor Cyan
Write-Host "按任意键退出..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
