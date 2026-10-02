// sync-agents —— 把规范源 AGENTS.md / skills 以软链接方式同步到各 AI 编码工具的配置目录。
// 由仓库根目录 setup.ps1 移植而来，Native AOT 发布为单个原生 exe：
// 目标机器无需任何运行时，运行过程也不解包临时文件。

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

int created = 0;
int skipped = 0;

// ===== 1. 权限检查：创建软链接需要管理员权限；开启开发者模式后普通权限即可 =====
using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
{
    bool isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    int devMode = 0;
    using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"))
        devMode = key?.GetValue("AllowDevelopmentWithoutDevLicense") as int? ?? 0;

    if (!isAdmin && devMode != 1)
    {
        WriteLineC("当前未以管理员身份运行且未开启开发者模式，正在请求提升权限...", ConsoleColor.Yellow);
        var elevate = new ProcessStartInfo
        {
            // 重新拉起自身并触发 UAC；命令行参数原样透传
            FileName = Environment.ProcessPath!,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (string a in args)
            elevate.ArgumentList.Add(a);
        try
        {
            Process.Start(elevate)?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            WriteLineC("无法自动获取管理员权限，请右键此程序选择「以管理员身份运行」", ConsoleColor.Red);
            WriteLineC("或前往 Windows 设置 -> 隐私和安全性 -> 开发者选项，开启开发者模式", ConsoleColor.Yellow);
            WriteLineC("原因: " + ex.Message, ConsoleColor.Red);
            WaitKey();
            return 1;
        }
    }
}

string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

// ===== 2. 规范源检查 =====
string canonicalSource = Path.Combine(userProfile, ".agents", "AGENTS.md");

WriteLineC("正在检查规范源...", ConsoleColor.Cyan);

// 规范源是唯一母版：各工具目录里的 AGENTS.md / CLAUDE.md 只是指向它的软链接，
// 通过任意工具编辑配置时实际修改的都是规范源本身
if (!File.Exists(canonicalSource))
{
    WriteLineC("未找到规范源: " + canonicalSource, ConsoleColor.Red);
    WriteLineC("请先在该路径创建 AGENTS.md 后再运行本程序", ConsoleColor.Yellow);
    WaitKey();
    return 1;
}

// 规范源为空时不继续，避免用空文件覆盖各工具原有配置
string? csContent = null;
try { csContent = File.ReadAllText(canonicalSource); } catch { }
if (string.IsNullOrEmpty(csContent))
{
    WriteLineC("规范源为空: " + canonicalSource, ConsoleColor.Red);
    WriteLineC("请写入内容后再运行本程序", ConsoleColor.Yellow);
    WaitKey();
    return 1;
}

// ===== 3. 各工具的 AGENTS.md / CLAUDE.md 同步 =====
// dshHome 解析规则与 dsh 自身一致：优先 DSH_HOME 环境变量，未设置时回退 ~\.dsh
string dshHome = Environment.GetEnvironmentVariable("DSH_HOME") is { Length: > 0 } dshEnv
    ? dshEnv
    : Path.Combine(userProfile, ".dsh");

// ConfigDir 仅用于判断工具是否已安装；dsh 排最前，其 AGENTS.md 在所有 Harness 软件中优先级最高
var targets = new (string Tool, string ConfigDir, string TargetFile)[]
{
    ("DSH",      dshHome,                     Path.Combine(dshHome, "AGENTS.md")),
    ("Codex",    Home(".codex"),              Home(".codex", "AGENTS.md")),
    ("OpenCode", Home(".config", "opencode"), Home(".config", "opencode", "AGENTS.md")),
    ("Gemini",   Home(".gemini", "config"),   Home(".gemini", "config", "AGENTS.md")),
    ("Claude",   Home(".claude"),             Home(".claude", "CLAUDE.md")),
    // ZCode：用户级指令文件固定为 ~\.zcode\AGENTS.md；以 ~\.zcode\cli 判断是否已安装
    ("ZCode",    Home(".zcode", "cli"),       Home(".zcode", "AGENTS.md")),
    ("Qoder-CN", Home(".qoder-cn"),           Home(".qoder-cn", "AGENTS.md")),
    ("Qoder",    Home(".qoder"),              Home(".qoder", "AGENTS.md")),
    ("MiMoCode", Home(".config", "mimocode"), Home(".config", "mimocode", "AGENTS.md")),
};

// 先清理所有目标目录中的同名旧文件，为创建软链接做准备
foreach (var t in targets)
{
    string dir0 = Path.GetDirectoryName(t.TargetFile)!;
    foreach (string name in new[] { "AGENTS.md", "CLAUDE.md" })
    {
        string p = Path.Combine(dir0, name);
        if (ExistsIncludingLink(p))
            RemoveItem(p);
    }
}

foreach (var t in targets)
{
    if (!Directory.Exists(t.ConfigDir))
    {
        WriteLineC("[跳过] " + t.Tool + ": 未检测到配置目录 " + t.ConfigDir, ConsoleColor.DarkGray);
        skipped++;
        continue;
    }

    try
    {
        CreateLink(t.TargetFile, canonicalSource);
        WriteLineC("[完成] 已创建软链接 [" + t.Tool + "]: " + t.TargetFile, ConsoleColor.Green);
        created++;
    }
    catch (Exception ex)
    {
        LinkError(t.TargetFile, ex);
        return 1;
    }
}

// ===== 4. Trae Work CN 规则文件 =====
// 以 .trae-cn 根目录判断软件是否存在；user_rules 不存在时自动创建，
// 其中原有 rule-*.md 会被替换为指向规范源的软链接（时间戳文件由 Trae 自行管理）
string traeConfigDir = Home(".trae-cn");
string traeRuleDir = Path.Combine(traeConfigDir, "user_rules");

if (Directory.Exists(traeConfigDir))
{
    Console.WriteLine();
    WriteLineC("正在同步 Trae Work CN 规则文件...", ConsoleColor.Cyan);

    if (!Directory.Exists(traeRuleDir))
    {
        try
        {
            Directory.CreateDirectory(traeRuleDir);
            WriteLineC("已创建 Trae Work CN 规则目录: " + traeRuleDir, ConsoleColor.Yellow);
        }
        catch (Exception ex)
        {
            WriteLineC("创建 Trae Work CN 规则目录失败: " + traeRuleDir, ConsoleColor.Red);
            WriteLineC("原因: " + ex.Message, ConsoleColor.Red);
            WaitKey();
            return 1;
        }
    }

    // 只处理文件，与 setup.ps1 的 Get-ChildItem -File 一致
    foreach (string f in Directory.GetFiles(traeRuleDir, "rule-*.md"))
    {
        if ((new FileInfo(f).Attributes & FileAttributes.Directory) != 0)
            continue;
        RemoveItem(f);
    }

    // 用固定名称 rule-agents.md，避免与 Trae 自动生成的时间戳文件混淆
    string traeRuleLink = Path.Combine(traeRuleDir, "rule-agents.md");
    try
    {
        CreateLink(traeRuleLink, canonicalSource);
        WriteLineC("[完成] 已创建软链接 [Trae-Work]: " + traeRuleLink, ConsoleColor.Green);
        created++;
    }
    catch (Exception ex)
    {
        LinkError(traeRuleLink, ex);
        return 1;
    }
}
else
{
    WriteLineC("[跳过] Trae-Work: 未检测到配置目录 " + traeConfigDir, ConsoleColor.DarkGray);
    skipped++;
}

// ===== 5. Qoder Work CN 规则文件 =====
string qoderRuleDir = Home(".qoderworkcn", "awareness", "main");
if (Directory.Exists(qoderRuleDir))
{
    Console.WriteLine();
    WriteLineC("正在同步 Qoder Work CN 规则文件...", ConsoleColor.Cyan);

    string qoderTarget = Path.Combine(qoderRuleDir, "AGENTS.md");
    if (ExistsIncludingLink(qoderTarget))
        RemoveItem(qoderTarget);

    try
    {
        CreateLink(qoderTarget, canonicalSource);
        WriteLineC("[完成] 已创建软链接 [QoderWork]: " + qoderTarget, ConsoleColor.Green);
        created++;
    }
    catch (Exception ex)
    {
        LinkError(qoderTarget, ex);
        return 1;
    }
}
else
{
    WriteLineC("[跳过] QoderWork: 未检测到配置目录 " + qoderRuleDir, ConsoleColor.DarkGray);
    skipped++;
}

// ===== 6. Skills 目录同步 =====
// 只同步一级 Skills 子目录，目标 skills 目录本身保持为普通目录；
// dsh、Codex 与 ZCode 直接读取 ~\.agents\skills，无需同步
string skillsSource = Home(".agents", "skills");

if (!Directory.Exists(skillsSource))
{
    WriteLineC("未检测到 Skills 源目录: " + skillsSource + " ，跳过 Skills 同步。", ConsoleColor.DarkGray);
}
else
{
    Console.WriteLine();
    WriteLineC("正在同步 Skills 目录...", ConsoleColor.Cyan);

    // 按名称排序，与 setup.ps1 Get-ChildItem 的输出顺序一致
    DirectoryInfo[] skillSourceDirs = new DirectoryInfo(skillsSource)
        .GetDirectories()
        .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (skillSourceDirs.Length == 0)
        WriteLineC("Skills 源目录下没有一级子目录，跳过 Skills 同步。", ConsoleColor.DarkGray);

    foreach (var s in new (string Tool, string TargetDir)[]
    {
        ("WorkBuddy",    Home(".workbuddy", "skills")),
        ("WorkBuddy-AI", Home(".workbuddy-ai", "skills")),
        ("Trae-CN",      Home(".trae-cn", "skills")),
        ("Claude",       Home(".claude", "skills")),
        ("QoderWork",    Home(".qoderworkcn", "skills")),
    })
    {
        try
        {
            if (!SyncSkillsDir(s.Tool, s.TargetDir, skillSourceDirs))
                skipped++;
        }
        catch (Exception ex)
        {
            WriteLineC("同步 Skills 目录失败 [" + s.Tool + "]: " + s.TargetDir, ConsoleColor.Red);
            WriteLineC("原因: " + ex.Message, ConsoleColor.Red);
            WaitKey();
            return 1;
        }
    }

    // Marvis 用户 ID 每台电脑不同，动态扫描 User 目录，排除 default_user
    string marvisUserDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tencent", "Marvis", "User");
    if (Directory.Exists(marvisUserDir))
    {
        string? marvisUser = Directory.GetDirectories(marvisUserDir)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(d => !string.Equals(Path.GetFileName(d), "default_user", StringComparison.OrdinalIgnoreCase));
        if (marvisUser == null)
        {
            WriteLineC("未找到 Marvis 用户目录，跳过 Marvis Skills 同步。", ConsoleColor.DarkGray);
        }
        else
        {
            try
            {
                if (!SyncSkillsDir("Marvis", Path.Combine(marvisUser, "skills", "custom"), skillSourceDirs))
                    skipped++;
            }
            catch (Exception ex)
            {
                WriteLineC("同步 Skills 目录失败 [Marvis]: " + marvisUser, ConsoleColor.Red);
                WriteLineC("原因: " + ex.Message, ConsoleColor.Red);
                WaitKey();
                return 1;
            }
        }
    }
}

// ===== 7. WSL 同步 =====
// WSL 内无法使用 Windows 软链接，改用文件复制方式同步。
// 本工具只管 Windows 与 Ubuntu：通过注册表挑 Ubuntu 发行版，不启动 wsl 服务枚举，
// 绝不触碰 docker-desktop 等第三方发行版；
// 没有 WSL / 没有 Ubuntu / 命令失败时一律静默跳过，不报错不中断
Console.WriteLine();
WriteLineC("正在同步 WSL 配置 (OpenCode / Codex)...", ConsoleColor.Cyan);

var (wslPresent, wslDistro) = GetWslUbuntu();
if (wslDistro == null)
{
    WriteLineC(wslPresent
        ? "[跳过] WSL: 未检测到 Ubuntu 发行版"
        : "[跳过] WSL: 未安装 WSL 或没有已注册的发行版", ConsoleColor.DarkGray);
    skipped++;
}
else
{
    var (homeCode, homeOut) = CaptureRun("wsl.exe", "-d", wslDistro, "--", "bash", "-c", "echo $HOME");
    string wslHome = homeOut.Trim();
    if (homeCode != 0 || wslHome.Length == 0)
    {
        WriteLineC("[跳过] WSL: 无法获取 " + wslDistro + " 主目录", ConsoleColor.DarkGray);
        skipped++;
    }
    else
    {
        string wslCanonicalSource = ToWslPath(canonicalSource);
        string? wslSkillsSource = Directory.Exists(skillsSource) ? ToWslPath(skillsSource) : null;
        bool wslBroken = false;

        foreach (var (Tool, Dir) in new[] { ("OpenCode", wslHome + "/.config/opencode"), ("Codex", wslHome + "/.codex") })
        {
            if (wslBroken)
            {
                skipped++;
                continue;
            }

            // 创建目标目录并复制 AGENTS.md
            if (!RunWsl(wslDistro, "mkdir", "-p", Dir)
                || !RunWsl(wslDistro, "cp", wslCanonicalSource, Dir + "/AGENTS.md"))
            {
                WslGiveUp(Tool);
                wslBroken = true;
                skipped++;
                continue;
            }
            WriteLineC("[完成] 已复制 AGENTS.md -> WSL [" + Tool + "]: " + Dir + "/AGENTS.md", ConsoleColor.Green);
            created++;

            // 复制 Skills：移除旧副本后整体复制，确保内容与规范源一致
            if (wslSkillsSource != null)
            {
                if (RunWsl(wslDistro, "bash", "-c",
                        "rm -rf '" + Dir + "/skills' && cp -r '" + wslSkillsSource + "' '" + Dir + "/skills'"))
                {
                    WriteLineC("[完成] 已复制 Skills -> WSL [" + Tool + "]: " + Dir + "/skills/", ConsoleColor.Green);
                    created++;
                }
                else
                {
                    WslGiveUp(Tool);
                    wslBroken = true;
                    skipped++;
                }
            }
        }

        if (!wslBroken && wslSkillsSource == null)
            WriteLineC("[跳过] WSL Skills: 未检测到源目录 " + skillsSource, ConsoleColor.DarkGray);
    }
}

// ===== 8. 收尾 =====
Console.WriteLine();
WriteLineC($"完成！新建 {created} 个软链接，跳过 {skipped} 个未安装的工具。", ConsoleColor.Cyan);
WriteLineC("规范源: " + canonicalSource, ConsoleColor.Cyan);
// 无报错时 5 秒后自动退出；报错路径仍保留按键等待，方便阅读错误信息
WriteLineC("无报错，5 秒后自动退出...", ConsoleColor.Gray);
Thread.Sleep(5000);
return 0;

// ---- 以下为辅助函数 ----

// 单个工具的 skills 目录同步：清理旧项后为每个一级源子目录创建软链接；返回 false 表示整目录跳过。
// 非静态局部函数：需要直接累加顶层的 created 计数
bool SyncSkillsDir(string tool, string targetDir, DirectoryInfo[] skillSourceDirs)
{
    // 父目录不存在则跳过，不创建不存在的工具目录
    string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(targetDir))!;
    if (!Directory.Exists(parentDir))
    {
        WriteLineC("[跳过] " + tool + ": 未检测到配置目录 " + parentDir, ConsoleColor.DarkGray);
        return false;
    }

    // 兼容旧版本：若整个 skills 目录本身是链接，先移除并改建为普通目录
    if (IsReparsePoint(targetDir))
    {
        WriteLineC("移除旧版 Skills 目录链接: " + targetDir, ConsoleColor.Yellow);
        RemoveLink(targetDir);
    }

    if (!Directory.Exists(targetDir))
        Directory.CreateDirectory(targetDir);

    // 清理目标 skills 目录下指向已不存在源的一级链接。
    // 悬空链接会让 Directory.Exists 返回 false，必须检查链接目标是否真实存在
    foreach (FileSystemInfo e in new DirectoryInfo(targetDir).EnumerateFileSystemInfos().ToArray())
    {
        if ((e.Attributes & FileAttributes.ReparsePoint) != 0 && !LinkTargetExists(e))
        {
            WriteLineC("移除失效的 Skills 软链接: " + e.FullName, ConsoleColor.Yellow);
            RemoveLink(e.FullName);
        }
    }

    foreach (DirectoryInfo skillSourceDir in skillSourceDirs)
    {
        string skillTargetDir = Path.Combine(targetDir, skillSourceDir.Name);

        // 同名旧项需先清理：链接直接移除，普通文件/目录送入回收站
        if (ExistsIncludingLink(skillTargetDir))
            RemoveItem(skillTargetDir);

        try
        {
            CreateLink(skillTargetDir, skillSourceDir.FullName);
            WriteLineC("[完成] 已创建 Skills 软链接 [" + tool + "]: " + skillTargetDir, ConsoleColor.Green);
            created++;
        }
        catch (Exception ex)
        {
            LinkError(skillTargetDir, ex);
            throw;
        }
    }

    return true;
}

// 用户主目录下相对路径拼接
static string Home(params string[] parts)
{
    string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(new[] { up }.Concat(parts).ToArray());
}

static void WriteLineC(string msg, ConsoleColor color)
{
    ConsoleColor prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(msg);
    Console.ForegroundColor = prev;
}

// 报错路径统一等待按键，方便双击运行时阅读错误信息；输入被重定向时直接跳过
static void WaitKey()
{
    WriteLineC("按任意键退出...", ConsoleColor.Gray);
    if (!Console.IsInputRedirected)
    {
        try { Console.ReadKey(true); } catch { }
    }
}

// 软链接创建失败时输出与 setup.ps1 相同的提示
static void LinkError(string target, Exception ex)
{
    WriteLineC("创建软链接失败: " + target, ConsoleColor.Red);
    WriteLineC("原因: " + ex.Message, ConsoleColor.Red);
    WriteLineC("请确保以管理员身份运行此程序，或在 Windows 设置中启用开发者模式", ConsoleColor.Yellow);
    WaitKey();
}

// 悬空软链接会让 File.Exists / Directory.Exists 都返回 false，但链接本身仍存在，需一并识别
static bool ExistsIncludingLink(string path)
{
    if (Directory.Exists(path) || File.Exists(path))
        return true;
    return IsReparsePoint(path);
}

// 是否为重解析点（符号链接 / junction）。GetFileAttributes 不追踪链接，
// 因此对悬空链接同样有效；路径不存在时抛异常按 false 处理
static bool IsReparsePoint(string path)
{
    try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
    catch (FileNotFoundException) { return false; }
    catch (DirectoryNotFoundException) { return false; }
}

// 判断软链接的目标是否真实存在；相对目标按链接所在目录解析
static bool LinkTargetExists(FileSystemInfo link)
{
    string? t = link.LinkTarget;
    if (string.IsNullOrEmpty(t))
        return false;
    if (!Path.IsPathRooted(t))
        t = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link.FullName)!, t));
    return File.Exists(t) || Directory.Exists(t);
}

// 删除链接本身（不影响链接目标）。
// 目录链接必须用 RemoveDirectory 语义删除，用文件 API 会被拒绝；
// 悬空链接无法判断指向，先按文件删，被拒再按目录删
static void RemoveLink(string path)
{
    if (Directory.Exists(path))
    {
        new DirectoryInfo(path).Delete();
        return;
    }
    try
    {
        File.Delete(path);
    }
    catch (UnauthorizedAccessException)
    {
        new DirectoryInfo(path).Delete();
    }
}

// 链接直接删除（不影响规范源）；普通文件/目录送入回收站，保留恢复可能性
static void RemoveItem(string path)
{
    if (IsReparsePoint(path))
    {
        WriteLineC("移除现有软链接: " + path, ConsoleColor.Yellow);
        RemoveLink(path);
    }
    else if (new FileInfo(path).Exists)
    {
        WriteLineC("移入回收站: " + path, ConsoleColor.Yellow);
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }
    else
    {
        WriteLineC("移入回收站: " + path, ConsoleColor.Yellow);
        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }
}

// 创建软链接：目标为目录时创建目录链接，否则创建文件链接，类型不匹配会被 Windows 拒绝
static void CreateLink(string linkPath, string target)
{
    if (Directory.Exists(target))
        new DirectoryInfo(linkPath).CreateAsSymbolicLink(target);
    else
        new FileInfo(linkPath).CreateAsSymbolicLink(target);
}

// 从注册表读取已注册的 WSL 发行版，只挑 Ubuntu。
// 用注册表而非 wsl -l 枚举：读注册表不会启动 wslservice，
// docker-desktop 等第三方发行版完全不会被触碰；
// Lxss 键不存在即视为未安装 WSL。UbuntuDistro 为 null 时上层按跳过处理
static (bool WslPresent, string? UbuntuDistro) GetWslUbuntu()
{
    const string preferred = "Ubuntu-26.04"; // 当前主力发行版，存在时优先选用
    var names = new List<string>();
    RegistryKey? lxss = null;
    try
    {
        lxss = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Lxss");
        if (lxss == null)
            return (false, null);
        foreach (string guid in lxss.GetSubKeyNames())
        {
            using RegistryKey? k = lxss.OpenSubKey(guid);
            if (k?.GetValue("DistributionName") is string name
                && name.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase))
                names.Add(name);
        }
    }
    catch
    {
        return (false, null);
    }
    finally
    {
        lxss?.Dispose();
    }

    if (names.Count == 0)
        return (true, null);
    return (true,
        names.Contains(preferred, StringComparer.OrdinalIgnoreCase)
            ? preferred
            : names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).First());
}

// 在指定发行版内执行命令；wsl.exe 缺失或命令非零退出都按失败处理
static bool RunWsl(string distro, params string[] args)
{
    var (code, _) = CaptureRun("wsl.exe", new[] { "-d", distro, "--" }.Concat(args).ToArray());
    return code == 0;
}

// WSL 侧某个工具同步失败时统一提示；WSL 同步是尽力而为，不因此中断主流程
static void WslGiveUp(string tool)
{
    WriteLineC("[跳过] WSL: 同步 " + tool + " 失败，跳过剩余 WSL 同步", ConsoleColor.DarkGray);
}

// 运行外部命令并捕获标准输出；stderr 丢弃，与 setup.ps1 的 2>$null 一致
static (int ExitCode, string Output) CaptureRun(string fileName, params string[] args)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string a in args)
            psi.ArgumentList.Add(a);
        using Process? p = Process.Start(psi);
        if (p == null)
            return (-1, "");
        // stdout 与 stderr 必须并发读取，顺序读取在缓冲区写满时会互相死锁
        Task<byte[]> outTask = ReadAllBytesAsync(p.StandardOutput.BaseStream);
        Task<byte[]> errTask = ReadAllBytesAsync(p.StandardError.BaseStream);
        p.WaitForExit();
        return (p.ExitCode, DecodeProcessOutput(outTask.GetAwaiter().GetResult()));
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
    {
        // wsl 未安装等场景：按无输出处理，走跳过分支
        return (-1, "");
    }
}

static async Task<byte[]> ReadAllBytesAsync(Stream stream)
{
    using var ms = new MemoryStream();
    await stream.CopyToAsync(ms);
    return ms.ToArray();
}

// wsl.exe 自身的管道输出是 UTF-16LE（常不带 BOM），Linux 侧输出是 UTF-8，按字节特征嗅探解码
static string DecodeProcessOutput(byte[] b)
{
    if (b.Length == 0)
        return "";
    if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        return Encoding.Unicode.GetString(b, 2, b.Length - 2);
    if (b.Length >= 2 && b[0] != 0x00 && b[1] == 0x00)
        return Encoding.Unicode.GetString(b);
    return Encoding.UTF8.GetString(b);
}

// Windows 路径转 WSL 挂载路径：D:\a\b -> /mnt/d/a/b（避免依赖 wslpath 子进程）
static string ToWslPath(string winPath)
{
    string full = Path.GetFullPath(winPath);
    return "/mnt/" + char.ToLowerInvariant(full[0]) + full.Substring(2).Replace('\\', '/');
}