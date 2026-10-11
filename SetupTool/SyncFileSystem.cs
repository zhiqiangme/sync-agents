using Microsoft.VisualBasic.FileIO;

internal static class SyncFileSystem
{
    // LinkTarget 仅识别符号链接和 junction；云占位文件等重解析点仍按真实数据处理。
    internal static bool IsLink(FileSystemInfo item) => item.LinkTarget != null;

    internal static bool IsLink(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return IsLink(GetItem(path, attributes));
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static bool ExistsIncludingLink(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
            return true;
        return IsLink(path);
    }

    internal static bool LinkTargetExists(FileSystemInfo link)
    {
        string? target = link.LinkTarget;
        if (target == null)
            return false;
        if (!Path.IsPathRooted(target))
            target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link.FullName)!, target));
        return File.Exists(target) || Directory.Exists(target);
    }

    internal static void RemoveLink(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        FileSystemInfo item = GetItem(path, attributes);
        if (!IsLink(item))
            throw new IOException("拒绝按链接删除非链接对象: " + path);

        // Windows 的 SetFileAttributes 修改链接本身，不改链接目标；与原脚本 -Force 行为一致。
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);

        // Directory 属性来自链接本身，悬空目录链接也能选到正确的删除 API。
        item.Delete();
    }

    internal static void RemoveItem(string path, Action<string, ConsoleColor> report)
    {
        if (IsLink(path))
        {
            report("移除现有软链接: " + path, ConsoleColor.Yellow);
            RemoveLink(path);
        }
        else
        {
            report("移入回收站: " + path, ConsoleColor.Yellow);
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }

    internal static void CreateLink(string linkPath, string target)
    {
        if (Directory.Exists(target))
            new DirectoryInfo(linkPath).CreateAsSymbolicLink(target);
        else
            new FileInfo(linkPath).CreateAsSymbolicLink(target);
    }

    // 所在目录本身是链接时解析到最终目标，覆盖 DSH_HOME=~\.agents 或 ~\.dsh 链接到 ~\.agents 等配置；
    // 更上层父目录的链接不在此解析。
    internal static bool IsSamePath(string a, string b) =>
        string.Equals(ResolveDirectory(a), ResolveDirectory(b), StringComparison.OrdinalIgnoreCase);

    // 普通文件与规范源内容不同，通常是工具以「写临时文件再改名」方式保存而替换掉了软链接；
    // 读取失败时按不同处理，宁可多提示一次。
    internal static bool ContentDiffers(string path, string canonical)
    {
        try { return !File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(canonical)); }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    // 移除旧规则文件；内容与规范源不同的普通文件先醒目提示，避免工具内的修改随回收站被遗忘。
    internal static void RemoveStale(string path, string canonical, Action<string, ConsoleColor> report)
    {
        if (!IsLink(path) && File.Exists(path) && ContentDiffers(path, canonical))
            report("警告：" + path + " 是内容与规范源不同的普通文件，将移入回收站；如有需要保留的修改，请从回收站恢复后合并到规范源",
                ConsoleColor.Magenta);
        RemoveItem(path, report);
    }

    // 单个工具的规则文件同步，返回本次新建链接数，-1 表示未安装而跳过（此时不触碰任何文件）。
    // 先确认所有待清理路径都不是规范源本身，再清理并立即创建链接，失败时只影响当前工具。
    internal static int SyncFileLink(string tool, string configDir, string targetFile, string canonical,
        IEnumerable<string> cleanupPaths, Action<string, ConsoleColor> report)
    {
        if (!Directory.Exists(configDir))
        {
            report("[跳过] " + tool + ": 未检测到配置目录 " + configDir, ConsoleColor.DarkGray);
            return -1;
        }

        string[] paths = cleanupPaths.Append(targetFile).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string p in paths)
        {
            if (IsSamePath(p, canonical))
                throw new InvalidOperationException("目标路径指向规范源本身，已拒绝清理: " + p);
        }

        foreach (string p in paths)
        {
            if (ExistsIncludingLink(p))
                RemoveStale(p, canonical, report);
        }

        CreateLink(targetFile, canonical);
        report("[完成] 已创建软链接 [" + tool + "]: " + targetFile, ConsoleColor.Green);
        return 1;
    }

    // 只同步有效 Skill：跳过隐藏目录（如 .git）、目标不存在的链接和缺少 SKILL.md 的目录；按名称排序保证输出稳定。
    internal static DirectoryInfo[] GetSkillSources(string sourceDir, Action<string, ConsoleColor> report)
    {
        var result = new List<DirectoryInfo>();
        foreach (DirectoryInfo d in new DirectoryInfo(sourceDir).GetDirectories()
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            string? reason =
                d.Name.StartsWith('.') || (d.Attributes & FileAttributes.Hidden) != 0 ? "隐藏目录"
                : IsLink(d) && !LinkTargetExists(d) ? "链接目标不存在"
                : !File.Exists(Path.Combine(d.FullName, "SKILL.md")) ? "缺少 SKILL.md"
                : null;
            if (reason == null)
                result.Add(d);
            else
                report("[跳过] Skills 源 " + d.Name + ": " + reason, ConsoleColor.DarkGray);
        }
        return result.ToArray();
    }

    // 返回本次新建链接数，-1 表示跳过；空源不能清理或转换现有目标目录。
    internal static int SyncSkillsDir(string tool, string targetDir, DirectoryInfo[] sources,
        Action<string, ConsoleColor> report)
    {
        if (sources.Length == 0)
            return -1;

        string parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(targetDir))!;
        if (!Directory.Exists(parentDir))
        {
            report("[跳过] " + tool + ": 未检测到配置目录 " + parentDir, ConsoleColor.DarkGray);
            return -1;
        }

        if (IsLink(targetDir))
        {
            report("移除旧版 Skills 目录链接: " + targetDir, ConsoleColor.Yellow);
            RemoveLink(targetDir);
        }
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        foreach (FileSystemInfo item in new DirectoryInfo(targetDir).EnumerateFileSystemInfos().ToArray())
        {
            if (IsLink(item) && !LinkTargetExists(item))
            {
                report("移除失效的 Skills 软链接: " + item.FullName, ConsoleColor.Yellow);
                RemoveLink(item.FullName);
            }
        }

        int created = 0;
        foreach (DirectoryInfo source in sources)
        {
            string target = Path.Combine(targetDir, source.Name);
            if (ExistsIncludingLink(target))
                RemoveItem(target, report);
            CreateLink(target, source.FullName);
            report("[完成] 已创建 Skills 软链接 [" + tool + "]: " + target, ConsoleColor.Green);
            created++;
        }
        return created;
    }

    private static FileSystemInfo GetItem(string path, FileAttributes attributes) =>
        (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);

    private static string ResolveDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full)!;
        try
        {
            if (Directory.Exists(dir) && new DirectoryInfo(dir).ResolveLinkTarget(returnFinalTarget: true) is { } resolved)
                dir = Path.GetFullPath(resolved.FullName);
        }
        catch (IOException) { }
        return Path.Combine(dir, Path.GetFileName(full));
    }
}
