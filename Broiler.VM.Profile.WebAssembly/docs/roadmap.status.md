# Broiler.VM.Profile.WebAssembly roadmap status

**Last updated:** 2026-08-30

**Authority:** This file is the authoritative current-evidence ledger for the milestones in the
[WebAssembly profile roadmap](roadmap.md). The roadmap defines planned work and objective exit
gates; this ledger records whether those gates have accepted evidence.

**At this snapshot, every milestone WA-0 through WA-10 is `Not started`.** The component has no
source tree, no solution, no project, no descriptor, no decoder, no pinned specification revision,
no pinned suite revision, no evidence bundle, and no assurance record. What exists is the roadmap
and this ledger. No milestone is complete because its design appears in the roadmap, and nothing in
this component may be described as implemented, validated, accepted, supported, or published.

The component also has no repository of its own yet. The two documents are staged in the aggregate
repository beside the components they will sit next to; **WA-0 owns the placement decision** and
moving them is part of it, not a side effect of it.

**This component has no seed.** Nothing is copied, no snapshot is taken, and no fork exists. Every
line will be written here. That removes a whole family of blockers the other intended first profile
carries, and it removes a head start as well; roadmap section 4 states both directions and this
ledger holds the consequence — an origin distribution that is anything other than uniform is a
finding here rather than an expected fact.

---

## 1. Reading this ledger

Four categories must remain distinct, and conflating any two of them is how an unfounded claim gets
recorded:

- **Plan** is proposed scope, sequencing, ownership, or an exit gate in `roadmap.md`. It is not
  implementation evidence and not validation evidence.
- **Observed repository state** is a reviewable fact about the current checkout — for instance that
  this component contains no project file. It can explain a status; it cannot satisfy a future
  implementation, contract, conformance, Native AOT, or release gate.
- **Accepted evidence** is an immutable, reviewable bundle that identifies the exact sources and
  gate, records the executed commands and environment, retains their outputs, and demonstrates every
  part of the objective exit gate. Only accepted evidence may advance a milestone to `Accepted`.
- **Ingested material** is third-party content this component brings into its tree — the conformance
  test suite and the archived specification document. It carries **no status of its own**. A suite
  file is an input, never evidence; the retained *run* over it is the evidence, and the distinction
  is what stops a large corpus in the tree from reading as a large amount of work done.

**Work in other components is not this component's evidence.** In particular, no conformance result,
benchmark, measurement, review decision, or Native AOT sample produced by the Broiler.VM core or by
any other language profile establishes anything here, and no gate in this ledger may cite one. That
rule is what makes a number in this file mean something.

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
| [NONE] | **WA-0 — boundary, placement, identity, assurance floor** | **Not started** | None. No project, no rule register, no assurance manifest, no licence or notice file, no evidence-collection script, and no declared limit vectors. | Take the placement decision with the core's topology owner co-signing, then stand up the shell graph and the architecture rule register with both halves of the no-edge-to-another-profile rule, the no-product-reference-to-the-harness rule, and a negative control for each. **Publish the fifteen hard maxima and fifteen defaults, with the three guest-load maxima explicitly `Unconstrained`** — a dimension declared inapplicable in the budget matrix is a statement about what this profile charges, not a licence to write a zero into a vector that clamps every profile beside it — and add the two-profile catalog test that catches the mistake. Nothing else in this component may land first. |
| [NONE] | **WA-1 — the whole contract loop on a slice module** | **Not started** | None. No descriptor, no decoder, no validator, no executor, no composition root. | After WA-0: mint the slice manifest, define format version 1 as a bare module, and drive one module through verify, instantiate, and invoke in a composition that publishes and runs under Native AOT. The entry-point encoding is decided here or it is decided by accident later; the `StructuralDepth` declaration is no longer open, roadmap section 3 having closed it as `Charged` once the metering surface's retain/release pair was read correctly. |
| [NONE] | **WA-2 — the decoder, the integer decision, the malformed corpus** | **Blocked** (recorded as `Not started` above the blocker, because no work has begun either) | None. No decoder, no corpus, no specification pin. | **Blocked on one named external dependency and one unopened human action.** See section 3. |
| [NONE] | **WA-3 — validation and the diagnostic registry** | **Not started** | None. No validator, no diagnostic registry, no invalid-module corpus. | After WA-2: implement the specification's single-pass algorithm, publish the registry bound in both directions, and prove malformed-before-invalid with a case that fails when the phases are fused. |
| [NONE] | **WA-4 — the oracle** | **Not started** | None. No suite pin, no script reader, no harness, no self-check fixture. | After WA-3, and in parallel with WA-5. This is the milestone whose value is lost by serialising it: the malformed and invalid families can be scored before any interpreter exists, and that is the main structural advantage this profile has. |
| [NONE] | **WA-5 — value model, store, interpreter** | **Not started** | None. The value and frame ABI decision is **open**, and roadmap section 9 makes it a gate on entry rather than this milestone's first task. A second decision is now open beside it: whether a `LiveBytes` aggregate breach may terminate an operation, given that a retained-state dimension cannot carry a guest-observable refusal and section 12 requires a refused `memory.grow` to be exactly that. | Open both decisions now — neither needs code, both can be prepared against WA-1 rather than waiting on the acceptance gate, and the ABI's vector-width row is the one whose late answer invalidates the others. |
| [NONE] | **WA-6 — linking, host imports, the store decision** | **Not started** | None. The store reading of roadmap section 11 is **open**, with three candidates and one already rejected. | Open the store decision now — it needs no code either — and cost the naming channel for the runtime-scoped reading, because that is the part with no contract member behind it. |
| [NONE] | **WA-7 — `core1` complete and the embedding seam** | **Not started** | None. No manifest is minted, no seam exists. | After WA-6. |
| [NONE] | **WA-8 — the second standardised group and the vector family** | **Not started** | None. No manifest beyond the slice is planned to exist before this point. | After WA-7. The extraction-gate verdict of roadmap section 25 is recorded here, because this is the first point at which a second validator exists to compare. |
| [NONE] | **WA-9 — adversarial input, aggregate budgets, soak** | **Not started** | None. No fuzz target, no soak host, no aggregate-budget exercise. | After WA-8, though the malformed corpus grows from WA-1 onward rather than starting here. |
| [NONE] | **WA-10 — baselines, packaging, support table, release gate** | **Not started** | None. No measurement lane, no baseline register, no package, no support table, no human review decision on anything. | After WA-9, and after a named human has read every relevant unit — which is the largest single-owner task in the programme and must be scheduled, not assumed. |

### What this component is not claiming

Stated positively, because a table of empty rows invites a reader to fill them in:

- **No WebAssembly is supported.** No feature manifest exists, none is accepted, and a specification
  version name would not be a conformance claim even if one did.
- **No composition is advertised**, none is packable, and no runtime identifier is claimed.
- **No conformance result exists.** Neither the specification nor the suite is pinned, and the
  harness is not built. No family total exists, and no aggregate percentage will ever be published
  in place of one.
- **No measurement exists**, and no figure from any other component or engine stands in for one.
- **Nothing is reviewed.** No human has read anything here.
- **The deterministic-profile position is a plan, not a demonstration.** Roadmap section 6 states
  that this component implements `DET`; no fixture asserts it, because no fixture exists.
- **The JavaScript API for WebAssembly is not provided and is not planned here.** Roadmap section 17
  prices the boundary and names it as belonging to whichever component composes two profiles. A
  reader who sees this profile in a browser image must not infer that a page can call
  `WebAssembly.instantiate`.

---

## 3. Open external dependencies

A milestone blocked by a named external dependency records the blocker, its holder, and its unblock
condition. One is open today and it belongs to WA-2.

| Blocker | Holder | Unblock condition | Note |
|---|---|---|---|
| **The core contract is not accepted.** Every core milestone is in progress and unaccepted, and the core's review record is unsigned. The core's own ledger records that a profile roadmap may open once the contract is accepted, and that it is implemented but not accepted. | The Broiler.VM core's architecture and release owners | A recorded human review decision on the core's contract surface, at a named contract version | This blocks WA-2 onward. It does **not** block WA-0 or WA-1, which build against the contract as implemented — a distinction the roadmap's delivery order states and this ledger holds it to. |

Three further dependencies are **unopened rather than blocked**, and naming them here is the point.

| Unopened dependency | Why it is not a blocker yet | What opens it |
|---|---|---|
| **The specification revision has not been retrieved, hashed, or archived.** Retrieving and archiving a third-party document is a human action, not a build step. Until it is performed, every reference in the roadmap is a discovery link and the pin is provisional. | Nobody has been asked to do it, which is a scheduling gap and not a dependency. | WA-0 records the intended revision and names an owner; WA-2's gate requires the pin actually taken, or a named exclusion. |
| **The conformance suite revision has not been pinned**, and the licence and attribution consequences of ingesting it into this tree have not been confirmed. | Same. | WA-0 publishes the licence and notice and confirms or amends the core's standing third-party claim, with the release owner co-signing. WA-4 resolves the commit. |
| **The cross-profile boundary of roadmap section 17 has no owner.** A browser that runs WebAssembly through JavaScript needs a component that composes two profiles, and none exists or is planned. Roadmap section 17 now also records the two frozen facts that shape it — a guest-initiated load may not name another profile, and cross-runtime reentry is legal and is the route the seam takes — so the price is written down even though nobody is paying it. | It is outside this component by construction, and this component's obligation is to price it rather than to pay it. | A browser-integration component, whenever one is opened. That component owns the two-profile composition's closure report, its Native AOT evidence, its shared aggregate budget, and the reconciliation of two profiles' maxima. Until it exists, WA-0's hard-maxima record states the clamping consequence, which is the half this component can discharge alone. |

---

## 4. Required evidence bundle

Every status claim beyond `Not started` must point to a retained bundle carrying all applicable
fields below. **A command written in a plan is not evidence that the command ran.**

| Field | Required record |
|---|---|
| **Identity** | Milestone and item IDs, roadmap and gate revision, core contract version, format version, feature manifest set, evidence-bundle ID, collection timestamp, owner, and reviewer. |
| **Source** | Component commit, dirty-tree state and patch identity, and the exact paths and projects under test. |
| **Pins** | The specification revision with its hash and the human action that archived it; the conformance suite commit; the scope manifests binding manifests to suite paths. A provisional pin is recorded as provisional. |
| **Dependencies and corpus** | Lockfile and package identities, toolchain and SDK versions, corpus and fixture hashes, and applicable provenance or licence decisions. |
| **Environment** | OS, architecture, RID, hardware or lane identity, runtime mode, configuration, JIT/trimming/Native AOT mode, effective environment variables, and resource limits. Secrets redacted without hiding semantically relevant configuration. |
| **Effective limits** | The effective limit vector every conformance and measurement run executed under. A conformance total obtained under generous ceilings is not the total a product shipping tight ones would get, and a bundle that omits the vector cannot be compared with any other. |
| **Procedure** | Exact commands, working directories, ordered setup, inputs, repetitions and seeds, timeouts, and clean or pristine-consumer conditions. |
| **Results** | Raw outputs retained, including failures, and conformance results reported **per assertion family**. A bundle that retains only the passing half is not a bundle, and a bundle that reports one percentage is not a conformance result. |
| **Negative controls** | Each control, the injection that must make it fail, and the revert that must make it pass. The count is stated and grows across milestones. |
| **Closure** | For any Native AOT claim: the published output's dependency closure, read off the published image rather than asserted, with the absence of the ingestion path asserted explicitly. |
| **Exclusions** | What the bundle does **not** show. Every open gate clause, every unexercised path, every single-machine or single-RID limitation, named. |

---

## 5. Update rules

1. Update this ledger in the same change that accepts, rejects, blocks, supersedes, or materially
   narrows a milestone claim. Preserve earlier evidence links and decisions as dated history.
2. Do not copy a planned exit gate into the evidence column. Link the immutable bundle and state
   what it demonstrated, **including its failures and its exclusions**.
3. Do not infer completion transitively. WA-1 acceptance does not accept WA-2; a slice-manifest
   result does not accept a later manifest; a strong result in one assertion family does not accept
   another; and JIT, trimmed, or one-RID success does not accept an untested Native AOT or RID
   claim.
4. Do not promote shell, smoke, analyzer-only, or shape-only results beyond what they prove. A
   failing or partial bundle is retained but leaves the milestone `In progress` unless a named
   dependency meets the `Blocked` definition.
5. If a gate changes, record the gate revision and re-evaluate existing evidence. Evidence gathered
   against a different population is not silently carried forward. **A specification or suite
   re-pin is such a change**, and so is a core contract amendment: record the new revision and
   state, per affected record, what recertifies unchanged, what must be re-collected, and what is
   superseded. A conformance total is bound to the suite revision that produced it and to nothing
   else.
6. **Do not record core work here, and never record profile work in the core's ledger.** A core
   result never advances a row in this file, and no row here advances a row there. The same rule
   holds in both directions for every other language profile: this component has no dependency edge
   to one and takes no evidence from one.
7. A milestone moves to `Accepted` only after its owner and reviewer confirm that every objective
   exit condition for that record is covered. Record the decision date and the evidence-bundle ID in
   the affected row. Where owner and reviewer are the same person, record the non-independence in
   the row rather than resolving it by assertion.
8. **Human review gates a release, not a development step.** Development work — implementing a
   milestone, landing it, collecting its evidence — may proceed and merge without a review decision.
   A **release** may not: no package is published, no RID is claimed, no support table is issued,
   and no milestone moves to `Accepted` until a named human has read the work and recorded a
   decision on every relevant code unit, bound to that declaration's fingerprint so a unit that
   changes afterwards reports stale rather than being silently carried.

   One consequence is worth stating plainly, and it is the opposite of the one a seeded component
   feels. **This component's review debt starts at zero and grows only as fast as the work does.**
   There is no inherited body of unreviewed code arriving on day one. That makes the review queue
   trackable from the first commit, and it removes the excuse a large inherited backlog would
   supply.
9. **A unit's origin is recorded, and the expected distribution is uniform.** Every unit in this
   component is written here. The generated assurance report publishes the origin distribution, and
   **a unit whose origin is anything else is a finding** — either an undocumented copy, which the
   licence position must then cover, or a mis-annotation. A seeded component publishes this
   distribution to show how much it inherited; this one publishes it to show that it inherited
   nothing.
10. **No count, total, graph, commit, or score is copied into prose.** This ledger names the command
    or the retained record that reads it. That rule extends to the specification: instruction
    counts, opcode counts, and section counts are read off the pinned revision's own indexes and are
    not transcribed into this component's documents. A number transcribed into a sentence goes stale
    silently, and a ledger that goes stale silently is worse than one with a gap in it.

---

Until such updates are recorded, section 2 remains the complete status of this component: **WA-0
through WA-10 are not started, no source exists, neither the specification nor the conformance suite
is pinned, no language surface is supported, no composition is advertised, no runtime identifier is
claimed, no measurement or conformance result exists, and nothing has been reviewed.**
