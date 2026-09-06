#!/usr/bin/env bash
#
# Fails when one build graph produces two assemblies of the same name.
#
# Several submodules vendor a nested checkout of a component so they still build
# standalone: Broiler.Graphics is vendored by Broiler.HTML and by Broiler.UI,
# Broiler.Media and Broiler.Input are vendored beneath Broiler.Graphics, and
# Broiler.DOM beneath Broiler.CSS. A root build is supposed to collapse those to
# one checkout each, through the $(Broiler*Path) / $(Broiler*Root) properties the
# root Directory.Build.props sets. A submodule that adds a component reference
# without routing it through its property escapes the collapse, and then two
# projects with the same assembly name land in one output directory.
#
# Nothing about that is loud. Both projects compile, the copy to the output
# directory silently keeps one of them, and the failure arrives at runtime as a
# TypeLoadException a long way from its cause:
#
#   System.TypeLoadException: Method 'DecodeAsync' in type
#   'Broiler.Media.Image.Managed.PngImageCodec' from assembly
#   'Broiler.Media.Image.Managed, Version=0.1.0.0, Culture=neutral,
#   PublicKeyToken=null' does not have an implementation.
#
# That one shipped. Debug and Release had resolved the race differently, so it
# reproduced in one configuration and not the other.
#
# This asks the question the build does not: across the whole project closure of
# a solution -- submodules included, not just the projects the .slnx lists --
# does any output assembly name come from more than one project file?
#
# Two things it deliberately does NOT check.
#
# It does not compare submodule SHAs. Nested checkouts are expected to trail the
# root's pin, because the collapse means their sources are compiled against the
# root's copy and never their own; whether those sources still compile is a
# question the build answers directly and loudly. It is the duplicate that is
# silent, so the duplicate is what this looks for.
#
# It does not build. GetTargetPath evaluates a project and reports what it would
# produce, which is enough, and means this can run before the long jobs rather
# than after them.
#
# Usage: scripts/check-component-graph.sh [solution ...]
#        Defaults to every .slnx at the repository root.
#
# Evaluating a solution needs whatever workloads its projects require, so
# Broiler.Android.Browser.slnx needs the android workload and is checked from the
# CI job that already installs it rather than from the one that does not.
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
  echo "── $solution"

  # NuGet's restore graph is the cheapest complete view of the closure: one
  # evaluation for the whole solution, and it descends through every
  # ProjectReference into the submodules rather than stopping at what the .slnx
  # lists. It is a means to the project list here, nothing more.
  dg="$work/dg.json"
  if ! dotnet msbuild "$solution" -t:GenerateRestoreGraphFile \
        -p:RestoreGraphOutputPath="$dg" -nologo -v:q >"$work/dg.log" 2>&1; then
    annotate "$solution: could not evaluate the project graph."
    sed 's/^/    /' "$work/dg.log" >&2
    status=1
    continue
  fi

  probe="$work/probe.proj"
  python3 - "$dg" "$probe" >"$work/in_graph.txt" <<'PY' || { annotate "$solution: could not read the project graph."; status=1; continue; }
import json, sys, xml.sax.saxutils as x

dg, out = sys.argv[1], sys.argv[2]
graph = json.load(open(dg, encoding="utf-8"))
paths = sorted({(p.get("restore") or {}).get("projectPath") or key
                for key, p in graph.get("projects", {}).items()})

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
  # $(AssemblyName) applied -- which several projects here override, so the
  # project's own file name is not a stand-in for it.
  #
  # SkipNonexistentTargets is what lets a cross-targeting project through. One
  # declaring <TargetFrameworks> has no outer GetTargetPath, so it is skipped and
  # NOT covered by this check -- today that is the Unicode data set under
  # Broiler.JS, which is vendored twice and held together by $(BroilerUnicodeRoot)
  # instead. The count is reported rather than swallowed, so the gap stays
  # visible; covering those would mean probing each inner framework by hand.
  names="$work/names.txt"
  : >"$names"
  if ! dotnet msbuild "$probe" -t:Probe \
        -p:Configuration="${CONFIGURATION:-Debug}" -p:ProbeOutput="$names" \
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

  # Report every duplicate, not the first. A collapse that regressed usually
  # regressed for a whole component at once, and stopping at one name hides how
  # far it went.
  duplicated="$(cut -d'|' -f1 "$names" | sort | uniq -d)"
  if [ -z "$duplicated" ]; then
    printf '   ok  %d of %d projects probed, %d distinct assemblies\n' \
      "$checked" "$in_graph" "$(cut -d'|' -f1 "$names" | sort -u | grep -c .)"
    continue
  fi

  status=1
  duplicates_found=1
  while IFS= read -r assembly; do
    [ -n "$assembly" ] || continue
    annotate "$solution builds $assembly from more than one project. Route the duplicate through the component-root property the root Directory.Build.props sets, so the graph keeps one."
    awk -F'|' -v a="$assembly" '$1 == a {print $2}' "$names" | sort -u | sed 's|^|        |'
  done <<<"$duplicated"
done

echo
if [ "$status" -eq 0 ]; then
  echo "One assembly per name across ${#solutions[@]} solution(s), $checked_total projects."
elif [ "$duplicates_found" -eq 1 ]; then
  echo "Duplicate assemblies found. See the errors above."
else
  echo "A solution could not be evaluated, so nothing was proved about it. See the errors above."
fi
exit "$status"
