[CmdletBinding()]
param(
    [switch] $Verify
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $repositoryRoot 'eng/solutions.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

# Every component this repository composes -- Broiler.HTML, Broiler.HtmlBridge, Broiler.JS,
# Broiler.VM, Broiler.Graphics, Broiler.Input, Broiler.UI and the rest -- is a PackageReference
# now, not a checkout. A package is not a project a solution can list, so the closure this
# walks is the ProjectReference graph under src/ and nothing else, and the tables that used to
# fold nested submodule checkouts onto their top-level copy went with the submodules.
#
# A ProjectReference that names a path outside the repository, or one that does not exist, is
# still an error below rather than something to skip: a component that came back as a checkout
# would have to come back through this script deliberately.

$referenceCache = @{}

function Convert-ToRepositoryPath {
    param(
        [Parameter(Mandatory)]
        [string] $FullPath
    )

    $normalizedFullPath = [IO.Path]::GetFullPath($FullPath)
    $rootPrefix = $repositoryRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $normalizedFullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Project path escapes the repository: $normalizedFullPath"
    }
    $relativePath = $normalizedFullPath.Substring($rootPrefix.Length).Replace('\', '/')

    $canonicalFullPath = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $canonicalFullPath -PathType Leaf)) {
        throw "Project does not exist: $relativePath"
    }

    return $relativePath
}

function Resolve-ProjectReference {
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath,

        [Parameter(Mandatory)]
        [string] $Include
    )

    $projectFullPath = Join-Path $repositoryRoot $ProjectPath
    $projectDirectory = Split-Path -Parent $projectFullPath

    $resolvedInclude = $Include
    $resolvedInclude = $resolvedInclude.Replace(
        '$(MSBuildThisFileDirectory)',
        $projectDirectory + [IO.Path]::DirectorySeparatorChar)

    # $(MSBuildThisFileDirectory) is the only property a ProjectReference here may use. The
    # $(Broiler*Root) / $(Broiler*Path) properties that pointed a submodule's references at
    # the top-level checkout left with the submodules, and this script stopped resolving them
    # when they did: a reference spelled through one is rejected here rather than guessed at.
    if ($resolvedInclude.Contains('$(')) {
        throw "Unsupported property in ProjectReference '$Include' from '$ProjectPath'."
    }

    # MSBuild writes ProjectReference includes with backslashes whatever the host. On a
    # non-Windows host a backslash is an ordinary filename character, so GetFullPath below
    # would fold '..\Broiler.Browser.Core\...' into one nonsensical component instead of
    # walking up a directory. Fold them onto the platform separator first; on Windows this
    # is a no-op. The absolute path substituted above is already host-native.
    $resolvedInclude = $resolvedInclude.Replace('\', [IO.Path]::DirectorySeparatorChar)

    if (-not [IO.Path]::IsPathRooted($resolvedInclude)) {
        $resolvedInclude = Join-Path $projectDirectory $resolvedInclude
    }

    return Convert-ToRepositoryPath -FullPath $resolvedInclude
}

function Get-ProjectReferences {
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath
    )

    if ($referenceCache.ContainsKey($ProjectPath)) {
        return @($referenceCache[$ProjectPath])
    }

    $projectFullPath = Join-Path $repositoryRoot $ProjectPath
    [xml] $project = Get-Content -Raw -LiteralPath $projectFullPath
    $references = @(
        $project.SelectNodes('//ProjectReference[@Include]') |
            ForEach-Object {
                foreach ($include in ([string] $_.Include).Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
                    Resolve-ProjectReference -ProjectPath $ProjectPath -Include $include
                }
            } |
            Sort-Object -Unique
    )

    $referenceCache[$ProjectPath] = $references
    return @($references)
}

function Get-ProjectClosure {
    param(
        [Parameter(Mandatory)]
        [string[]] $Roots
    )

    $pending = [Collections.Generic.Queue[string]]::new()
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($root in $Roots) {
        $pending.Enqueue((Convert-ToRepositoryPath -FullPath (Join-Path $repositoryRoot $root)))
    }

    while ($pending.Count -gt 0) {
        $projectPath = $pending.Dequeue()
        if (-not $visited.Add($projectPath)) {
            continue
        }

        foreach ($reference in Get-ProjectReferences -ProjectPath $projectPath) {
            $pending.Enqueue($reference)
        }
    }

    return @($visited | Sort-Object)
}

function Convert-ToXmlAttribute {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    return [Security.SecurityElement]::Escape($Value)
}

function New-SolutionText {
    param(
        [Parameter(Mandatory)]
        [pscustomobject] $Definition,

        [Parameter(Mandatory)]
        [string[]] $Projects
    )

    $rootSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $Definition.roots) {
        [void] $rootSet.Add((Convert-ToRepositoryPath -FullPath (Join-Path $repositoryRoot $root)))
    }

    # Solution-level deploy flags. Visual Studio's F5 and `msbuild /t:Deploy` on
    # the solution use these to install an application head; `dotnet build` does
    # not, which is why nothing in CI depends on them. They live in the manifest
    # rather than in the .slnx because a hand-edit to a generated file is silently
    # reverted by the next generator run.
    $deployByProject = @{}
    foreach ($entry in @($Definition.deploy)) {
        if ($null -eq $entry) {
            continue
        }

        $deployProject = Convert-ToRepositoryPath -FullPath (Join-Path $repositoryRoot $entry.project)
        if ($deployProject -notin $Projects) {
            throw (
                "$($Definition.path) declares a deploy entry for '$($entry.project)', " +
                'which is not in its project closure.')
        }

        $solutionExpression = [string] $entry.solution
        if ([string]::IsNullOrWhiteSpace($solutionExpression)) {
            throw "$($Definition.path) declares a deploy entry for '$($entry.project)' with no solution expression."
        }

        $deployByProject[$deployProject] = $solutionExpression
    }

    # Whether the projects are grouped into an "Entry points" folder and a "Dependencies/<dir>"
    # folder per top-level directory, or listed at the root. The manifest decides, for the deploy
    # flags' reason: a hand-edit to the .slnx is reverted by the next generator run, and fails
    # -Verify until then. Absent means folders, which every solution had before the option.
    if ($null -ne $Definition.folders -and $Definition.folders -isnot [bool]) {
        throw "$($Definition.path) declares 'folders' as '$($Definition.folders)'; it must be true or false."
    }
    $useFolders = $null -eq $Definition.folders -or $Definition.folders

    # The build types the .slnx offers. A configuration a solution does not declare cannot be
    # built THROUGH it at all -- MSBuild answers MSB4126 and stops before evaluating a project --
    # which is why the Debug-VM/Release-VM pair has to be listed here and not only on the head
    # projects. They come from the manifest rather than being hardcoded so that a solution which
    # has no business offering a variant does not offer it.
    $buildTypes = @($Definition.configurations | Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_) })
    if ($buildTypes.Count -eq 0) {
        $buildTypes = @('Debug', 'Release')
    }

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('<Solution>')
    $lines.Add('  <!-- Generated from eng/solutions.json by scripts/update-solutions.ps1. -->')
    $lines.Add('  <Configurations>')
    foreach ($buildType in $buildTypes) {
        $buildTypeName = Convert-ToXmlAttribute -Value ([string] $buildType)
        $lines.Add("    <BuildType Name=`"$buildTypeName`" />")
    }
    $lines.Add('  </Configurations>')

    # One <Project> per path, in path order, with its deploy flag when the manifest gives one.
    function Add-ProjectLines {
        param(
            [Parameter(Mandatory)]
            [string] $Indent,

            [Parameter(Mandatory)]
            [string[]] $ProjectPaths
        )

        foreach ($project in $ProjectPaths | Sort-Object) {
            $projectPath = Convert-ToXmlAttribute -Value $project
            if ($deployByProject.ContainsKey($project)) {
                $deploySolution = Convert-ToXmlAttribute -Value $deployByProject[$project]
                $lines.Add("$Indent<Project Path=`"$projectPath`">")
                $lines.Add("$Indent  <Deploy Solution=`"$deploySolution`" />")
                $lines.Add("$Indent</Project>")
            }
            else {
                $lines.Add("$Indent<Project Path=`"$projectPath`" />")
            }
        }
    }

    if (-not $useFolders) {
        Add-ProjectLines -Indent '  ' -ProjectPaths $Projects
    }
    else {
        $groups = [ordered]@{}
        $groups['Entry points'] = @($Projects | Where-Object { $rootSet.Contains($_) })
        foreach ($project in $Projects | Where-Object { -not $rootSet.Contains($_) }) {
            $topLevelDirectory = $project.Split('/')[0]
            $groupName = "Dependencies/$topLevelDirectory"
            if (-not $groups.Contains($groupName)) {
                $groups[$groupName] = @()
            }
            $groups[$groupName] += $project
        }

        foreach ($group in $groups.GetEnumerator()) {
            if ($group.Value.Count -eq 0) {
                continue
            }

            $folderName = Convert-ToXmlAttribute -Value "/$($group.Key)/"
            $lines.Add("  <Folder Name=`"$folderName`">")
            Add-ProjectLines -Indent '    ' -ProjectPaths $group.Value
            $lines.Add('  </Folder>')
        }
    }

    $lines.Add('</Solution>')
    return ($lines -join "`n") + "`n"
}

$manifestSolutionPaths = @($manifest.solutions | ForEach-Object { [string] $_.path })
$duplicateManifestPaths = @(
    $manifestSolutionPaths |
        Group-Object |
        Where-Object Count -gt 1 |
        ForEach-Object Name
)
if ($duplicateManifestPaths.Count -gt 0) {
    throw "Duplicate solution paths in eng/solutions.json:`n  $($duplicateManifestPaths -join "`n  ")"
}

$testRootOwners = @{}
foreach ($definition in $manifest.solutions | Where-Object path -Like '*.Tests.slnx') {
    foreach ($root in $definition.roots) {
        $canonicalRoot = Convert-ToRepositoryPath -FullPath (Join-Path $repositoryRoot $root)
        if ($testRootOwners.ContainsKey($canonicalRoot)) {
            throw (
                "Test root '$canonicalRoot' belongs to both " +
                "'$($testRootOwners[$canonicalRoot])' and '$($definition.path)'.")
        }
        $testRootOwners[$canonicalRoot] = $definition.path
    }
}

$topLevelSolutions = @(
    Get-ChildItem -LiteralPath $repositoryRoot -Filter '*.slnx' -File |
        ForEach-Object { $_.Name } |
        Sort-Object
)
$unexpectedSolutions = @(
    $topLevelSolutions |
        Where-Object { $_ -notin $manifestSolutionPaths }
)
if ($unexpectedSolutions.Count -gt 0) {
    throw "Top-level solution files are not declared in eng/solutions.json:`n  $($unexpectedSolutions -join "`n  ")"
}

$errors = [Collections.Generic.List[string]]::new()
$utf8WithoutBom = [Text.UTF8Encoding]::new($false)

foreach ($definition in $manifest.solutions) {
    $solutionPath = Join-Path $repositoryRoot $definition.path
    $projects = Get-ProjectClosure -Roots @($definition.roots)

    if ($definition.path -notlike '*.Tests.slnx' -and
        $definition.path -ne 'Broiler.Benchmarks.slnx') {
        $qualityProjects = @(
            $projects |
                Where-Object { $_ -match '(?i)\.(Tests|Benchmarks)\.csproj$' }
        )
        if ($qualityProjects.Count -gt 0) {
            $errors.Add(
                "$($definition.path) includes test or benchmark projects: " +
                ($qualityProjects -join ', '))
        }
    }

    foreach ($pattern in @(
        $definition.forbiddenProjectPatterns |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_) }
    )) {
        $violations = @($projects | Where-Object { $_ -match $pattern })
        if ($violations.Count -gt 0) {
            $errors.Add(
                "$($definition.path) crosses a declared platform boundary for '$pattern': " +
                ($violations -join ', '))
        }
    }

    $expectedText = New-SolutionText -Definition $definition -Projects $projects
    if ($Verify) {
        if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
            $errors.Add("$($definition.path) is missing. Run scripts/update-solutions.ps1.")
            continue
        }

        $actualText = [IO.File]::ReadAllText($solutionPath).Replace("`r`n", "`n")
        if ($actualText -ne $expectedText) {
            $errors.Add("$($definition.path) is stale. Run scripts/update-solutions.ps1.")
        }
    }
    else {
        [IO.File]::WriteAllText($solutionPath, $expectedText, $utf8WithoutBom)
        Write-Host ("Updated {0} ({1} roots, {2} projects)." -f
            $definition.path,
            @($definition.roots).Count,
            $projects.Count)
    }
}

if ($errors.Count -gt 0) {
    throw "Solution verification failed:`n  $($errors -join "`n  ")"
}

if ($Verify) {
    Write-Host "Verified $($manifest.solutions.Count) focused solutions against eng/solutions.json."
}
