# FP-08: embedded source is not a collapsed label

Implemented and verified locally on **2026-10-09**. This small diagnostic slice
was selected first from the [compatibility checklist](fingerprinting-compatibility-roadmap.md).
The worker/API items and the rest of FP-08 remain open.

CreepJS embeds stylesheets inside its result cards. The analyzer found zero-size
geometry entries for them and reported their CSS source as text that failed to
render. Its existing non-content classification applied only to elements without
geometry entries, leaving this second path inconsistent.

[LayoutInspector](../src/Broiler.Browser.Cli/Analysis/LayoutInspector.cs) now applies
that same tag/ancestor classification to collapsed-text findings. This is a
diagnostic filter; it does not change page layout or hide JavaScript/API failures.
Ordinary elements with zero-size boxes remain eligible for warnings.

## Validation

The new end-to-end regression in
[PageAnalysisTests](../src/Broiler.Browser.Cli.Tests/PageAnalysisTests.cs) embeds a
stylesheet and JSON script alongside visible text and a deliberately collapsed
label. Before the fix it fails because the stylesheet is reported as a second
collapsed element. Afterward only the genuine collapsed label is reported.

**50 focused tests passed**, covering that regression, the existing SVG-content
case and AnalysisInspectorTests. Command:

```powershell
dotnet test src/Broiler.Browser.Cli.Tests/Broiler.Browser.Cli.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~Embedded_Source_Text|FullyQualifiedName~An_Svgs_Content|FullyQualifiedName~AnalysisInspectorTests' `
  --logger 'trx;LogFileName=after.trx' --results-directory artifacts/fp08-source-text-tests
```

Both unmodified live sites were rerun with `--analyze`, 1280×900, the default
BrowserApp comparison and a 180-second watchdog, using the rebuilt Release CLI.
The fetched HTML hashes match the earlier baseline for both sites.

| Observation | CreepJS | Sannysoft |
| --- | --- | --- |
| Analysis exit | 0 | 0 |
| Collapsed-text warnings, before → after | **15 → 0** | 0 → 0 |
| Post-script element count, before → after | 2,316 → 2,316 | 289 → 289 |
| Analyzer script failures, before → after | 1 → 1 | 2 → 2 |
| Layout invariant reports, before → after | 104 → 104 | 0 → 0 |
| BrowserApp comparison | Done, settled, difference ratio 0 | Done, settled, difference ratio 0 |

The comparison is between the analyzer and BrowserApp render in each run, not a
claim of reference-browser parity. The unchanged API errors and invariant reports
remain separate work. No dependency updates or HtmlBridge production changes were
needed; this validation used the local Browser build, not a published release.

[Durable evidence](repros/fp08-source-text-observed-2026-10-09.json) includes the
removed findings, before/after counts, exit codes and hashes. Full analysis bundles
are in `artifacts/fp08-source-text/`; regression TRX files are in
`artifacts/fp08-source-text-tests/`. Both directories are Git-ignored.
