# Human review summary: Broiler.Browser preview

> **Status: PENDING — and SUMMARY ONLY, not a repository-level human approval.**

No human reviewer has yet attested to the browser application code in this repository
(`src/`). Until a reviewer is named below with a reviewed commit, evidence and a decision,
this repository must not be described as human-approved.

This file additionally aggregates the review records carried by the dependency components.
It is a summary: it does not replace the individual component files, their reviewer
attestations, their conditions, or their pending-review warnings. Read the linked file
before relying on any component.

One submodule listed below, `Broiler.VM`, is not a dependency of the browser: nothing in
`src/` references it and it appears in no head's project graph. It is listed so the table
stays a complete account of the submodules pinned here, not because the browser's behaviour
depends on it. It has changed materially since the last bump - it now carries an
implementation of its core contract rather than project shells - and that implementation has
had no human review, so the row below is unchanged at PENDING for a stronger reason than
before.

## This repository

| Scope | Status |
|---|---|
| `src/` — browser heads, shared chrome, HtmlBridge | **PENDING** |
| `Broiler.Layout/` — vendored, see component record | see below |

Reviewer: _not yet assigned_
Reviewed commit: _none_
Evidence: _none_
Decision: **PENDING**

## Dependency components

Statuses as recorded by each component at the commit pinned here. Re-read these after any
submodule bump — an approval is revision-scoped and does not carry forward.

| Component | Recorded status |
|---|---|
| [Broiler.CSS](Broiler.CSS/HUMAN_REVIEW.md) | Approved for first preview |
| [Broiler.DOM](Broiler.DOM/HUMAN_REVIEW.md) | Approved with conditions |
| [Broiler.Graphics](Broiler.Graphics/HUMAN_REVIEW.md) | Approved with conditions |
| [Broiler.HTML](Broiler.HTML/HUMAN_REVIEW.md) | Approved with conditions — first preview only |
| [Broiler.JS](Broiler.JS/HUMAN_REVIEW.md) | **PENDING** — usable in preview only with its safety warning |
| [Broiler.JS/Broiler.DateTime](Broiler.JS/Broiler.DateTime/HUMAN_REVIEW.md) | Approved for preview |
| [Broiler.JS/Broiler.Regex](Broiler.JS/Broiler.Regex/HUMAN_REVIEW.md) | Approved for preview |
| [Broiler.JS/Broiler.Unicode](Broiler.JS/Broiler.Unicode/HUMAN_REVIEW.md) | Approved with conditions |
| [Broiler.Layout](Broiler.Layout/HUMAN_REVIEW.md) | Approved for first preview |
| [Broiler.UI](Broiler.UI/HUMAN_REVIEW.md) | **PENDING** |
| [Broiler.VM](Broiler.VM/HUMAN_REVIEW.md) | **PENDING** - core contract version 1 is now implemented and unreviewed; not in the browser's build closure |
| Broiler.Input | **No review record in the component** |
| Broiler.Media | **No review record in the component** |

## Overall position

This repository is suitable only for first-preview, controlled development, testing and
evaluation. It must not be presented as production-ready, security-audited, or free of
defects or vulnerabilities.

Two components in the browser's dependency closure — `Broiler.JS` and `Broiler.UI` — are
still `PENDING`, and two more — `Broiler.Input` and `Broiler.Media` — carry no review
record at all. `Broiler.JS` executes untrusted page script and is **not a security
sandbox**; the embedding application must restrict CLR and host capabilities before
running untrusted content.

HTML, CSS, JavaScript, font, image and Unicode-data inputs all cross complex parser or
native-interop boundaries that require focused security review. None has had one.
