# JSEAL — the JavaScript Engine Abstraction Layer

**This document moved.** JSEAL is the set of engine-neutral contracts the DOM is bound
against, so that *which JavaScript engine runs a page* is a provider choice rather than a
compile-time fact about the bindings. All of it — the contracts, the two providers, the
conformance suite, the coupling budget and the script that enforces it — left this
repository with the bridge in September 2026.

It now lives in the Broiler.HtmlBridge component, which this repository consumes as packages:
<https://github.com/Broiler-Platform/Broiler.HtmlBridge/blob/main/docs/jseal.md>

The ratchet moved with it. `eng/jseal-budget.json` and
`scripts/check-engine-neutrality.sh` are now that component's, measuring that component's
`src/`, and `.github/workflows/ci.yml` here says why there is no replacement job in this
repository.

**What stayed here is one line of coupling and it is this repository's own.**
`BrowserApp.NewScriptEngine()` picks an engine with an `#if` on `BROILER_VM_JS` rather than
asking `JsEngineRegistry`, which is why `src/Broiler.Browser.Core` needs Broiler.JS in its
closure unconditionally — today it arrives through the `Broiler.HtmlBridge.*` packages rather
than a reference of its own. That is an *embedder* choosing an engine — the thing
JSEAL exists to make a runtime decision — and it is argued for in
[docs/vm-javascript-profile.md](vm-javascript-profile.md), beside the configurations that
select it.
