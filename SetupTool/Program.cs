// sync-agents —— 把规范源 AGENTS.md / skills 以软链接方式同步到各 AI 编码工具的配置目录。
// Native AOT 发布为单个原生 exe：目标机器无需任何运行时，运行过程也不解包临时文件。

using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

// 提权重启时附带启动账户的 SID，用于识别 UAC 换成了其他管理员账户的情况
const string ElevatedFromArg = "--elevated-from";

int created = 0;
int skipped = 0;

// ===== 1. 权限检查：创建软链接需要管理员权限；开启开发者模式后普通权限即可 =====
using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
{
    string currentSid = identity.User!.Value;
    int marker = Array.IndexOf(args, ElevatedFromArg);
    string? elevatedFrom = marker >= 0 && marker + 1 < args.Length ? args[marker + 1] : null;

    // 换账户提权后 UserProfile 等路径属于另一个账户，继续执行会把链接建到别人的配置目录
    if (elevatedFrom != null && !string.Equals(elevatedFrom, currentSid, StringComparison.OrdinalIgnoreCase))
    {
        WriteLineC("提升权限后的账户与启动程序的账户不同，已停止，以免改动其他账户的配置。", ConsoleColor.Red);
        WriteLineC("请使用当前账户的管理员权限运行，或在 Windows 设置中开启开发者模式后直接运行", ConsoleColor.Yellow);
        WaitKey();
        return 1;
    }

    bool isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    int devMode = 0;
    using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"))
        devMode = key?.GetValue("AllowDevelopmentWithoutDevLicense") as int? ?? 0;

    if (!isAdmin && devMode != 1)
    {
        // 已提权一次仍无权限时不再请求，避免反复弹出 UAC
        if (elevatedFrom != null)
        {
            WriteLineC("提升权限后仍未获得管理员权限，请右键此程序选择「以管理员身份运行」", ConsoleColor.Red);
            WaitKey();
            return 1;
        }

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
        elevate.ArgumentList.Add(ElevatedFromArg);
        elevate.ArgumentList.Add(currentSid);
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

string csContent;
try { csContent = File.ReadAllText(canonicalSource); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    WriteLineC("无法读取规范源: " + canonicalSource, ConsoleColor.Red);
    WriteLineC("原因: " + ex.Message, ConsoleColor.Red);
    WaitKey();
    return 1;
}

// 规范源为空时不继续，避免用空文件覆盖各工具原有配置
if (csContent.Length == 0)
{
    WriteLineC("规范源为空: " + canonicalSource, ConsoleColor.Red);
    WriteLineC("请写入内容后再运行本程序", ConsoleColor.Yellow);
    WaitKey();
    return 1;
}

// ===== 3. 各工具的 AGENTS.md / CLAUDE.md 同步 =====
// 主目录解析规则与各工具自身一致：优先环境变量，未设置时回退到用户主目录下的默认目录
string dshHome = EnvDir("DSH_HOME", ".dsh");
string codexHome = EnvDir("CODEX_HOME", ".codex");
string claudeHome = EnvDir("CLAUDE_CONFIG_DIR", ".claude");

// ConfigDir 仅用于判断工具是否已安装；所有目标指向同一规范源，排列顺序只影响输出顺序
var targets = new (string Tool, string ConfigDir, string TargetFile)[]
{
    ("DSH",      dshHome,                     Path.Combine(dshHome, "AGENTS.md")),
    ("Codex",    codexHome,                   Path.Combine(codexHome, "AGENTS.md")),
    ("OpenCode", Home(".config", "opencode"), Home(".config", "opencode", "AGENTS.md")),
    ("Gemini",   Home(".gemini", "config"),   Home(".gemini", "config", "AGENTS.md")),
    ("Claude",   claudeHome,                  Path.Combine(claudeHome, "CLAUDE.md")),
    // ZCode：用户级指令文件固定为 ~\.zcode\AGENTS.md；以 ~\.zcode\cli 判断是否已安装
    ("ZCode",    Home(".zcode", "cli"),       Home(".zcode", "AGENTS.md")),
    ("Qoder-CN", Home(".qoder-cn"),           Home(".qoder-cn", "AGENTS.md")),
    ("Qoder",    Home(".qoder"),              Home(".qoder", "AGENTS.md")),
    ("MiMoCode", Home(".config", "mimocode"), Home(".config", "mimocode", "AGENTS.md")),
};

// 目标目录中同名的 AGENTS.md / CLAUDE.md 都视为旧文件，逐个工具清理后立即创建链接
foreach (var t in targets)
{
    string dir = Path.GetDirectoryName(t.TargetFile)!;
    string[] oldFiles = [Path.Combine(dir, "AGENTS.md"), Path.Combine(dir, "CLAUDE.md")];
    if (!RunSync(t.TargetFile, () =>
            SyncFileSystem.SyncFileLink(t.Tool, t.ConfigDir, t.TargetFile, canonicalSource, oldFiles, WriteLineC)))
        return 1;
}

// ===== 4. Trae Work CN 规则文件 =====
// 以 .trae-cn 根目录判断软件是否存在；user_rules 不存在时自动创建，
// 其中原有 rule-*.md 会被替换为指向规范源的软链接（时间戳文件由 Trae 自行管理）
string traeConfigDir = Home(".trae-cn");
string traeRuleDir = Path.Combine(traeConfigDir, "user_rules");
// 用固定名称 rule-agents.md，避免与 Trae 自动生成的时间戳文件混淆
string traeRuleLink = Path.Combine(traeRuleDir, "rule-agents.md");

if (Directory.Exists(traeConfigDir))
{
    Console.WriteLine();
    WriteLineC("正在同步 Trae Work CN 规则文件...", ConsoleColor.Cyan);
}
if (!RunSync(traeRuleLink, () =>
    {
        if (Directory.Exists(traeConfigDir) && !Directory.Exists(traeRuleDir))
        {
            Directory.CreateDirectory(traeRuleDir);
            WriteLineC("已创建 Trae Work CN 规则目录: " + traeRuleDir, ConsoleColor.Yellow);
        }
        string[] oldRules = Directory.Exists(traeRuleDir) ? Directory.GetFiles(traeRuleDir, "rule-*.md") : [];
        return SyncFileSystem.SyncFileLink("Trae-Work", traeConfigDir, traeRuleLink, canonicalSource, oldRules, WriteLineC);
    }))
    return 1;

// ===== 5. Qoder Work CN 规则文件 =====
string qoderRuleDir = Home(".qoderworkcn", "awareness", "main");
string qoderTarget = Path.Combine(qoderRuleDir, "AGENTS.md");

if (Directory.Exists(qoderRuleDir))
{
    Console.WriteLine();
    WriteLineC("正在同步 Qoder Work CN 规则文件...", ConsoleColor.Cyan);
}
if (!RunSync(qoderTarget, () =>
        SyncFileSystem.SyncFileLink("QoderWork", qoderRuleDir, qoderTarget, canonicalSource, [], WriteLineC)))
    return 1;

// ===== 6. Skills 目录同步 =====
// 只同步一级 Skills 子目录，目标 skills 目录本身保持为普通目录；
// dsh、Codex 直接读取 ~\.agents\skills，无需同步
string skillsSource = Home(".agents", "skills");

if (!Directory.Exists(skillsSource))
{
    WriteLineC("未检测到 Skills 源目录: " + skillsSource + " ，跳过 Skills 同步。", ConsoleColor.DarkGray);
}
else
{
    Console.WriteLine();
    WriteLineC("正在同步 Skills 目录...", ConsoleColor.Cyan);

    DirectoryInfo[] skillSourceDirs = SyncFileSystem.GetSkillSources(skillsSource, WriteLineC);

    if (skillSourceDirs.Length == 0)
        WriteLineC("Skills 源目录下没有可同步的 Skill，跳过 Skills 同步。", ConsoleColor.DarkGray);
    else
    {
        foreach (var s in new (string Tool, string TargetDir)[]
        {
            ("WorkBuddy",    Home(".workbuddy", "skills")),
            ("WorkBuddy-AI", Home(".workbuddy-ai", "skills")),
            ("Trae-CN",      Home(".trae-cn", "skills")),
            ("Claude",       Path.Combine(claudeHome, "skills")),
            ("QoderWork",    Home(".qoderworkcn", "skills")),
        })
        {
            if (!RunSync(s.TargetDir, () => SyncFileSystem.SyncSkillsDir(s.Tool, s.TargetDir, skillSourceDirs, WriteLineC)))
                return 1;
        }
    }
}

// ===== 7. 收尾 =====
Console.WriteLine();
WriteLineC($"完成！新建 {created} 个软链接，跳过 {skipped} 个同步项（对应配置目录不存在）。", ConsoleColor.Cyan);
WriteLineC("规范源: " + canonicalSource, ConsoleColor.Cyan);
// 无报错时 5 秒后自动退出；报错路径仍保留按键等待，方便阅读错误信息
WriteLineC("无报错，5 秒后自动退出...", ConsoleColor.Gray);
Thread.Sleep(5000);
return 0;

// ---- 以下为辅助函数 ----

// 执行单个同步项并累计计数（返回 -1 表示跳过）；失败时输出原因并返回 false，由调用方退出。
// 非静态局部函数：需要直接累加顶层的 created / skipped 计数
bool RunSync(string target, Func<int> sync)
{
    try
    {
        int count = sync();
        if (count < 0)
            skipped++;
        else
            created += count;
        return true;
    }
    catch (Exception ex)
    {
        WriteLineC("同步失败: " + target, ConsoleColor.Red);
        WriteLineC("原因: " + ex.Message, ConsoleColor.Red);
        if (IsPrivilegeError(ex))
            WriteLineC("请确保以管理员身份运行此程序，或在 Windows 设置中启用开发者模式", ConsoleColor.Yellow);
        WriteLineC("如有旧文件已移入回收站，可从回收站恢复。", ConsoleColor.Yellow);
        WaitKey();
        return false;
    }
}

// 0x80070522 = HRESULT_FROM_WIN32(ERROR_PRIVILEGE_NOT_HELD)：无权创建符号链接
static bool IsPrivilegeError(Exception ex) =>
    ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070522);

// 用户主目录下相对路径拼接
static string Home(params string[] parts)
{
    string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(new[] { up }.Concat(parts).ToArray());
}

// 优先读取工具自身的主目录环境变量，未设置时回退到用户主目录下的默认目录
static string EnvDir(string variable, string defaultDir) =>
    Environment.GetEnvironmentVariable(variable) is { Length: > 0 } dir ? dir : Home(defaultDir);

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
