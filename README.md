# Broiler.Browser

[![CI](https://github.com/Broiler-Platform/Broiler.Browser/actions/workflows/ci.yml/badge.svg)](https://github.com/Broiler-Platform/Broiler.Browser/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

Broiler.Browser is the browser application of the [Broiler](https://github.com/Broiler-Platform/Broiler)
managed-code browser stack for .NET. It holds the three platform heads — Windows, Linux
and Android — and the shared `Broiler.Browser.Core` chrome they have in common, and embeds
the `Broiler.HtmlBridge` control that binds the DOM, the renderer and the JavaScript engine
into one page lifecycle. `Broiler.Cli`, a headless command line, loads pages through the
same pipeline to capture, evaluate and diagnose them.

Everything below the browser — DOM, CSS, layout, graphics, media, input, UI toolkit, the
HTML control and the JavaScript engines — lives in its own repository and is consumed here
as a NuGet package from nuget.org.

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

The dependency components are NuGet packages, restored from nuget.org by the first build, so a
plain clone is the whole checkout:

```bash
git clone https://github.com/Broiler-Platform/Broiler.Browser.git
```

Build and run the Windows head:

```bash
dotnet build Broiler.Windows.Browser.slnx -c Release
```

```bash
dotnet run --project src/Broiler.Browser.Windows/Broiler.Browser.Windows.csproj -c Release
```

Capture a page as an image from the command line:

```bash
dotnet run --project src/Broiler.Browser.Cli/Broiler.Browser.Cli.csproj -c Release -- --capture-image https://example.com --output example.png
```

See [docs/command-line.md](docs/command-line.md) for everything else it does.

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
| `Broiler.Windows.Browser.slnx` | `src/Broiler.Browser.Windows` | 2 |
| `Broiler.Linux.Browser.slnx` | `src/Broiler.Browser.Linux` | 2 |
| `Broiler.Android.Browser.slnx` | `src/Broiler.Browser.Android` | 3 |
| `Broiler.Browser.Tests.slnx` | `src/Broiler.Browser.Core.Tests`, `src/Broiler.Browser.Cli.Tests` | 4 |

The components are packages, and a package is not a project a solution lists, so each solution
holds only this repository's own projects. The Broiler.VM JavaScript profile and the script engine
over it arrive as the `Broiler.HtmlBridge.Scripting.Vm` package, which `src/Broiler.Browser.Core`
references only under `Debug-VM`/`Release-VM`: those two configurations change what is restored,
not what a solution lists.

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
projects by changing the reference graph, then regenerate. How a solution is laid out belongs to
the manifest too: `"folders": false` lists its projects at the root instead of under the
"Entry points" and "Dependencies" folders, which is how `Broiler.Windows.Browser.slnx` is laid out.

## Continuous integration

[`ci.yml`](.github/workflows/ci.yml) runs on every push to `main` and every pull request:

- **Solution manifest** — `scripts/update-solutions.ps1 -Verify`, which fails if a
  checked-in `.slnx` no longer matches the reference graph. This is what catches a new
  `ProjectReference` that was never folded into a solution.
- **Component graph** — one assembly per name across each solution's whole closure, its projects
  *and* the packages NuGet restores for them, in `Debug` and again in `Debug-VM`, which restores a
  different package closure. Also that every project declares every build type its solution offers,
  and that the configuration mapping reaches every project, selects the VM engine under `-VM` and
  is applied exactly once — a configuration the mapping misses compiles unoptimised and says
  nothing, and one it maps twice defines `RELEASE` twice.
- **Build** — the Windows head on `windows-latest`, the Linux head on `ubuntu-latest`.
- **Tests** — the shared chrome's suite and the command line's, on both hosts, because the
  shared chrome does clipboard and file-dialog work that is easy to make accidentally
  platform-specific.
- **VM profile** — the suite on Linux and the Windows head under `Release-VM`. The engine's own
  tests left with it for Broiler.HtmlBridge; what the suite asks here is the embedder's question,
  the browser's host code run against the VM script engine that configuration selects.
- **Android head** — a separate job, since it pays for the `android` workload. It runs the two
  graph checks for its own solution, which the other job cannot evaluate without the workload.
- **Publish** — `Release-Windows` and `Release-Linux`, the runtime-identifier-pinned
  configurations, in both variants the release ships: self-contained single-file and
  framework-dependent. They are
  project-level builds by necessity: no solution declares them, so a solution-level build with
  either fails `MSB4126`.

Every job checks out without submodules — there are none — and sets up the SDK through
[`.github/actions/setup-broiler`](.github/actions/setup-broiler/action.yml).

## Releases

[`release.yml`](.github/workflows/release.yml) is dispatch-only (*Actions → Release → Run
workflow*). Each run picks the next preview version, builds every head with it, tags the commit
`browser-v<version>` and drafts a GitHub **pre-release** carrying:

| Asset | Contents |
|---|---|
| `Broiler.Browser-<version>-win-x64-self-contained.zip` | `Broiler.Browser.Windows.exe` |
| `Broiler.Browser-<version>-win-x64-framework-dependent.zip` | the publish folder: `Broiler.Browser.Windows.exe` and its assemblies, no runtime |
| `Broiler.Browser-<version>-linux-x64-self-contained.zip` | `Broiler.Browser.Linux`, recorded executable |
| `Broiler.Browser-<version>-linux-x64-framework-dependent.zip` | the publish folder: `Broiler.Browser.Linux` (recorded executable) and its assemblies, no runtime |
| `Broiler.Browser-<version>.aab` | the Android app bundle (arm64 + x86_64), for Google Play |
| `Broiler.Browser-<version>-arm64.apk` | the Android APK, for sideloading |

The self-contained desktop executables are single files: the .NET runtime and every assembly in
one file, nothing to install. The framework-dependent variants are the same heads published with
`--self-contained false`: a folder without the runtime, much smaller, that needs the .NET 10
runtime installed. Neither is NativeAOT, as Broiler.Writer's are, because Broiler.JS does not
start under NativeAOT.

The release optimizations are set in the head projects, so CI and a publish from Visual Studio get
them too:

| Head | Optimizations | Measured |
|---|---|---|
| Windows, Linux | ReadyToRun for a release publish with a runtime identifier; tiered compilation with dynamic PGO | a script-driven page finishes in 1.46 s instead of 2.11 s, first render 62 ms instead of 273 ms; executable 115 MB instead of 84 MB |
| Android | profiled AOT (the SDK's startup profile) for Release, with the partial trimming AOT requires | cold start 1.98 s instead of 3.15 s on the API 36 emulator; APK 10 MB instead of 24 MB, app bundle 21 MB instead of 49 MB |

Nothing is fully trimmed: Broiler.JS reflects over its own members. Partial trimming keeps every
assembly whole except those that declare themselves trimmable, and one of Broiler.JS's,
`Broiler.JavaScript.Expressions`, does so wrongly. The Android head roots it; without that, every
page with a script fails to load there.

The Android packages are attached as they are, not zipped, and
are signed with the Broiler release key by
[`eng/sign-android-packages.ps1`](eng/sign-android-packages.ps1) (the monorepo's script, copied
unchanged), which reads the `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`,
`ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASSWORD` repository secrets and checks them before
anything is built.

The release stays a draft until someone reviews and publishes it. The version is
`BroilerBrowserVersion` in [`Directory.Build.props`](Directory.Build.props) raised past every
earlier `browser-v*` tag ([`eng/resolve-preview-version.mjs`](eng/resolve-preview-version.mjs)),
so preview numbers only increase; raise the property to start a new release line. The preview
number is also the Android `versionCode`. [`eng/release-draft.sh`](eng/release-draft.sh) drafts
the release, and can do so by hand from a run's downloaded artifacts.

## Repository layout

| Path | Contents |
|---|---|
| `src/Broiler.Browser.Windows` | Windows head — `WinExe`, Direct2D, Win32 input |
| `src/Broiler.Browser.Linux` | Linux head — X11/GL window and render loop; clipboard and input coordination come from `Broiler.Hosting.Linux` |
| `src/Broiler.Browser.Android` | Android head — activity, manifest, resources |
| `src/Broiler.Browser.Core` | Shared browser chrome, palette, HTML form hosting |
| `src/Broiler.Browser.Core.Tests` | xUnit suite for the shared chrome |
| `src/Broiler.Browser.Cli` | `Broiler.Cli`, the headless command line — see [docs/command-line.md](docs/command-line.md) |
| `src/Broiler.Browser.Cli.Tests` | xUnit suite for the command line |
| `src/Broiler.App` | Source-only directory shared by the heads — rendering pipeline, page loader, favorites. It has no project of its own; each head links the files it needs. The per-platform hosting utilities (clipboards, input coordinators, the Android view, canvas renderer and input connection) come from the `Broiler.Hosting.Windows`, `.Linux` and `.Android` packages. |
| `eng/`, `scripts/` | Solution manifest, configuration mapping and generator; the release's version resolver, Android signing and draft-release scripts |

*(Corrected 2026-09-08. Two rows here described `Broiler.VM.Profile.JavaScript` and
`Broiler.VM.Profile.WebAssembly` as top-level directories of this repository holding "documents
only — no source tree yet". Neither is a path here at all: both are product projects inside the
`Broiler.VM` submodule, at `Broiler.VM/src/`, and both have had source for some time. The rows are
removed rather than repaired because a repository-layout table should list paths that exist. What
replaces them is the row above, which is a real directory this change added.)*

## Dependencies

Every component below is a NuGet package from nuget.org, referenced from the projects under
`src/`. Their versions are managed centrally in `Directory.Packages.props` (Central Package
Management): a `PackageReference` names the package only. Each version there is a minimum, not
necessarily the version a build gets: restore takes the lowest version nuget.org has at or above it, and several of those
minimums name a preview nuget.org does not have, so restore resolves them upward and warns
`NU1603` for each one. What a project actually restored is in its `obj/project.assets.json`.
There are no submodules: the last ones — Broiler.HTML, Broiler.HtmlBridge, Broiler.JS,
Broiler.Layout and Broiler.VM — were replaced by their packages in September 2026.

| Component | Purpose |
|---|---|
| `Broiler.DOM` | Canonical DOM, HTML tokenization, parsing, traversal, serialization |
| `Broiler.CSS` | CSS parsing, selectors, cascade, computed values |
| `Broiler.Graphics` | Managed bitmap/codec/raster core plus platform backends |
| `Broiler.Media` | Image, audio and video abstractions and managed codecs |
| `Broiler.Input` | Keyboard, mouse, pen, touch and text input abstractions |
| `Broiler.UI` | Platform-neutral retained-mode UI toolkit |
| `Broiler.HTML` | Modular HTML/CSS renderer |
| `Broiler.Net` | Cookie engine, site resolution and the profile's HTTP transport (`BrowserNetworkSession`). `Broiler.HTML` and `Broiler.HtmlBridge` depend on it too |
| `Broiler.HtmlBridge` | The HTML control: the DOM bridge, JSEAL and the two engine providers. Extracted from `src/` on 2026-09-16 — nothing in it was about being a browser, so this repository is now one embedder of it rather than its owner. Reached from `src/Broiler.Browser.Core` through `Broiler.HtmlBridge.Scripting` |
| `Broiler.JS` | JavaScript parser, compiler, runtime and built-ins |
| `Broiler.VM` | Generic execution core — a host for language profiles, not a language. Owns profile selection, bounded loading, the verification boundary, the execution lifecycle, resource authority and diagnostics; owns no opcode set, value representation or language semantics |

`Broiler.VM` is reached by the browser heads **only under the `Debug-VM` and `Release-VM`
configurations**, through the `Broiler.HtmlBridge.Scripting.Vm` package. It remains a separate clean-room
component with its own roadmap, `Broiler.JS` does not depend on it, and neither depends on the
other. Its two language profiles — JavaScript and WebAssembly — are product projects of that
component; the browser reaches the JavaScript one and none of the WebAssembly one, which every
head's solution manifest also forbids by pattern. See
[docs/vm-javascript-profile.md](docs/vm-javascript-profile.md).

*(Corrected 2026-09-08. This paragraph read "`Broiler.VM` is not yet used by the browser heads"
and described both profiles as living in this repository as documents with no source tree. The
first half is what this change made false, so it is restated rather than deleted; the second half
was already false before it — see the note under* Repository layout.*)*

### Network and cookies

Every head creates one `BrowserProfile` (`src/Broiler.Browser.Core/BrowserProfile.cs`) and
hands it to its `BrowserApp`: a cookie store and one Broiler.Net `BrowserNetworkSession` over it.
That session is the only network the browser uses for a page. Navigations go through
`PageLoader` as top-level navigation requests that name the document that started them; the
scripts the extractor fetches, the DOM bridge's loaders (`fetch`, XHR, `sendBeacon`, module
imports, inserted scripts, stylesheets, frames) and the renderer's images, stylesheets and fonts
are sub-resource requests of the page's document. A cookie a navigation receives is therefore
what every one of them carries, under Fetch's credentials, CORS and SameSite rules, and
`document.cookie` reads the same store without ever seeing an `HttpOnly` cookie. No loader keeps
a cookie jar of its own. A `BrowserApp` constructed without a profile gets a private, ephemeral
one; there is no process-global profile.

A page's own navigations (script, refresh meta, links, forms) never open a local file: only the
user — typed, a bookmark, the command line — or a page that is itself a `file:` document does, and
a file on a share only the user. A page may go back once to a URL its load was redirected away from
(a cookie challenge reached by a redirect); every URL of the redirect chain counts towards the
same-path budget that stops a real loop.

### Known issues from composing checkouts

This section described consequences of building the components from checkouts here —
`Broiler.Layout` vendored as a directory, `Broiler.HTML`'s pre-`src/` reference paths, and
components compiled more than once. All of them ended with the checkouts: `Broiler.Layout`
arrives as a dependency of the `Broiler.HTML` and `Broiler.HtmlBridge` packages, and nothing here
compiles a component any more.

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
