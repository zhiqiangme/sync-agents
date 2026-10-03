internal static class WslSkillsCopy
{
    // 路径只通过位置参数传入；先在同一文件系统完成复制，才替换旧副本。
    // Windows checkout 可能写入 CRLF，传给 Bash 前固定为 LF，避免回车成为命令参数。
    internal static string Script => """
    set -eu
    source=$1
    target=$2
    while [ "${target%/}" != "$target" ]; do target=${target%/}; done
    [ -n "$target" ] || { printf '%s\n' 'Refusing an empty or root target.' >&2; exit 1; }
    parent=$(dirname -- "$target")
    name=$(basename -- "$target")
    case "$name" in .|..) printf '%s\n' 'Refusing a dot directory target.' >&2; exit 1;; esac
    [ -d "$source" ] || { printf '%s\n' 'Skills source is not a directory.' >&2; exit 1; }
    stage=$(mktemp -d -- "$parent/.sync-skills.XXXXXXXX")
    old_moved=0
    installed=0
    cleanup() {
        result=$?
        trap - EXIT
        if [ "$old_moved" -eq 1 ] && [ "$installed" -eq 0 ]; then
            # 不覆盖并发产生的新目标；恢复失败时保留备份，避免清理掉唯一旧副本。
            if [ -e "$target" ] || [ -L "$target" ] || ! mv -T -- "$stage/old" "$target"; then
                printf 'Cannot restore Skills; backup retained at %s\n' "$stage/old" >&2
                exit 1
            fi
        fi
        rm -rf -- "$stage" || result=1
        exit "$result"
    }
    trap cleanup EXIT
    trap 'exit 129' HUP
    trap 'exit 130' INT
    trap 'exit 143' TERM
    # new 不存在，cp 不会把整个源目录嵌套到已有目标之下。
    cp -r -- "$source" "$stage/new"
    if [ -e "$target" ] || [ -L "$target" ]; then
        # -T 移动链接本身，包括悬空链接，不进入其指向的目录。
        mv -T -- "$target" "$stage/old"
        old_moved=1
    fi
    mv -T -- "$stage/new" "$target"
    installed=1
    """.Replace("\r\n", "\n", StringComparison.Ordinal);

    internal static string[] GetCommandArguments(string source, string target) =>
        ["bash", "-c", Script, "sync-skills", source, target];
}
