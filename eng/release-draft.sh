#!/usr/bin/env bash
# Creates the draft GitHub pre-release for one Broiler.Browser version.
#
#   eng/release-draft.sh <version> <artifacts-dir>
#
# <artifacts-dir> holds the Release workflow's artifacts, extracted as
# `gh run download` / actions/download-artifact leave them:
#
#   <artifacts-dir>/broiler-browser-win-x64-self-contained-<version>/Broiler.Browser.Windows.exe
#   <artifacts-dir>/broiler-browser-win-x64-framework-dependent-<version>/   (publish folder)
#   <artifacts-dir>/broiler-browser-linux-x64-self-contained-<version>/Broiler.Browser.Linux
#   <artifacts-dir>/broiler-browser-linux-x64-framework-dependent-<version>/ (publish folder)
#   <artifacts-dir>/broiler-browser-android-<version>/Broiler.Browser-<version>.aab
#                                                     Broiler.Browser-<version>-arm64.apk
#
# Each desktop artifact becomes one release asset,
# Broiler.Browser-<version>-<rid>-<variant>.zip. A self-contained one holds the executable
# alone; a framework-dependent one holds the whole publish folder, its files at the root of
# the zip. The executable is recorded as rwxr-xr-x and every other file as rw-r--r--: the
# workflow artifact drops the Linux binary's executable bit, the release zip restores it.
# The Android packages are archives already and signed, so they are attached as they are.
# They are optional, so a run from before Android was published can still be released;
# ANDROID_SIGNING_CERT_SHA256, when set, is quoted in the notes for checking a download.
#
# The tag browser-v<version> must already exist on the remote; the release is created
# for it as a draft, so nothing is public until someone publishes it. Needs the GitHub
# CLI with GH_TOKEN (or a login) that may write releases, and Python 3.
#
# Broiler.Writer's eng/release-draft.sh, with the names changed, a framework-dependent
# variant beside each self-contained one, and the notes saying what the executables are
# here: single-file and self-contained (or framework-dependent), not NativeAOT.
set -euo pipefail

version=${1:?usage: eng/release-draft.sh <version> <artifacts-dir>}
artifacts=${2:?usage: eng/release-draft.sh <version> <artifacts-dir>}
tag="browser-v$version"
out=$(mktemp -d)

# Python writes the zip so the Unix mode is recorded explicitly (rwxr-xr-x for the
# executable, rw-r--r-- for everything else) instead of read from the file system, which on
# Windows has no executable bit to read.
asset() {
  local rid=$1 variant=$2 executable=$3
  local dir="$artifacts/broiler-browser-$rid-$variant-$version"
  local zip="$out/Broiler.Browser-$version-$rid-$variant.zip"
  [ -f "$dir/$executable" ] || { echo "missing $dir/$executable" >&2; exit 1; }
  if [ "$variant" = self-contained ]; then
    local others
    others=$(find "$dir" -type f ! -name "$executable")
    [ -z "$others" ] || { echo "unexpected files beside $executable in $dir: $others" >&2; exit 1; }
  fi
  "$python" - "$dir" "$zip" "$executable" <<'PY'
import os, sys, zipfile, time
root, target, executable = sys.argv[1:4]
stamp = time.localtime()[:6]
with zipfile.ZipFile(target, 'w') as z:
    for folder, dirs, files in os.walk(root):
        dirs.sort()
        for name in sorted(files):
            path = os.path.join(folder, name)
            arcname = os.path.relpath(path, root).replace(os.sep, '/')
            info = zipfile.ZipInfo(arcname, date_time=stamp)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3              # Unix, so external_attr carries a mode
            mode = 0o755 if arcname == executable else 0o644
            info.external_attr = ((0o100000 | mode) << 16)
            with open(path, 'rb') as f:
                z.writestr(info, f.read())
PY
  echo "$zip"
}

python=$(command -v python3 || command -v python) || { echo "needs python3" >&2; exit 1; }

# One assignment per asset, so a failing asset() stops the script under set -e.
win_sc=$(asset win-x64 self-contained Broiler.Browser.Windows.exe)
win_fd=$(asset win-x64 framework-dependent Broiler.Browser.Windows.exe)
linux_sc=$(asset linux-x64 self-contained Broiler.Browser.Linux)
linux_fd=$(asset linux-x64 framework-dependent Broiler.Browser.Linux)
desktop_assets=("$win_sc" "$win_fd" "$linux_sc" "$linux_fd")

android_dir="$artifacts/broiler-browser-android-$version"
aab="$android_dir/Broiler.Browser-$version.aab"
apk="$android_dir/Broiler.Browser-$version-arm64.apk"
android_assets=()
android_rows=''
if [ -d "$android_dir" ]; then
  for f in "$aab" "$apk"; do [ -f "$f" ] || { echo "missing $f" >&2; exit 1; }; done
  android_assets=("$aab" "$apk")
  android_rows="| Android (arm64) | \`Broiler.Browser-$version-arm64.apk\` | install on the device (sideload) |
| Android | \`Broiler.Browser-$version.aab\` | app bundle for Google Play (arm64 + x86_64) |"
fi

cat > "$out/notes.md" <<EOF
Broiler Browser **$version**, a preview build for evaluation and testing, not for
production use. Broiler.JS is **not a security sandbox**: open controlled content only.

## Downloads

| Platform | File | Run |
| --- | --- | --- |
| Windows x64 | \`Broiler.Browser-$version-win-x64-self-contained.zip\` | unzip, start \`Broiler.Browser.Windows.exe\` |
| Windows x64 | \`Broiler.Browser-$version-win-x64-framework-dependent.zip\` | install the .NET 10 runtime, unzip, start \`Broiler.Browser.Windows.exe\` |
| Linux x64 | \`Broiler.Browser-$version-linux-x64-self-contained.zip\` | unzip, run \`./Broiler.Browser.Linux\` (X11) |
| Linux x64 | \`Broiler.Browser-$version-linux-x64-framework-dependent.zip\` | install the .NET 10 runtime, unzip, run \`./Broiler.Browser.Linux\` (X11) |
$android_rows

Each **self-contained** zip holds a single executable with the .NET runtime inside: nothing
to install, nothing else to copy. Each **framework-dependent** zip holds the application's
files without the runtime - much smaller, but it needs the .NET 10 runtime
(Microsoft.NETCore.App) installed, and the whole folder kept together. The executables are
not code-signed, so Windows SmartScreen may ask before the first start.
EOF

if [ ${#android_assets[@]} -gt 0 ]; then
  {
    echo
    echo 'The Android packages are signed with the Broiler release key.'
    if [ -n "${ANDROID_SIGNING_CERT_SHA256:-}" ]; then
      echo "Signing certificate SHA-256: \`$ANDROID_SIGNING_CERT_SHA256\`"
    fi
  } >> "$out/notes.md"
fi

gh release create "$tag" "${desktop_assets[@]}" "${android_assets[@]}" \
  --verify-tag \
  --draft \
  --prerelease \
  --title "Broiler Browser $version" \
  --notes-file "$out/notes.md" \
  --generate-notes
