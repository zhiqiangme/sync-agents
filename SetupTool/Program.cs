// sync-agents —— 把规范源 AGENTS.md / skills 以软链接方式同步到各 AI 编码工具的配置目录。
// 由仓库根目录 setup.ps1 移植而来，Native AOT 发布为单个原生 exe：
// 目标机器无需任何运行时，运行过程也不解包临时文件。

using System.Diagnostics;
using System.Security.Principal;
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
    else
    {
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
}

// ===== 7. 收尾 =====
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
    int count = SyncFileSystem.SyncSkillsDir(tool, targetDir, skillSourceDirs, WriteLineC);
    if (count < 0)
        return false;
    created += count;
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

static bool ExistsIncludingLink(string path) => SyncFileSystem.ExistsIncludingLink(path);
static void RemoveItem(string path) => SyncFileSystem.RemoveItem(path, WriteLineC);
static void CreateLink(string linkPath, string target) => SyncFileSystem.CreateLink(linkPath, target);
