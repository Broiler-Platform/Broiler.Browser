#!/usr/bin/env bash
#
# Fails when a solution offers a build type that one of its projects does not declare.
#
# A solution's <Configurations> lists the build types you can pick. Visual Studio
# then resolves the chosen one onto a project configuration OF THE SAME NAME and
# checks that the project has one; it does not fall back. So a solution offering
# Debug-VM to a project whose $(Configurations) is still the SDK default
# `Debug;Release` is an unbuildable combination, and VS says so on open:
#
#   Invalid project mappings. See log file
#
# with a temp file naming every project it could not map. That is what happened
# here: 87 of the 88 projects in Broiler.Windows.Browser.slnx, all at once.
#
# THE COMMAND LINE CANNOT REPRODUCE IT. `dotnet build <solution> -c Debug-VM`
# hands a project a configuration it never mentioned and builds it happily, which
# is why the whole -VM configuration pair shipped, passed CI, and was still broken
# for anyone who opened the solution in an IDE. Nothing but a check like this one
# closes that gap, because the thing that notices is not on this machine.
#
# eng/Broiler.ConfigurationNames.targets is what makes the declaration true for
# every project without editing eighty-seven project files, most of which live in
# other repositories. This asserts the outcome rather than trusting the mechanism:
# a project that spells out its own <Configurations> in its csproj body overwrites
# what a parent set, and that overwrite is silent.
#
# Usage: scripts/check-configuration-names.sh [solution ...]
#        Defaults to every .slnx at the repository root.
#
# Evaluating a project needs whatever workloads it requires, so
# Broiler.Android.Browser.slnx is checked from the CI job that installs the
# android workload rather than from the one that does not.
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

annotate() {
  if [ -n "${GITHUB_ACTIONS:-}" ]; then
    echo "::error::$1"
  else
    echo "ERROR: $1" >&2
  fi
}

# $(Configurations) is a property, and no built-in target reports one. This target
# does, and the two CustomAfter* extension points below inject it into every
# project the probe evaluates -- so the whole solution costs ONE dotnet invocation
# instead of one per project. It is injected for the probe only and is not part of
# any build.
#
# It RETURNS the answer rather than writing it. The projects are probed in
# parallel, and having each append to one shared file is a race that fails as
# "the process cannot access the file"; the parent collects TargetOutputs and
# writes once instead.
cat >"$work/report.targets" <<'TARGETS'
<Project>
  <Target Name="BroilerReportConfigurations" Returns="@(BroilerConfigurationReport)">
    <ItemGroup>
      <BroilerConfigurationReport Include="$(MSBuildProjectFullPath)">
        <Declared>$(Configurations)</Declared>
      </BroilerConfigurationReport>
    </ItemGroup>
  </Target>
</Project>
TARGETS

status=0
checked_total=0

for solution in "${solutions[@]}"; do
  echo "── $solution"

  # The projects the SOLUTION lists, not the whole reference closure. VS validates
  # a mapping for each project the solution loads; a ProjectReference reached from
  # one is given its configuration as a global property and never consulted about
  # what it declares, so the closure is a different question from this one.
  report="$work/report.txt"
  probe="$work/probe.proj"
  : >"$report"

  python3 - "$solution" "$probe" >"$work/counts.txt" <<'PY' || { annotate "$solution: could not read the solution."; status=1; continue; }
import os, re, sys, xml.etree.ElementTree as ET, xml.sax.saxutils as x

solution, out = sys.argv[1], sys.argv[2]
tree = ET.parse(solution)
root = tree.getroot()

build_types = sorted({b.get("Name") for b in root.iter("BuildType")
                      if b.get("Name") and b.get("Project") is None})

base = os.path.dirname(os.path.abspath(solution))
projects = sorted({os.path.normpath(os.path.join(base, p.get("Path").replace("\\", "/")))
                   for p in root.iter("Project") if p.get("Path")})

items = "\n".join('    <ProjectToProbe Include=%s />' % x.quoteattr(p) for p in projects)
open(out, "w", encoding="utf-8").write(f"""<Project>
  <ItemGroup>
{items}
  </ItemGroup>
  <Target Name="Probe">
    <MSBuild Projects="@(ProjectToProbe)"
             Targets="BroilerReportConfigurations"
             BuildInParallel="true"
             SkipNonexistentProjects="false">
      <Output TaskParameter="TargetOutputs" ItemName="Probed" />
    </MSBuild>
    <WriteLinesToFile File="$(BroilerConfigurationReport)"
                      Lines="@(Probed->'%(Identity)|%(Declared)')"
                      Overwrite="true" />
  </Target>
</Project>
""")
print(len(projects))
print(";".join(build_types))
PY

  listed="$(sed -n 1p "$work/counts.txt")"
  wanted="$(sed -n 2p "$work/counts.txt")"

  if [ -z "$wanted" ]; then
    annotate "$solution: declares no build types, so this proves nothing about it."
    status=1
    continue
  fi

  # Both extension points, because a project declaring <TargetFrameworks> does not
  # import Microsoft.Common.targets in its outer build and would answer MSB4057 --
  # which the count check below turns into a failure rather than a silent gap, but
  # covering them is better than reporting them.
  if ! dotnet msbuild "$probe" -t:Probe \
        -p:BroilerConfigurationReport="$report" \
        -p:CustomAfterMicrosoftCommonTargets="$work/report.targets" \
        -p:CustomAfterMicrosoftCommonCrossTargetingTargets="$work/report.targets" \
        -nologo -v:q >"$work/probe.log" 2>&1; then
    annotate "$solution: could not evaluate its projects."
    sed 's/^/    /' "$work/probe.log" >&2
    status=1
    continue
  fi

  # WriteLinesToFile uses the platform line ending, so a run on Windows leaves a
  # carriage return on the last field. Without this the check reports every project
  # as missing the LAST build type the solution offers and nothing else -- which
  # looks exactly like a real finding.
  tr -d '\r' <"$report" >"$report.lf" && mv "$report.lf" "$report"

  checked="$(grep -c . "$report" 2>/dev/null || echo 0)"
  if [ "$checked" -ne "$listed" ]; then
    annotate "$solution lists $listed projects but only $checked reported. This check cannot pass on a partial answer."
    status=1
    continue
  fi
  checked_total=$((checked_total + checked))

  # Report every project that is missing every name it is missing. This failure
  # arrives for a whole solution at once -- a mapping that regressed took 87
  # projects with it -- and stopping at the first would misrepresent the size of it.
  missing=0
  while IFS='|' read -r project declared; do
    [ -n "$project" ] || continue
    absent=""
    IFS=';' read -ra want <<<"$wanted"
    for name in "${want[@]}"; do
      case ";$declared;" in
        *";$name;"*) ;;
        *) absent="$absent $name" ;;
      esac
    done
    if [ -n "$absent" ]; then
      missing=$((missing + 1))
      status=1
      absent="${absent# }"
      annotate "${project#$root/} does not declare ${absent// /, }, which $solution offers. Visual Studio refuses to map a solution build type onto a project configuration that does not exist; the command line accepts it silently."
    fi
  done <"$report"

  if [ "$missing" -eq 0 ]; then
    printf '   ok  %d projects, each declaring %s\n' "$checked" "${wanted//;/, }"
  fi
done

echo
if [ "$status" -eq 0 ]; then
  echo "Every project declares every build type its solution offers, across ${#solutions[@]} solution(s), $checked_total projects."
else
  echo "A solution offers a build type a project does not declare. See the errors above."
fi
exit "$status"
