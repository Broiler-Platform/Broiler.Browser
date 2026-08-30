# Broiler.VM.Profile.JavaScript roadmap status

**Last updated:** 2026-08-30

**Authority:** This file is the authoritative current-evidence ledger for the milestones in the
[JavaScript profile roadmap](roadmap.md). The roadmap defines planned work and objective exit
gates; this ledger records whether those gates have accepted evidence.

**At this snapshot, every milestone JS-0 through JS-10 is `Not started`.** The component has no
source tree, no solution, no project, no descriptor, no snapshot, no evidence bundle, and no
assurance record. What exists is the roadmap and this ledger. No milestone is complete because
its design appears in the roadmap, and nothing in this component may be described as implemented,
validated, accepted, supported, or published.

The component also has no repository of its own yet. The two documents are staged in the
aggregate repository beside the components they will sit next to; **JS-0 owns the placement
decision** and moving them is part of it, not a side effect of it.

---

## 1. Reading this ledger

Four categories must remain distinct, and conflating any two of them is how an unfounded claim
gets recorded:

- **Plan** is proposed scope, sequencing, ownership, or an exit gate in `roadmap.md`. It is not
  implementation evidence and not validation evidence.
- **Observed repository state** is a reviewable fact about the current checkout — for instance
  that this component contains no project file. It can explain a status; it cannot satisfy a
  future implementation, contract, conformance, Native AOT, or release gate.
- **Accepted evidence** is an immutable, reviewable bundle that identifies the exact sources and
  gate, records the executed commands and environment, retains their outputs, and demonstrates
  every part of the objective exit gate. Only accepted evidence may advance a milestone to
  `Accepted`.
- **Inherited material** is anything copied from the seed. It carries **no status of its own**.
  A copied file is unvalidated and unreviewed in this component on the day it lands, however long
  it has existed elsewhere.

**Work in other components is not this component's evidence.** In particular, no conformance
result, benchmark, measurement, review decision, or Native AOT sample produced by the legacy
JavaScript engine component or by the Broiler.VM core establishes anything here, and no gate in
this ledger may cite one. That rule is not a courtesy to the fork; it is what makes a number in
this file mean something.

### Status vocabulary

| State | Meaning |
|---|---|
| `Not started` | No milestone-owned implementation or accepted gate evidence has been recorded. Planning text does not change this state. |
| `In progress` | Milestone-owned work or evidence collection has begun, but the objective exit gate has not been accepted. The ledger must link its working evidence and list every open gate condition. |
| `Blocked` | Work has a named external dependency that prevents the next action. The blocker, its holder, and its unblock condition must be recorded. **Lack of scheduling is not a blocker; an unaccepted upstream contract is.** |
| `Accepted` | Every objective exit condition has an immutable evidence bundle and an owner and reviewer decision recorded here. Partial success cannot use this state. |
| `Superseded` | A dated decision replaced the milestone or gate. The replacement and the decision record must be linked; evidence history is retained. |

---

## 2. Current milestone status

The leading column is an **evidence verdict** — the author's mark about what a row's retained
evidence shows. It is not a reviewer's finding and not a change of state. Every row below is
`[NONE]`, because no row has retained evidence of any kind.

| Verdict | Milestone | State | Current evidence | Immediate evidence-producing action |
|---|---|---|---|---|
| [NONE] | **JS-0 — boundary, placement, identity, assurance floor** | **Not started** | None. No project, no rule register, no assurance manifest, no licence or notice file, no evidence-collection script. | Take the placement decision with the core's topology owner co-signing, then stand up the shell graph and the architecture rule register with both halves of the legacy-boundary rule and a negative control for each. Nothing else in this component may land first. |
| [NONE] | **JS-1 — the whole contract loop on a narrow slice** | **Not started** | None. No descriptor, no format, no verifier, no executor, no composition root. | After JS-0: mint the slice manifest, define format version 1, and drive one artifact through verify, instantiate, and invoke in a composition that publishes and runs under Native AOT. |
| [NONE] | **JS-2 — seeding snapshot and front-end ingest** | **Blocked** (recorded as `Not started` above the blocker, because no work has begun either) | None. No snapshot has been taken. The candidate identity in roadmap section 4.1 is a recorded candidate, not a taken snapshot. | **Blocked on two named external dependencies.** See section 3. |
| [NONE] | **JS-3 — static semantics, diagnostic registry, the oracle** | **Not started** | None. No diagnostic registry, no harness, no pinned suite revision, no self-check fixture. | After JS-2: publish the registry bound in both directions, then stand the harness up and prove the self-check before scoring anything. |
| [NONE] | **JS-4 — value representation and object model** | **Not started** | None. The value-representation decision is **open**, and roadmap section 23 makes copying standard-library source while it is open a stop condition. | Open the decision now — it needs no copied code and can be prepared against JS-1 rather than waiting on the acceptance gate. |
| [NONE] | **JS-5 — executor, abrupt completion, budgets** | **Not started** | None. No interpreter, no charging model, no measured `CallDepth`. | After JS-4. |
| [NONE] | **JS-6 — the standard library** | **Not started** | None. The satellite-acquisition dependency is unopened and has no named owner. | Open the satellite dependency at JS-0 so JS-6 is not the milestone that discovers it. |
| [NONE] | **JS-7 — suspension** | **Not started** | None. The continuation design needs no copied code and may be opened early. | After JS-5 and JS-6. |
| [NONE] | **JS-8 — guest-initiated loads and the three compositions** | **Not started** | None. No guest-load declaration, no mediator adapter, no composition registers a provider or declines to. | After JS-7. |
| [NONE] | **JS-9 — adversarial input, agents, soak** | **Not started** | None. No corpus, no fuzz target, no soak host, no aggregate-budget exercise. | After JS-8, though the corpus grows from JS-1 onward rather than starting here. |
| [NONE] | **JS-10 — baselines, packaging, support table, release gate** | **Not started** | None. No measurement lane, no baseline register, no package, no support table, no human review decision on anything. | After JS-9, and after a named human has read every relevant unit — which is the largest single-owner task in the programme and must be scheduled, not assumed. |

### What this component is not claiming

Stated positively, because a table of empty rows invites a reader to fill them in:

- **No language is supported.** No feature manifest exists, none is accepted, and a manifest name
  would not be a conformance claim even if one did.
- **No composition is advertised**, none is packable, and no runtime identifier is claimed.
- **No conformance result exists.** The suite is not pinned and the harness is not built.
- **No measurement exists**, and no figure from any other component stands in for one.
- **Nothing is reviewed.** No human has read anything here, and nothing that will be copied
  arrives reviewed.
- **The seed has not been taken.** Section 4.1 of the roadmap records a candidate identity so the
  record has a shape; JS-2 records what was actually taken, and may differ.

---

## 3. Open external dependencies

A milestone blocked by a named external dependency records the blocker, its holder, and its
unblock condition. Two are open today and both belong to JS-2.

| Blocker | Holder | Unblock condition | Note |
|---|---|---|---|
| **The core contract is not accepted.** Every core milestone is in progress and unaccepted, and the core's review record is unsigned. The core roadmap's own seeding conditions require the copy to be adapted to an accepted contract rather than a moving one. | The Broiler.VM core's architecture and release owners | A recorded human review decision on the core's contract surface, at a named contract version | This blocks JS-2 onward. It does **not** block JS-0 or JS-1, which build against the contract as implemented — a distinction the roadmap's delivery order states and this ledger holds it to. |
| **The seed's waited-on set has not been itemised.** Roadmap section 4.2 lists the candidate items and their dispositions; nobody has ruled on them, and no snapshot-as-is date or commit-count budget has been recorded. | This component's architecture owner, at JS-0 | A dated ruling per item, plus a recorded stop condition after which the snapshot is taken as-is | Without the stop condition this is not a dependency, it is an open-ended postponement — which roadmap section 23 makes a named risk. |

One further dependency is **unopened rather than blocked**, and naming it here is the point:
acquisition of the regular-expression matcher and the Unicode and locale data as this checkout's
own dependencies has no named owner. JS-0 opens it. If it has not landed by JS-6, that milestone
excludes every surface needing it and publishes the exclusions, rather than waiting.

---

## 4. Required evidence bundle

Every status claim beyond `Not started` must point to a retained bundle carrying all applicable
fields below. **A command written in a plan is not evidence that the command ran.**

| Field | Required record |
|---|---|
| **Identity** | Milestone and item IDs, roadmap and gate revision, core contract version, format version, feature manifest set, evidence-bundle ID, collection timestamp, owner, and reviewer. |
| **Source** | Component commit, recursive submodule revisions, dirty-tree state and patch identity, and the exact paths and projects under test. |
| **Dependencies and corpus** | Lockfile and package identities, toolchain and SDK versions, corpus and fixture hashes, the pinned conformance suite revision, and applicable provenance or licence decisions. |
| **Environment** | OS, architecture, RID, hardware or lane identity, runtime mode, configuration, JIT/trimming/Native AOT mode, effective environment variables, and resource limits. Secrets redacted without hiding semantically relevant configuration. |
| **Procedure** | Exact commands, working directories, ordered setup, inputs, repetitions and seeds, timeouts, and clean or pristine-consumer conditions. |
| **Results** | Raw outputs retained, including failures. A bundle that retains only the passing half is not a bundle. |
| **Negative controls** | Each control, the injection that must make it fail, and the revert that must make it pass. The count is stated and grows across milestones. |
| **Closure** | For any Native AOT claim: the published output's dependency closure, read off the published image rather than asserted. |
| **Exclusions** | What the bundle does **not** show. Every open gate clause, every unexercised path, every single-machine or single-RID limitation, named. |

---

## 5. Update rules

1. Update this ledger in the same change that accepts, rejects, blocks, supersedes, or materially
   narrows a milestone claim. Preserve earlier evidence links and decisions as dated history.
2. Do not copy a planned exit gate into the evidence column. Link the immutable bundle and state
   what it demonstrated, **including its failures and its exclusions**.
3. Do not infer completion transitively. JS-1 acceptance does not accept JS-2; a slice-manifest
   result does not accept a later manifest; and JIT, trimmed, or one-RID success does not accept
   an untested Native AOT or RID claim.
4. Do not promote seed, shell, smoke, analyzer-only, or shape-only results beyond what they prove.
   A failing or partial bundle is retained but leaves the milestone `In progress` unless a named
   dependency meets the `Blocked` definition.
5. If a gate changes, record the gate revision and re-evaluate existing evidence. Evidence
   gathered against a different population is not silently carried forward. A core contract
   amendment is such a change: record the new version and state, per affected record, what
   recertifies unchanged, what must be re-collected, and what is superseded.
6. **Do not record core work here, and never record profile work in the core's ledger.** A core
   result never advances a row in this file, and no row here advances a row there.
7. A milestone moves to `Accepted` only after its owner and reviewer confirm that every objective
   exit condition for that record is covered. Record the decision date and the evidence-bundle ID
   in the affected row. Where owner and reviewer are the same person, record the
   non-independence in the row rather than resolving it by assertion.
8. **Human review gates a release, not a development step.** Development work — implementing a
   milestone, landing it, collecting its evidence — may proceed and merge without a review
   decision. A **release** may not: no package is published, no RID is claimed, no support table
   is issued, and no milestone moves to `Accepted` until a named human has read the work and
   recorded a decision on every relevant code unit, bound to that declaration's fingerprint so a
   unit that changes afterwards reports stale rather than being silently carried.

   Two consequences are worth stating plainly, because this component will feel them harder than
   a greenfield one would. **Unreviewed work accumulates**, and this component starts with a large
   copied body of it: everything the snapshot brings in is unreviewed here on the day it lands,
   and the review debt is real from the first commit rather than from the first release. And **a
   development step that lands unreviewed carries its risk forward rather than dissolving it** —
   a passing conformance run over an unreviewed parser is a statement about the parser's outputs,
   not about the parser.
9. **A copied unit records its origin.** Every unit taken from the seed is annotated as ported,
   and the origin distribution is published in the generated assurance report. A component whose
   report cannot say how much of it was written here is a component whose review status cannot be
   read.
10. **No count, total, graph, commit, or score is copied into prose.** This ledger names the
    command or the retained record that reads it. A number transcribed into a sentence goes stale
    silently, and a ledger that goes stale silently is worse than one with a gap in it.

---

Until such updates are recorded, section 2 remains the complete status of this component: **JS-0
through JS-10 are not started, no source exists, no snapshot has been taken, no language surface
is supported, no composition is advertised, no runtime identifier is claimed, no measurement or
conformance result exists, and nothing has been reviewed.**
