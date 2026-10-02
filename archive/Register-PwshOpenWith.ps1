<#
.SYNOPSIS
    把 PowerShell 7（pwsh.exe）注册进右键"打开方式"列表；只写当前用户，无需管理员权限。

.DESCRIPTION
    Windows 的"打开方式"列表并不扫描已安装程序或 PATH，而是读注册表
    HKEY_CLASSES_ROOT\Applications\<exe>\shell\open\command。
    PowerShell 7 的官方安装包只注册了 App Paths（让 pwsh 能在任意目录直接运行）
    以及 Microsoft.PowerShellScript.1 下的 "Run with PowerShell 7" 动词，
    并不会把 pwsh.exe 注册成通用的"打开方式"程序，
    所以刚装好的机器上右键 .ps1 是看不到 PowerShell 7 的。

    本脚本在 HKCU\Software\Classes\Applications\pwsh.exe 下补上这条注册。
    HKCU\Software\Classes 会并入 HKEY_CLASSES_ROOT，因此不需要管理员权限，
    也不影响系统里其他用户。写入前会先把同名注册表项导出到
    %TEMP%\pwsh-openwith-backup 备份。

.PARAMETER PwshPath
    pwsh.exe 的完整路径；省略时自动查找。

.PARAMETER Undo
    删除本脚本写入的注册表项，恢复原状。

.EXAMPLE
    pwsh -File .\Register-PwshOpenWith.ps1

.EXAMPLE
    pwsh -File .\Register-PwshOpenWith.ps1 -Undo
#>
[CmdletBinding()]
param(
    [string]$PwshPath,
    [switch]$Undo
)

$ErrorActionPreference = 'Stop'

$AppKey       = 'HKCU:\Software\Classes\Applications\pwsh.exe'
$AppKeyNative = 'HKCU\Software\Classes\Applications\pwsh.exe'
$CmdKey       = Join-Path $AppKey 'shell\open\command'

function Find-PwshExe {
    param([string]$Hint)

    if ($Hint) {
        if (Test-Path -LiteralPath $Hint -PathType Leaf) { return (Resolve-Path -LiteralPath $Hint).Path }
        throw "指定的路径不存在：$Hint"
    }

    # 优先用 PATH 上的 pwsh，其次按常见安装位置兜底
    $cmd = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $roots = @(
        $env:ProgramFiles
        [Environment]::GetFolderPath('ProgramFilesX86')
        $env:LOCALAPPDATA
    ) | Where-Object { $_ }

    $candidates = @()
    foreach ($root in $roots) {
        $candidates += Join-Path $root 'PowerShell\7\pwsh.exe'
        $candidates += Join-Path $root 'PowerShell\7-preview\pwsh.exe'
    }
    if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\pwsh.exe' }

    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c -PathType Leaf) { return $c }
    }

    throw '没有找到 pwsh.exe，请用 -PwshPath 指定完整路径。'
}

if ($Undo) {
    if (Test-Path -LiteralPath $AppKey) {
        Remove-Item -LiteralPath $AppKey -Recurse -Force
        Write-Host "已删除 $AppKeyNative" -ForegroundColor Yellow
    }
    else {
        Write-Host "未找到 $AppKeyNative，无需处理。"
    }
    return
}

$pwsh = Find-PwshExe -Hint $PwshPath

# 备份原有注册表项，便于回滚
if (Test-Path -LiteralPath $AppKey) {
    $backupDir = Join-Path $env:TEMP 'pwsh-openwith-backup'
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    $backup = Join-Path $backupDir ('pwsh.exe-{0}.reg' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    & reg.exe export $AppKeyNative $backup /y | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Host "已备份原有注册表项：$backup" }
    else { Write-Warning "备份失败（错误码 $LASTEXITCODE），继续注册。" }
}

New-Item -Path $CmdKey -Force | Out-Null
# FriendlyAppName 是"打开方式"里显示的名字；不写则回退到 exe 的文件说明
New-ItemProperty -Path $AppKey -Name 'FriendlyAppName' -Value 'PowerShell 7' -PropertyType String -Force | Out-Null
Set-Item -Path $CmdKey -Value ('"{0}" "%1"' -f $pwsh)

Write-Host '注册完成：' -NoNewline
Write-Host (Get-Item -Path $CmdKey).GetValue('') -ForegroundColor Green
Write-Host '右键 .ps1 → 打开方式 → 选择其他应用，列表里就有 PowerShell 7 了。'
Write-Host '若要恢复：pwsh -File .\Register-PwshOpenWith.ps1 -Undo'
