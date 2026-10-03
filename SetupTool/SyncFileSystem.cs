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
}
