# The `Debug-VM` and `Release-VM` configurations

These two configurations build the browser against the **Broiler.VM JavaScript profile** instead
of Broiler.JS. Everything else — the DOM, layout, graphics, the shell — is unchanged.

```bash
dotnet build Broiler.Windows.Browser.slnx -c Debug-VM
```

```bash
dotnet run --project src/Broiler.Browser.Windows/Broiler.Browser.Windows.csproj -c Release-VM
```

A configuration is not the only door. `-p:BroilerJavaScriptEngine=Vm` makes any configuration
build the VM engine, which is what a bisect or a one-off comparison wants:

```bash
dotnet build Broiler.Linux.Browser.slnx -c Release -p:BroilerJavaScriptEngine=Vm
```

## What actually changes, and what does not

**One construction site.** `BrowserApp.NewScriptEngine()` returns
`new VmScriptEngine(new ScriptEngine())` under these configurations and `new ScriptEngine()`
under every other. That is the whole of the run-time difference.

**`VmScriptEngine` runs the script-only paths on the VM and delegates the document-bearing ones.**

| `IScriptEngine` member | Under `-VM` |
|---|---|
| `Execute(scripts)` | Broiler.VM: compile → verify → instantiate → invoke → drain |
| `ExecuteDetailed(scripts)` | Broiler.VM, with per-script errors |
| `Execute(scripts, html[, url][, deferred][, moduleRoots])` | Broiler.JS |
| `ExecuteInteractive(...)` | Broiler.JS |
| `MicroTasks` | Broiler.JS's queue (there is only one) |

**Broiler.JS is still in the graph, and cannot not be.** This is the part most likely to be
misread, so it is stated plainly: `Debug-VM` *adds* the Broiler.VM JavaScript profile and
*selects* it for script execution. It removes nothing.

### No page load runs on the VM yet, and that is worth saying first

`RenderingPipeline` — the only type in the repository written against `IScriptEngine` — calls
**exactly one** engine method, `ExecuteInteractive`
([RenderingPipeline.cs:63](../src/Broiler.App/Rendering/RenderingPipeline.cs)), and everything
after it goes through the concrete `InteractiveSession`. `ExecuteInteractive` is a delegated
member.

So loading a page in a `-VM` build runs **no JavaScript on the VM**. What reaches the VM today is
`Execute(scripts)` and `ExecuteDetailed(scripts)` — the document-free entry points — whose only
callers in this checkout are tests. A reader who took "the browser now runs on Broiler.VM" from
the configuration name and stopped there would be wrong about every page they loaded, which is
why this sits above the table rather than below it.

What the configuration is genuinely good for at this snapshot: it proves the profile composes
into a real application graph and survives a head build on three platforms; and it gives
`VmScriptEngineTests` — compiled only under `-VM` — a place to run actual JavaScript on the
profile inside this repository's own suite. The step from there to a page is item 1 below.

### Why the document paths cannot run on the VM yet

Three separate obstacles, none of which is an unfinished port:

1. **The host boundary is bytes.** A host capability of the JavaScript profile is
   `VmHostBytesCapabilityHandler` — a `VmBytes` in, a `VmOpaqueRef` out. A DOM is not a byte
   string, and there is no surface on the profile through which a live object graph could be
   projected.
2. **The DOM bridge is written against Broiler.JS.** `Broiler.HtmlBridge.Dom` defines the
   document's JavaScript objects as `Broiler.JavaScript` types whose accessors are CLR delegates
   over live nodes — **891 references across 250 files**. Nothing about that is portable to a
   different engine by configuration.
3. **`InteractiveSession` cannot be built from outside Broiler.JS.** Its constructor is internal
   and takes a `JSContext`, so `ExecuteInteractive` could not return one even if the first two
   were solved.

Closing (1) is the prerequisite for the rest. Until then, a `-VM` build renders pages through
Broiler.JS and runs document-free script — the WPT-style batches, `ExecuteDetailed` callers, and
anything a future headless script path asks for — on the VM.

A fourth obstacle is smaller but blocks the same door from the other side: `InteractiveSession`'s
only constructor is `internal` to `Broiler.HtmlBridge.Scripting`, and
`Broiler.HtmlBridge.Scripting.Vm` is not one of its `InternalsVisibleTo` friends. That costs
nothing today — `VmScriptEngine` returns whatever the document engine hands back rather than
minting a session — but any future VM-hosted interactive path has to move into that assembly or be
granted internals by it.

### What the VM engine refuses on purpose

`VmScriptEngine.RuntimeOptions()` registers exactly one host capability, `print`, which reaches
the render log. It registers **no source provider** and **no module resolver**, so:

- `eval()` and `new Function()` are refused deterministically, whatever the page's CSP says.
- `import()` is refused the same way.

Both are policy decisions belonging to a composition, and this composition has no answer to
"what may this page evaluate" or "what does this specifier name" yet. The `Csp` property is still
forwarded to the Broiler.JS engine, because on the delegated paths it does decide something.

The instruction allowance is the profile's own declared default. The VM charges fuel per
instruction rather than per second, so a script that never terminates ends after a bounded number
of instructions, at the same instruction on a fast machine and a slow one.

## The build wiring

| File | What it does |
|---|---|
| `eng/Broiler.Configurations.props` | Maps every configuration name to a base configuration; sets `$(BroilerJavaScriptEngine)` and defines `BROILER_VM_JS` |
| `Directory.Build.props` | Imports it, for projects that chain to the root |
| `Directory.Build.targets` | Imports it again for the ones that do not — see below |
| `src/Broiler.Browser.Core/*.csproj` | The one conditional `ProjectReference` |
| `src/Broiler.Browser.Core/BrowserApp.cs` | The one `#if BROILER_VM_JS` |
| `eng/solutions.json` | `configurations` per solution, becoming the `.slnx` `<BuildType>` list |
| `src/Broiler.HtmlBridge.Scripting.Vm/` | The engine |

### Why the mapping is imported twice

Without the mapping, `-c Release-VM` compiles **unoptimised** and `-c Debug-VM` compiles
**without symbols**: the SDK keys `DebugSymbols` off `Configuration == 'Debug'` and `Optimize` off
`Configuration == 'Release'`, and a name it does not recognise matches neither. That is a build
that succeeds and is quietly wrong, which is why the mapping exists at all — `Debug-Linux` and
`Release-Windows` have needed it since they were introduced.

`Broiler.VM` and `Broiler.Input` each carry a `Directory.Build.props` that deliberately does not
chain to a parent, so the root's never runs for them. Neither has a `Directory.Build.targets`,
and MSBuild looks that up separately — so the root's *does* run for them. The second import
exploits that asymmetry; what stops it applying the mapping a second time is described under
*The mapping has to apply exactly once* below.

This only started to matter with these configurations: `Release-Linux` never built a Broiler.VM
project, so the gap cost nothing and nobody could observe it.

Where each component gets the mapping from, measured rather than assumed:

| Component | Route |
|---|---|
| `src/**`, CSS, DOM, Graphics, HTML, Layout | root `Directory.Build.props` |
| Input, Media, UI, VM | root `Directory.Build.targets` (the second import) |
| **Broiler.JS** | root `Directory.Build.targets`, via a chain in its own — see below |

Every component answers `Optimize=true` under `Release-VM`. `ci.yml`'s graph job asserts exactly
that, one project per family, so a component dropping out of the mapping fails there rather than
shipping quietly.

### Broiler.JS needed a third route, and got one

**Broiler.JS is the only submodule carrying a `Directory.Build.targets` of its own.** MSBuild's
targets lookup stops at the first one it finds, so the root's second import reached every other
component and none of Broiler.JS's — and Broiler.JS's own vendored copy of the mapping knows the
four `-Linux`/`-Windows` names it was written for and not the `-VM` pair. `Release-VM` therefore
compiled its twenty-two projects **unoptimised**, and succeeded.

That mattered more here than it would anywhere else, because under `-VM` Broiler.JS is still the
engine doing all the document work: comparing the two engines on such a build would have compared
an optimised VM against an unoptimised Broiler.JS.

It could not be fixed from this repository — every route into a Broiler.JS project is a file
inside that submodule. `Broiler.JS/Directory.Build.targets` now chains to its parent when there is
one, the same idiom `Broiler.JS/Broiler.JS/Directory.Build.props` already used to reach its own:

```xml
<PropertyGroup>
  <BroilerParentBuildTargets>$([MSBuild]::GetPathOfFileAbove(`Directory.Build.targets`, `$(MSBuildThisFileDirectory)../`))</BroilerParentBuildTargets>
</PropertyGroup>
<Import Project="$(BroilerParentBuildTargets)" Condition="'$(BroilerParentBuildTargets)' != ''" />
```

Measured after the change, and unchanged for every name that already worked:

| Configuration | `Optimize` | `DefineConstants` |
|---|---|---|
| `Release` | `true` | `TRACE;RELEASE` |
| `Release-Windows` | `true` | `;RELEASE;WINDOWS;TRACE;RELEASE_WINDOWS` |
| `Release-VM` | `true` | `TRACE;RELEASE;BROILER_VM_JS;RELEASE_VM` |
| `Debug-VM` | `false` (symbols on) | `TRACE;DEBUG;BROILER_VM_JS;DEBUG_VM` |

A standalone Broiler.JS checkout has no `Directory.Build.targets` above it, so the search returns
empty, the condition is false and nothing is imported — verified against a worktree with no parent
above it. The search starts one directory up, so it cannot find that file itself and recurse.

### The mapping has to apply exactly once

Four submodules — Input, JS, Media and UI — vendor their own copy of the mapping so they still
build standalone. For the `-Linux`/`-Windows` names those copies decompose the configuration at
props time, and the targets-side import then did it *again*:

```
Broiler.Input at Release-Windows   ;RELEASE;WINDOWS;TRACE;RELEASE;WINDOWS;RELEASE_WINDOWS
```

`eng/Broiler.Configurations.props` is therefore guarded on `'$(BroilerBaseConfiguration)' == ''`
as well as on its sentinel: a configuration something already decomposed is left alone, and one no
vendored copy recognises — every one of them at `-VM` — still gets mapped here. A duplicate
conditional symbol is harmless to the compiler, but a constants list that reads differently
depending on which component you ask defeats the purpose of having one mapping. `ci.yml` asserts
`RELEASE` appears exactly once.

**Broiler.UI still duplicates and is excluded from that check.** It both chains to the root props
*and* carries a copy, so both run at props time and no guard on this side can see the second one
coming. That predates this mapping and is the submodule's to resolve.

### Why the Broiler.VM projects are in every solution

`scripts/update-solutions.ps1` reads every `ProjectReference` regardless of its `Condition`, so
the seven added projects appear in all four `.slnx` files and are built by a solution build in
**all four** configurations — including plain `Debug` and `Release`, where nothing references
them.

That is the deliberate cost of one solution per head. A solution can only offer a configuration in
the Visual Studio dropdown if it declares it, and MSBuild answers `MSB4126` for a configuration a
solution does not declare — so the solution has to carry the union. The build that is genuinely
free of Broiler.VM is the project-level one:

```bash
dotnet build src/Broiler.Browser.Windows/Broiler.Browser.Windows.csproj -c Release
```

## Verifying a change to any of this

The mapping is observable without building anything:

```bash
dotnet msbuild src/Broiler.Browser.Core/Broiler.Browser.Core.csproj -p:Configuration=Release-VM -getProperty:DefineConstants -getProperty:Optimize -getProperty:BroilerJavaScriptEngine
```

`RELEASE;BROILER_VM_JS`, `true`, `Vm`. The same question against a Broiler.VM project answers
`RELEASE` and `true` through the targets-side import:

```bash
dotnet msbuild Broiler.VM/src/Broiler.VM.Profile.JavaScript/Broiler.VM.Profile.JavaScript.csproj -p:Configuration=Release-VM -getProperty:Optimize
```

And against Broiler.JS, which reaches the same import through the chain in its own
`Directory.Build.targets` — this is the one that answered empty before that chain existed:

```bash
dotnet msbuild Broiler.JS/Broiler.JS/Broiler.JavaScript.Engine/Broiler.JavaScript.Engine.csproj -p:Configuration=Release-VM -getProperty:Optimize -getProperty:BroilerParentBuildTargets
```

And the duplicate-assembly check takes the configuration from the environment:

```bash
CONFIGURATION=Release-VM ./scripts/check-component-graph.sh Broiler.Linux.Browser.slnx
```
