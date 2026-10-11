using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// 不引用生产入口，只调用文件系统辅助类，所有写操作限制在本次创建的临时目录。

var checks = new (string Name, Action<string> Run)[]
{
    ("只读文件链接删除不影响源文件", ReadOnlyFileLink),
    ("空 Skills 源保留现有目录链接", EmptySkillsSource),
    ("同步清理悬空链接并保留自有文件", SkillsSync),
    ("移除 junction 保留其目标", JunctionRemoval),
    ("非链接重解析文件不会被清理", NonLinkReparsePoint),
    ("未安装工具不创建配置目录", MissingTool),
    ("未安装工具不清理其规则文件", MissingToolKeepsRuleFile),
    ("目标指向规范源时拒绝清理", CanonicalGuard),
    ("规则文件链接替换与同目录旧链接清理", RuleLinkReplace),
    ("内容差异检测", ContentDiffers),
    ("Skills 源过滤", SkillSourceFilter),
};
int failed = 0;
foreach (var (name, run) in checks)
{
    string root = Path.Combine(Path.GetTempPath(), "agents-config-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        run(root);
        Console.WriteLine("PASS: " + name);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("FAIL: " + name + ": " + ex);
        failed++;
    }
    finally
    {
        // root 始终是本轮 Guid 目录；测试链接的目标也全部位于 root 内。
        Assert(Path.GetDirectoryName(Path.GetFullPath(root)) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
            && Path.GetFileName(root).StartsWith("agents-config-test-", StringComparison.Ordinal), "临时清理边界不匹配");
        Directory.Delete(root, recursive: true);
    }
}
Console.WriteLine($"{checks.Length - failed}/{checks.Length} checks passed.");
return failed == 0 ? 0 : 1;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Quiet(string message, ConsoleColor color) { }

static void ReadOnlyFileLink(string root)
{
    string source = Path.Combine(root, "source.md");
    string link = Path.Combine(root, "AGENTS.md");
    File.WriteAllText(source, "keep source");
    FileAttributes sourceAttributes = File.GetAttributes(source);
    File.CreateSymbolicLink(link, source);
    File.SetAttributes(link, File.GetAttributes(link) | FileAttributes.ReadOnly);
    try
    {
        Assert((File.GetAttributes(link) & FileAttributes.ReadOnly) != 0, "未生成只读文件链接");
        SyncFileSystem.RemoveLink(link);
        Assert(!SyncFileSystem.ExistsIncludingLink(link), "链接未移除");
        Assert(File.ReadAllText(source) == "keep source", "删除链接改动了源文件内容");
        Assert(File.GetAttributes(source) == sourceAttributes, "删除链接改动了源文件属性");
    }
    finally
    {
        if (File.Exists(link))
            File.SetAttributes(link, File.GetAttributes(link) & ~FileAttributes.ReadOnly);
    }
}

static void EmptySkillsSource(string root)
{
    string existing = Path.Combine(root, "existing");
    Directory.CreateDirectory(existing);
    File.WriteAllText(Path.Combine(existing, "keep.txt"), "existing skills");
    string tool = Path.Combine(root, "tool");
    Directory.CreateDirectory(tool);
    string target = Path.Combine(tool, "skills");
    Directory.CreateSymbolicLink(target, existing);
    int count = SyncFileSystem.SyncSkillsDir("test", target, [], Quiet);
    Assert(count == -1 && SyncFileSystem.IsLink(target), "空源替换了原目录链接");
    Assert(File.ReadAllText(Path.Combine(target, "keep.txt")) == "existing skills", "原有 Skills 不再可见");
    string absent = Path.Combine(tool, "absent");
    SyncFileSystem.SyncSkillsDir("test", absent, [], Quiet);
    Assert(!Directory.Exists(absent), "空源创建了目标目录");
}

static void SkillsSync(string root)
{
    string source = Path.Combine(root, "source", "new-skill");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "SKILL.md"), "source skill");
    string target = Path.Combine(root, "tool", "skills");
    Directory.CreateDirectory(target);
    File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
    string missing = Path.Combine(root, "missing");
    string deadFile = Path.Combine(target, "dead-file");
    string deadDirectory = Path.Combine(target, "dead-directory");
    File.CreateSymbolicLink(deadFile, missing);
    Directory.CreateSymbolicLink(deadDirectory, missing);
    // 同名只读链接也必须能够替换，而不能中断同步。
    string oldLink = Path.Combine(target, "new-skill");
    File.CreateSymbolicLink(oldLink, missing);
    File.SetAttributes(oldLink, File.GetAttributes(oldLink) | FileAttributes.ReadOnly);
    int count = SyncFileSystem.SyncSkillsDir("test", target, [new DirectoryInfo(source)], Quiet);
    Assert(count == 1, "新建链接计数错误");
    Assert(!SyncFileSystem.ExistsIncludingLink(deadFile) && !SyncFileSystem.ExistsIncludingLink(deadDirectory),
        "悬空链接未被清理");
    Assert(SyncFileSystem.IsLink(oldLink) && File.ReadAllText(Path.Combine(oldLink, "SKILL.md")) == "source skill",
        "同名只读文件链接未替换为源 Skills 目录链接");
    Assert(File.ReadAllText(Path.Combine(target, "keep.txt")) == "keep", "工具自有文件被删除");
}

static void JunctionRemoval(string root)
{
    string source = Path.Combine(root, "source");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "keep.txt"), "keep");
    string link = Path.Combine(root, "junction");
    var psi = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true };
    foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
        "New-Item -ItemType Junction -Path $env:AGENTS_TEST_LINK -Target $env:AGENTS_TEST_TARGET -ErrorAction Stop | Out-Null" })
        psi.ArgumentList.Add(arg);
    psi.Environment["AGENTS_TEST_LINK"] = link;
    psi.Environment["AGENTS_TEST_TARGET"] = source;
    using Process process = Process.Start(psi)!;
    process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    Assert(process.ExitCode == 0 && SyncFileSystem.IsLink(link), "junction 创建或识别失败");
    SyncFileSystem.RemoveLink(link);
    Assert(!Directory.Exists(link) && File.ReadAllText(Path.Combine(source, "keep.txt")) == "keep",
        "junction 删除影响了目标");
}

static void NonLinkReparsePoint(string root)
{
    string source = Path.Combine(root, "source", "skill");
    Directory.CreateDirectory(source);
    string target = Path.Combine(root, "tool", "skills");
    Directory.CreateDirectory(target);
    string keep = Path.Combine(target, "keep.txt");
    File.WriteAllText(keep, "real data");
    // 使用临时文件上的自定义重解析标签，验证与云占位文件相同的「非链接」分类。
    using (var reparse = new TestReparsePoint(keep))
    {
        Assert((File.GetAttributes(keep) & FileAttributes.ReparsePoint) != 0, "重解析测试前提不成立");
        Assert(!SyncFileSystem.IsLink(keep), "非链接重解析文件被误判为链接");
        try
        {
            SyncFileSystem.RemoveLink(keep);
            throw new InvalidOperationException("RemoveLink 未拒绝真实数据对象");
        }
        catch (IOException) { }
        SyncFileSystem.SyncSkillsDir("test", target, [new DirectoryInfo(source)], Quiet);
        Assert((File.GetAttributes(keep) & FileAttributes.ReparsePoint) != 0,
            "Skills 清理删除了非链接重解析文件");
    }
    Assert(File.ReadAllText(keep) == "real data", "非链接重解析文件的数据被改变");
}

static void MissingTool(string root)
{
    string source = Path.Combine(root, "source");
    Directory.CreateDirectory(source);
    string target = Path.Combine(root, "missing-tool", "skills");
    int count = SyncFileSystem.SyncSkillsDir("test", target, [new DirectoryInfo(source)], Quiet);
    Assert(count == -1 && !Directory.Exists(Path.GetDirectoryName(target)), "创建了未安装工具的目录");
}

// 规则文件只使用链接：真实文件会被移入回收站，测试不应污染用户回收站。
static string WriteCanonical(string root)
{
    string canonical = Path.Combine(root, "agents", "AGENTS.md");
    Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
    File.WriteAllText(canonical, "canonical");
    return canonical;
}

static void MissingToolKeepsRuleFile(string root)
{
    // 对应 ZCode：配置目录（cli）缺失，但规则文件所在目录存在
    string canonical = WriteCanonical(root);
    string toolDir = Path.Combine(root, "zcode");
    Directory.CreateDirectory(toolDir);
    string target = Path.Combine(toolDir, "AGENTS.md");
    File.CreateSymbolicLink(target, Path.Combine(root, "elsewhere.md"));
    int count = SyncFileSystem.SyncFileLink("test", Path.Combine(toolDir, "cli"), target, canonical, [target], Quiet);
    Assert(count == -1 && SyncFileSystem.IsLink(target), "未安装工具的规则文件被清理");
}

static void CanonicalGuard(string root)
{
    string canonical = WriteCanonical(root);
    string agentsDir = Path.GetDirectoryName(canonical)!;
    ExpectGuard(() => SyncFileSystem.SyncFileLink("test", agentsDir, canonical, canonical, [], Quiet));
    // 工具目录本身是指向规范源目录的链接（如 ~\.dsh -> ~\.agents）
    string linkedHome = Path.Combine(root, "dsh");
    Directory.CreateSymbolicLink(linkedHome, agentsDir);
    string viaLink = Path.Combine(linkedHome, "AGENTS.md");
    ExpectGuard(() => SyncFileSystem.SyncFileLink("test", linkedHome, viaLink, canonical, [viaLink], Quiet));
    // 只在待清理列表中命中规范源时，也不能先删除其他文件
    string toolDir = Path.Combine(root, "tool");
    Directory.CreateDirectory(toolDir);
    string otherLink = Path.Combine(toolDir, "AGENTS.md");
    File.CreateSymbolicLink(otherLink, canonical);
    ExpectGuard(() => SyncFileSystem.SyncFileLink("test", toolDir, otherLink, canonical, [otherLink, viaLink], Quiet));
    Assert(SyncFileSystem.IsLink(otherLink), "规范源保护触发前已清理其他文件");
    Assert(!SyncFileSystem.IsLink(canonical) && File.ReadAllText(canonical) == "canonical", "规范源被改动");

    static void ExpectGuard(Action run)
    {
        try
        {
            run();
            throw new IOException("未触发规范源保护");
        }
        catch (InvalidOperationException) { }
    }
}

static void RuleLinkReplace(string root)
{
    string canonical = WriteCanonical(root);
    string toolDir = Path.Combine(root, "claude");
    Directory.CreateDirectory(toolDir);
    string target = Path.Combine(toolDir, "CLAUDE.md");
    string stale = Path.Combine(toolDir, "AGENTS.md");
    string other = Path.Combine(root, "other.md");
    File.WriteAllText(other, "other");
    File.CreateSymbolicLink(target, other);
    File.CreateSymbolicLink(stale, Path.Combine(root, "missing.md"));
    int count = SyncFileSystem.SyncFileLink("test", toolDir, target, canonical, [stale, target], Quiet);
    Assert(count == 1 && File.ReadAllText(target) == "canonical", "规则文件未指向规范源");
    Assert(!SyncFileSystem.ExistsIncludingLink(stale), "同目录旧链接未清理");
    Assert(File.ReadAllText(other) == "other", "删除旧链接影响了其目标");
}

static void ContentDiffers(string root)
{
    string canonical = WriteCanonical(root);
    string same = Path.Combine(root, "same.md");
    string changed = Path.Combine(root, "changed.md");
    File.WriteAllText(same, "canonical");
    File.WriteAllText(changed, "edited in tool");
    Assert(!SyncFileSystem.ContentDiffers(same, canonical), "相同内容被判为不同");
    Assert(SyncFileSystem.ContentDiffers(changed, canonical), "不同内容未被识别");
}

static void SkillSourceFilter(string root)
{
    string source = Path.Combine(root, "skills");
    foreach (string name in new[] { "b-skill", "A-skill", ".git", "no-skill-md" })
        Directory.CreateDirectory(Path.Combine(source, name));
    File.WriteAllText(Path.Combine(source, "b-skill", "SKILL.md"), "b");
    File.WriteAllText(Path.Combine(source, "A-skill", "SKILL.md"), "a");
    File.WriteAllText(Path.Combine(source, ".git", "SKILL.md"), "hidden");
    string hidden = Path.Combine(source, "hidden-attr");
    Directory.CreateDirectory(hidden);
    File.WriteAllText(Path.Combine(hidden, "SKILL.md"), "hidden");
    File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
    Directory.CreateSymbolicLink(Path.Combine(source, "dangling"), Path.Combine(root, "missing"));
    string[] names = SyncFileSystem.GetSkillSources(source, Quiet).Select(d => d.Name).ToArray();
    Assert(names.SequenceEqual(new[] { "A-skill", "b-skill" }), "Skills 源过滤结果错误: " + string.Join(",", names));
}

sealed class TestReparsePoint : IDisposable
{
    private readonly SafeFileHandle handle;
    private readonly byte[] header = new byte[24];

    internal TestReparsePoint(string path)
    {
        // 非 Microsoft 标签使用 24 字节 REPARSE_GUID_DATA_BUFFER；非链接、无目标路径。
        // https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-reparse_guid_data_buffer
        BitConverter.GetBytes(0x00000042u).CopyTo(header, 0);
        Guid.NewGuid().ToByteArray().CopyTo(header, 8);
        handle = CreateFile(path, 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!DeviceIoControl(handle, 0x000900A4, header, header.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
    }

    public void Dispose()
    {
        try
        {
            if (!DeviceIoControl(handle, 0x000900AC, header, header.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { handle.Dispose(); }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputLength,
        IntPtr output, int outputLength, out uint bytesReturned, IntPtr overlapped);
}
