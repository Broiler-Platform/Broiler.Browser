#!/usr/bin/env bash
#
# Fails when one build graph produces two assemblies of the same name.
#
# Two sources that ship an assembly of the same name into one output directory
# is the failure mode this repository kept hitting. Nothing about it is loud.
# Both compile, the copy to the output directory silently keeps one of them, and
# the failure arrives at runtime as a TypeLoadException a long way from its
# cause:
#
#   System.TypeLoadException: Method 'DecodeAsync' in type
#   'Broiler.Media.Image.Managed.PngImageCodec' from assembly
#   'Broiler.Media.Image.Managed, Version=0.1.0.0, Culture=neutral,
#   PublicKeyToken=null' does not have an implementation.
#
# That one shipped. Debug and Release had resolved the race differently, so it
# reproduced in one configuration and not the other.
#
# It used to arrive through nested submodule checkouts compiled twice. Every
# component is a NuGet package now, so the route that is left is a package: two
# packages that both carry Broiler.X.dll in their lib/ folder -- one depending on
# the component, one bundling a copy of it -- or a project here producing an
# assembly a package already brings. The SDK's conflict resolution
# (ResolvePackageFileConflicts) then keeps one copy by version and says so only
# at low verbosity, which is the same silence by another name.
#
# So this asks, for every project in a solution's closure and every target
# framework (and runtime identifier) it restores: across the assemblies its own
# projects produce and the runtime assets of every package NuGet resolved for it,
# does any file name come from more than one source?
#
# What it deliberately does NOT do.
#
# It does not build. It restores -- the package closure does not exist until
# NuGet has resolved it -- and reads project.assets.json, and it evaluates
# GetTargetPath for the projects, which reports what each would produce with
# $(AssemblyName) applied. That is enough, and means this runs before the long
# jobs rather than after them.
#
# It does not treat one package at two versions as a duplicate. Two projects in
# one solution can resolve different versions of the same package, and each
# project's closure still holds one copy; the question is asked per project and
# per target, where the SDK resolves conflicts, and one package id is one source.
#
# And it does not treat two packages carrying BYTE-IDENTICAL copies of a file as
# a duplicate: whichever copy wins, the same assembly loads, so there is no race
# to lose. Microsoft.TestPlatform.ObjectModel and .TestHost ship three DLLs that
# way, which is what the test solution trips without this. The copies are hashed
# from the package folder rather than assumed, and a project output is never
# excused, because it does not exist yet to be compared.
#
# Usage: scripts/check-component-graph.sh [solution ...]
#        Defaults to every .slnx at the repository root.
#        $CONFIGURATION picks the configuration (default Debug). It matters: the
#        -VM pair adds the Broiler.HtmlBridge.Scripting.Vm package and with it the
#        Broiler.VM packages, so Debug-VM restores a different package closure.
#
# Evaluating a solution needs whatever workloads its projects require, so
# Broiler.Android.Browser.slnx needs the android workload and is checked from the
# CI job that already installs it rather than from the one that does not.
#
# Restoring writes each project's obj/project.assets.json for the configuration
# asked about. A later build restores again for its own configuration, so this
# does not leak into one -- but two runs of this script in one checkout at the
# same time would race on those files.
#
# Requires: dotnet, python3.

set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root" || exit 1

if [ "$#" -gt 0 ]; then
  solutions=("$@")
else
  mapfile -t solutions < <(find . -maxdepth 1 -name '*.slnx' -printf '%f\n' | sort)
fi

if [ "${#solutions[@]}" -eq 0 ]; then
  echo "::error::No solutions found to check. This cannot pass vacuously."
  exit 1
fi

configuration="${CONFIGURATION:-Debug}"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# GitHub renders ::error annotations; a local run should not be shouted at in
# workflow-command syntax it cannot use.
annotate() {
  if [ -n "${GITHUB_ACTIONS:-}" ]; then
    echo "::error::$1"
  else
    echo "ERROR: $1" >&2
  fi
}

status=0
checked_total=0
duplicates_found=0

for solution in "${solutions[@]}"; do
  echo "── $solution ($configuration)"

  # NuGet's restore graph is the cheapest complete view of the project closure:
  # one evaluation for the whole solution, and it descends through every
  # ProjectReference rather than stopping at what the .slnx lists. It also names
  # each project's obj directory, which is where the restore below writes the
  # package closure this reads.
  dg="$work/dg.json"
  if ! dotnet msbuild "$solution" -t:GenerateRestoreGraphFile \
        -p:Configuration="$configuration" \
        -p:RestoreGraphOutputPath="$dg" -nologo -v:q >"$work/dg.log" 2>&1; then
    annotate "$solution: could not evaluate the project graph."
    sed 's/^/    /' "$work/dg.log" >&2
    status=1
    continue
  fi

  # The package closure. The configuration is passed for the same reason it is
  # passed to every other call here: the -VM pair changes which packages a
  # project references, so a restore under the default would answer about a
  # different graph than the one the rest of this run is asking about.
  if ! dotnet restore "$solution" -p:Configuration="$configuration" \
        -nologo -v:q >"$work/restore.log" 2>&1; then
    annotate "$solution: could not restore, so its package closure is unknown."
    sed 's/^/    /' "$work/restore.log" >&2
    status=1
    continue
  fi

  probe="$work/probe.proj"
  python3 - "$dg" "$probe" "$work/projects.txt" >"$work/in_graph.txt" <<'PY' || { annotate "$solution: could not read the project graph."; status=1; continue; }
import json, sys, xml.sax.saxutils as x

dg, out, listing = sys.argv[1], sys.argv[2], sys.argv[3]
graph = json.load(open(dg, encoding="utf-8"))
projects = {}
for key, p in graph.get("projects", {}).items():
    restore = p.get("restore") or {}
    projects[restore.get("projectPath") or key] = restore.get("outputPath") or ""
paths = sorted(projects)

with open(listing, "w", encoding="utf-8") as f:
    for path in paths:
        f.write("%s|%s\n" % (path, projects[path]))

items = "\n".join('    <ProjectToProbe Include=%s />' % x.quoteattr(p) for p in paths)
open(out, "w", encoding="utf-8").write(f"""<Project>
  <ItemGroup>
{items}
  </ItemGroup>
  <Target Name="Probe">
    <MSBuild Projects="@(ProjectToProbe)"
             Targets="GetTargetPath"
             BuildInParallel="true"
             SkipNonexistentProjects="false"
             SkipNonexistentTargets="true"
             Properties="Configuration=$(Configuration)">
      <Output TaskParameter="TargetOutputs" ItemName="Probed" />
    </MSBuild>
    <WriteLinesToFile File="$(ProbeOutput)"
                      Lines="@(Probed->'%(Filename)%(Extension)|%(MSBuildSourceProjectFile)')"
                      Overwrite="true" />
  </Target>
</Project>
""")
print(len(paths))
PY

  in_graph="$(cat "$work/in_graph.txt")"

  # GetTargetPath reports the assembly a project would produce, with
  # $(AssemblyName) applied, so the project's own file name is not a stand-in
  # for it.
  #
  # SkipNonexistentTargets is what lets a cross-targeting project through. One
  # declaring <TargetFrameworks> has no outer GetTargetPath, so it is skipped and
  # its own output is NOT covered by this check. None of the projects here
  # cross-targets today; the count is reported rather than swallowed, so a gap
  # stays visible if one ever does.
  names="$work/names.txt"
  : >"$names"
  if ! dotnet msbuild "$probe" -t:Probe \
        -p:Configuration="$configuration" -p:ProbeOutput="$names" \
        -nologo -v:q >"$work/probe.log" 2>&1; then
    annotate "$solution: could not resolve the output assembly names."
    sed 's/^/    /' "$work/probe.log" >&2
    status=1
    continue
  fi

  checked="$(grep -c . "$names" 2>/dev/null || echo 0)"
  if [ "$checked" -eq 0 ]; then
    annotate "$solution: no projects were probed. This check cannot pass vacuously."
    status=1
    continue
  fi
  checked_total=$((checked_total + checked))

  # Per project, per restore target: every assembly file name and the set of
  # sources it comes from. A project source is the project file; a package
  # source is the package id, deliberately without its version.
  #
  # Only 'runtime' assets are read. They are what is copied next to the
  # application and so what the race is about; 'compile' assets are reference
  # assemblies that never reach the output, and 'runtimeTargets' are RID-specific
  # copies that a RID-specific target already resolves into 'runtime'.
  python3 - "$work/projects.txt" "$names" >"$work/result.txt" 2>"$work/result.err" <<'PY'
import hashlib, json, os, sys
from collections import defaultdict

listing, names = sys.argv[1], sys.argv[2]

def key(path):
    return os.path.normcase(os.path.normpath(path))

produced = {}
for line in open(names, encoding="utf-8"):
    line = line.strip()
    if line:
        assembly, project = line.split("|", 1)
        produced[key(project)] = assembly

hashes = {}
def digest(folders, library_path, asset):
    for folder in folders:
        candidate = os.path.join(folder, library_path, asset)
        if os.path.isfile(candidate):
            if candidate not in hashes:
                with open(candidate, "rb") as f:
                    hashes[candidate] = hashlib.sha256(f.read()).hexdigest()
            return hashes[candidate]
    return None

findings = {}
identical = set()
packages = set()
assemblies = set()
problems = []

for line in open(listing, encoding="utf-8"):
    line = line.rstrip("\n")
    if not line:
        continue
    project, output = line.split("|", 1)
    assets_path = os.path.join(output, "project.assets.json")
    if not output or not os.path.isfile(assets_path):
        problems.append("%s has no project.assets.json at %s after the restore" % (project, output or "<no outputPath>"))
        continue
    assets = json.load(open(assets_path, encoding="utf-8"))
    libraries = assets.get("libraries", {})
    folders = list(assets.get("packageFolders", {}))
    base = os.path.dirname(project)

    for target, libs in assets.get("targets", {}).items():
        # name -> {source: sha256 of the file, or None for a project, which is not built yet}
        sources = defaultdict(dict)
        own = produced.get(key(project))
        if own:
            sources[own.lower()]["project " + project] = None
        for lib, info in libs.items():
            library = libraries.get(lib) or {}
            if info.get("type") == "project":
                ref = library.get("path") or ""
                ref_full = os.path.normpath(os.path.join(base, ref.replace("\\", "/")))
                assembly = produced.get(key(ref_full))
                if assembly:
                    sources[assembly.lower()]["project " + ref_full] = None
                continue
            package_id = lib.split("/", 1)[0]
            packages.add(package_id.lower())
            for asset in (info.get("runtime") or {}):
                name = os.path.basename(asset)
                if name == "_._":
                    continue
                sources[name.lower()]["package " + package_id] = digest(folders, library.get("path") or "", asset)
        for name, where in sources.items():
            assemblies.add(name)
            if len(where) < 2:
                continue
            # Byte-identical copies from two packages are not the race: whichever one the
            # copy keeps, the application loads the same assembly. Microsoft's test
            # platform ships three of its DLLs in two packages exactly like that. A
            # project is never excused, because its output does not exist to compare yet,
            # and neither is a file this could not find to hash.
            digests = set(where.values())
            if len(digests) == 1 and None not in digests:
                identical.add(name)
                continue
            findings.setdefault(name, set()).add((target, tuple(sorted(where))))

for p in problems:
    print("PROBLEM|" + p)
for name in sorted(findings):
    for target, where in sorted(findings[name]):
        print("DUPLICATE|%s|%s|%s" % (name, target, ";".join(where)))
print("SUMMARY|%d|%d|%d" % (len(packages), len(assemblies), len(identical)))
PY
  if [ "$?" -ne 0 ]; then
    annotate "$solution: could not read the restored package closure."
    sed 's/^/    /' "$work/result.err" >&2
    status=1
    continue
  fi

  if grep -q '^PROBLEM|' "$work/result.txt"; then
    while IFS='|' read -r _ message; do
      annotate "$solution: $message. This check cannot pass on a partial answer."
    done < <(grep '^PROBLEM|' "$work/result.txt")
    status=1
    continue
  fi

  IFS='|' read -r _ package_count assembly_count identical_count < <(grep '^SUMMARY|' "$work/result.txt")

  # Report every duplicate, not the first. A regression usually arrives for a
  # whole component at once, and stopping at one name hides how far it went.
  if ! grep -q '^DUPLICATE|' "$work/result.txt"; then
    printf '   ok  %d of %d projects probed, %d packages, %d distinct assemblies (%d shipped byte-identically by more than one package)\n' \
      "$checked" "$in_graph" "$package_count" "$assembly_count" "$identical_count"
    continue
  fi

  status=1
  duplicates_found=1
  while IFS='|' read -r _ assembly target where; do
    annotate "$solution ships $assembly from more than one source, and the copies are not provably identical (restore target $target). Depend on the package that owns it rather than carrying a copy, so the graph keeps one."
    printf '%s\n' "$where" | tr ';' '\n' | sed 's|^|        |'
  done < <(grep '^DUPLICATE|' "$work/result.txt")
done

echo
if [ "$status" -eq 0 ]; then
  echo "One assembly per name across ${#solutions[@]} solution(s), $checked_total projects and their packages ($configuration)."
elif [ "$duplicates_found" -eq 1 ]; then
  echo "Duplicate assemblies found. See the errors above."
else
  echo "A solution could not be evaluated, so nothing was proved about it. See the errors above."
fi
exit "$status"
