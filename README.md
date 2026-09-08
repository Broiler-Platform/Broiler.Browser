# Broiler.Browser

[![CI](https://github.com/Broiler-Platform/Broiler.Browser/actions/workflows/ci.yml/badge.svg)](https://github.com/Broiler-Platform/Broiler.Browser/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

Broiler.Browser is the browser application of the [Broiler](https://github.com/Broiler-Platform/Broiler)
managed-code browser stack for .NET. It holds the three platform heads — Windows, Linux
and Android — the shared `Broiler.Browser.Core` chrome they have in common, and the
`Broiler.HtmlBridge` layer that binds the DOM, the renderer and the JavaScript engine into
one page lifecycle.

Everything below the browser — DOM, CSS, layout, graphics, media, input, UI toolkit and
the JavaScript engine — lives in its own repository and is consumed here as a submodule.

> **Preview status.** APIs, repository layout and persisted formats are unstable and may
> change without notice. Substantial portions of this project were developed with AI
> assistance. No component is human-approved for preview use until its `HUMAN_REVIEW.md`
> names a human reviewer, reviewed commit, evidence and approval decision. See
> [HUMAN_REVIEW.md](HUMAN_REVIEW.md) for the aggregate position.
>
> This preview is intended for evaluation, testing and contribution — not production,
> security-critical or safety-critical use. Broiler.JS is **not a security sandbox**: CLR
> and host capabilities must be restricted by the embedding application before running
> untrusted scripts.

## Getting started

The dependency components are submodules, so the checkout must be recursive:

```bash
git clone --recurse-submodules https://github.com/Broiler-Platform/Broiler.Browser.git
```

If you already cloned without them:

```bash
git submodule update --init --recursive
```

Build and run the Windows head:

```bash
dotnet build Broiler.Windows.Browser.slnx -c Release
```

```bash
dotnet run --project src/Broiler.Browser.Windows/Broiler.Browser.Windows.csproj -c Release
```

Run the tests:

```bash
dotnet test Broiler.Browser.Tests.slnx -c Release
```

### Prerequisites

- **.NET SDK 10.0** or later. The repository is developed against 10.0.400.
- **Windows head** — builds on Windows; targets `net10.0-windows` and renders through the
  Direct2D graphics backend.
- **Linux head** — targets `net10.0`. The `Debug-Linux` and `Release-Linux` configurations
  pin `linux-x64`; the plain `Debug`/`Release` configurations build framework-dependent.
- **Android head** — needs the `android` workload (`dotnet workload install android`).
  Targets `net10.0-android36.0` with a minimum SDK of 24, builds `android-arm64` and
  `android-x64`, and produces an `.aab` in `Release` and an `.apk` otherwise. Override
  `BroilerAndroidAbis` to package a single ABI.

### Configurations

Every configuration name decomposes into a base (`Debug` or `Release`, deciding symbols and
optimisation) and one axis, in [`eng/Broiler.Configurations.props`](eng/Broiler.Configurations.props):

| Configuration | What it adds | Built through |
|---|---|---|
| `Debug` / `Release` | — | solution or project |
| `Debug-Linux` / `Release-Linux` | pins `linux-x64` on the Linux head | project only |
| `Debug-Windows` / `Release-Windows` | pins `win-x64` on the Windows head | project only |
| `Debug-VM` / `Release-VM` | runs script on the Broiler.VM JavaScript profile | solution or project |

The runtime-identifier-pinned pairs are absent from the solutions on purpose — they are published
per project, and a solution-level build of a configuration a `.slnx` does not declare fails
`MSB4126`. The `-VM` pair *is* declared, so it works both ways:

```bash
dotnet build Broiler.Windows.Browser.slnx -c Debug-VM
```

See [docs/vm-javascript-profile.md](docs/vm-javascript-profile.md) for what that changes — and for
the two things it does **not**: it does not remove Broiler.JS, and no page load runs on the VM yet.

## Solutions

Each head has a focused solution containing exactly its transitive closure, so opening one
does not drag in another platform's backends.

| Solution | Entry point | Projects |
|---|---|---|
| `Broiler.Windows.Browser.slnx` | `src/Broiler.Browser.Windows` | 88 |
| `Broiler.Linux.Browser.slnx` | `src/Broiler.Browser.Linux` | 87 |
| `Broiler.Android.Browser.slnx` | `src/Broiler.Browser.Android` | 88 |
| `Broiler.Browser.Tests.slnx` | `src/Broiler.Browser.Core.Tests` | 82 |

Seven of those projects in each — the Broiler.VM JavaScript profile and the script engine over it
— are only *referenced* under `Debug-VM`/`Release-VM`, but the generator reads every
`ProjectReference` regardless of its `Condition`, so they are listed and built in all four
configurations. That is the price of one solution per head; a project-level build under `Debug` or
`Release` is free of them.

The solutions are **generated, not hand-edited**. `eng/solutions.json` declares each entry
point and the platform boundaries it must not cross; `scripts/update-solutions.ps1` walks
the real project-reference graph and writes the `.slnx` files from it:

```bash
pwsh scripts/update-solutions.ps1
```

`-Verify` fails instead of writing, which is the form CI should run:

```bash
pwsh scripts/update-solutions.ps1 -Verify
```

A hand-edit to a `.slnx` is silently reverted by the next generator run. Add or remove
projects by changing the reference graph, then regenerate.

## Continuous integration

[`ci.yml`](.github/workflows/ci.yml) runs on every push to `main` and every pull request:

- **Solution manifest** — `scripts/update-solutions.ps1 -Verify`, which fails if a
  checked-in `.slnx` no longer matches the reference graph. This is what catches a new
  `ProjectReference` that was never folded into a solution.
- **Component graph** — one assembly per name across each solution's whole closure, in `Debug`
  and again in `Debug-VM`, which is a different closure rather than the same one built twice.
  The same job pins that the configuration mapping reaches every component and is applied exactly
  once — a configuration the mapping misses compiles unoptimised and says nothing, and one it maps
  twice defines `RELEASE` twice.
- **Build** — the Windows head on `windows-latest`, the Linux head on `ubuntu-latest`.
- **Tests** — the suite on both hosts, because the shared chrome does clipboard and
  file-dialog work that is easy to make accidentally platform-specific.
- **VM profile** — the suite on Linux and the Windows head under `Release-VM`. The suite is the
  one that carries weight: `VmScriptEngineTests` compiles only under that configuration, so this
  is where JavaScript actually runs on the Broiler.VM profile rather than merely linking against it.
- **Android head** — a separate job, since it pays for the `android` workload.
- **Publish** — `Release-Windows` and `Release-Linux`, the runtime-identifier-pinned
  configurations. They are project-level builds by necessity: no solution declares them, so a
  solution-level build with either fails `MSB4126`.

[`release.yml`](.github/workflows/release.yml) is dispatch-only and uploads build
artifacts for manual testing — `win-x64`, `linux-x64` and a **debug-signed**
`android-arm64` APK. It creates no GitHub release and signs nothing for distribution;
store-ready signed preview packages come from the monorepo's *Prepare Broiler Preview
Package* workflow, which owns the signing material.

The nested-submodule set the browser needs is defined once, in
[`.github/actions/setup-broiler`](.github/actions/setup-broiler/action.yml).

## Repository layout

| Path | Contents |
|---|---|
| `src/Broiler.Browser.Windows` | Windows head — `WinExe`, Direct2D, Win32 input |
| `src/Broiler.Browser.Linux` | Linux head — X11 clipboard and input coordination |
| `src/Broiler.Browser.Android` | Android head — activity, manifest, resources |
| `src/Broiler.Browser.Core` | Shared browser chrome, palette, HTML form hosting |
| `src/Broiler.Browser.Core.Tests` | xUnit suite for the shared chrome |
| `src/Broiler.App` | Source-only directory shared by the heads — rendering pipeline, page loader, favorites, per-platform clipboards. It has no project of its own; each head links the files it needs. |
| `src/Broiler.App.Android` | Android view, canvas renderer, input connection |
| `src/Broiler.HtmlBridge.Core` | Bridge models, logging, CSP and script-extraction support |
| `src/Broiler.HtmlBridge.Dom` | DOM bridge, tree building, JavaScript DOM objects |
| `src/Broiler.HtmlBridge.Jseal` | **JSEAL** — the JavaScript Engine Abstraction Layer. Engine-neutral contracts the DOM binds against; has no project reference of any kind, which is what makes the neutrality a compiler-checked claim. See [docs/jseal.md](docs/jseal.md) |
| `src/Broiler.HtmlBridge.Jseal.BroilerJs` | The JSEAL provider over Broiler.JS |
| `src/Broiler.HtmlBridge.Scripting` | JavaScript execution integration (Broiler.JS) |
| `src/Broiler.HtmlBridge.Scripting.Vm` | The same integration over the Broiler.VM JavaScript profile, referenced only under `Debug-VM`/`Release-VM` |
| `Broiler.Layout` | Vendored layout engine — see *Dependencies* below |
| `eng/`, `scripts/` | Solution manifest, configuration mapping and generator |

*(Corrected 2026-09-08. Two rows here described `Broiler.VM.Profile.JavaScript` and
`Broiler.VM.Profile.WebAssembly` as top-level directories of this repository holding "documents
only — no source tree yet". Neither is a path here at all: both are product projects inside the
`Broiler.VM` submodule, at `Broiler.VM/src/`, and both have had source for some time. The rows are
removed rather than repaired because a repository-layout table should list paths that exist. What
replaces them is the row above, which is a real directory this change added.)*

## Dependencies

Nine components are submodules. Eight are pinned to `main`; `Broiler.VM`'s entry in
[`.gitmodules`](.gitmodules) names no branch, so `git submodule update --remote` leaves it where
the gitlink puts it while moving the other eight.

| Component | Purpose |
|---|---|
| `Broiler.DOM` | Canonical DOM, HTML tokenization, parsing, traversal, serialization |
| `Broiler.CSS` | CSS parsing, selectors, cascade, computed values |
| `Broiler.Graphics` | Managed bitmap/codec/raster core plus platform backends |
| `Broiler.Media` | Image, audio and video abstractions and managed codecs |
| `Broiler.Input` | Keyboard, mouse, pen, touch and text input abstractions |
| `Broiler.UI` | Platform-neutral retained-mode UI toolkit |
| `Broiler.HTML` | Modular HTML/CSS renderer |
| `Broiler.JS` | JavaScript parser, compiler, runtime and built-ins |
| `Broiler.VM` | Generic execution core — a host for language profiles, not a language. Owns profile selection, bounded loading, the verification boundary, the execution lifecycle, resource authority and diagnostics; owns no opcode set, value representation or language semantics |

`Broiler.JS` carries `Broiler.DateTime`, `Broiler.Regex` and `Broiler.Unicode` as its own
nested submodules.

`Broiler.VM` is reached by the browser heads **only under the `Debug-VM` and `Release-VM`
configurations**, through `src/Broiler.HtmlBridge.Scripting.Vm`. It remains a separate clean-room
component with its own roadmap, `Broiler.JS` does not depend on it, and neither depends on the
other. Its two language profiles — JavaScript and WebAssembly — are product projects inside that
submodule under `Broiler.VM/src/`; the browser references the JavaScript one and none of the
WebAssembly one, which every head's solution manifest also forbids by pattern. See
[docs/vm-javascript-profile.md](docs/vm-javascript-profile.md).

*(Corrected 2026-09-08. This paragraph read "`Broiler.VM` is not yet used by the browser heads"
and described both profiles as living in this repository as documents with no source tree. The
first half is what this change made false, so it is restated rather than deleted; the second half
was already false before it — see the note under* Repository layout.*)*

### Broiler.Layout is vendored, not a submodule

`Broiler.Layout` — the graphics-independent CSS box-model and layout engine — has **no
standalone repository**. It exists only as a directory inside the `Broiler` monorepo, and
both `Broiler.HTML.Core` and `src/Broiler.HtmlBridge.Core` need it. It is therefore
checked in here as ordinary tracked files under `Broiler.Layout/`.

Should `Broiler-Platform/Broiler.Layout` ever be published, this directory can be replaced
by a submodule at the same path with no reference changes: `Broiler.HTML.Core` already
reaches it as `..\..\..\Broiler.Layout\`, which resolves to the repository root either way.

### Known issues

Two consequences of composing independently released components are worth knowing before
you file a bug:

- **`Broiler.HTML` still uses the pre-`src/` paths.** It spells its top-level
  `Broiler.Media` and `Broiler.Graphics` references in the flat layout those components
  used while they were vendored inside the monorepo. Both now publish under `src/`, so the
  references as written resolve nowhere. `Directory.Build.targets` rewrites exactly those
  two references onto the top-level checkouts. Both blocks are marked for deletion once
  `Broiler.HTML` follows the components into `src/` and the gitlink here is bumped.

- **Some components compile more than once.** Each component repository carries nested
  checkouts of its own dependencies so it still builds standalone, and its projects
  reference those nested copies by literal relative path. Composed here that means
  `Broiler.Media` is compiled three times and `Broiler.Graphics` and `Broiler.Input` twice.
  Every nested gitlink points at the same commit as the top-level one, so the duplicates
  are assembly-identical and the build reports no reference conflicts — but it is wasted
  work and it violates the one-project-per-assembly-identity rule that
  `Directory.Build.props` states. `Broiler.CSS` and `Broiler.HTML` already avoid it with a
  `$(BroilerDomPath)` / `$(BroilerGraphicsPath)` property hook; the fix is to give
  `Broiler.UI`, `Broiler.Graphics` and `Broiler.Media` the same hook upstream. The solution
  generator already folds the nested paths onto the top-level ones, so the `.slnx` files
  list each assembly once.

## Provenance

Broiler's rendering lineage comes from
[HTML Renderer](https://github.com/ArthurHub/HTML-Renderer) and its JavaScript-engine
lineage from [Yantra JS](https://github.com/yantrajs/yantra). Broiler has diverged
substantially and is maintained independently; it is not a continuation, official edition
or release of either upstream project, and neither upstream team is affiliated with or
endorses it. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for complete provenance
and license references.

## License

Apache License 2.0 — see [LICENSE](LICENSE).
