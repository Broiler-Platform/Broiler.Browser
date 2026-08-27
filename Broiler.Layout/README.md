# Broiler.Layout

A .NET 10 CSS box-model and layout engine over the canonical Broiler DOM and computed
styles. The component is graphics-backend independent and is currently integrated with
Broiler.HTML through internal compatibility boundaries.

## Preview status

This is first-preview software. The API, layout behavior, and integration boundaries may
change without compatibility guarantees. Substantial implementation work was AI-assisted.
The recorded [human review](HUMAN_REVIEW.md) approved a specific 2026-07-01
revision with conditions. The component has changed substantially since that
revision, so the approval must not be treated as covering current source or a
broader release. Re-review and hardening work is tracked in the
[current roadmap](docs/roadmap.md).

Broiler.Layout is an independent Broiler component. Its integration with Broiler.HTML
inherits architectural context from HTML Renderer, but it must not be represented as an
official HTML Renderer component or as endorsed by that project's contributors.

## Architecture and API boundary

The public seam consists of the host abstractions (`ILayoutEnvironment`, `ILayoutFont`,
and `ILayoutImageLoader`), their value types, and the renderer-neutral HTML metadata
types used to construct a layout input. The concrete box tree and layout algorithms
remain internal. Layout consumes only `Broiler.CSS`, `Broiler.CSS.Dom`, `Broiler.Dom`,
the backend-neutral `Broiler.Graphics` primitive layer, and the BCL; painting,
resource acquisition, HTML parsing, and platform UI remain in their owning
assemblies.

Ten assemblies have deliberate access to internal layout state:

| Friend assembly | Compatibility need |
|---|---|
| `Broiler.HTML.Dom` | Parses, serializes, and visits renderer box trees. |
| `Broiler.HTML` | Implements selection, hit-testing, and context-menu interaction over laid-out boxes. |
| `Broiler.HTML.Orchestration` | Projects computed style, invokes layout, and builds paint fragments. |
| `Broiler.DevConsole` | Converts the internal tree into renderer-independent diagnostic snapshots. |
| `Broiler.Cli.Tests` | Characterizes renderer integration and internal box state during the preview compatibility window. |
| `Broiler.DevConsole.Tests` | Characterizes diagnostic snapshots with constructed boxes. |
| `Broiler.Layout.Tests` | Exercises the internal layout kernel directly. |
| `Broiler.Wpt` | Enables scoped native anchor placement around final WPT renders. |
| `Broiler.HTML.Headless` | Produces the live geometry snapshot through `ILayoutView`. |
| `Broiler.HtmlBridge.Dom` | Supplies the visual-viewport scale around geometry snapshots. |

Facade and application assemblies must consume owned projections instead of traversing
the internal tree. In particular, `Broiler.HTML.Image` delegates canvas-background
resolution to orchestration, and the WPF app consumes dev-console snapshots. The exact
friend set is locked by `LayoutArchitectureTests` so new grants require an explicit
boundary decision and documentation update.

## Build and test

From the main Broiler repository root:

```bash
dotnet build Broiler.Layout/Broiler.Layout/Broiler.Layout.csproj
dotnet test Broiler.Layout/Broiler.Layout.Tests/Broiler.Layout.Tests.csproj
```

## License

Broiler.Layout is licensed under the [Apache License 2.0](LICENSE). Third-party material,
if present, retains the license identified with that material. The license provides the
software on an “AS IS” basis, without warranties or conditions.
