# Broiler.VM.Profile.JavaScript roadmap

**Status:** Proposed component roadmap for the JavaScript language profile of the Broiler.VM
execution core. [The evidence ledger](roadmap.status.md) is the authority for what has been
accepted; at the time of writing it records **JS-0 through JS-10 as not started**, and it records
that the component has no source tree, no snapshot, no descriptor, and no evidence bundle. No
milestone is complete because its design appears here.

`Broiler.VM.Profile.JavaScript` is a **language profile**: one bytecode format, one verifier, one
value and frame model, one executor, one set of host imports, and one conformance suite, compiled
into a product by a composition root that names its descriptor directly. It is not an execution
core and owns none of the mechanism the core owns. It references exactly two core assemblies and
nothing else Broiler-owned, and no core milestone waits for it.

The component does not start empty and this roadmap does not pretend otherwise. It starts as a
**snapshot copy** of a large existing JavaScript engine whose execution arm emits IL at run time,
hosted on a core that forbids dynamic code in any product closure.
[Section 4](#4-the-seed-what-is-copied-what-is-rewritten-what-is-written-fresh) states what that
copy actually costs, milestone by milestone, and [section 19](roadmap.delivery.md#19-milestones)
sequences the work so the adaptation is de-risked early rather than discovered late.

Two properties of this document are load-bearing and stated once here. **It links to no document,
path, result, or item identifier in the legacy engine component**, in either direction: the copy
is a fork, and a roadmap that cited its origin's plans would have re-created the dependency the
fork exists to avoid. And **no figure, total, conformance result, benchmark, or Native AOT sample
from any other component appears anywhere in it**. Every number this component publishes will be
its own, from its own lane, at its own commit.

---

### How this roadmap is split

This plan is three files and one ledger. The argument stays whole in this file; the
milestones and the gate material have their own, because they are read one entry at a time
rather than start to finish.

| File | Sections | What it carries |
|---|---|---|
| `roadmap.md` — this file | 1–16, 18 | The argument: what this profile is, what the core gives it and refuses it, how each piece works, and what it will ask of the core. |
| [`roadmap.delivery.md`](roadmap.delivery.md) | 19–20 | The milestones and the order they are delivered in. |
| [`roadmap.gates.md`](roadmap.gates.md) | 17, 21–24 | The measurement rules, the test and evidence matrix, the release gates, the stop conditions, and the references. |
| [`roadmap.status.md`](roadmap.status.md) | — | The evidence ledger. It, and not any file above, is the authority for what has been accepted. |

Two rules keep the split cheap and are not negotiable. **Section numbers are global and do
not change when a section moves**, so every reference written before the split still resolves
and the gates file holding 17 before 21 is intentional rather than an error. And **milestone
identifiers are never written as links**: `JS-0` through `JS-10` are the join key between this
plan and the ledger, and they stay bare.

### Contents

1. [Terminology and support claims](#1-terminology-and-support-claims)
2. [Engineering invariants](#2-engineering-invariants)
3. [What the core already gives this profile, and what it refuses](#3-what-the-core-already-gives-this-profile-and-what-it-refuses)
4. [The seed: what is copied, what is rewritten, what is written fresh](#4-the-seed-what-is-copied-what-is-rewritten-what-is-written-fresh)
5. [Package boundaries and the dependency graph](#5-package-boundaries-and-the-dependency-graph)
6. [Feature manifests: how the language surface is admitted](#6-feature-manifests-how-the-language-surface-is-admitted)
7. [The bytecode format and the verifier](#7-the-bytecode-format-and-the-verifier)
8. [The value, frame, and call model](#8-the-value-frame-and-call-model)
9. [The semantic front end and lowering](#9-the-semantic-front-end-and-lowering)
10. [Execution: mapping JavaScript onto the core lifecycle](#10-execution-mapping-javascript-onto-the-core-lifecycle)
11. [Guest-initiated loads: `eval`, the `Function` constructor, dynamic `import()`, modules](#11-guest-initiated-loads-eval-the-function-constructor-dynamic-import-modules)
12. [Suspension: generators, async functions, and top-level await](#12-suspension-generators-async-functions-and-top-level-await)
13. [Realms, agents, and the host boundary](#13-realms-agents-and-the-host-boundary)
14. [The conformance oracle](#14-the-conformance-oracle)
15. [Deployment compositions, Native AOT, and the browser embedding](#15-deployment-compositions-native-aot-and-the-browser-embedding)
16. [Persistence and the code cache](#16-persistence-and-the-code-cache)
17. [Measurement discipline](roadmap.gates.md#17-measurement-discipline) · `roadmap.gates.md`
18. [Amendments this profile expects to ask of the core](#18-amendments-this-profile-expects-to-ask-of-the-core)
19. [Milestones](roadmap.delivery.md#19-milestones) · `roadmap.delivery.md`
20. [Delivery order](roadmap.delivery.md#20-delivery-order) · `roadmap.delivery.md`
21. [Test and evidence matrix](roadmap.gates.md#21-test-and-evidence-matrix) · `roadmap.gates.md`
22. [Release gates](roadmap.gates.md#22-release-gates) · `roadmap.gates.md`
23. [Risks and stop conditions](roadmap.gates.md#23-risks-and-stop-conditions) · `roadmap.gates.md`
24. [Specification and platform references](roadmap.gates.md#24-specification-and-platform-references) · `roadmap.gates.md`

---

## 1. Terminology and support claims

The core fixes most of this vocabulary and this roadmap uses it unchanged. The rows below are the
terms this component adds or narrows; where a term is the core's, that is said.

| Term | Meaning in this roadmap |
|---|---|
| **This profile** | `Broiler.VM.Profile.JavaScript`. One profile ID, one descriptor, one verifier, one executor factory, one payload-kind range. |
| **The core** | The Broiler.VM execution core: its three packable assemblies and the numbered core contract version they carry. Core-owned terms — verified artifact, verified handle, guest-initiated load, artifact-provider capability, external suspension, deployment composition, feature manifest, core contract version, operation-result envelope — keep their core meanings. |
| **The seed** | The named snapshot copy of the legacy JavaScript engine component from which this component's front end, object model, and standard library are taken. A fork with its own history and no dependency edge in either direction. [Section 4](#4-the-seed-what-is-copied-what-is-rewritten-what-is-written-fresh) is its whole treatment. |
| **Feature manifest** | The core's term, with this profile's content: the exact JavaScript surface accepted by one version of this profile, minted as a `VmFeatureManifestId` under this profile's own ID. **A profile name alone is never a conformance claim**, and neither is a manifest name; a manifest claims only what its own retained oracle run shows. |
| **Manifest increment** | One further feature-manifest identity with a reviewed scope, its own corpus extension, and its own oracle run. An increment is not a milestone and closes none. |
| **The format** | This profile's bytecode: magic, format version, section framing, constant pool, code, exception regions, and position tables. Versioned from the first byte, independently of the core contract version, the package version, and any feature manifest. |
| **The lowering** | Source-to-bytecode translation. It is a **sibling** of the executor, not a part of it: a composition that executes precompiled artifacts contains a format, a verifier, and an interpreter and no lowering at all. |
| **Deployment composition** | The core's term. This component uses exactly three labels and mints no fourth: `execution-only`, `narrow-runtime-compiler`, `general-runtime-compiler`. They describe **when source is compiled, not how much of the language is supported.** |
| **The oracle** | An external conformance suite pinned at an immutable revision, run by this component's own harness, whose self-check proves that a failing test comes back as a failure before any shard is scored. |
| **The ratchet** | The first accepted per-host-mode totals for a manifest. No later run of that manifest may regress against it. |

A release of this profile claims this profile: its accepted feature-manifest set, its accepted
format-version range, the core contract version it is built against, the compositions it
publishes and runs, and its deterministic exclusions. It claims no language surface a manifest
does not name and no capability a composition does not contain. An unknown feature, an
unsupported manifest, or an out-of-range format version is a deterministic load failure, never a
best-effort partial execution.

### Scope

This profile owns:

- its bytecode payload format, its format-version range, and its feature manifests;
- decoding, structural validation, control-flow and stack-consistency validation, static
  semantics, and every profile-specific resource check, all of it inside the one verification
  entry point the core provides;
- its value, frame, environment, call, construct, completion-record, exception, and suspension
  model;
- the language meaning of every guest-initiated load it declares — `eval`, the `Function`
  constructor, dynamic `import()`, and module-graph dependencies — including specifier
  resolution requests, linking, lexical context, and evaluation ordering;
- its typed normal-result and fault payloads and the projection accessors that expose them
  without adding a case to any core result enum;
- its host imports: their capability IDs, versions, signature IDs, kinds, reentrancy, thread
  affinity, and exception-translation modes;
- its standard library, its realm model, and the agent model it exposes over the core's shared
  aggregate budgets;
- its conformance harness, its pinned suite revision, its scope manifests, its failure manifest,
  and its own regression suite for that machinery;
- its own overhead measurements, its own baseline register, and the honest limits on both; and
- its packages, its compositions, its support table, and its assurance and human-review records.

The core owns, and this profile never re-implements: profile selection and the immutable catalog;
bounded binary reading, checked arithmetic, variable-length integers, section framing, and
allocation guards; the verified-artifact handle, its identity, its leases, and its lifetime; the
limit-precedence algorithm across host ceilings, profile maxima, and artifact requests; the
fifteen budget dimensions and their metering; the lifecycle state machine, thread affinity,
reentrancy, cancellation, and idempotent disposal; guest-initiated-load mediation and its bounds;
external suspension; the profile-neutral operation-result envelopes; and the composition,
trimming, and Native AOT gates for the core boundary.

### Non-goals

- **A second execution arm.** This profile has one executor. It emits no IL, builds no expression
  tree, compiles no delegate, and contains no tiering path into dynamic code. There is no
  bytecode-to-IL promotion, no deoptimization from a compiled tier, and no on-stack replacement,
  because there is no second tier for any of them to reach. A product closure containing an IL
  emitter is a release blocker, not a configuration.
- **A second verifier.** Whatever validates an artifact is this profile's verifier, reached
  through the core's one verification entry point. A build-time reimplementation that is merely
  supposed to agree with it is a security defect with a schedule attached.
- **A second lowering.** Where a composition compiles at run time and a later one compiles ahead
  of time, both use the same lowering assembly. The composition decides which is present.
- **A security sandbox claim.** Verification, bounded budgets, and a typed host boundary are
  correctness properties of this profile. They are not an isolation claim for untrusted script,
  and no conformance total or benchmark result may be presented as one.
- **CLR interop.** No JavaScript-reachable surface resolves a CLR type by name, constructs a
  generic type at run time, or enumerates CLR members. A host reaches guest code through typed,
  allowlisted, versioned capabilities and through nothing else.
- **A debug wire protocol.** External suspension is a core lifecycle state; what a paused profile
  exposes is this profile's own surface, and a wire protocol is a separate component if it is
  ever wanted.
- **Filesystem, network, or module-map ownership.** The host owns identity resolution, transport,
  content policy, integrity checks, the module map, and the event loop. This profile asks; it
  never fetches.
- **A change to the core.** A JavaScript requirement that the frozen contract cannot express is an
  amendment proposal or a recorded refusal
  ([section 18](#18-amendments-this-profile-expects-to-ask-of-the-core)). It is never a
  language-specific path added to the core's execution loop, and never a second core state
  machine.
- **Any performance claim about another engine.** This profile publishes its own overhead against
  its own controls. Fuel figures are not comparable across profiles and are never presented as if
  they were.

---

## 2. Engineering invariants

1. **Nothing runs that verification did not admit.** Every byte this profile executes came out of
   the core's verification entry point as an immutable, profile-bound handle. Bytes acquired
   while executing take the same path before anything in them runs.
2. **Verification is total.** The verifier answers; it does not throw. Every rejection is one of
   the five verifier outcomes the core admits, carrying this profile's own diagnostic code and
   source position. An exception escaping the verifier is a contract violation, not a rejection.
3. **A structural check happens at verification or it does not happen.** No structural, index,
   stack-consistency, or handler-nesting rule migrates into first execution because a lazily
   compiling engine finds it convenient. A late check reported as a language fault makes a
   malformed artifact indistinguishable from a program that threw, and hollows out the corpus
   that is supposed to prove the boundary.
4. **The executor answers in the core's vocabulary and no other.** Every step is one of the five
   execution-step kinds. Language outcomes are typed payloads this profile owns; no profile code
   names a core outcome category, and adding a language feature never adds a core result case.
5. **No exception escapes into the core.** Every internal failure is caught at this profile's own
   adapter and converted. An escaped exception is a defect of this component even when the core
   survives it.
6. **Guest-controlled cost is charged proportionally.** An operation whose work grows with its
   input charges fuel as a declared monotone function of that input, at the declared granularity,
   with a retained fixture and an unsimplified control. A flat charge on a superlinear operation
   means a bounded budget bounds nothing.
7. **Mutable optimization state has an owner and is never reachable from a shared handle.**
   Property shapes, inline-cache slots, feedback, interned key identities, and warmed structures
   belong to a realm, a program, a function, or a runtime. Two runtimes sharing one verified
   handle share nothing mutable, and nothing process-global keys them together.
8. **The language surface grows only in reviewed increments.** Each increment mints one feature
   manifest, extends the retained corpus, and re-runs the oracle against the ratchet. No
   increment is justified by claiming an earlier manifest implies it.
9. **Unsupported surface is truthful.** Every capability a composition or manifest excludes has a
   named deterministic failure that the support table publishes. A shape-only stub does not
   satisfy a capability gate, and a composition label is not a language claim.
10. **Native AOT is demonstrated, not inferred.** Analyzer cleanliness and a trimmed build are
    inputs. Each claimed composition publishes **and runs** its workload on every declared RID
    with trim and AOT warnings treated as errors, and its published closure is read off the
    published output.
11. **The fork is one-way and mechanical.** No project reference, package reference, or shared
    source item runs between this component and any legacy Broiler component in either direction,
    and an architecture rule with a passing witness enforces it. Fixes do not flow across the
    fork after the snapshot, and neither side is the other's upstream.
12. **No evidence transfers.** No conformance result, benchmark, measurement, review decision, or
    Native AOT sample produced by any other component is this component's evidence, and no gate
    here may cite one. Every claim starts at zero.
13. **The component is provable at every milestone.** Each milestone closes against something a
    reader can re-run: a corpus with recorded expected answers, a publish-and-run log with a
    closure report, a negative control that fails when injected and passes after revert. A gate
    that can only be closed by reading a document is a gate-design defect.

---

## 3. What the core already gives this profile, and what it refuses

The core is implemented, not paper. This section records what a profile author actually finds
there, so this roadmap plans against code rather than against prose. Nothing in it is a claim that
the core is accepted: every core milestone is in progress and unaccepted, its review record is
unsigned, and [section 19](roadmap.delivery.md#19-milestones) carries that as a dependency rather
than assuming it away.

### The seven types this profile implements

| Type | What this profile owes it |
|---|---|
| `IVmProfileVerifier` | `Verify` over a descriptor, a payload span, a verification context, and a token, returning a `VmVerifierOutcome`. Plus three version integers — the authored core contract version, the built-against core contract version, and this profile's own verifier semantic version — and its profile ID. |
| `IVmProfileExecutor` | `Instantiate`, `Invoke`, and `Resume`, each returning a `VmExecutionStep` and each taking the operation's cancellation token; and `Unwind`, which **returns nothing** and takes a continuation plus one effective unwind allowance the core has already reduced to the tighter of the descriptor's abandon budget and the runtime's unwind budget — no token, no result. Plus its profile ID. One executor instance per runtime, created by the descriptor's factory from an `IVmExecutionEnvironment`. |
| `IVmVerifiedState` | The immutable decoded program a successful verification produces. Opaque to the core; the whole of what execution may read. |
| `IVmInstanceState` | The mutable per-instance state instantiation produces. Realms, environments, heaps, and caches live behind it. |
| `IVmProfileContinuation` | A captured, resumable suspension. Single-use, runtime-owned. |
| `IVmProfilePayload` | Every value crossing back to the caller: normal results, language faults, suspension projections. Carries a `VmPayloadIdentity` whose kind IDs must lie inside the descriptor's declared range. |
| `IVmBoundedAllocationMeter` | The adapter that lets the core's bounded allocator charge this profile's allocations, because the core's own meter type is not public. Writing it is this profile's work, not the core's. |

The five verifier outcomes are `Verified`, `InvalidArtifact`, `ResourceExhaustion`,
`Cancellation`, and `UnsupportedProfile`. The five execution-step kinds are `Completed`,
`Instantiated`, `Suspended`, `Faulted`, and `ContractViolation`. There are no others, and this
profile's whole answer space is those two closed sets.

### The descriptor is a contract, not a registration form

One full-arity construction supplies every row: identity and display name, descriptor revision,
supported format-version range, accepted feature manifests, the verifier instance, the executor
factory, artifact representation kind, artifact lifetime kind, concurrent-verification support,
thread affinity, cancellation poll bound, abandon budget, a fifteen-element default limit vector,
a fifteen-element profile hard-maximum vector, a fifteen-row budget declaration matrix, host
capability imports, the guest-load declaration, the asynchronous-instantiation declaration, the
external-suspension declaration, the payload kind-ID range, the authored and built-against core
contract versions, the conformance manifest identity and version, the diagnostics identity, the
package identity, the fault-recovery mode, the maximum uncharged work, the charging granularity,
and the artifact sharing mode.

The catalog validates it and refuses with a named reason from a closed set — among them
`ProfileIdReservedNamespace`, `FeatureManifestIdOutOfNamespace`,
`BudgetDeclarationMatrixIncomplete`, `VerifierWorkNotApplicable`, `PayloadKindIdRangeInvalid`,
`ProfileDefaultExceedsProfileMaximum`, `GuestLoadDeclarationIncomplete`,
`GuestLoadMaximumUnbounded`, `VerifierWorkToFuelRateInvalid`, `MaxUnchargedWorkInvalid`, and
`ChargingGranularityInvalid`. Each one this profile's descriptor can provoke gets a named negative
case; a refusal reported with the wrong reason is a defect.

Two identity rules bind this component's names before any code exists. A profile ID is two to
eight dot-separated ASCII labels, and the first label `broiler` is reserved and paired with a
Broiler package identity — so `broiler.javascript` is legitimate for this component and obliges
a `Broiler.*` package ID. A feature manifest ID must begin with its own profile's ID followed by
a dot and at least one further label, which makes `broiler.javascript.<surface>` the shape of
every manifest this component ever mints.

### The fifteen budget dimensions

`Fuel`, `WallClock`, `AllocatedBytes`, `LiveBytes`, `HostCalls`, `CallDepth`, `VerifierWork`,
`ArtifactBytes`, `SectionCount`, `DeclaredCount`, `StructuralDepth`, `NestedLoadDepth`,
`NestedLoadFanOut`, `NestedLoadBytes`, `LiveRuntimes`. The declaration matrix has no default row:
a dimension this profile does not charge says `NotApplicable` and the catalog checks that answer
against the structural consequences of the rest of the descriptor.

The intended matrix, which JS-1 fixes and later milestones may correct with a dated record but may
not drift. It is stated here rather than deferred to the first descriptor, because a matrix nobody
wrote down is a matrix assembled from whatever made the catalog stop refusing:

| Dimension | Intended | What charges it |
|---|---|---|
| `Fuel` | Charged | Every instruction dispatched, plus the proportional families of invariant 6. |
| `WallClock` | Charged | Core-metered against the operation; this profile polls often enough for it to bite. |
| `AllocatedBytes` | Charged | Verification buffers, the constant pool, environments, objects, and the storage layer's transitions. |
| `LiveBytes` | Charged | Retained realm and heap state, reported on retention and released on instance disposal. |
| `HostCalls` | Charged | Every call into an imported host capability, including every artifact-provider request. |
| `CallDepth` | Charged | Every interpreter frame. The default is measured, not chosen — see [section 8](#8-the-value-frame-and-call-model). |
| `VerifierWork` | Charged | Required by the catalog. Decode, structural validation, and the static-semantic stage. |
| `ArtifactBytes` | Charged | Enforced by the core's reader over the payload. |
| `SectionCount` | Charged | This format's sections are literal and framed, so the dimension has a direct referent. |
| `DeclaredCount` | Charged | Constant-pool, code, exception-region, and position-table counts, through the core's guarded count reader. |
| `StructuralDepth` | Charged | Section and exception-region nesting inside one artifact. It is a **ceiling-class** dimension, so it is charged on entry and **released on exit** rather than accumulated — see below. |
| `NestedLoadDepth` | Charged | Declared, because this profile declares guest-initiated loads. [Section 11](#11-guest-initiated-loads-eval-the-function-constructor-dynamic-import-modules) records what the contract makes reachable. |
| `NestedLoadFanOut` | Charged | Every admitted mediator request. This is the counter `eval` chains actually consume. |
| `NestedLoadBytes` | Charged | Provider-returned bytes for one operation. |
| `LiveRuntimes` | Charged | Core-metered; this profile adds nothing beyond the agent model of [section 13](#13-realms-agents-and-the-host-boundary). |

**Allowances accumulate; ceilings occupy, and the two use different members.** Seven of the
fifteen are allowance-class and are consumed monotonically with no refund. The other eight are
ceiling-class: they bound a live measure, they are reported as retained and released, and only a
ceiling-class dimension releases. A depth counter is therefore an ordinary, refusable,
high-water-mark bound today — charge on entry, release on exit — and this profile does not need an
amendment to express one. The single caution the core's own surface carries is that the retention
report returns nothing: a refusal on a ceiling-class dimension is latched and surfaces at the
operation's next charge or poll, so **a ceiling-class dimension cannot carry a guest-observable
refusal**. **Gating on a charge instead does not recover one, and this roadmap checked the shipped
core rather than assuming it:** a refused `TryCharge` at any scope latches exhaustion on the meter,
and the core rewrites the completed step as `ResourceExhaustion` whatever the profile does with the
`false` it was handed. So there is no guest-observable budget refusal on the current contract at
all. This profile has no construct that needs one today — a JavaScript allocation failure is a
host-level condition, not a value the language reads back — but it must not design one on the
assumption that a charge can be refused politely, and the other intended profile has met exactly
that wall.

**A maximum is a statement about this profile; a default is a statement about its neighbours.** A
profile hard maximum is **not** a statement of what this profile uses; the defaults are that. The
maximum is the most this profile would tolerate a host granting, and it binds **this profile's own
artifacts and nobody else's** — verification intersects the host's ceiling with the maxima of the
profile the artifact names, and with no other profile's.

**The default is the declaration that reaches other profiles.** A host that adopts profile defaults
rather than stating numbers gets the *tightest default in the catalog*, per dimension, because at
runtime creation no profile has been selected and there is no other safe answer. So a stingy default
on a dimension this profile barely uses is what constrains a profile composed beside it, and a host
that wants more states an explicit ceiling.

*Corrected 2026-08-31, and the correction is worth reading rather than skipping.* Until then the core
also clamped every runtime ceiling to the tightest **maximum** in the catalog, one step earlier, and
this section planned around it at length: this profile is obliged to publish finite guest-load maxima,
a neighbour declaring those dimensions inapplicable could write zero into them, and `eval` would fail
with a resource exhaustion naming a dimension the other profile never used — in a verifier that had
done nothing wrong. **That clamp was a defect and has been removed**; the core's own record always
placed a profile maximum at verification, against the selected profile. The exposure is gone with it,
and so is the obligation to publish an unconstrained maximum on a dimension declared inapplicable.

What survives is the smaller, real version of the same hazard, moved one column across: **a
neighbour's zero *default* still reaches this profile** wherever the host adopts rather than
states. JS-0 declares this profile's defaults with that in mind, and
[section 15](#15-deployment-compositions-native-aot-and-the-browser-embedding) records that
reconciling two profiles' declarations belongs to whichever component composes both.

### What the core refuses to do for this profile

- It stores no values, inspects no frames, and knows no opcode. There is no shared value ABI to
  reach for and none is coming.
- It discovers nothing. No assembly load, no type lookup by name, no scan, no activator, no
  module-initializer ordering. A composition root names this profile's descriptor directly or the
  profile is not in the image.
- It provides no argument channel. An invocation request carries one UTF-8 entry-point name and
  nothing else. [Section 10](#10-execution-mapping-javascript-onto-the-core-lifecycle) records
  what this profile does about that, and
  [section 18](#18-amendments-this-profile-expects-to-ask-of-the-core) records whether it becomes
  an amendment proposal.
- It gives an executing profile no way to *instantiate* through the core the handle a
  guest-initiated load returns. What it does give is the handle's own verified state, which is
  this profile's object — and that, not a nested core instantiation, is how `eval` runs. Section
  11 works this through, because it is the single most consequential contract reading in this
  document.
- It offers no persisted envelope. Bounded outer-envelope parsing is admitted by the contract and
  implemented by no core milestone, so [section 16](#16-persistence-and-the-code-cache) plans a
  code cache that does not exist yet and gates it accordingly.
- **It admits exactly one verification input form, and that is settled rather than open.** There
  is no compile-directly-to-verified-handle path and no lazy per-section verification: the byte
  round trip is mandatory, and verification is whole-artifact and eager, so a handle means the
  whole artifact was verified. Every byte this profile executes — including every byte it lowers
  in its own process, on a browser's critical path — is serialized and re-decoded through the one
  verification entry point. Each is reopened only by a numbered amendment, and
  [section 18](#18-amendments-this-profile-expects-to-ask-of-the-core) carries both with their
  counterweights.
- It will not learn this profile's semantics. A requirement that cannot be expressed through the
  profile-facing checklist is an amendment or a refusal, never a special case.

---

## 4. The seed: what is copied, what is rewritten, what is written fresh

The core roadmap fixes four conditions on this copy, and they are conditions on *this* document:
the snapshot is a named commit recorded here; the copy is a fork with its own history and no
dependency edge in either direction; fixes do not flow across the fork afterwards and neither
side is the other's upstream; and because the seed is a large existing codebase rather than a
greenfield interpreter, the core's profile-facing contract must be reachable by code that was not
written for it.

This section satisfies them in full, and adds the two things they do not settle: what the copy
actually contains, and what waiting longer buys.

### 4.1 The snapshot identity

**A snapshot identity is not one commit.** The seed component has three nested submodules whose
revisions its build depends on, so the record is recursive and a second checkout must be able to
re-derive the same tree from it:

| Field | Recorded value |
|---|---|
| Seed component commit | `0341e5c98553b43569217aa7a30c8a01a1eada0c` (branch `main`, 2026-08-27) |
| Nested submodule | `d0c036783bdeeedaeb657a69bea6e2d5f5d438e9` — extended date-time |
| Nested submodule | `4df3fb8e005d9688921c235ccc44e2e89746180e` — regular-expression engine |
| Nested submodule | `151799bb010bd8c882e07bace636ed12197c3410` — Unicode and locale data |
| Resolved package graph | Recorded at snapshot time, with the lockfile identity |
| SDK and runtime | Recorded at snapshot time |
| Working tree | Clean, asserted, or a retained patch identity |

That row set is the **candidate** identity, not the taken one. It is written here so the record
has a shape and a starting value; JS-2 takes the snapshot and replaces these values with the ones
it actually took, or records why it took different ones.

**One honest defect in the candidate, recorded rather than discovered later.** A repository gate
in the seed is red at that commit: a configuration test asserts a smaller ownership set than the
tree contains. A snapshot precondition that asks for every gate green at the snapshot commit is
not satisfied by this candidate today. That is a small, cheap, nameable thing to fix before the
snapshot — and naming it is the point, because "take it when it is green" is not a precondition
if nobody has checked whether it is green.

### 4.2 What "after the fix work lands" can and cannot mean

The core roadmap says the snapshot is taken "once the legacy fix work has landed". **There is no
programme in the seed component under that name.** What exists is several concurrently open
programmes, most of which cannot be forecast to complete: one is blocked on a cyclic graph
proposal, one is blocked on an unanswered soundness question, one is explicitly permitted to end
in cancellation, and the performance programme is unaccepted on every platform it names. A
precondition written as "when the programme completes" would be a precondition that never fires.

So this roadmap replaces it with an itemised waited-on set. JS-0 records, per open item in the
seed that would rewrite source this component copies, either **wait** with a stated reason or
**do not wait** with a stated consequence. The set is small and knowable:

| Open work in the seed | Would rewrite | Disposition |
|---|---|---|
| The module/ESM conformance push and the generator, async, and early-error correctness work landing beside it | Parser, static semantics, and the built-in library — precisely the copied surface | **Wait.** These are semantics this profile wants correct in its seed, and re-deriving them after the fork costs more than waiting. |
| Regular-expression backend adoption: one match-data abstraction across the exec, split, and replace paths, retiring the translator | The regular-expression surface of the library | **Wait**, or scope the first manifest to exclude regular expressions and record the exclusion. Either is legitimate; drifting into it is not. |
| The standard-library split into core, temporal, internationalization, and regular-expression parts | The library's assembly shape | **Do not wait.** This component performs its own split at ingest along manifest lines, which is a different split for a different reason. |
| A rename of every assembly, namespace, and package ID across the seed | Every file a copy takes | **Do not wait.** This component renames on ingest into its own namespace on the first commit, which subsumes it. Waiting for a rename in order to rename again is pure delay. |
| The project-shell restructure that would extract a backend-neutral front end | Nothing, by its own terms — it is forbidden from moving production code | **Do not wait**, and do not plan against it. This component performs its own extraction. [Section 9](#9-the-semantic-front-end-and-lowering) says so plainly because the alternative is planning around an extraction that will not arrive. |

**And a stop condition, because the seed does not stand still.** It moves at a rate that makes a
late snapshot strictly more expensive than an early one: every further release is more to adapt
and more to re-review. JS-0 records a date, or a commit-count budget, after which the snapshot is
taken as-is and the remaining waited-on items are re-derived on this side of the fork. A
precondition without a deadline is how a fork becomes a permanent postponement.

The second leg of the core's own condition — that the core contract is **accepted** — is a
separate gate and is unmet today. This roadmap shows both legs, and lets neither imply the other.

### 4.3 The copy table

Sizes are approximate and are sizing evidence, not measurements. What matters is the verdict
column.

| Seed material | Roughly | Verdict |
|---|---|---|
| Tokenizer and parser: scanner, token stream, classifiers, numeric coercion, pattern validation | 11,000 lines | **Copy.** Its reference closure contains no IL emitter, and a forced trim/AOT analyzer build of it produces no warnings attributed to it. That is the best-conditioned material in the seed. |
| Syntax tree and visitors | 3,000 lines | **Copy**, with two conditions: it requires unsafe blocks (a visitor takes the address of a stack local, and the pervasive string type is an unsafe struct over source, offset, length), and it depends on three small primitives from a neighbouring assembly that must be copied in rather than referenced. |
| Backend-neutral static analysis extracted from the lowering project: post-parse validation, free-name analysis, declaration and hoisting analysis | 5,000 lines, out of a 20,000-line project | **Copy and re-home.** The remaining three quarters of that project emits against the seed's expression model and is not front-end code. |
| Property storage: hidden-class shapes with a transition table, shape-only slot storage with its one-way materialization boundary, packed/holey/dictionary element arrays, the named-property trie | Part of ~2,700 lines | **Copy, with its tests and its recorded defect history.** This is the strongest single asset in the seed and the least likely to be improved by rewriting. |
| The interned property-key table | Small | **Rewrite.** Its static constructor initialises its own fields by reflection, which is trim- and AOT-hostile in the lowest layer of the graph, and its identities are process-wide where this profile needs them realm-scoped. |
| Standard library, core surface | ~30,000 lines | **Copy, or port.** Whether it is a copy or a port is decided by the value-representation decision in [section 8](#8-the-value-frame-and-call-model), and that decision is a gate on entry to JS-4 precisely so this answer is known before a file is taken. |
| Standard library, optional surfaces: temporal, internationalization, regular expressions | ~29,000 lines | **Copy behind separate manifests.** Together they are about half the library. None of them belongs in the first feature manifest, and each gets its own manifest identity so a composition can decline it truthfully. |
| The built-in registration source generator and its attribute vocabulary | ~1,600 lines | **Copy, and change one thing.** It is a Roslyn incremental generator emitting static creation and registration methods with no runtime reflection — which already satisfies the core's static-and-typed rule. Its generated prototype lookup reads ambient context and must take a realm parameter instead. |
| The value base type's dynamic-metaobject interface and its binder | Small, pervasive | **Amputate at ingest.** It is a runtime-code-generation path sitting on the base class of every JavaScript value, so the decision cannot be deferred past the first copied file. |
| A dead registration attribute family | ~120 lines, zero usages | **Delete at ingest.** A copy that begins by deleting provably dead inherited code is cheaper to review than one that carries it. |
| Cross-assembly module-initializer wiring | ~360 lines — one initializer body, plus seven satellite initializer files | **Delete.** The core forbids the discovery this exists to perform; a composition root wires what it composes. |
| Prototype patching that the same file registers rather than initialises: substitution for the replace protocol, legacy accessor lookups, species constructors, string tags, Annex B legacy statics, disposable stacks | ~2,000 lines, in the same file as the wiring above | **Re-home, then copy or port with the library.** It is registry-registered semantics that happens to sit beside a module initializer. The attachment is deleted; the semantics are not. **These lines are already inside the core-surface row above — the two library rows partition the same assembly, so do not add the sizes twice.** |
| The CLR-interop assembly | ~1,600 lines | **Exclude by name.** It resolves types from script strings, constructs generic types at run time, and activates instances. It is structurally incompatible with the non-goals in [section 1](#1-terminology-and-support-claims). |
| The module-host assemblies | ~1,500 lines | **Exclude by name.** They are host integration doing filesystem and package resolution. Module *syntax* lowering is front-end work and is copied; module *hosting* is the embedder's, behind the artifact provider. |
| The expression model, the IL emitter, and the tree-building and generator-rewriting layer between them and the runtime | ~16,500 lines | **Exclude.** This is the arm this profile replaces, and it is larger than the part of the seed being kept for the same job. |
| The numeric bytecode island and its offline compiler | ~660 lines | **Reference as prior art in prose only; copy nothing.** It has no object model, no strings, no properties, no calls, no closures, no exceptions, no modules, no async. Its value is that it proved a no-emitter closure can publish and run, and this roadmap does not restate that as evidence for anything. |
| The regular-expression engine, the Unicode property tables, and the locale data | ~3,700 for the matcher; ~26,000 more on the Unicode and locale side, most of it generated tables | **Acquire as this checkout's own dependencies.** They are independently versioned components, not part of the seed's own tree, and the Unicode side is not only tables — it carries hand-maintained calendar, plural-rule, and special-casing code that lands inside this component's root and under JS-0's warning and resolution gates. The dead extended date-time reference is dropped. |
| The test corpus for the library and the storage layer | ~27,000 lines | **Copy, as a port wherever the value model changed.** Labelled as a port, not as a pass. |
| The conformance harness, sharding, host modes, self-check fixtures, merge, and audit tooling | Method, not code | **Re-implement the method.** [Section 14](#14-the-conformance-oracle) states the method in full so it can be built from this document. **No total, manifest entry, known-gap entry, or triage finding crosses the fork.** |

### 4.4 What the copy actually costs

Four things are true at once and the roadmap is easier to execute if all four are said.

**The front end is in good condition, and the distinction matters.** A forced trim and AOT
analyzer build over the parser produces **no warning attributed to the parser or the syntax
tree** — and produces plenty attributed to the neighbouring assemblies its reference closure
drags in. Both halves are the finding. The copied source is clean; the closure it currently sits
in is not, which is exactly why JS-2's gate asks for zero warnings **anywhere in the closure**
rather than zero attributed to the project. A per-project number would be satisfied on day one
and would prove nothing.

**Two things make the seed's lowering AOT-hostile, and only one of them is a graph problem.** The
*transitive project reference* into the IL emitter is caught by a graph assertion. The lowering's
own source is not clean: it carries roughly two dozen per-call-site reflective member
resolutions across nine files, one of them keyed on a run-time string behind a locked static
dictionary, plus a module initializer. Both families are exactly what JS-2's exit gate scans for,
so the gate on the lowering is that metadata scan and not only a graph assertion.

What [section 4.3](#43-the-copy-table) actually re-homes — the post-parse validation, the
free-name analysis, and the declaration and hoisting analysis — is free of both, and *that* subset
is what "adaptable" describes. It does not describe the emitting visitors, which this profile is
not copying.

**The runtime is in better condition than a blanket claim would suggest.** The value model, the
storage layer, the standard library, the globals, and the debugger contain no IL-emission API in
their own source at all. The library reaches an emitter transitively through three files and two
runtime types. The interop assembly is the only structurally incompatible project in the set.

**The value representation is the expensive problem.** Every JavaScript value in the seed is a
heap-allocated CLR reference type: no tagged small integers, no NaN boxing. An eight-byte tagged
struct exists in the seed and is deliberately unused. This is the seed's own most-measured
performance defect, and it is *also* an ABI decision this profile cannot defer, because the
standard library is typed against whatever answer it gets.
[Section 8](#8-the-value-frame-and-call-model) makes it a gate on entry to JS-4, and
[section 23](roadmap.gates.md#23-risks-and-stop-conditions) makes shipping library code while it
is open a stop condition.

**Nothing is reviewed and nothing is green.** The seed carries no human review decision that this
component inherits; its own review record is stale by hundreds of commits and its own rule
invalidates it on any later change. Every copied unit enters this component as unreviewed code
under this component's own assurance annotations, and the review debt is this component's from the
first commit. [Section 19](roadmap.delivery.md#19-milestones) schedules that as work with an owner
rather than assuming it away.

### 4.5 Licence, attribution, and one notice that must change

The seed is Apache-2.0 and is itself a derivative of an upstream Apache-2.0 JavaScript engine, so
a copy carries the obligations of that licence: retain the notices, mark modified files as
changed, and carry the NOTICE content forward. This component's own licence and notice file
satisfy that on its own terms, without any pointer into the seed's tree.

One consequence reaches outside this component and must not be discovered at release time: the
core component's third-party notice currently asserts that nothing it ships is vendored or
copied. That assertion is scoped to the core's own packages and stays true — but only if it
stays scoped. JS-2 carries an explicit item to confirm the scoping, or to amend the notice, with
the release owner co-signing. An attribution obligation discovered during a publish is a stop.

### 4.6 A hazard a reader will meet

The seed's own documents still contain a substantial amount of stale prose describing a retired
plan to build a JavaScript bytecode profile inside that component: sequencing rows, dependency
bullets, and rationale text. The plan documents themselves were deleted; the prose was not. A
reader who goes looking will find a competing, superseded plan for this component's work.

**This document is the plan.** Nothing in that component plans, schedules, or gates anything
here, and no item identifier from it appears anywhere in this roadmap.

---

## 5. Package boundaries and the dependency graph

These names follow the pattern the core fixes for a profile and are hypotheses until JS-0 proves
the graph with project shells and an explicit assembly budget. No assembly is created to shorten
a file; each must enforce a dependency, AOT, deployment, ownership, test, or package boundary.

| Logical boundary | Candidate assembly | Responsibility and dependency rule |
|---|---|---|
| Format | `Broiler.VM.Profile.JavaScript.Format` | Opcodes, schema, encoder, decoder, and the format-version range. **The pivot**: the executor and the lowering must agree on the bytecode and neither may depend on the other, so both reference this and it references neither. |
| Profile | `Broiler.VM.Profile.JavaScript` | Descriptor, verifier, executor, value and frame model, object model, standard library, host imports, payload projections. References the two core assemblies and the format. |
| Lowering | `Broiler.VM.Profile.JavaScript.Compiler` | Tokenizer, syntax tree, static semantics, and source-to-bytecode lowering. A **sibling** of the profile, not a part of it. References the format; never referenced by the profile. |
| Composition roots | `Broiler.VM.Profile.JavaScript.Composition.*` | One per named deployment composition. The only projects that know which profiles and capabilities an image contains. Non-packable unless the composition register advertises them. |
| Test-only | conformance host, corpus store, fuzz host, soak host, bench host | Never referenced by a product project and never present in a published closure. |

Whether the profile is one assembly or several — a value and object model separated from the
standard library, for instance — is a JS-0 decision with a dated record, not an assumption. The
single-assembly default needs no justification; a split does.

```text
Broiler.VM.Abstractions            ──→ (nothing)
Broiler.VM.Binary                  ──→ (nothing)
…Profile.JavaScript.Format         ──→ (nothing Broiler-owned)
…Profile.JavaScript                ──→ Abstractions + Binary + Format
…Profile.JavaScript.Compiler       ──→ Format  (+ Abstractions where it builds descriptors)
composition root                   ──→ Broiler.VM.Runtime + the profile + (a lowering, or not)
```

The rules the verified graph must retain, whatever the names become:

- the profile's Broiler.VM reference set is **exactly** the two core assemblies — no reference
  to the core runtime, no package reference to a third core package, no `InternalsVisibleTo` in
  either direction. **One question inside this rule is the core's to answer and is open today**:
  the frozen profile-facing contract states the reference set as exactly those two assemblies, and
  the graph above adds a third Broiler.VM-named assembly that is this component's own format. Either
  a profile's own sibling assemblies sit outside that set or the graph above is illegal under a rule
  that is already active, and JS-0's placement decision cannot be taken correctly until the core has
  ruled. The risk table already names the shape of this hazard; this is where it has actually
  arrived;
- the profile never references the lowering, which is what makes an execution-only image contain
  a format, a verifier, and an interpreter and no compiler at all;
- no product project references a test project, a fixture, or a conformance host;
- **no edge in either direction reaches any legacy Broiler component**, asserted by an
  architecture rule with a passing witness and a negative control, including the inbound half;
- **no edge in either direction reaches any other Broiler.VM profile component**, asserted the
  same way and with the same negative control. This is a separate rule from the one above and it
  is not implied by the reference-set clause, because that clause already tolerates one further
  Broiler.VM-named assembly — this component's own format. Two profiles in one browser image are
  composed by a composition root; they are not linked to each other. The core states the rule, the
  extraction gate's fourth condition *is* this property, and
  [section 15](#15-deployment-compositions-native-aot-and-the-browser-embedding)'s cross-profile
  boundary depends on it staying true — so a rule only one of the two profiles enforces is a rule
  that can be violated from the untested side;
- every namespace matches its assembly. The seed violates this in one place in a way that makes a
  copied assembly *look* like it depends on an IL emitter, and copying that verbatim would put a
  false dependency into the first commit; and
- there is no aggregate profile-listing type anywhere. One would reference every profile assembly
  and defeat the exact-closure reports the compositions depend on.

---

## 6. Feature manifests: how the language surface is admitted

The core fixes a manifest's shape and identity; this profile fixes its content. Three rules make
that a gate rather than a label.

**One manifest, one reviewed scope, one oracle run.** A manifest is minted with an explicit list
of what it admits, an extension to the retained malformed corpus, and its own conformance run
from an exact commit against the pinned suite revision. A manifest with no retained run of its
own is not accepted, and the support table says so.

**Increments do not inherit.** Manifest *n+1* admits what its own scope names. It may not be
justified by arguing that manifest *n* implies it, and the admission criterion for what belongs
in the next increment is recorded in the allocation table below rather than decided per commit.

**A manifest is refused, not degraded, and so is a feature outside it.** An artifact naming a
manifest this descriptor does not accept is `InvalidArtifact` with reason
`UnsupportedFeatureManifest`. There is no partial acceptance and no fallback to a smaller manifest.
And **a well-formed artifact that uses a construct outside its declared manifest is rejected at
verification** rather than failing at first execution — the same rule invariant 3 states for
structural checks, applied to the manifest boundary. This matters most where the manifest split is
a policy boundary: a composition that declines `broiler.javascript.dynamic` refuses `eval` at
verification with an invalid-artifact reason, which is **not** the same event as a composition that
admits the manifest and registers no artifact provider, where the refusal is a run-time
`ProviderNotRegistered` the guest may catch. Two different outcomes, two different catchabilities,
one situation a reader will experience as the same. JS-1 states the rule with format version 1, and
the verifier's rejection list carries it.

The intended allocation, which JS-0 fixes and later milestones may extend but not silently widen:

| Manifest | Admits | Earliest milestone |
|---|---|---|
| `broiler.javascript.slice` | Numbers, arithmetic, comparison, local variables, structured control flow. No objects, no strings, no functions, no property access. **Deliberately not JavaScript anyone would ship** — its purpose is to close the whole contract loop against about two thousand readable lines. | JS-1 |
| `broiler.javascript.core` | The language surface: objects, prototypes, properties, closures, functions, classes, exceptions, iteration, destructuring, strict mode, and the core standard library. | JS-5 opens it; increments extend it |
| `broiler.javascript.modules` | Module records, live bindings, import and export forms, and — where declared — top-level await. | JS-7 |
| `broiler.javascript.dynamic` | `eval`, the `Function` constructor, and dynamic `import()`. Separate because a composition that registers no artifact provider must be able to decline exactly this and say so. | JS-8 |
| `broiler.javascript.regexp` | Regular expressions, over the from-scratch matcher. | JS-6, or excluded with a published failure |
| `broiler.javascript.intl` | Internationalization. | Deferred; excluded by name until it has a run |
| `broiler.javascript.temporal` | The temporal surface. | Deferred; excluded by name until it has a run |

### Where the language is deliberately underspecified, and why a manifest has to say so

A retained corpus compares an observed answer against a recorded one, byte for byte, across three
publish modes. **A component whose whole method is recorded expected answers cannot hold a corpus
whose expected answers legitimately vary**, so every surface the specification leaves
implementation-defined, implementation-approximated, or host-defined has to be named and placed
before a corpus entry is written over it — not discovered when one publish mode disagrees with
another.

The named surfaces are not exotic and the list is short enough to write: property enumeration order
where the specification does not fix it; the contents and format of stack traces and error
messages; number-to-string and string-to-number precision at the edges the specification leaves to
the implementation; locale-, calendar-, and time-zone-sensitive behaviour; and anything the host
supplies rather than the language. Each is either **fixed by this profile and recorded as fixed**,
so a corpus entry may pin it, or **declared varying and excluded from the corpus by name**, so no
entry pins it by accident. The internationalization and temporal surfaces are already deferred to
their own manifests for a related reason; what this rule adds is that the approximated surfaces
*inside* `broiler.javascript.core` get the same treatment.

JS-1 records the list with the first format version, each manifest extends it, and the support
table publishes it. **A determinism claim broader than the list is an untruthful support claim**,
and a corpus entry over an unlisted varying surface is a test that will eventually fail for a
reason that is not a defect.

---

## 7. The bytecode format and the verifier

### The format

Format version 1 is defined with the first manifest and grows with the interpreter. It is not
enumerated as a whole-language opcode set in advance, because an opcode set designed before the
value model is a set that will be redesigned after it.

What the format carries from the first version, because retrofitting any of it is expensive:

- magic, format version, and the feature-manifest identity the artifact was produced for;
- length-framed sections with a declared count, read through the core's bounded reader;
- a constant pool with load-time property-name interning, so a name is interned once per program
  rather than at each use;
- a code section with fixed instruction boundaries;
- exception regions with explicit nesting and `finally` continuation targets;
- suspension and resume targets, reserved from version 1 even before generators exist, because
  adding a control-flow target kind to a frozen format is a format-version break;
- a canonical position table mapping bytecode offsets to source positions, independent of any
  later peephole or specialization, so a stack trace and a breakpoint name a stable thing; and
- declared maxima for operand stack, locals, frames, and constants — **declared for checking,
  never used to size an allocation before the bound comparison**.

The format is internal and versioned during development. Compatibility is promised only when a
persisted-artifact version is explicitly accepted, which
[section 16](#16-persistence-and-the-code-cache) gates and no milestone here grants.

### The verifier

The verifier is a trust boundary even when a local tool produced the bytes, and it is the only
one: there is exactly one verifier in this component, reached only through the core's
verification entry point.

It rejects, before execution, at least the following. **Two outcome categories appear in this list
and the split is the core's ruling, not this profile's preference**: a malformed or ill-typed
artifact is `InvalidArtifact` carrying this profile's diagnostic code and a position, while a breach
of an effective ceiling that names a budget dimension is `ResourceExhaustion` naming that dimension
and its scope. Conflating them tells a caller its program is malformed when the truth is that this
host declined to spend the memory, and — because every entry in the retained corpus pins its
observed triple — a miscategorised entry does not fail later, it passes and records the wrong
answer. Each bullet below states which category it produces.

- **`InvalidArtifact`** — a profile ID, format version, or feature manifest this descriptor does not
  accept, each with its own distinct reason, and the unsupported-profile case answered **without
  examining a payload byte**;
- **`InvalidArtifact`** — malformed framing, truncation, and invalid variable-length encodings,
  mapped from the core's bounded-read statuses onto this profile's diagnostic codes;
- **`InvalidArtifact`** — opcode and operand kinds, constant, local, and function indexes, and
  instruction boundaries;
- **`InvalidArtifact`** — control-flow validity over reachable and unreachable code, with consistent
  stack and value states at every join;
- **`InvalidArtifact`** — exception-region nesting, `finally` continuation targets, and suspension
  and resume targets;
- **`InvalidArtifact`** — every static semantic the manifest requires, and any construct outside
  the declared manifest — see [section 9](#9-the-semantic-front-end-and-lowering), which makes
  early errors a verification stage rather than a parser side effect;
- **`ResourceExhaustion`** — structural depth, section count, declared counts, and artifact bytes,
  against the effective ceilings the core materialized before the first byte was read. Each names
  one dimension and one scope, and none of them is an invalid-artifact answer: the artifact is
  well formed and this image declined to admit it;
- **`InvalidArtifact`** — any host assumption the artifact declares, checked against the
  capabilities the verification context reports as registered. An artifact that names an import the
  composition does not carry is refused at verification rather than at first call, and a
  verification whose context reports no capability at all still answers, deterministically, rather
  than throwing. **The two host checks answer different questions and both are needed**: this one
  says *this image can never run this artifact*, while a binding failure at runtime creation says
  *this runtime does not have it right now*; and
- **`InvalidArtifact`** — position and debug metadata that refers only to valid canonical bytecode
  positions.

**Positions travel in the core's own position record**, which carries a section index, a byte
offset, and two profile-owned coordinates whose meaning the core does not interpret. This profile
states at JS-3a which of the four fields it populates, what it puts in the two coordinates, and what
a section index of `-1` means for it. That sentence exists because two profiles designing two
position encodings against one shared record, neither naming it, is how two incompatible
conventions get built against one struct.

Three disciplines make that list provable rather than aspirational:

1. **A retained malformed corpus.** Every entry carries its bytes, its hash, and its expected
   outcome, reason, and diagnostic code, and every entry is replayed under JIT, trimmed, and
   Native AOT with the three tables compared byte for byte. The corpus grows at every milestone
   that grows the format, and it contains **control entries that verify successfully** — a
   corpus in which nothing passes is a corpus that would not notice a verifier that rejects
   everything.
2. **Coverage-guided fuzzing over four surfaces**, not one: the verifier, the source tokenizer
   and parser, the regular-expression matcher over both pattern and subject, and the executor
   over verified-but-adversarial artifacts. Every session retains its seed, its iteration budget,
   and every minimized counterexample. **A counterexample is closed by a named regression, never
   by an allow-list entry.**
3. **Ordering assertions.** The effective ceilings are materialized before the first byte is
   read; a refusal happens before the allocation it would have authorised; a declared count is
   compared against its bound before it sizes anything. These are asserted mechanically for every
   corpus entry including every failing one, because the ordering is the property and the answer
   alone does not show it.

---

## 8. The value, frame, and call model

**This decision is taken before the standard library is copied, and it is a gate on entry to
JS-4 rather than that milestone's first task.** The seed's library is typed against the seed's
value base type; if this profile is going to replace that representation, JS-6 is a rewrite and
must be re-scoped before it starts, not during it.

What the decision must state, in both directions — what it buys and what it costs:

| Row | What must be decided and recorded |
|---|---|
| Representation | How a Number and a managed reference are held. The seed boxes every value on the heap; an unused eight-byte tagged struct sits beside it. Either answer is defensible; an unrecorded answer is not. |
| Rooting and lifetime | GC rooting for operand slots, locals, environments, arguments, and constants, and who owns each. |
| Call and construct | Calling convention for call, construct, host call, and return, including how `this`, `new.target`, and the arguments object are carried. |
| Frames | Frame ownership, the native cost of one interpreter frame, and how that cost fixes the `CallDepth` default (below). |
| Completion | Completion records and handler state for `return`, `break`, `continue`, and `throw`, explicit rather than emergent from the dispatch loop. |
| Suspension | How a frame and its handler state are captured on the heap and reconstituted — designed here, implemented at JS-7, because a frame model that cannot be captured cannot be retrofitted. |
| Safepoints | Stable source, exception, suspension, and diagnostic safepoints, canonical against bytecode positions rather than against any later specialization. |
| Metering | Where every `Poll()` and every charge sits in the loop, and against which dimension. A representation decision that makes charging awkward is a decision with a hidden cost. |

Each row carries correctness fixtures and Native AOT representation probes retained beside it.
**A representation is not accepted because it looks compact**, and it is not accepted on a JIT
measurement alone.

### `CallDepth` is measured, not chosen

A recursing program must be refused as `ResourceExhaustion` naming `CallDepth`, on every claimed
RID, under Native AOT — **rather than terminating the process**. A stack overflow is not
translatable into a result, so claiming to handle deep recursion without a measured bound would
be an untruthful capability claim. The default is therefore derived from a retained, reproducible
measurement of native frame cost per interpreter frame on each claimed RID, and a recursion case
proves the refusal on each.

The same discipline fixes `MaxUnchargedWork`, `ChargingGranularity`, and `CancellationPollBound`:
each is a number chosen from a measurement and recorded with it, not a round figure.

### Proportional charging

This is the core's obligation **CO-1** in ADR 0007, cited rather than re-derived: the rule that
work be charged as a monotone non-decreasing function of the input, at least the ceiling of that
function over the declared granularity, in the profile's own work units and never in measured time,
is the core's and not this profile's invention. What this profile owns is the family list, the
functions, and the fixtures.

For every named operation family whose cost grows with its input — string concatenation and
comparison, array copy and sort, property enumeration, regular-expression matching, numeric
conversion of large values, structured cloning — this profile declares a monotone
non-decreasing charging function and a granularity, and charges at least the ceiling of that
function over the granularity. Each family gets a retained fixture with an unsimplified control.
**An operation family without a proportionality fixture does not ship in the increment.**

---

## 9. The semantic front end and lowering

### Static semantics are one verification stage

In the seed, early-error responsibility is split across four places in two assemblies, the parser
deliberately tracks no strict mode, and two checks re-tokenize raw source text because the syntax
tree keeps only a token span. That split is workable when the consumer is a compiler; it is not
workable when the consumer is a verifier that must answer totally, in one pass, with one
diagnostic per rejection.

So: **consolidate every early error the manifest requires into one validation stage over the
tree**, carry on the tree the facts the re-scans recover, and delete the re-scans. Each artifact
is tokenized at most once during verification, asserted by a case. Where strict mode lives — in
the parser or in the validator — is a named architectural decision with an owner, taken at JS-3b
and recorded, because the seed's answer is a split this component may ratify or correct but may
not inherit by accident.

### Where the verification boundary actually falls, which this document must say and currently does not

The sentence above says "one verification stage", and the core's verification entry point takes a
descriptor, a payload span, a context and a token — **bytes, not a tree**. The lowering that
produces the tree is a sibling assembly a composition may omit entirely, and this component fuzzes
it as a surface distinct from the verifier. So the two stages do not straddle one call, and three
consequences follow that no gate can be written over until they are settled:

- **Source with an early error never becomes an artifact.** Its diagnostic never occupies the
  core's profile diagnostic code, never carries a position whose byte offset is an offset within an
  artifact, and never crosses a core result envelope at all. Half of this component's published
  registry therefore has a transport that is the embedder's own seam rather than a core result, and
  the registry has to say which half each code belongs to.
- **Either the verifier re-derives every early error from artifact bytes, or it does not** — and if
  it does, the front-end contract that returns "a validated tree the lowering consumes" is doing
  work the verifier then repeats, which is a design this component may choose but not by accident.
- **An artifact that is both malformed in framing and invalid in static semantics gets exactly one
  answer**, and which one is a property of phase order rather than of implementation convenience.
  A profile that fuses the phases scores it differently from one that does not, and the difference
  is invisible to a suite that never presents a doubly-bad input.

JS-3a records the registry split and JS-3b records the boundary and the doubly-bad artifact's
answer, with a named case that **fails when the phases are fused**. Until then, a gate clause asserting that every
early error "maps onto exactly one core invalid-artifact reason" is asserting a mapping onto a
reason that half of them may never carry.

The static-semantic vocabulary the copied analysis already speaks is kept verbatim, because
renaming it would be renaming the specification: `VarDeclaredNames`, `LexicallyDeclaredNames`,
`BoundNames`, `ImportedBoundNames`, `HoistingScope`, `FormalParameters`, `ArrowParameters`,
`IdentifierReference`, `BindingIdentifier`, `ModuleItem`, and the global- and
function-declaration-instantiation operations. The invariant the binding algorithm enforces is
carried in its own words — *`VarDeclaredNames` and `LexicallyDeclaredNames` must not intersect
at any single scope* — rather than paraphrased into something weaker. Annex B clauses keep
their clause numbers.

The free-name analysis keeps its stated soundness contract verbatim, because it is the sentence
that makes the analysis reviewable: **over-approximation is safe and under-approximation is a
miscompile.** Its escape hatch is justified by naming the three constructs together — a direct
`eval`, a `with`, and a `debugger` can each reach a binding that is never mentioned at all —
and not by naming one of them.

### Parse options are explicit, and this is not optional

The seed's parser reads its two most consequential grammar switches — module-versus-script goal
and top-level-await permission — out of ambient async-local state in a different assembly. That
is unusable here for three separate reasons: it is a hidden dependency across an assembly
boundary the fork removes, it makes two concurrent parses with different goals mutually
corrupting, and ambient per-thread state in a profile is exactly the shape the core's lifecycle
rules exist to keep out.

The replacement is an explicit options value passed in. The gate is a test in which two parses
with different goals run concurrently in one process, each producing the goal-appropriate result,
and which **fails when the options are replaced by a shared static**.

### Deep nesting must not terminate the process

The parser, the validator, and the lowering each recurse over program structure, and `CallDepth`
does not reach any of them — it bounds guest frames, not compile-time recursion. The seed
mitigates this with stack segmentation and by running whole compilations on an oversized thread.
This component decides, at JS-2, between an explicit compile-time depth bound and a worklist
rewrite, records the decision, and pins it with a nesting corpus that must be refused rather than
survived. **A process termination on a nesting case blocks the milestone.**

### Deterministic lowering, and one lowering

The same source, lowering version, and format version produce a byte-identical artifact. No
consumer requires this on day one — a host's cache keys on source and versions rather than on
output bytes — but retrofitting determinism means auditing every iteration order, timestamp,
and identity-derived value in a finished compiler. It is preserved, not engineered for.

Where a composition compiles at run time and a later one compiles ahead of time, both use this
lowering assembly. The composition decides which is present; the code is not written twice.

### What the front end is not

The compiler plug-in interface in the seed returns the seed's expression-tree type, which means a
bytecode back end physically cannot implement it. It is not copied. This profile's front-end
contract returns a validated tree or a back-end-neutral intermediate form, and the lowering
consumes that.

The module host projects are excluded by name: they are host integration doing filesystem and
package resolution against the seed's object model, and they contain no parser or semantic work.
Module *syntax* lowering lives in the front end and is copied.

---

## 10. Execution: mapping JavaScript onto the core lifecycle

The core's lifecycle is fixed and this profile refines observable behaviour inside it. The
mapping:

| Core stage | What this profile does |
|---|---|
| Catalog build | Supplies one descriptor through one static accessor. No aggregate listing type exists anywhere in the graph. |
| Runtime creation | The composition supplies ceilings, capabilities, guest-load bounds, and the external-suspension mode. The executor factory creates one executor per runtime from the execution environment. |
| Verification | Decodes and validates into an immutable `IVmVerifiedState` — the program, its constants, its position tables, and the ceilings computed for it. Owns or fully decodes its input: later mutation, disposal, or concurrent overwrite of the caller's buffer changes nothing. |
| Instantiation | Creates a realm and its mutable state behind `IVmInstanceState`. Returns `Instantiated`, `Faulted`, or — for a module graph with top-level await, and only where declared — `Suspended`. |
| Invocation | Runs to `Completed` with a typed payload, `Faulted` with a typed language fault, or `Suspended` with a continuation and a projection. |
| Resume | Re-enters a captured continuation. Single-use: a second resume, a resume after cancellation or disposal, and a resume presented to a runtime that does not own the continuation each answer with the named invalid-state reason. |
| Unwind | Terminal. Runs `finally` blocks and releases resources under the tighter of the abandon budget and the unwind budget, and **runs no guest code able to request a load or to suspend**. |
| Disposal | Drains an in-flight step before releasing the artifact lease under it. This profile's obligation is that a step is interruptible often enough for the drain to succeed, which is what the cancellation poll bound is for. |

Two consequences of the core's result vocabulary that this profile must live inside:

**A language throw is not a core category.** A JavaScript exception is a typed payload behind
`ProfileFault`. The core's categories describe what happened to the *operation*, not what the
program computed, and this profile adds no case to them.

**A host exception is a host failure, unless it is cancellation or exhaustion.** The core's
translation precedence applies, and it is **ADR 0011**'s rules X1/X2/X3 — ordered and exhaustive,
evaluated in order, stopped at the first match. Cited by that identifier rather than restated,
because the core has already recorded that two profile roadmaps restating this rule from the
implementation instead of citing the record is a discoverability defect. The rule: a cancellation
exception carrying the operation's own token is
cancellation; an exhausted meter at the moment of the catch is resource exhaustion; anything else
is a host failure naming the capability. `finally` blocks run in every one of those cases, and
the handler matrix is tested in both directions across the boundary.

### The entry-point problem, stated rather than deferred

An invocation request carries one UTF-8 entry-point name and nothing else. There is no argument
channel and no return channel except a typed payload.

For a browser this is less of a problem than it first looks, because the caller-driven path
compiles a *program*, not a function call: the host lowers the script it fetched, verifies it,
instantiates it into a realm, and invokes it. Arguments, where they exist, are encoded by the
lowering into the artifact the host asked for.

For a host that wants to call `f(1, 2)` on an already-instantiated realm, it is a real gap. Three
answers exist and JS-1 picks one and records it: encode the call into the entry-point text, which
works and is ugly; lower a one-line calling program and verify it as a guest-initiated load, which
is correct and costs a verification; or propose an amendment.
[Section 18](#18-amendments-this-profile-expects-to-ask-of-the-core) carries the third as a
candidate rather than an assumption.

---

## 11. Guest-initiated loads: `eval`, the `Function` constructor, dynamic `import()`, modules

This is the section where the core contract and JavaScript semantics meet most sharply, so it
states the reading it depends on explicitly.

**The mediator returns a verified handle, and nothing else.** At core contract version 1, a
profile that requests a load during execution receives a `VmVerifiedArtifact`. The core gives it
no way to instantiate that handle as a nested core operation.

**That is not a gap for `eval`; it is the right shape.** `eval` does not create a realm. It runs
in the *caller's* realm and lexical environment, and a nested instantiation would be
semantically wrong. What this profile needs is the verified program, which the handle carries as
its own `IVmVerifiedState` — this profile's object, retrievable from the handle, executable
inside the frame that asked for it. So the path is: guest asks → mediator bounds and charges
→ provider answers with bytes → core verifies through this profile's own verifier → this
profile pulls its verified state and executes it in the requesting frame.

Consequences that follow, and that JS-8's gate pins:

- **Every dynamic byte source is the mediator.** An architecture rule asserts the profile
  assembly reaches no filesystem, socket, embedded resource, byte-returning host object, or
  in-process lowering shortcut. `eval`, the `Function` constructor, and dynamic `import()` funnel
  through one adapter and there is no second route. The seed already funnels its two dynamic
  entry points through a single runtime-owned indirection, which is the shape this adapter takes.
- **A composition that registers no provider is a content policy.** Every request is refused
  deterministically with `ProviderNotRegistered`, *before the request payload is inspected*, and
  the refusal becomes a JavaScript error the guest may catch. So a refusal counter must be
  non-zero on an operation that completed `Normal` — a test asserts exactly that, because
  otherwise a policy refusal leaves no evidence.
- **Admission is ordered and the order is asserted step by step.** Depth, then fan-out, then
  already-exhausted allowances — all before the provider is called. Then one host-call unit and
  the elapsed wall clock. Then the returned length against the nested-bytes bound, with an
  over-bound artifact **dropped unverified**.
- **Nested failures are converted, and the conversion is a table.** A nested invalid-artifact or
  unsupported-profile result surfaced unconverted is reported as `NestedFailureNotConverted`. A
  nested resource exhaustion or cancellation is **not catchable from guest code**: it unwinds
  with bounded unwinding that runs no further guest code able to request a load, which is what
  keeps a budget a budget.
- **The mediator is scoped to its invocation.** Retaining and using it later returns
  `MediatorOutOfScope`. A module map that caches handles is fine; a module map that caches the
  mediator is not.
- **A nested handle is runtime-scoped and never shareable.** It is refused in a second runtime
  *before* identity comparison, and no member of this profile hands one to the host.
- **The malformed corpus is replayed through the nested path** as well as the caller-driven one,
  because a verifier reached from a different call site is still the verifier and must answer the
  same way.

### Direct `eval` detection

The seed detects direct `eval` textually: the callee identifier is matched against the literal
name at several call sites, plus a substring scan of class-element source text. That is an
approximation, and the specification's rule is a binding resolution, not a spelling.

This roadmap does not paper over it. JS-8 either replaces the heuristic with a decision the front
end records during binding analysis, or **declares it an intentional documented approximation
with its deviation stated in the support table.** What it may not do is inherit the heuristic
silently, because a wrong direct-`eval` decision is a scope bug that presents as a correct
program.

---

## 12. Suspension: generators, async functions, and top-level await

Three pause kinds exist and they are not interchangeable:

| Pause | Origin | Declared by |
|---|---|---|
| A generator `yield` or an `await` inside an async function | Guest | Nothing extra; guest suspension is ordinary |
| Instantiation parked on top-level await | Instantiation | The descriptor's asynchronous-instantiation declaration. Core contract version 1 **admits** it, gated on that declaration; an undeclared park is `InvalidState` / `UndeclaredAsynchronousInstantiation` and is not resumable |
| A host or diagnostic client pausing execution | External | A double gate: the descriptor declares it **and** the runtime enables it. Neither alone suffices, and the two failure modes are distinguishable |

**Continuations are captured by unwinding, not by rewriting.** The seed reaches an IL emitter for
its generator implementation through a narrow edge; that route does not exist here. The executor
captures its own frame and handler state onto the heap and reconstitutes it, which is why section
8 designs the frame model for capture before
[section 12](#12-suspension-generators-async-functions-and-top-level-await) needs it.

**A pause holds no thread.** The gate is a test that resumes on a *different thread* than the one
that suspended. Nothing in this profile's public surface returns a task, a value task, or a
custom awaitable; no product type implements a completion-notification interface; and no product
assembly references a timer, a delay, or a thread-abort API. Each of those is asserted by its own
metadata scan with its own witness, because "we do not block a thread" is a claim that decays
silently.

**Budgets across a pause are frozen, not stopped.** Fuel, allocated bytes, host calls, and the
nested-load counters hold their values; the wall clock pauses; live bytes and live runtimes keep
being metered. A budget snapshot across a suspension asserts exactly that.

**A suspended operation must be disposable without ever being resumed.** It is cancelled and
disposed on the disposing thread, no instance is published, the terminal unwind runs under the
tighter of the two budgets, and the release order is observed. The residency and live-suspension
bounds each get a named case: expiry lands as `Cancellation` / `SuspendedResidencyExpired`, the
limit as `InvalidState` / `SuspendedOperationLimitReached`.

**The job queue belongs to the host.** Promise reactions, microtasks, and the event loop are the
embedder's; this profile exposes the queue's contents and drains what the host tells it to drain.
Which pauses route through core suspension and which are represented as this profile's own job
records is a decision JS-7 takes and records **with the live-suspension count a representative
workload produces**, because routing every microtask through a core suspension would make the
suspended-operation limit the thing that governs a page.

---

## 13. Realms, agents, and the host boundary

**A realm is this profile's object, not the core's.** One instance may hold several realms; the
core sees one instance state. Cross-realm identity, the well-known intrinsics per realm, and the
membrane between them are this profile's semantics.

**An agent is a runtime.** Worker-style agents are separate core runtimes under one shared
aggregate budget, which is what makes a host ceiling shared rather than multiplied. Two facts
about that must be published and not softened:

- exhausting the parent is reported to whichever operation observes it, so **no test may assert
  which sibling observes a shared-parent exhaustion** — that is not a property this profile
  gets to promise; and
- a shared parent is a **channel**, not isolation. Two agents under one parent can starve each
  other. A host that needs isolation must not share a parent, and claiming isolation over a
  shared parent would be an untruthful support claim.

**The host boundary is typed, versioned, and refused at binding.** Every import names one exact
capability ID, one exact version, and one signature ID; a mismatch is refused when the runtime is
created, never at first call. Kind (`Value` or `ArtifactProvider`), reentrancy, thread affinity,
and exception translation are declared per capability, and registering value capabilities never
implies a provider. A failed required import leaves no partially bound runtime. An unbound
optional import has its branch exercised, because an optional capability nobody ever tested
without is not optional.

No CLR type crosses the boundary. Arguments and results are the core's transfer types, and
diagnostics carry identity and position without carrying host secrets.

---

## 14. The conformance oracle

An engine that grades itself is not evidence. This profile builds the harness before it builds
the language surface, and the harness's first job is not to score anything — it is to prove
that a failing test comes back as a failure.

**The method, stated so it can be built from this document.**

- **A pinned suite revision, resolved once.** An immutable commit, resolved before any shard
  starts, cached under a key containing it, and verified by re-reading the checked-out revision.
  A branch name is not a pin.
- **Content-independent sharding.** A test's shard is a stable hash of its normalized path modulo
  the shard count, so shard membership does not move when the selection changes and a shard's
  history stays comparable.
- **Selection as a recorded pipeline.** Discovery, then known-incorrect exclusion, then scope
  filtering, then feature-metadata filtering, then per-file selectability. The candidate count
  and the pre-sharding selected count are emitted separately from each shard's executed count,
  which is what lets the merge prove the shards covered the whole selection rather than a subset.
- **Per-host-mode totals.** Script, module, and raw each report their own selected, executed,
  passed, failed, skipped, and timed-out counts. A mode that selects files and executes none is a
  named configuration failure, not a small total.
- **The self-check runs before every shard.** Deliberately broken fixtures with declared verdicts
  are run against the built profile, **and at least one control fixture that must pass.** A
  mismatch stops the run. A negative control injects a scoring regression, observes the mismatch,
  and reverts.
- **Asynchronous completion by marker protocol, with the completion kind on every result** —
  completed, reported-failure, never-settled, completed-twice. A test that never settles or
  settles twice is a failure, not a pass with a caveat.
- **Negative-metadata tests are opt-in and required for a release run**, with the uncaught error
  reported by its JavaScript type name so a parse-phase syntax error is matched on what it is.
- **Configuration failures are a closed, named set and each is a failure**: inconsistent shard
  configuration, missing suite revision, incomplete variant coverage, empty selection, no
  executed tests. Removing one shard's report must produce incomplete coverage, not a smaller
  total.
- **The failure manifest is a queue, not an allow-list.** A path leaves it only after a minimal
  repository regression exists, the focused reproduction passes, the affected shard passes, and
  the record is updated. A hand-written entry that a run does not confirm does not survive.
- **The harness has its own regression suite**, run before any shard starts, with the crash
  classifier tested against recorded output. A measurement tool nobody tests is a measurement
  nobody can read.
- **The ratchet.** The first accepted per-host-mode totals for a manifest are the floor. No later
  run of that manifest regresses against them. **The floor records the pinned suite
  revision it was set under.** A suite-revision change re-bases the floor from the first accepted
  run on the new revision, with the old floor and the reason retained; a floor is never compared
  across revisions, because a suite that added tests would otherwise read as a regression and a
  suite that removed them would silently lower the bar. This is the same discipline both the
  diagnostic registry and the corpus already apply to their own pinned revisions.
- **The ingestion path ships nowhere.** A scan asserts the suite harness appears in no product
  package and in no published closure.

Two things this section deliberately refuses. **No total, manifest entry, known-gap entry, or
triage finding from any other component is carried across** — the method is copied, the results
are not, and this component starts at zero. And **a differential against another implementation
is a cross-check, never the oracle**: two arms agreeing on the same wrong answer is still a
failure, and a reference engine's movement may invalidate an attribution but never accept one.

---

## 15. Deployment compositions, Native AOT, and the browser embedding

Three composition labels exist and no fourth is minted. They describe **when source is compiled,
not how much of the language is supported** — a point the support table repeats, because it is
the most likely misreading of this table:

| Label | Contains at run time | What its Native AOT gate proves |
|---|---|---|
| `execution-only` | Format, verifier, executor, standard library. **No tokenizer, no lowering.** | The approved precompiled surface verifies and executes under Native AOT |
| `narrow-runtime-compiler` | The above plus tokenizer, static semantics, and lowering for a named restricted surface | Approved source is compiled and executed inside the published Native AOT application |
| `general-runtime-compiler` | The above for the approved general surface | Approved general source is compiled and executed inside the published Native AOT application |

**No publish is evidence for another kind.** An execution-only publish is not evidence for a
compiler-bearing closure and never appears in one's evidence bundle. Each composition's closure
is read off its own published output, contains exactly the assemblies its register row declares,
and contains no test, reflection, dynamic-code, or IL-emission assembly.

### The browser is always a runtime-compiler composition

There is no ahead-of-time path for the open web, because a page cannot be compiled before it is
visited. A browser composition links the tokenizer, the static semantics, and the lowering into
the image, and its Native AOT gate proves *that* closure publishes and runs — not the smaller
execution-only one.

The embedder keeps its own seam. It already talks to script in terms of source text, a resource
identity, and a realm; an adapter behind that seam lowers, verifies, instantiates, and invokes.
The embedder never handles bytecode, and swapping the engine behind the seam stays a bounded
change. Source arrives in exactly the two directions
[section 11](#11-guest-initiated-loads-eval-the-function-constructor-dynamic-import-modules)
already contracts: caller-driven, where nothing is executing and the adapter lowers and verifies
directly; and guest-driven, through the mediator.

The useful consequence is one this profile should state in its support table rather than leave
for a reader to notice: **a content policy forbidding dynamic evaluation is expressed by
registering no artifact provider.** The refusal is then a contract outcome with recorded
evidence, not an ad-hoc check somewhere inside an engine.

### The other profile in the same image: the JavaScript API for WebAssembly

This section exists because the browser that consumes this profile first will also carry a
WebAssembly profile, because the web reaches WebAssembly *through JavaScript*, and because **nothing
in the core makes that free**. No milestone here delivers it and no manifest admits it. What this
section refuses to do is leave it unpriced from this side, which would leave it priced from one side
only.

**What the boundary is.** `WebAssembly.Module`, `WebAssembly.Instance`, `WebAssembly.Memory`,
`WebAssembly.Table`, and `WebAssembly.Global` are a separate specification's objects, defined in
terms of JavaScript values, with a defined coercion in both directions. Implementing them means two
core runtimes, carrying two profiles, exchanging values.

**Two frozen facts settle the shape, and both are written here** because the core states them once,
at its own boundary section, which is not where a browser team will look. The first is
that a guest-initiated load **may not name another profile**: the provider must answer with an
artifact of the profile that asked, and a different profile is a provider contract breach reported
as a host failure. So `WebAssembly.instantiate` is *not* a mediated load with a different descriptor
on it, and no amount of amendment to the mediator makes it one. The second is that **cross-runtime
reentry is legal and depth-bounded**, and was admitted deliberately so a host object may bridge two
independent runtimes. That is the route: the embedder receives the call from this profile, converts
arguments into the core's transfer types, invokes the other profile's runtime, and converts results
back.

**The depth bound has a precondition, and it is this profile's business because this profile
originates the call.** The chain is bounded by aggregate call depth, which is only a bound when both
runtimes were created under one shared parent — so a two-profile composition root creates one, and a
composition that creates two unparented runtimes has **no bound on the chain at all**. Stating the
second fact without its precondition would leave a browser team believing the core bounds something
it does not. The other intended profile records the same precondition, and the two statements agree.

**What follows, stated so a browser team meets it here rather than in a benchmark:**

1. **Every call crosses the host twice.** A JavaScript-to-WebAssembly call is two host-boundary
   transits and a conversion in each direction. That is the correct price for two profiles that
   share no semantics, and it is a price that shows up in exactly the measurement people run first.
2. **A linear memory exposed as a JavaScript buffer is the hard case, not the call.** The core's
   transfer types are integers, byte spans, and opaque references; none of them is a *shared mutable
   region*, and a shared mutable region is shared semantics by another name. Either the embedder
   mediates every access, which is unusably slow, or something outside both profiles owns the
   region. **This profile co-signs the refusal**: it does not ask the core for a cross-profile value
   channel, and it records that growth on the other side invalidating a view held on this side is a
   rule nobody currently owns.
3. **Function identity across the seam is the embedder's.** A WebAssembly export held as a
   JavaScript value, and a JavaScript function imported into WebAssembly, both need stable identity
   and a callback direction. The callback direction exists — it is cross-runtime reentry — but the
   identity rules are not this profile's and are written in no document.
4. **Capability bindings are fixed when a runtime is created; an import object is built per
   instantiation.** Those two facts do not compose for free, and the choice between one runtime per
   instantiation and a fixed indirection with a dynamic table is a decision the browser component
   takes. The first branch makes `LiveRuntimes` the budget that governs a page.
5. **The two profiles reach each other through their defaults, not their maxima.** A maximum binds
   only the profile an artifact names. But a host that adopts profile defaults gets the tightest
   in the catalog, so the two components' *default* vectors are coupled in a browser image whether
   or not anyone intended it — see
   [section 3](#3-what-the-core-already-gives-this-profile-and-what-it-refuses). The composing
   component either reconciles them or states explicit ceilings, which is what the core's own
   two-profile composition does for the dimensions where it mattered.

**What this roadmap commits to.** It commits to **owning none of the implementation**, and to saying
so in the support table by name rather than letting a reader infer that a JavaScript profile in a
browser image implies a working `WebAssembly` namespace. It commits to **not foreclosing it**: where
a design choice here would make the seam harder — a host-object model with no stable identity, a
realm model that cannot hold a foreign exotic object, a transfer surface that cannot carry an opaque
reference — the choice is recorded with that consequence noted at the milestone that takes it. And
it commits to **naming the owner**: a browser integration is a consumer of two profile components
and belongs to whichever component composes them. That component owns the two-profile composition's
closure report, its Native AOT evidence, its shared aggregate budget, and the reconciliation of two
profiles' maxima. This roadmap's obligation is to make the price visible before that component
exists, not to pay it.

---

## 16. Persistence and the code cache

**No milestone here delivers persistence, and the reason is not scheduling.** The core admits a
bounded persisted envelope by contract and implements none, and no core milestone approves one.
A profile-owned cache format written against a core envelope that does not exist would be a
second serialization path with nothing to hold it to the first.

What this roadmap does instead is fix the design so it stays reachable, at no cost today:

- **The cache key is named now, and it is derived from the handle's identity rather than invented
  beside it.** The persisted key is the **artifact content hash**, the format version, the feature
  manifest identity and version, the descriptor revision, the verifier semantic version, the core
  contract version, and **the per-import capability tuple for every import the artifact binds** —
  which the core defines as seven fields, not two: capability ID, version, signature ID, kind,
  reentrancy, exception-translation mode, and whether an optional import was bound. The last three
  are load-bearing rather than decorative: all three change the legal control flow at a call site,
  and a profile may compile a different path depending on them, so a key that omits them collides
  two variants onto one entry, which is a correctness defect in the cache and not a tuning one. The
  tuple covers imports the artifact **binds**; registered-but-unimported capabilities are excluded,
  so an unrelated composition change does not invalidate every entry. This profile cites the core's
  tuple rather than restating it, because restating it is how the two-field version got written.

  Three terms are deliberately **not** in it, each for a stated reason:

  - **The effective limit vector is not a persisted key input.** It is part of the handle's
    in-process identity, so two runtimes with different ceilings do not share a handle — but it is
    a process-local, timing-dependent quantity, and persisting it would produce a key that never
    recurs. Correctness does not depend on it being in the key, because loading always re-verifies
    and recomputes it.
  - **Source identity is echoed, not compared.** It is the *host's* own lookup key; the core
    records it and compares it never. Naming it first, as an earlier draft of this section did,
    quietly made the key a *derivation* — source plus lowering version plus format version — whose
    validity rests on deterministic lowering, a property
    [section 9](#9-the-semantic-front-end-and-lowering) preserves but explicitly declines to
    warrant. The key is over the output bytes.
  - **Provider presence is not a persisted key input either**, and it cannot be, because a
    guest-initiated-origin handle is ineligible for any persisted envelope and may not contribute
    to any persisted cache key. This is the one persistence rule that binds this profile alone —
    the other intended profile declares no guest loads and the rule is vacuous for it — and
    [section 11](#11-guest-initiated-loads-eval-the-function-constructor-dynamic-import-modules)'s
    module map, which legitimately caches handles, is where it has to be enforced. **JS-8 carries
    the exclusion as a gate clause.**
- **Nothing warmed or process-local is ever serializable.** No object references, no delegates,
  no intern-table indexes, no process-local identities, no warmed caches, no specialized opcodes
  that have become authoritative, no host handles. That is a property of how the verified state
  is designed, and invariant 7's no-mutable-state-reachable-from-a-handle rule — pinned by the
  handle-immutability structural scan in JS-4's exit gate — is what keeps it true before there
  is a writer to violate it.
- **Loading always re-verifies.** Outer-envelope compatibility never implies payload
  compatibility, and interpreting old bytes under new semantics is prohibited. A checksum detects
  corruption; it does not authenticate code.
- **The reopening trigger is a measurement, not an argument.** JS-10 measures verification
  throughput per byte and cold-start cost. If a host's latency budget is missed by a stated
  margin, the persistence question reopens against that number with the core, as a joint gate.

**Two neighbouring questions are already answered, and this profile plans against the answers
rather than waiting for them.** At core contract version 1 the byte round trip is mandatory —
bytes are the only input from which a verified artifact may be produced — and verification is
whole-artifact and eager, so a handle means the whole artifact was verified. Both are discharged
as deterministic exclusions: no compile-to-handle entry point and no per-section verification
member appears in the core's frozen public surface, which exposes verification only as a
descriptor, a payload span, a context, and a token.

Neither is a settlement this profile awaits; each is a numbered amendment this profile would have
to drive, and [section 18](#18-amendments-this-profile-expects-to-ask-of-the-core) carries both
with their counterweights. What JS-10 buys is the number that would fund one — and the stop
condition in [section 23](roadmap.gates.md#23-risks-and-stop-conditions) stands over both: no
second verifier, and no build-time shortcut past the one.

---

## 18. Amendments this profile expects to ask of the core

The core's amendment procedure exists because a contract frozen before its first profile will
meet something it cannot express. Recording the candidates now is cheap; discovering them during
an implementation is not. Each of these is a **proposal or a refusal**, never a workaround inside
the core's execution loop, and each carries the counterweight test: would a profile with no
parser, no text format, and no dynamic loads need this too, or is this one language's need
wearing a general shape?

The counterweight answer is **not this profile's to write alone**. The core's procedure requires
every amendment record to state whether the other intended profile could use the capability, is
unaffected, or refuses it — so a row graded here without knowing the other profile's grade is a row
whose answer changes depending on which component files first. Two rows below were graded that way
in an earlier draft and are corrected; the rest name the other profile's position where it is known.

| Candidate | Why it might be needed | Counterweight |
|---|---|---|
| An argument channel on invocation | An invocation request carries one entry-point name and there is no typed way to pass values. For a caller-driven browser compile this is mild, because the adapter compiles a *program* and the lowering encodes the arguments into it. **It stops being mild the moment this profile hosts another one**: `instance.exports.f(a, b)` is a typed call whose arguments originate here, so [section 15](#15-deployment-compositions-native-aot-and-the-browser-embedding)'s seam needs the channel in exactly the case a browser is built for. | **Strong, and stronger than an earlier draft of this table recorded.** The other intended profile rates this the strongest ask in its own document, on the ground that a language with no parser, no text format, no dynamic loads and no notion of a program still needs it — which is the counterweight test passing, not failing. [Section 10](#10-execution-mapping-javascript-onto-the-core-lifecycle)'s two workarounds are still tried first and their cost recorded, but the row is no longer graded weak on the argument that a fixed-entry-point profile would not need it. |
| A result channel on invocation | Kept out of the row above deliberately. The typed payload already carries results, and several of them, so multi-value returns are expressible today. | **None needed.** The other profile states plainly that the result channel is adequate. Filing argument and result as one amendment would put two differently-scoped versions of one capability into the register, which is how a capability gets approved at the wrong width. |
| Multi-result host capabilities | A capability returns one value. A host import whose signature has two results has nowhere to put the second, so an import that needs one is refused rather than truncated. | Moderate: any profile whose calling convention admits multiple results meets it, and the other intended profile raises it independently. Until then the refusal is deterministic and published. |
| A wider value slot on the capability channel | A value wider than the channel's slot must be split, which works and needs a published encoding. | Weak, and recorded so it is not mistaken for the row above. |
| Nested instantiation through the mediator | The contract names it and version 1 provides no path to it. This profile does not need it for `eval` — [section 11](#11-guest-initiated-loads-eval-the-function-constructor-dynamic-import-modules) shows why — but a module graph that instantiates a dependency as its own instance would. | Moderate: any profile with a module system meets it. Opened only if this profile's realm model actually requires a separate instance per module, which [section 13](#13-realms-agents-and-the-host-boundary) answers first: one instance may hold several realms, so a module needing its own realm does not by itself need its own instance. |
| A charging hook for work done inside a host capability | Wall clock covers a slow capability; it does not cover a capability that allocates on this profile's behalf. | Strong: general. |
| An in-process producer input form — compiling straight to a verified handle | Version 1 admits no other input form, so every caller-driven compile and every mediated dynamic compile serializes and re-decodes on the critical path. | Moderate: general to any composition that compiles at run time; a profile shipped as pre-built artifacts never meets it. Opened only against JS-10's verification-throughput-per-byte and cold-start figures, never against an intuition. |
| Lazy per-section verification | A browser compiles function bodies on first call and will not verify a whole bundle to run one entry point; version 1 fixes whole-artifact eager verification. | Moderate: any profile with large artifacts and a cold-start budget meets it. This profile's invariant 3 fixes the shape of any proposal it would sign: each section verified **completely** before that section's first execution, with no structural, index, stack-consistency, or handler-nesting check migrating into execution. Funded by a measurement, not by argument. |
| Streaming or incremental verification | A browser wants to verify as bytes arrive. | Strong: general, and the core already carries a registered amendment shape for it. Reopened against a measurement, not an intuition. |
| A persisted envelope | [Section 16](#16-persistence-and-the-code-cache). | Strong: general, and already admitted by contract. It needs a gate rather than an amendment. |

The rule that governs all of them: **a design that can only be hosted by a second core state
machine is refused.** Exactly one core state machine and one core contract version exist in a
product graph at any time.

**Two procedural facts belong here rather than being discovered when the first row is filed.** The
amendment procedure is currently unexecutable — no amendment has been minted, and the minting role
and both co-signing roles are held by one person, so a co-signature would not be independent. And a
counterweight *refusal* by the other profile is **recorded, not blocking**: a profile with a veto
over a core amendment would be a profile-to-profile dependency established by governance rather than
by reference, which is exactly what the extraction gate's fourth condition exists to prevent. Every
row above is therefore filed and held rather than scheduled, and none is admissible until it names a
merged or approved capability.
