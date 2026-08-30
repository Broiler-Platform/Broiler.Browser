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
hosted on a core that forbids dynamic code in any product closure. Section 4 states what that
copy actually costs, milestone by milestone, and section 19 sequences the work so the adaptation
is de-risked early rather than discovered late.

Two properties of this document are load-bearing and stated once here. **It links to no document,
path, result, or item identifier in the legacy engine component**, in either direction: the copy
is a fork, and a roadmap that cited its origin's plans would have re-created the dependency the
fork exists to avoid. And **no figure, total, conformance result, benchmark, or Native AOT sample
from any other component appears anywhere in it**. Every number this component publishes will be
its own, from its own lane, at its own commit.

---

## 1. Terminology and support claims

The core fixes most of this vocabulary and this roadmap uses it unchanged. The rows below are the
terms this component adds or narrows; where a term is the core's, that is said.

| Term | Meaning in this roadmap |
|---|---|
| **This profile** | `Broiler.VM.Profile.JavaScript`. One profile ID, one descriptor, one verifier, one executor factory, one payload-kind range. |
| **The core** | The Broiler.VM execution core: its three packable assemblies and the numbered core contract version they carry. Core-owned terms — verified artifact, verified handle, guest-initiated load, artifact-provider capability, external suspension, deployment composition, feature manifest, core contract version, operation-result envelope — keep their core meanings. |
| **The seed** | The named snapshot copy of the legacy JavaScript engine component from which this component's front end, object model, and standard library are taken. A fork with its own history and no dependency edge in either direction. Section 4 is its whole treatment. |
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
- **A change to the core.** A JavaScript requirement that the frozen contract cannot express is
  an amendment proposal or a recorded refusal (section 18). It is never a language-specific path
  added to the core's execution loop, and never a second core state machine.
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
there, so this roadmap plans against code rather than against prose. Nothing in it is a claim
that the core is accepted: every core milestone is in progress and unaccepted, its review record
is unsigned, and section 19 carries that as a dependency rather than assuming it away.

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

A profile hard maximum is **not** a statement of what this profile uses; the defaults are that.
It is the most this profile would tolerate a host granting, and a runtime ceiling is clamped to
the *tightest* hard maximum across every profile in the catalog. A profile that declares its own
usage as its maximum caps every profile composed beside it, which is a composition defect this
component must not introduce.

### What the core refuses to do for this profile

- It stores no values, inspects no frames, and knows no opcode. There is no shared value ABI to
  reach for and none is coming.
- It discovers nothing. No assembly load, no type lookup by name, no scan, no activator, no
  module-initializer ordering. A composition root names this profile's descriptor directly or the
  profile is not in the image.
- It provides no argument channel. An invocation request carries one UTF-8 entry-point name and
  nothing else. Section 10 records what this profile does about that, and section 18 records
  whether it becomes an amendment proposal.
- It gives an executing profile no way to *instantiate* through the core the handle a
  guest-initiated load returns. What it does give is the handle's own verified state, which is
  this profile's object — and that, not a nested core instantiation, is how `eval` runs. Section
  11 works this through, because it is the single most consequential contract reading in this
  document.
- It offers no persisted envelope. Bounded outer-envelope parsing is admitted by the contract and
  implemented by no core milestone, so section 16 plans a code cache that does not exist yet and
  gates it accordingly.
- **It admits exactly one verification input form, and that is settled rather than open.** There
  is no compile-directly-to-verified-handle path and no lazy per-section verification: the byte
  round trip is mandatory, and verification is whole-artifact and eager, so a handle means the
  whole artifact was verified. Every byte this profile executes — including every byte it lowers
  in its own process, on a browser's critical path — is serialized and re-decoded through the one
  verification entry point. Each is reopened only by a numbered amendment, and section 18 carries
  both with their counterweights.
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
| The project-shell restructure that would extract a backend-neutral front end | Nothing, by its own terms — it is forbidden from moving production code | **Do not wait**, and do not plan against it. This component performs its own extraction. Section 9 says so plainly because the alternative is planning around an extraction that will not arrive. |

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
| Standard library, core surface | ~30,000 lines | **Copy, or port.** Whether it is a copy or a port is decided by the value-representation decision in section 8, and that decision is a gate on entry to JS-4 precisely so this answer is known before a file is taken. |
| Standard library, optional surfaces: temporal, internationalization, regular expressions | ~29,000 lines | **Copy behind separate manifests.** Together they are about half the library. None of them belongs in the first feature manifest, and each gets its own manifest identity so a composition can decline it truthfully. |
| The built-in registration source generator and its attribute vocabulary | ~1,600 lines | **Copy, and change one thing.** It is a Roslyn incremental generator emitting static creation and registration methods with no runtime reflection — which already satisfies the core's static-and-typed rule. Its generated prototype lookup reads ambient context and must take a realm parameter instead. |
| The value base type's dynamic-metaobject interface and its binder | Small, pervasive | **Amputate at ingest.** It is a runtime-code-generation path sitting on the base class of every JavaScript value, so the decision cannot be deferred past the first copied file. |
| A dead registration attribute family | ~120 lines, zero usages | **Delete at ingest.** A copy that begins by deleting provably dead inherited code is cheaper to review than one that carries it. |
| Cross-assembly module-initializer wiring | ~360 lines — one initializer body, plus seven satellite initializer files | **Delete.** The core forbids the discovery this exists to perform; a composition root wires what it composes. |
| Prototype patching that the same file registers rather than initialises: substitution for the replace protocol, legacy accessor lookups, species constructors, string tags, Annex B legacy statics, disposable stacks | ~2,000 lines, in the same file as the wiring above | **Re-home, then copy or port with the library.** It is registry-registered semantics that happens to sit beside a module initializer. The attachment is deleted; the semantics are not. **These lines are already inside the core-surface row above — the two library rows partition the same assembly, so do not add the sizes twice.** |
| The CLR-interop assembly | ~1,600 lines | **Exclude by name.** It resolves types from script strings, constructs generic types at run time, and activates instances. It is structurally incompatible with the non-goals in section 1. |
| The module-host assemblies | ~1,500 lines | **Exclude by name.** They are host integration doing filesystem and package resolution. Module *syntax* lowering is front-end work and is copied; module *hosting* is the embedder's, behind the artifact provider. |
| The expression model, the IL emitter, and the tree-building and generator-rewriting layer between them and the runtime | ~16,500 lines | **Exclude.** This is the arm this profile replaces, and it is larger than the part of the seed being kept for the same job. |
| The numeric bytecode island and its offline compiler | ~660 lines | **Reference as prior art in prose only; copy nothing.** It has no object model, no strings, no properties, no calls, no closures, no exceptions, no modules, no async. Its value is that it proved a no-emitter closure can publish and run, and this roadmap does not restate that as evidence for anything. |
| The regular-expression engine, the Unicode property tables, and the locale data | ~3,700 for the matcher; ~26,000 more on the Unicode and locale side, most of it generated tables | **Acquire as this checkout's own dependencies.** They are independently versioned components, not part of the seed's own tree, and the Unicode side is not only tables — it carries hand-maintained calendar, plural-rule, and special-casing code that lands inside this component's root and under JS-0's warning and resolution gates. The dead extended date-time reference is dropped. |
| The test corpus for the library and the storage layer | ~27,000 lines | **Copy, as a port wherever the value model changed.** Labelled as a port, not as a pass. |
| The conformance harness, sharding, host modes, self-check fixtures, merge, and audit tooling | Method, not code | **Re-implement the method.** Section 14 states the method in full so it can be built from this document. **No total, manifest entry, known-gap entry, or triage finding crosses the fork.** |

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

What section 4.3 actually re-homes — the post-parse validation, the free-name analysis, and the
declaration and hoisting analysis — is free of both, and *that* subset is what "adaptable"
describes. It does not describe the emitting visitors, which this profile is not copying.

**The runtime is in better condition than a blanket claim would suggest.** The value model, the
storage layer, the standard library, the globals, and the debugger contain no IL-emission API in
their own source at all. The library reaches an emitter transitively through three files and two
runtime types. The interop assembly is the only structurally incompatible project in the set.

**The value representation is the expensive problem.** Every JavaScript value in the seed is a
heap-allocated CLR reference type: no tagged small integers, no NaN boxing. An eight-byte tagged
struct exists in the seed and is deliberately unused. This is the seed's own most-measured
performance defect, and it is *also* an ABI decision this profile cannot defer, because the
standard library is typed against whatever answer it gets. Section 8 makes it a gate on entry to
JS-4, and section 23 makes shipping library code while it is open a stop condition.

**Nothing is reviewed and nothing is green.** The seed carries no human review decision that this
component inherits; its own review record is stale by hundreds of commits and its own rule
invalidates it on any later change. Every copied unit enters this component as unreviewed code
under this component's own assurance annotations, and the review debt is this component's from
the first commit. Section 19 schedules that as work with an owner rather than assuming it away.

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
  either direction;
- the profile never references the lowering, which is what makes an execution-only image contain
  a format, a verifier, and an interpreter and no compiler at all;
- no product project references a test project, a fixture, or a conformance host;
- **no edge in either direction reaches any legacy Broiler component**, asserted by an
  architecture rule with a passing witness and a negative control, including the inbound half;
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

**A manifest is refused, not degraded.** An artifact naming a manifest this descriptor does not
accept is `InvalidArtifact` with reason `UnsupportedFeatureManifest`. There is no partial
acceptance and no fallback to a smaller manifest.

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
persisted-artifact version is explicitly accepted, which section 16 gates and no milestone here
grants.

### The verifier

The verifier is a trust boundary even when a local tool produced the bytes, and it is the only
one: there is exactly one verifier in this component, reached only through the core's
verification entry point.

It rejects, before execution, at least:

- a profile ID, format version, or feature manifest this descriptor does not accept — each with
  its own distinct reason, and the unsupported-profile case answered **without examining a
  payload byte**;
- malformed framing, truncation, and invalid variable-length encodings, mapped from the core's
  bounded-read statuses onto this profile's diagnostic codes;
- opcode and operand kinds, constant, local, and function indexes, and instruction boundaries;
- control-flow validity over reachable and unreachable code, with consistent stack and value
  states at every join;
- exception-region nesting, `finally` continuation targets, and suspension and resume targets;
- every static semantic the manifest requires — see section 9, which makes early errors a
  verification stage rather than a parser side effect;
- structural depth, section count, declared counts, and artifact bytes, against the effective
  ceilings the core materialized before the first byte was read;
- any host assumption the artifact declares, checked against the capabilities the verification
  context reports as registered — an artifact that names an import the composition does not
  carry is refused at verification rather than at first call, and a verification whose context
  reports no capability at all still answers, deterministically, rather than throwing; and
- position and debug metadata that refers only to valid canonical bytecode positions.

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
the parser or in the validator — is a named architectural decision with an owner, taken at JS-3
and recorded, because the seed's answer is a split this component may ratify or correct but may
not inherit by accident.

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
translation precedence applies: a cancellation exception carrying the operation's own token is
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
works and is ugly; lower a one-line calling program and verify it as a guest-initiated load,
which is correct and costs a verification; or propose an amendment. Section 18 carries the third
as a candidate rather than an assumption.

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
8 designs the frame model for capture before section 12 needs it.

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
  run of that manifest regresses against them.
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
change. Source arrives in exactly the two directions section 11 already contracts: caller-driven,
where nothing is executing and the adapter lowers and verifies directly; and guest-driven,
through the mediator.

The useful consequence is one this profile should state in its support table rather than leave
for a reader to notice: **a content policy forbidding dynamic evaluation is expressed by
registering no artifact provider.** The refusal is then a contract outcome with recorded
evidence, not an ad-hoc check somewhere inside an engine.

---

## 16. Persistence and the code cache

**No milestone here delivers persistence, and the reason is not scheduling.** The core admits a
bounded persisted envelope by contract and implements none, and no core milestone approves one.
A profile-owned cache format written against a core envelope that does not exist would be a
second serialization path with nothing to hold it to the first.

What this roadmap does instead is fix the design so it stays reachable, at no cost today:

- **The cache key is named now.** Source identity, lowering version, format version, feature
  manifest identity, verifier semantic version, core contract version, and the identity and
  version of every host capability and artifact provider whose presence affects semantics.
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
to drive, and section 18 carries both with their counterweights. What JS-10 buys is the number
that would fund one — and the stop condition in section 23 stands over both: no second verifier,
and no build-time shortcut past the one.

---

## 17. Measurement discipline

Every figure this component publishes obeys the same rules, and the rules are stricter than the
figures are interesting.

1. **A control that is the same workload minus the thing being measured.** A difference between
   two different programs is a comparison, not an attribution.
2. **Interleaved lanes.** Candidate and control alternate inside each repetition rather than
   running as two blocks, so a machine that gets slower slows both.
3. **An A/A lane.** The candidate is measured a second time, identically. A candidate-versus-
   control difference smaller than the A/A difference is reported **below resolution**, not as a
   result.
4. **Every repetition retained**, with no outlier policy and no statistical model. The spread
   between repetitions is most of what a single figure hides.
5. **A condition checked before and after every lane.** The operation must still do what its name
   says. A measurement whose operation quietly failed is the most dangerous output a harness can
   produce: it is fast, it is stable, and it is a number for the refusal path.
6. **An immutable manifest written before either arm runs**, carrying both commits with recursive
   submodule revisions, the clean-tree assertion or the retained patch, the resolved dependency
   graph, and the SDK and runtime identity.
7. **Effective, not requested, configuration.** Each measured child reports its actual RID,
   process architecture, GC mode, and tiering state, and the arm fails on a mismatch.
8. **Exactly one evidence class per bundle**, declared up front, with exactly one predeclared
   decision. A bundle that proves the harness works accepts nothing, even when every number in it
   moves the right way.

And three things this component will not do. **No benchmarking framework**, because a framework's
warmup, pilot, and outlier policies would be part of every published figure and invisible in this
repository. **No cross-profile fuel comparison**, because fuel is this profile's own unit and
means nothing beside another's. **No comparison against any other engine or component**, in
either direction, at any point.

---

## 18. Amendments this profile expects to ask of the core

The core's amendment procedure exists because a contract frozen before its first profile will
meet something it cannot express. Recording the candidates now is cheap; discovering them during
an implementation is not. Each of these is a **proposal or a refusal**, never a workaround inside
the core's execution loop, and each carries the counterweight test: would a profile with no
parser, no text format, and no dynamic loads need this too, or is this one language's need
wearing a general shape?

| Candidate | Why it might be needed | Counterweight |
|---|---|---|
| An argument and result channel on invocation | An invocation request carries one entry-point name. A host calling an already-instantiated realm has no typed way to pass values. | Weak: a profile with one fixed entry point does not need it. Section 10's two workarounds are tried first, and the proposal is opened only if both are shown to cost something real. |
| Nested instantiation through the mediator | The contract names it and version 1 provides no path to it. This profile does not need it for `eval` — section 11 shows why — but a module graph that instantiates a dependency as its own instance would. | Moderate: any profile with a module system meets it. Opened only if this profile's realm model actually requires a separate instance per module, which section 13 answers first: one instance may hold several realms, so a module needing its own realm does not by itself need its own instance. |
| A charging hook for work done inside a host capability | Wall clock covers a slow capability; it does not cover a capability that allocates on this profile's behalf. | Strong: general. |
| An in-process producer input form — compiling straight to a verified handle | Version 1 admits no other input form, so every caller-driven compile and every mediated dynamic compile serializes and re-decodes on the critical path. | Moderate: general to any composition that compiles at run time; a profile shipped as pre-built artifacts never meets it. Opened only against JS-10's verification-throughput-per-byte and cold-start figures, never against an intuition. |
| Lazy per-section verification | A browser compiles function bodies on first call and will not verify a whole bundle to run one entry point; version 1 fixes whole-artifact eager verification. | Moderate: any profile with large artifacts and a cold-start budget meets it. This profile's invariant 3 fixes the shape of any proposal it would sign: each section verified **completely** before that section's first execution, with no structural, index, stack-consistency, or handler-nesting check migrating into execution. Funded by a measurement, not by argument. |
| Streaming or incremental verification | A browser wants to verify as bytes arrive. | Strong: general, and the core already carries a registered amendment shape for it. Reopened against a measurement, not an intuition. |
| A persisted envelope | Section 16. | Strong: general, and already admitted by contract. It needs a gate rather than an amendment. |

The rule that governs all seven: **a design that can only be hosted by a second core state machine
is refused.** Exactly one core state machine and one core contract version exist in a product
graph at any time.

---

## 19. Milestones

The [status ledger](roadmap.status.md) is the authority for what has been accepted. This section
states planned work and objective exit gates only.

Two dependencies run through every milestone and are stated once. **The core is implemented, not
accepted**, so JS-0 and JS-1 build against implemented contracts while JS-2 onward additionally
depend on the core contract being accepted — a gate this component does not hold. And **owner
and reviewer roles are named per milestone**; where one person holds several, the
non-independence is recorded as a limit on what these gates prove, not resolved by assertion.

### JS-0 — Boundary, placement, identity, and the assurance floor

- **Owner:** profile architecture owner, with the core's topology owner co-signing placement and
  the release owner co-signing the licence position.
- **Next action:** Decide and record, each as a dated decision with a registered rule and a
  passing witness: where this component lives relative to the core and the aggregate repository;
  the profile ID `broiler.javascript` and the `Broiler.*` package identity it obliges; the
  assembly topology of section 5 and whether the profile is one assembly or several; the feature
  manifest allocation of section 6; the three composition labels and which are advertised (none,
  at first); the waited-on set and the snapshot stop condition of section 4.2; the nullable and
  unsafe-code positions the seed forces; and the satellite-acquisition dependency and its owner.
  Stand up this component's own assurance system — annotation grammar, exemption predicate,
  generated review report, fingerprint binding, release-mode gate — and its own evidence-bundle
  contract and collection script. Publish the licence and third-party notice.
- **Dependencies:** Named ownership. No dependency on the seed, on the copy, or on any core
  milestone's acceptance.
- **Objective exit gate:** An acyclic shell graph builds Release with zero warnings; architecture
  rules express every forbidden edge **including both halves of the legacy-boundary rule**, each
  with a passing witness and a negative control that fails when injected and passes after revert;
  a scan asserts no source file, project file, or build item resolves outside the component root,
  and an unresolvable build item is **reported rather than skipped**; the public API baseline
  mechanism exists and compares in both directions, with an injected member failing it and a
  deleted member failing it too; the assurance generator is a fixed point — a regeneration moves
  no byte — and a negative control proves it refuses to write a reviewer identifier no source
  line carries; the release-mode gate names each blocking declaration individually rather than
  counting them; the evidence-collection script exists and this milestone's own bundle was
  produced by it; the snapshot identity schema is recorded and **a second checkout re-derives the
  same identity from the record**; and the licence and notice carry the Apache-2.0 text, the
  upstream derivation, and the marking of modified files.
- **Seed:** Nothing is copied. Every mechanism here is this component's own code.

### JS-1 — Close the whole contract loop on the smallest JavaScript that is still JavaScript

- **Owner:** profile contract owner, with release and AOT review of the composition root.
- **Next action:** Mint `broiler.javascript.slice` and define format version 1 for it. Write the
  verifier over the core's bounded reader and allocator, supplying the bounds projection and the
  allocation-meter adapter. Implement all seven core-facing types. Fill every descriptor row in
  one full-arity construction, with the language-shaped rows of section 8 marked **provisional**
  and each naming the milestone that will settle it. Write the lowering for this slice by hand in
  the lowering sibling. Stand up the execution-only composition root with a closure self-report
  mode. Decide and record the entry-point answer from section 10.
- **Dependencies:** JS-0. Deliberately **not** the copy, not a parser, and not core acceptance:
  the point of this milestone is to find contract defects against about two thousand readable
  lines rather than against a copied engine.
- **Objective exit gate:** The named execution-only composition **publishes and runs** on every
  claimed RID — the set named here, non-vacuously — under JIT, trimmed self-contained, and
  Native AOT with trim and AOT warnings treated as errors, executing a verified artifact to its
  expected answer in every mode, each closure report containing exactly the declared assemblies
  and no test, reflection, dynamic-code, or IL-emission assembly. **Each of the five verifier
  outcomes** is produced by a named retained corpus case, the invalid-artifact case carrying a
  diagnostic code and a source position and the exhaustion case naming one dimension and one
  scope. **Each of the five execution-step kinds** is produced by a named test, including a
  contract violation from a deliberately non-conforming variant; if `Suspended` is unreachable
  from this surface the milestone declares it produced at JS-7 rather than minting an
  out-of-manifest opcode. The descriptor is admitted by a catalog build, and named negative cases
  produce each catalog refusal this descriptor can provoke. An artifact naming an absent profile
  answers `UnsupportedProfile` / `ProfileNotInCatalog` **with no payload byte examined**; one
  naming an unaccepted manifest answers `UnsupportedFeatureManifest`; one naming an out-of-range
  format version answers `UnsupportedProfileFormatVersion`. A second profile composed in the same
  catalog proves a foreign payload is dropped rather than projected, and every payload kind this
  profile can mint lies inside its declared range. A case proves the executor sizes its operand
  stack from a bound **computed at verification and stored on the verified state**, never from a
  number the payload chose. The descriptor is reachable through exactly one static accessor, and
  no aggregate profile-listing type exists in the graph. A permutation of registration orders
  over the same descriptor set produces a byte-identical catalog identity encoding. A case
  mutates, disposes, and concurrently overwrites the caller's payload buffer after verification
  returns, and neither the verified state nor the execution result changes. The slice corpus
  replays identically twice with no residue, contains at least one successful control entry, and
  the verifier throws on none of it.
- **Seed:** Nothing. This milestone's hand-written encoder and lowering are **scheduled for
  deletion at JS-4** with a named owner and a gate clause, because a second handle-producing path
  and a second lowering are non-goals.

### JS-2 — Take the snapshot; make the copied front end this component's own code

- **Owner:** profile front-end owner, with the release owner co-signing the attribution change.
- **Next action:** Record the snapshot recursively. Copy the tokenizer, the syntax tree and its
  visitors, the parse-time binding and scope analysis, the free-name analysis, and the allocation
  and string primitives. Decide and record whether the few neighbouring primitives the tree
  consumes are copied in or replaced. Rename every namespace to match its assembly on the first
  commit. Delete the dead attribute family and every conditional-compilation directive. Replace
  the ambient parse-goal and top-level-await reads with an explicit options value. Take the
  deep-nesting decision of section 9. Annotate every copied unit as ported.
- **Dependencies:** JS-1, plus two external gates: **the core contract accepted**, which is open
  today and is recorded in the ledger as a named blocker with its holder and unblock condition;
  and the per-item ruling of section 4.2. Plus the nullable and unsafe positions from JS-0, which
  must be settled before the first compile.
- **Objective exit gate:** The snapshot identity is recorded recursively and re-derivable; the
  two-way boundary rule passes with its witnesses; the copied front end builds with the trim and
  AOT analyzers **force-enabled**, producing zero trim and AOT warnings **anywhere in its
  reference closure** rather than merely none attributed to the project, and a metadata scan finds
  no IL-emission assembly reference; scans assert zero conditional-compilation directives in
  covered files, zero occurrences of any legacy assembly name in any namespace, header, or
  documentation comment, and zero uses of assembly loading, name-based type resolution, activator
  construction, run-time generic construction, dynamic-method emission, IL generation, module
  initializers, or reflective member read or write; the parser takes goal and top-level-await
  permission as constructor arguments, a metadata scan finds no thread-static field and no
  ambient async-local type in the assembly, and **two parses with different goals run
  concurrently in one process each producing the goal-appropriate result, in a test that fails
  when the options are replaced by a shared static**; a nesting corpus proves a deeply nested
  program is refused rather than terminating the process; every relevant copied unit carries a
  parsed annotation with a current fingerprint, no placeholder, and a falsification criterion on
  every unit assessed at the top of the security vocabulary; the licence and notice changes are
  landed and the core's standing third-party claim is confirmed scoped or amended; and a **scan**
  over this component's roadmap and evidence tree finds no identifier from any other component
  cited as evidence.
- **Seed:** Section 4.3's copy table. Not copied: the expression-model seam, the interop surface,
  the dynamic-metaobject surface, the module hosts, the dead attribute family, and the
  module-initializer bootstrap.

### JS-3 — Static semantics as one verification stage; the diagnostic registry; the oracle

- **Owner:** verification-boundary owner, with the conformance owner for the harness half.
- **Next action:** Consolidate every early error the first manifest requires into one validation
  stage; carry on the tree the facts the two source re-scans recover, and delete the re-scans;
  take and record the strict-mode ownership decision; publish and version the diagnostic-code
  registry and the source-position encoding; write the lowering that feeds the one verification
  entry point. Then pin a suite revision and build the harness, the self-check, the merge, the
  scope manifests, and the audit command, and run the parse-and-early-error slice.
- **Dependencies:** JS-2 for the copied analysis; JS-1 for the format and the verifier shape.
- **Objective exit gate:** The diagnostic-code registry is published, versioned, and bound in
  **both** directions — every emittable code appears in it, every code in it is reachable from a
  named case; every early error the manifest requires is produced by a named case and maps onto
  exactly one core invalid-artifact reason with no invented or aliased reason; an illegal format-
  version and manifest pair is refused by this profile's own verifier with a diagnostic code; each
  artifact is tokenized at most once during verification, asserted by a case; **the self-check
  runs against the built profile before every shard** and every deliberately broken fixture
  returns its declared verdict alongside at least one passing control, with a negative control
  that injects a scoring regression, observes the mismatch, and reverts; the parse slice runs to
  completion and publishes per-host-mode totals from an exact commit and an exact suite revision,
  and **that run sets the ratchet**; removing one shard's report reports incomplete coverage
  rather than a smaller total, a configuration field differing between shards reports a named
  inconsistency, and an empty selection and an all-skipped selection are each named configuration
  failures; negative-metadata tests are executed and reported as their own totals, with the
  uncaught error matched on its JavaScript type name; the failure manifest is proved to be a queue
  by a case where a listed path still fails
  and a case where a hand-written entry does not survive; the harness, merge, audit, and scope
  tooling each carry their own regression tests run before any shard starts; a scan asserts the
  suite ingestion path appears in no product package and no closure report; and the
  narrow-runtime-compiler composition publishes and runs on every claimed RID with warnings as
  errors, its closure containing the tokenizer and the lowering and no test assembly, and cited
  as evidence for no other composition kind.
- **Seed:** Copied and re-homed — the post-parse validation stage and the free-name analysis.
  Written fresh — the registry, the position encoding, the reason mapping, the replacement for
  both re-scans, the lowering, and the entire harness. **No total, manifest entry, or triage
  finding is carried across.**

### JS-4 — The value representation and the object model

- **Owner:** profile runtime owner.
- **Next action:** Take the section 8 decision as a numbered decision stating its consequence in
  both directions, **before any standard-library source file is copied**. Record the eight-row
  ABI with fixtures and Native AOT representation probes retained beside it. Copy the property
  storage with its tests and its recorded defect history. Replace the reflective key-table
  initialiser with a generated table under a named owner and make key identity realm-scoped.
  Amputate the dynamic-metaobject interface from the value base type. Route what the front end
  and the executor need through a realm object the composition creates. **Delete JS-1's
  hand-written encoder and lowering**, and assert the deletion.
- **Dependencies:** JS-1 and JS-2. The ABI decision is a **gate on entry**, not this milestone's
  first task.
- **Objective exit gate:** The numbered ABI decision exists with all eight rows, with fixtures and
  AOT representation probes retained; the object model builds with analyzers force-enabled and
  zero trim and AOT warnings in its closure, and a metadata test finds no dynamic-loading,
  reflection-invocation, IL-emit, reflective-member-write, thread-static, or ambient async-local
  construct, **each clause with its own witness**; two runtimes in one process each mint
  properties under the same key text and neither observes the other's storage, shape identity, or
  key identity, in a test that **fails when the key table is made process-wide again**; two
  separately compiled programs whose first cache slot carries the same index run in separate
  runtimes and are evicted with no state crossing owners; two runtimes read one shareable handle
  concurrently with no synchronisation and a **structural scan** asserts no instance-owned cache,
  shape table, feedback, or warmed structure is reachable from a handle, with the scan's
  mechanism and its residual stated; each defect the copied storage carries in its recorded
  history has a named regression that fails when the fix is reverted; the copied storage's direct
  test coverage is **measured, not merely recorded**, with covered types named and uncovered
  public behaviour named with an owner, and closed to a stated line before the milestone closes;
  the representation decision is exercised by a retained figure per value kind under section 17's
  rules; and JS-1's encoder and lowering are gone, asserted by scan.
- **Seed:** Copied with tests — shapes and the transition table, shape-only slot storage with its
  one-way materialization boundary, element arrays, the named-property store. Rewritten — the
  interned key table, the ambient context. Written fresh — the value representation, if the
  decision replaces the hierarchy.

### JS-5 — The executor: frames, calls, abrupt completion, and the budgets it charges

- **Owner:** profile runtime owner.
- **Next action:** Implement the interpreter over the ABI. Implement abrupt completion so
  `finally` runs on every applicable exit including a host exception crossing profile frames.
  Place every poll and every charge. Measure native frame cost per interpreter frame on each
  claimed RID and derive the `CallDepth` default from it. Choose the uncharged-work bound, the
  charging granularity, and the cancellation poll bound from measurement. Catch every internal
  exception at this profile's own adapter. Run the vertical-slice loop until the first executable
  increment of `broiler.javascript.core` is complete.
- **Dependencies:** JS-4.
- **Objective exit gate:** Every executor answer is one of the five step kinds and a scan asserts
  no profile code names a core outcome category; a retained nested-handler and `finally` matrix
  passes in both directions across the boundary, covering `return`, `break`, `continue`, a
  language throw, and a host exception, with return and throw replacement by `finally` covered,
  the host exception surfacing as a host failure and a language throw as a typed payload behind a
  profile fault; **the host boundary is proved at binding time** — a value capability whose
  version, signature ID, or kind does not match a declared import is refused when the runtime is
  created and not at first call, each mismatch by its own named case; a failed required import
  leaves no partially bound runtime, asserted by a case that finds no usable runtime after the
  refusal; the unbound branch of at least one optional import is exercised; a scan asserts every
  argument and result crossing the boundary is one of the core's transfer types and no CLR type
  crosses it; and the translation precedence is proved per capability, a cancellation exception
  carrying the operation's own token as cancellation, an exhausted meter at the moment of the
  catch as resource exhaustion, and anything else as a host failure naming the capability;
  **no exception escapes the executor** across the increment's corpus; the
  `CallDepth` default is derived from a retained, reproducible frame-cost measurement on each
  claimed RID, and a recursing program is refused as resource exhaustion naming `CallDepth` and
  its scope **rather than terminating the process**, on every claimed RID under Native AOT; a
  deliberately non-polling variant completes as a profile fault with the poll-bound reason and
  the runtime poisoned to accept only disposal; **a proportionality fixture exists for each named
  operation family of section 8**, each with an unsimplified control, each showing fuel charged as
  a monotone non-decreasing function of input magnitude and at least the declared ceiling, with
  the declared function and granularity recorded — and an operation family without a fixture
  does not ship in the increment; a deliberately non-charging variant is detected and reported as
  a contract violation; each new opcode adds corpus entries covering its structural, index, and
  stack-consistency rejections; and the increment's suite results are published against the
  ratchet from an exact commit with the failure manifest regenerated and no host mode regressed.
- **Seed:** Copied and re-expressed — semantic operation bodies, value-conversion rules, the call
  surface and the identities a call must preserve. Written fresh — the opcode set and its
  encoding, the dispatch loop, every metering call, and the frame-cost measurement.

### JS-6 — The standard library

- **Owner:** profile built-ins owner, with the satellite-acquisition owner outside this component.
- **Next action:** Copy the registration source generator and its attribute vocabulary, changing
  its generated prototype lookup to take a realm parameter. Copy the core library and its tests.
  Mint separate manifest identities for the temporal, internationalization, and regular-expression
  surfaces and leave all three out of `broiler.javascript.core`. Acquire the regular-expression
  matcher and the Unicode and locale data as this checkout's own dependencies and drop the dead
  date-time reference. Route regular expressions through the from-scratch matcher. Delete the
  module-initializer wiring — the initializer bodies and the satellite initializer files, and
  only those — after re-homing into the library proper the prototype patching that the same file
  happens to register. Delete the assembly probing.
- **Dependencies:** JS-4 for the object model, JS-5 for calls. **Satellite acquisition is an
  external dependency opened at JS-0**: if it has not landed, the first manifest excludes every
  surface that needs it and publishes each exclusion with its deterministic failure, rather than
  this milestone waiting.
- **Objective exit gate:** The library's closure contains no IL-emission assembly **and no call
  site constructing a compiled-mode regular expression**, each asserted by its own metadata test
  with its own witness; the generator's emitted output is compiled and walked and contains no
  run-time reflection and no ambient context read, failing when the realm parameter is replaced by
  an ambient; `broiler.javascript.core` is declared and an artifact naming an unaccepted manifest
  is refused; the copied library tests run against this component's object model with the pass
  count, the covered list, the excluded list, and a justification per exclusion recorded — and
  the milestone does not close on a recorded number alone: zero unexplained failures, every
  exclusion owned; the satellites resolve from this checkout with nothing resolving outside the
  component root; and the compositions from JS-1 and JS-3 still publish and run with the library
  linked, closure reports unchanged in shape.
- **Seed:** Copied — the source generator and attribute vocabulary, the core library, and its
  tests **as a port wherever the value model changed at JS-4, and labelled as such**. Deleted at
  ingest — the dead attribute family, the dead date-time reference, the module-initializer
  wiring itself, the assembly probing. Re-homed rather than deleted — the prototype patching
  that wiring registers. Excluded by name — the interop assembly and the module hosts.

### JS-7 — Suspension: generators, async functions, top-level await, terminal unwind

- **Owner:** profile runtime owner.
- **Next action:** Make the executor's continuation capturable and reconstitutable on the heap.
  Implement generators and async functions on it. Take and record the routing decision of section
  12 per pause kind, **with the live-suspension count a representative workload produces**.
  Declare asynchronous instantiation and implement top-level await. Decide and declare external
  suspension. Write the terminal-unwind entry point and defend the abandon budget. Publish the
  safepoint-density statement.
- **Dependencies:** JS-5 for the frame model, JS-6 for the prototypes and job-queue types
  generators and promises need. **The JS-7/JS-8 edge runs one way only**: JS-8 depends on JS-7's
  continuation capture, and JS-7 depends on nothing JS-8 delivers. Where a module graph's
  dependencies arrive through the mediator, that is a JS-8 concern operating on a JS-7 mechanism
  — and a guest-initiated load may not itself suspend, which is what keeps the edge acyclic
  rather than merely asserted to be.
- **Objective exit gate:** A generator and an async function each suspend and resume across at
  least two suspensions, **proved by a test that resumes on a different thread than the one that
  suspended**; a second resume, a resume after cancellation or disposal, and a resume presented
  to a runtime that does not own the continuation each return the named invalid-state reason; a
  suspended operation is cancelled and disposed **without ever being resumed**, on the disposing
  thread, with no instance published, the terminal unwind run under the tighter of the abandon and
  unwind budgets, and the release order observed; a budget snapshot across a suspension shows fuel,
  allocated bytes, host calls, and the nested-load counters frozen, the wall clock paused under
  every origin, and live bytes and live runtimes still metered; a module with top-level await
  suspends during instantiation, publishes **no** instance while suspended, resumes to a live
  instance, and a resume that suspends again is covered, while an undeclared park returns the
  named invalid-state reason and is not resumable; a composition that does not enable external
  suspension answers `ExternalSuspensionNotEnabled` and a descriptor that does not declare it
  answers `ExternalSuspensionNotDeclared`, distinguishably; the residency and live-suspension
  bounds each have a named case; the routing decision is recorded with its count; the terminal
  unwind runs no guest code able to request a load or to suspend, asserted by a case; a scan
  asserts no public member returns a task, value task, or custom awaitable, no product type
  implements a completion-notification interface, and no product assembly references a timer,
  delay, or thread-abort API, **each clause with its own witness**; and the suspension and handler
  framing add their own corpus entries.
- **Seed:** Copied as specification only — completion-record semantics, abrupt-completion cases,
  generator resumption semantics, module specifier and binding semantics. Written fresh —
  continuation capture by unwinding rather than by an IL-emitting state-machine rewriter, the
  suspension projection, the terminal-unwind entry point, and every test that pins a pause.

### JS-8 — Guest-initiated loads and the three compositions

- **Owner:** profile security owner with the host-capability owner.
- **Next action:** Declare guest-initiated loads with finite maxima for all four bounds and a
  defended verifier-work-to-fuel rate. Route `eval`, the `Function` constructor, and dynamic
  `import()` through the mediator and remove every alternative byte source. Implement the
  conversion table. Replace the textual direct-`eval` decision with one the front end records, or
  record the deviation. Build the two compositions the claim needs — one registering a provider,
  one registering none — plus the general-runtime-compiler root.
- **Dependencies:** JS-5, JS-6, JS-7, and JS-0's placement ruling for where the lowering assembly
  may be referenced from.
- **Objective exit gate:** The declaration is admitted and named negative cases produce each of
  the guest-load catalog refusals; an architecture test asserts the profile assembly reaches no
  filesystem, socket, embedded resource, byte-returning host object, or in-process lowering
  shortcut, **with the check's mechanism and its residual stated**; registering value capabilities
  never satisfies an artifact-provider import, proved by a composition that registers only value
  capabilities and is refused when the runtime is created; a composition registering no
  provider refuses every request **before the request payload is inspected**, and a test asserts
  the refusal counter is non-zero on an operation that completed normally because guest code
  caught the resulting language error; the admission order is asserted step by step — depth,
  then fan-out, then already-exhausted allowances, all before the provider is called; then one
  host-call unit plus elapsed wall clock; then the returned length against the nested-bytes bound
  with an over-bound artifact **dropped unverified**; the conversion table passes case by case,
  with a variant surfacing an unconverted nested failure reported as such, and nested exhaustion
  and cancellation each proved **uncatchable from guest code** with bounded unwinding; a mediator
  used past its invocation is refused; a nested handle presented to a second runtime is refused
  **before** identity comparison and no member hands one to the host; the malformed corpus is
  **replayed through the nested path**; and each of the three compositions publishes and runs on
  every claimed RID with warnings as errors, the execution-only closure containing no lowering and
  each runtime-compiler closure containing one, with no publish cited as evidence for another kind.
- **Seed:** Copied and rewritten — the single runtime-owned indirection the two dynamic entry
  points already funnel through, which becomes the mediator adapter; specifier resolution and
  import-syntax lowering, re-homed into the lowering sibling; the direct-`eval` early-error
  validation. Written fresh — the declaration and its bounds, the conversion table, the provider
  adapter, and the direct-`eval` decision.

### JS-9 — Adversarial input, agents, and soak

- **Owner:** profile security owner with the fuzz-corpus owner.
- **Next action:** Grow the malformed corpus from slice scope to the full format. **Fuzz all four
  untrusted-input surfaces** — the verifier, the source parser, the regular-expression matcher
  over pattern and subject, and the executor over verified-but-adversarial artifacts — with
  recorded seeds, budgets, and runtime settings. Design and implement retained-bytes reporting
  over the object model and state the limits of what it measures. Run a soak over recycled
  runtimes. Exercise sibling runtimes under one aggregate budget.
- **Dependencies:** JS-5 through JS-8.
- **Objective exit gate:** Every entry in the full corpus produces its recorded outcome, reason,
  and diagnostic code on JIT, trimmed, and Native AOT hosts, the verifier throws on none, control
  entries verify successfully, and a repeat leaves no residue; a **mutated corpus entry** proves
  the replay detects a changed observed triple; each fuzz session retains its corpus identity,
  its iteration budget with a stated floor, its runtime settings, and **every minimized
  counterexample**, and any counterexample is closed by a **named regression, never an allow-list
  entry**; the compile-time nesting bound holds under fuzz; a soak over a recorded number of
  lifecycle cycles across recycled runtimes reaches a stated heap plateau and a disposed runtime
  leaves no per-thread state, each with a named regression that fails when the fix is reverted;
  two runtimes under one aggregate budget together spend no more than the parent's allowance,
  disposing a parent with live children is refused, sealing drains, and **no test asserts which
  sibling observes a shared-parent exhaustion**; and every negative control in this milestone's
  bundle fails when injected and passes after revert, with the running count recorded.
- **Seed:** Copied and rewritten — the corpus manifest schema, the negative-control discipline,
  the collection script that judges nothing. Written fresh — every corpus entry, every fuzz
  result, every retained-bytes report, every measurement. Defects the seed recorded are
  **hypotheses this component may test, carried without their numbers.**

### JS-10 — Baselines, packaging, the support table, and the release gate

- **Owner:** release owner with the package, security, API, performance, and documentation owners.
- **Next action:** Stand up the controlled measurement lane and take this component's own
  baselines under section 17. Resolve JS-0's packaging decision into a shipped identity or a
  stated refusal. Publish the support table and the composition register. Claim a RID only where a
  retained bundle published and ran the named composition on it. Run the release gate that
  refuses the tree while any relevant unit lacks a human decision.
- **Dependencies:** JS-3 and JS-9 for evidence, JS-8 for the composition set, JS-0 for the
  packaging ruling, and **a named human reading every relevant unit** — the largest
  single-owner task in the programme, decomposed and scheduled rather than assumed.
- **Objective exit gate:** Every published figure declares exactly one evidence class and returns
  exactly one predeclared decision, with an immutable manifest written before either arm ran, a
  comparable control, an A/A lane result, every repetition retained, and each measured child's
  effective configuration reported — and a candidate-versus-control difference smaller than the
  A/A difference is reported **below resolution**, not as a result; the baseline register and the
  retained log agree in both directions on both lanes, asserted by a rule; the support table names
  the core contract version **implemented** and the minimum **accepted** as two separate integers,
  plus the accepted format-version range, the accepted manifest set, and the conformance manifest
  identity and version, uses a vocabulary that never reads as a bare yes, gives every row an
  evidence cell naming a rule or a retained artifact, names a deterministic failure or an
  exclusion for every unimplemented capability, distinguishes what the contract admits from what
  this profile implements from what each composition provides, and closes with a section stating
  what the table does not say; **the accepted manifest set contains no manifest whose oracle
  totals show it failing**; the composition register and the checkout agree in both directions;
  every claimed RID has a retained publish-and-run bundle with its closure report, and every
  unclaimed one is listed with its reason; a pristine consumer restores and runs from a source
  containing only this component's packages with upstream feeds unreachable, and a rollback to the
  previous package set runs unchanged; the release gate refuses on each of its conditions, naming
  each blocker by its declaration, with a negative control proving the generator cannot invent a
  reviewer; a named human decision exists on **every** relevant unit before the first publish;
  every suppression is inventoried with an owner and a reachability argument; and no figure,
  total, claim, or platform result from any other component appears anywhere.
- **Seed:** Nothing. Every figure is this component's own, from this component's own lane and
  commit.

---

## 20. Delivery order

```text
     JS-0  boundary, placement, identity, assurance floor, evidence contract
        │        no copied line yet, no product code
        │
        └→ JS-1  the whole contract loop on a narrow slice, written fresh
             │        publish-and-run on the smallest closure
             │
             └→ JS-2  seeding snapshot; the front end becomes this component's code
                  │        ←── (core contract accepted): external gate, held by
                  │            the core, open today — it binds JS-2 onward and
                  │            binds neither JS-0 nor JS-1
                  │        ←── the copy lands here, behind the boundary rules
                  │
                  └→ JS-3  static semantics, diagnostic registry, the oracle
                       │        ←── an external correctness signal from here on
                       │
                       └→ JS-4  value representation decided; the object model
                            │
                            └→ JS-5  executor, abrupt completion, measured budgets
                                 │
                                 └→ JS-6  standard library; the core manifest
                                      │      ←── satellite acquisition lands
                                      │
                                      └→ JS-7  suspension; terminal unwind
                                           │
                                           └→ JS-8  guest loads; three compositions
                                                │
                                                └→ JS-9  corpus, fuzz, soak, agents
                                                     │
                                                     └→ JS-10 baselines, packaging,
                                                          │    support table,
                                                          │    release gate
                                                          │
                                                          └→ (an advertised composition:
                                                              a release decision)

Manifest increments 2..n re-enter JS-5's vertical-slice loop: each mints one
further feature-manifest identity, extends the retained corpus, re-runs the
oracle against the ratchet, and closes no milestone.
```

What this ordering does and does not imply:

- **Read the two arrow kinds differently.** A `└→` edge is milestone precedence. A `←──`
  annotation marks an input or an external gate entering at that node and constrains nothing
  above it.
- **Nothing here waits on a core milestone's *evidence*, and no gate here closes a core gate.**
  JS-0 and JS-1 depend on the core being *implemented*, which is why the acceptance gate hangs
  off JS-2 in the diagram rather than off the root. JS-2 onward additionally depend on the core
  contract being *accepted*, which this component does not hold and must record as a blocker
  rather than route around.
- **One pair can be staffed in parallel, and the chain above should not be read as forbidding
  it.** JS-3 and JS-4 are gated on JS-1 and JS-2 and on nothing else, and they are different
  skills with different owners: the verification-boundary and conformance owners hold JS-3's
  registry and harness, the profile runtime owner holds JS-4's ABI and object model. Once JS-2
  closes, both may open. Every other edge in the diagram is a real prerequisite — **JS-8 depends
  on JS-7 and cannot be staffed beside it.**
- **Several decisions need no copied code** and may be opened against JS-1 rather than waiting on
  the acceptance gate: the diagnostic registry and position encoding, the value and frame ABI,
  the continuation design, and the suspension-versus-job-queue routing. A team that reaches the
  acceptance gate after JS-1 should have prepared work rather than a hard stop.
- **Two milestones carry the bulk of the cost**, and an eleven-milestone diagram should not be
  read as eleven equal steps: JS-4, which is the ABI plus the object model, and JS-6, which is
  the standard library.
- **Manifest increments are not milestones.** Each mints one identity with a reviewed scope,
  extends the corpus, and re-runs the oracle. The admission criterion for the next increment is
  section 6's allocation table, not a judgement made per commit.

---

## 21. Test and evidence matrix

| Area | Required tests/evidence | Failure that blocks release |
|---|---|---|
| Dependency architecture | acyclic graph asserted against a checked-in manifest in both directions; exact profile reference set read from project text and from metadata; no edge to a legacy component in either direction, inbound recording its branch; no dynamic loading, reflection invocation, IL emit, reflective member write, or module initializer; no aggregate profile-listing type; namespace-matches-assembly scan; per-clause witnesses | any forbidden project or assembly edge, an unresolvable build item cleared as a pass, undeclared dynamic loading, a namespace that does not match its assembly, a registered rule with no witness |
| Identity and registration | descriptor admitted; one named negative case per catalog refusal the descriptor can provoke; identity grammar bounds; reserved-namespace and package-identity pairing; manifest namespace containment; payload-kind range containment; permutation of registration orders producing byte-identical catalog encodings | a descriptor admitted that should be refused, a refusal reported with the wrong reason, a payload kind outside the declared range, an encoding that depends on declaration order |
| Format and verifier safety | five verifier outcomes each by a named case; retained malformed corpus with expected-and-observed triples and successful control entries; double replay with no residue; ordering assertions — ceilings before the first byte, refusal before allocation, bound before declared-count use; capability-absent and capability-throws verification; caller-buffer mutation, disposal, and concurrent overwrite after return; bounded-read statuses mapped; corpus extended at every format-growing milestone and replayed through the nested path; coverage-guided fuzzing with minimized regressions | invalid input executes, a verifier throws, a late check is reported as a language fault, a declared count sizes an allocation before its bound comparison, a corpus in which nothing verifies successfully, a fuzz counterexample closed by an allow-list entry |
| Front end | explicit parse options with a concurrent two-goal test; zero ambient or thread-local reads; zero conditional-compilation directives; closure-wide trim and AOT analyzer cleanliness; early-error corpus with one diagnostic per case; single-tokenization assertion; compile-time nesting bound; no-reparse invariant | a parse that depends on ambient state, a warning anywhere in the closure, a source re-scan surviving, a deeply nested program terminating the process |
| Value model and storage | numbered ABI decision with fixtures and AOT representation probes; two-runtime key and shape isolation with its named falsifier; same-slot-index eviction test; handle-immutability structural scan plus concurrent read; a regression per recorded storage defect; measured storage coverage with owned exclusions | process-wide key or shape state, mutable state reachable from a handle, a cache slot keyed process-globally, a recorded defect without a regression |
| Executor and lifecycle | five step kinds each by a named case; handler and `finally` matrix in both directions across the boundary; outcome-to-instance-state mapping; no exception escaping; poll-bound breach poisoning the runtime; measured `CallDepth` with a recursion case per claimed RID; a proportionality fixture per named operation family with its declared function, granularity, and an unsimplified control | a language fault reported as a core category, an exception escaping into the core, a process termination on recursion, an operation family shipping without a proportionality fixture, a flat charge passing as proportional |
| Suspension | cross-thread resume across two suspensions; cancel-and-dispose of a suspended operation with no instance published; single-use continuation; frozen-and-paused budget snapshot; undeclared-park classification; residency and live-suspension bound classifications; terminal-unwind guest-code exclusion; awaitable and timer absence scans | a thread held across a pause, a continuation reused, an undeclared park reported as anything but the named invalid state, unbounded suspended residency, an awaitable on a public member |
| Guest loads and policy | no-provider refusal before payload inspection; refusal counter non-zero on a normal result; ordered admission assertions; conversion table case by case; mediator out-of-scope; nested handle non-shareability before identity comparison; byte-source exclusivity scan; nested-path corpus replay | a catchable resource exhaustion or cancellation in guest code, a nested failure surfaced unconverted, a byte source other than the mediator, a refusal with no recorded evidence |
| Host boundary | binding-time signature, version, and kind refusals; no partial binding on a failed required import; unbound-optional branch exercised; transfer-type closure; exception-translation precedence per capability | a mismatch discovered at first call, a partially bound runtime, a CLR type crossing the boundary, a capability with no declared translation mode |
| Standard library | generated output walked for reflection and ambient reads; compiled-mode regular-expression call-site absence with its own witness; per-manifest exclusion list with justifications; ported test corpus with zero unexplained failures | dynamic code inside the standard library, a generated ambient read, an unexplained library failure, an unowned exclusion |
| Conformance | pinned suite revision; self-check with failing **and** passing fixtures before every shard, plus an injected-and-reverted scoring regression; per-host-mode totals; negative-metadata totals; merge configuration-failure kinds; failure manifest as a queue; ratchet not regressed; per-manifest attribution; the harness's own regression suite | a failing test reported as a pass, a mode selecting files and executing none, a green run with zero executed tests, a regression against the ratchet, a claimed manifest whose totals show it failing |
| Native AOT | publish-and-run per claimed RID per composition, warnings as errors, closure report attached; execution-only and runtime-compiler evidence kept separate; suppressions inventoried with owner and reachability | an AOT claim derived from a property, an analyzer, or a non-AOT publish; a closure containing a test, reflection, or dynamic-code assembly; one composition's publish cited for another |
| Packaging and consumers | package count and identity; produced metadata declaring no foreign dependency; pristine-feed consumer restore-and-run; exercised rollback | a package that resolves a dependency from the internet, a packable identity outside the dated budget, a rollback that does not run |
| Assurance and review | generator fixed point; refusal-to-invent-a-reviewer negative control; per-declaration blocker naming; origin distribution published; review-mark vocabulary | a generated artifact differing from what the generator would write, a reviewer identifier no source line carries, a stale fingerprint at publish, an unreviewed relevant unit at release |
| Measurement | evidence class declared; immutable pre-run manifest; comparable control; A/A lane; every repetition; effective-configuration attestation; register bound to log in both directions | a figure without a control, an envelope widened after seeing a candidate, an effective-versus-requested mismatch, a cross-profile or cross-component comparison |

Generated results are evidence artifacts, not substitutes for pinned manifests and durable
summaries. Every accepted bundle records source revision with recursive submodule revisions, clean
or dirty inputs, SDK and runtime, publish properties, core contract version, RID and device,
effective GC/JIT/AOT state, commands, and raw outputs — and every bundle states its
negative-control count, which grows.

---

## 22. Release gates

A `Broiler.VM.Profile.JavaScript` preview or stable release must satisfy all applicable gates:

1. **Support truth:** the support table names the implemented and minimum-accepted core contract
   versions as two separate integers, the accepted format-version range, the accepted manifest
   set, and the conformance manifest identity and version; every unimplemented capability has a
   named deterministic failure or a named exclusion; composition label, contract admission, and
   implemented feature are kept apart per row; no row reads as a bare yes; and no figure from any
   other component appears.
2. **Graph and registration:** the graph is acyclic and matches its manifest; the profile
   reference set is exactly the two core assemblies; no edge reaches a legacy component in either
   direction; registration is static and typed, with no reflection, dynamic loading, IL emit, or
   module initializer anywhere in a product closure.
3. **Correctness and safety:** the malformed corpus replays with zero unexplained differences on
   all three publish modes; the verifier throws on nothing; every fuzz counterexample is closed by
   a named regression; verification is separable from execution and there is exactly one verifier.
4. **Lifecycle and results:** the step-kind mapping holds; no exception escapes into the core; no
   core outcome category or reason code is added; every pause is a suspension holding no thread;
   the terminal unwind and the release order are observed.
5. **Guest loads and policy:** a composition registering no provider refuses deterministically
   with recorded evidence; the conversion table holds in both directions; exhaustion and
   cancellation are not catchable from guest code.
6. **Host boundary:** every declared import binds by exact capability ID, version, signature ID,
   and kind when the runtime is created, or the runtime is refused; no required-import failure
   leaves a partially bound runtime; every optional import has its unbound branch exercised; only
   the core's transfer types cross the boundary; and every capability declares a translation mode
   whose precedence is proved.
7. **Native AOT:** each advertised composition publishes **and runs** on its declared matrix with
   trim and AOT warnings treated as errors, closure reports attached, suppressions reviewed and
   scoped. *A linker annotation without execution is insufficient.*
8. **Packages and consumers:** the packable set matches its dated budget; produced metadata
   declares no foreign dependency; a pristine consumer restores and runs; rollback is exercised.
9. **Conformance:** a release-candidate run of the pinned suite exists from an exact commit with
   retained artifacts; the ratchet is not regressed; every claimed manifest has its own totals;
   the failure manifest is generated from that run.
10. **Measurement honesty:** this profile's own overhead is published with its method and its
   limits; no claim is made without a predeclared rule, a comparable control, an A/A lane, and
   retained repetitions; fuel figures are never compared across profiles and no figure is cited
   from any other component.
11. **Human review:** no package is published, no RID is claimed, no support table is issued, and
    no milestone moves to accepted until a named human has recorded a decision on every relevant
    code unit, bound to that declaration's fingerprint.
12. **Licence and attribution:** this component's licence and notices carry the upstream
    derivation, modified files are marked as changed, and no standing third-party claim elsewhere
    is falsified by what this component ships.

Recertification is required when the SDK or runtime, core contract version, package graph, host
capability surface, Native AOT settings, RID matrix, cache identity, resource defaults, pinned
suite revision, or representative workload changes — and, per affected record, the ledger states
what recertifies unchanged, what must be re-collected, and what is superseded.

---

## 23. Risks and stop conditions

| Risk | Mitigation / stop condition |
|---|---|
| The copied seed quietly becomes a dependency — through a package reference, a shared-source item resolving outside the root, or a fix ported back across the fork. | Both halves are architecture rules with per-clause witnesses, including an item rule that **reports rather than skips** an unresolved build path; the restore configuration makes a legacy package reference unresolvable rather than merely detected; the snapshot is a recursive commit set. **Stop: a build edge in either direction, or a fix ported across the fork, stops the milestone. Fixes do not flow across the fork and neither side is the other's upstream.** |
| The value-representation decision is taken late or implicitly, and the standard library lands typed against a base type this profile then cannot change. | The decision is numbered, states its consequence in both directions, and is a gate on entry to JS-4. **Stop: no standard-library source file is copied while the decision is open; if the answer is replace, JS-6 is re-scoped from a copy to a rewrite before it starts, not during it.** |
| A verification check migrates out of verification into first execution, because a lazily compiling engine naturally defers function-body checks. | Invalid-artifact is illegal at instantiation, invocation, and resume by the core's own stage matrix; the corpus asserts every structural rejection happens at verification. **Stop: a late check reported as a language fault is a release blocker, because it makes a malformed artifact indistinguishable from a language error and silently hollows out the corpus.** |
| The oracle reports a failure as a pass, or a green run means nothing. | Failing **and** passing self-check fixtures run before every shard, with an injected-and-reverted scoring regression; per-host-mode totals; configuration failures rather than green results; a ratchet no later run may regress. **Stop: a self-check mismatch stops the run, a green run with zero executed tests is never a pass, and a regression against the ratchet fails the milestone.** |
| A published claim is untruthful — a composition label read as a capability claim, contract admission read as an implemented feature, or an execution-only publish promoted into evidence for a compiler-bearing one. | The support table separates the three facts per row, each with its own evidence cell; a composition label describes when source is compiled; no publish is cited for another kind. **Stop: a difficult or slow milestone is not itself a stop condition; an untruthful support claim is.** |
| Unreviewed copied units accumulate faster than anyone reads them, and a passing suite over them reads as assurance. | Annotation at ingest with a ported origin; a generator that refuses to invent a reviewer; decisions bound to fingerprints so a changed unit reports stale; a release gate naming each blocker by its declaration; the origin distribution published. **Stop: no publish, no claimed RID, no support table, and no accepted milestone while any relevant unit lacks a decision.** |
| Guest-controlled superlinear cost is not charged proportionally, so a bounded budget bounds nothing. | Per-family declared monotone charging functions with a declared granularity and a ceiling floor, each with a retained fixture and an unsimplified control; an uncharged-work breach is a profile fault that poisons the runtime. **Stop: an operation family without a proportionality fixture does not ship in the increment.** |
| A deeply nested program terminates the process at parse, validation, or lowering time, where `CallDepth` does not reach. | An explicit compile-time depth bound or a worklist rewrite, with a nesting corpus that must be refused; the seed's segmentation mitigation adopted only with the worklist named as a deferred risk. **Stop: a stack overflow is not translatable and claiming to handle it would be an untruthful capability claim; a process termination on a nesting case blocks the milestone.** |
| Dynamic code hides inside the standard library, where an emitter-reference scan does not look — a compiled-mode regular expression emitting and retaining a method per pattern. | A separate metadata test with its own witness for the compiled-mode call site, independent of the emitter-reference scan; routing through the from-scratch matcher. **Stop: if the matcher cannot carry the pinned surface, ship interpreted-only and record the consequence; do not reintroduce the compiled path.** |
| A JavaScript requirement maps onto no row of the core's profile checklist, and pressure builds to work around it inside the core. | Section 18's amendment proposals, each naming the driving capability, the profile-owned design tried and rejected, and the counterweight check — or a recorded refusal. **Stop: a design that can only be hosted by a second core state machine is refused; exactly one core state machine and one core contract version exist in the product graph at any time, and no language-specific path is added to the core's execution loop.** |
| Placement or assembly topology is assumed rather than decided, and the layout is illegal under rules that are active today. | Placement, the profile-ID and package-ID pairing, and the assembly topology are dated decisions with the core's topology owner co-signing, each enforced by a registered rule with a witness. **Stop: no product code lands while placement is open, and no milestone assumes a sibling layout works today.** |
| The programme stalls indefinitely on preconditions this component does not control. | The waited-on set is itemised per open item with a stated reason; a snapshot-as-is date or commit-count budget is recorded with a named owner; decisions needing no copied code are opened against JS-1. **Stop: a milestone blocked by a named external dependency is recorded blocked with its holder and its unblock condition — lack of scheduling is not a blocker, and an unaccepted contract is.** |
| Mutable optimization state becomes reachable from a shared handle, or is keyed process-globally so two runtimes collide. | Program-relative slots owned and reclaimed with the program, function, or runtime; the same-slot-index eviction test; the two-runtime key and shape isolation test with its named falsifier; nothing warmed or process-local is serialized. **Stop: any such reachability is a defect, not a tuning option, and the milestone does not close over it.** |
| A shared aggregate parent is treated as isolation for multi-tenant agents. | Section 13 states the channel property; hosts requiring isolation must not share a parent; no test asserts which sibling observes a shared-parent exhaustion. **Stop: an isolation claim over a shared parent is an untruthful support claim.** |
| The manifest set drifts upward one increment at a time, because each increment looks small and manifests are opaque to the core. | Each increment mints one identity with a reviewed scope, extends the corpus, and re-runs the oracle against the ratchet; the accepted set is published in every support claim. **Stop: an increment published without its own retained oracle run and corpus extension is not accepted, and no increment may be justified by claiming an earlier manifest implies it.** |
| Owner and reviewer are the same person, so no gate here is independently confirmed. | Roles are named per milestone; where one person holds several, the non-independence is recorded as a residual limit on what these gates prove rather than resolved by assertion. **Stop: a vacant role stops the point that requires it; a role held by nobody does not pass to whoever is available.** |
| A second verifier appears at build time, or a compile-to-handle shortcut is added for latency. | Verification stays separable on the ordinary surface so nothing needs its own; the one-verifier property is held from the first commit; the reopening trigger for the byte round trip is a predeclared measurement, not an argument. **Stop: two verifiers that must agree are a security defect with a schedule.** |

Stop or re-scope a milestone when the graph is cyclic; a product closure reaches dynamic code,
test tooling, or a legacy component; a verifier cannot produce an immutable bounded representation
before execution; trusted policy can be weakened by artifact input; a second core state machine is
maintained for one language; a declared Native AOT composition cannot publish and run; or the
named ownership or maintenance ceiling is absent. **A difficult or slow milestone is not itself a
stop condition; an untruthful support claim is.**

---

## 24. Specification and platform references

This roadmap records immutable revisions for implementation and release evidence. The moving
links below are discovery entry points, **not substitutes for the pinned manifests**.

- **The language specification edition**, pinned by immutable revision identifier, retrieved,
  hashed, and archived. Retrieving, hashing, and archiving a third-party document is a **human
  action**: until someone performs it the pin is provisional and carries a named exclusion in the
  ledger. JS-0 records the intended edition; JS-3 records the pin that was actually taken.
- **The conformance suite revision**, the immutable commit resolved once before any shard starts,
  never a branch name, together with the scope manifests mapping this component's assemblies to
  suite path prefixes.
- **Any host-integration specification in scope for a claimed composition**, pinned the same way.
- [.NET Native AOT deployment and limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [.NET Native AOT warning guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings)
- [.NET trimming options and analysis](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options)

**No reference here resolves into any legacy Broiler component.** These specification references
belong in this document precisely because the core's roadmap withheld them: a profile's own
specification references belong in that profile's roadmap, not there.
