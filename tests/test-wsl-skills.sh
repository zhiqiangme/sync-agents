#!/usr/bin/env bash
set -euo pipefail

# Git Bash 的 OSTYPE 可能是 cygwin，以内核名称识别实际的 MSYS 运行环境。
case "$(uname -s)" in
    MSYS*|MINGW*) export MSYS="${MSYS:+$MSYS }winsymlinks:nativestrict" ;;
    CYGWIN*) export CYGWIN="${CYGWIN:+$CYGWIN }winsymlinks:nativestrict" ;;
esac

repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
if [[ $# -gt 0 ]]; then
    script=$(cat -- "$1")
else
    script=$(awk '{sub(/\r$/, "")}
        /^    internal static string Script => """$/ {inside=1; next}
        inside && /^    """/ {exit}
        inside {sub(/^    /, ""); print}' "$repo/SetupTool/WslSkillsCopy.cs")
fi
[[ -n "$script" ]] || { printf '%s\n' 'Cannot extract the production Bash script.' >&2; exit 1; }
root=$(mktemp -d)
trap 'rm -rf -- "$root"' EXIT
cd -- "$root"
real_cp=$(command -v cp)
real_mv=$(command -v mv)
export REAL_CP="$real_cp" REAL_MV="$real_mv"

fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }
run_sync() { bash -c "$script" sync-skills "$1" "$2"; }
assert_clean() {
    local leftovers
    leftovers=$(find "$1" -maxdepth 1 -name '.sync-skills.*' -print)
    [[ -z "$leftovers" ]] || fail "temporary data remains: $leftovers"
}

# 引号、空格和 shell 元字符全部必须作为普通路径数据处理。
parent="$root/owner O'Brien; \$(touch injected); &"
source="$parent/source O'Brien \$() & [*]"
target="$parent/target O'Brien \$() & [*]"
mkdir -p -- "$source/skill" "$target"
printf 'new\n' > "$source/skill/SKILL.md"
printf 'old\n' > "$target/obsolete"
run_sync "$source" "$target"
[[ $(cat "$target/skill/SKILL.md") == new ]] || fail 'replacement content missing'
[[ ! -e "$target/obsolete" && ! -e "$target/$(basename -- "$source")" ]] || fail 'old or nested content remains'
[[ ! -e "$root/injected" ]] || fail 'path text was executed'
assert_clean "$parent"
printf '%s\n' 'PASS: successful replacement and special-character paths'

mock="$root/mock"
mkdir -- "$mock"
cat > "$mock/cp" <<'MOCK'
#!/usr/bin/env bash
set -eu
destination=${!#}
mkdir -p -- "$destination"
printf 'partial\n' > "$destination/partial"
exit 42
MOCK
cat > "$mock/mv" <<'MOCK'
#!/usr/bin/env bash
set -eu
destination=${!#}
if [[ ${MV_FAIL_DEST:-} == "$destination" && ( ${MV_ALWAYS_FAIL:-0} == 1 || ! -e "$MV_FAIL_MARKER" ) ]]; then
    : > "$MV_FAIL_MARKER"
    exit 43
fi
exec "$REAL_MV" "$@"
MOCK
chmod +x -- "$mock/cp" "$mock/mv"

printf 'old\n' > "$target/old-data"
if PATH="$mock:$PATH" run_sync "$source" "$target"; then fail 'copy failure returned success'; fi
[[ $(cat "$target/old-data") == old && ! -e "$target/partial" ]] || fail 'copy failure changed old data'
assert_clean "$parent"
printf '%s\n' 'PASS: partial copy failure preserves the old target'

rm -- "$mock/cp"
export MV_FAIL_DEST="$target" MV_FAIL_MARKER="$root/install-failed"
if PATH="$mock:$PATH" run_sync "$source" "$target"; then fail 'install failure returned success'; fi
[[ -e "$MV_FAIL_MARKER" && $(cat "$target/old-data") == old ]] || fail 'failed install did not restore old data'
assert_clean "$parent"
unset MV_FAIL_DEST MV_FAIL_MARKER
printf '%s\n' 'PASS: install failure rolls back the old target'

export MV_FAIL_DEST="$target" MV_FAIL_MARKER="$root/rollback-failed" MV_ALWAYS_FAIL=1
if PATH="$mock:$PATH" run_sync "$source" "$target"; then fail 'rollback failure returned success'; fi
backup=$(find "$parent" -maxdepth 1 -name '.sync-skills.*' -type d -print)
[[ -n "$backup" && $(cat "$backup/old/old-data") == old && ! -e "$target" ]] || fail 'rollback failure discarded the backup'
"$real_mv" -T -- "$backup/old" "$target"
rm -rf -- "$backup"
assert_clean "$parent"
unset MV_FAIL_DEST MV_FAIL_MARKER MV_ALWAYS_FAIL
printf '%s\n' 'PASS: rollback failure retains the old backup for recovery'

real_target="$parent/original linked content"
link_target="$parent/linked skills"
mkdir -- "$real_target"
printf 'keep\n' > "$real_target/keep"
ln -s -- "$real_target" "$link_target"
[[ -L "$link_target" ]] || fail 'test runtime did not create a real symbolic link'
run_sync "$source" "$link_target"
[[ ! -L "$link_target" && $(cat "$link_target/skill/SKILL.md") == new ]] || fail 'link target was not replaced'
[[ $(cat "$real_target/keep") == keep && ! -e "$real_target/skill" ]] || fail 'original linked content was modified'
assert_clean "$parent"
printf '%s\n' 'PASS: replacing a link preserves its original target'

dangling="$parent/dangling skills"
ln -s -- "$parent/nonexistent" "$dangling"
run_sync "$source" "$dangling"
[[ ! -L "$dangling" && $(cat "$dangling/skill/SKILL.md") == new ]] || fail 'dangling link was not replaced'
[[ ! -e "$parent/nonexistent" ]] || fail 'dangling target was created'
assert_clean "$parent"
printf '%s\n' 'PASS: dangling link replacement'

absent="$parent/new skills"
run_sync "$source" "$absent"
[[ $(cat "$absent/skill/SKILL.md") == new ]] || fail 'absent target was not installed'
assert_clean "$parent"
printf '%s\n' 'PASS: installing an absent target'
