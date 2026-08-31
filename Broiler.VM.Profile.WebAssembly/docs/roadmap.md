# Broiler.VM.Profile.WebAssembly roadmap

**Status:** Proposed component roadmap for the WebAssembly language profile of the Broiler.VM
execution core. [The evidence ledger](roadmap.status.md) is the authority for what has been
accepted; at the time of writing it records **WA-0 through WA-10 as not started**, and it records
that the component has no source tree, no descriptor, no pinned specification revision, and no
evidence bundle. No milestone is complete because its design appears here.

`Broiler.VM.Profile.WebAssembly` is a **language profile**: one artifact format, one decoder, one
validator, one store and instance model, one interpreter, one linker, one set of host imports, and
one conformance harness, compiled into a product by a composition root that names its descriptor
directly. It is not an execution core and owns none of the mechanism the core owns. It references
exactly two core assemblies and nothing else Broiler-owned, and no core milestone waits for it.

This component differs from the other intended first profile in the one way that matters most to a
plan: **it has no seed.** There is no existing Broiler WebAssembly engine to snapshot, no fork to
take, no inherited defect history, and no inherited review debt. Section 4 states what that costs
as well as what it buys, because "greenfield" is a description of the starting position and not an
argument that the work is smaller.

Two properties of this document are load-bearing and stated once here. **No figure, total,
conformance result, benchmark, or Native AOT sample from any other component appears anywhere in
it** — including from the core it is built on and from the sibling profile the core names beside
it. Every number this component publishes will be its own, from its own lane, at its own commit.
And **every claim about the core is checked against the shipped core assemblies rather than
against the core's prose.** Where the two disagree, this document records the code and says so;
section 7 contains one such disagreement, and it changes this component's first design decision.

---

## 1. Terminology and support claims

The core fixes most of this vocabulary and this roadmap uses it unchanged. The rows below are the
terms this component adds or narrows; where a term is the core's, that is said.

| Term | Meaning in this roadmap |
|---|---|
| **This profile** | `Broiler.VM.Profile.WebAssembly`. One profile ID, one descriptor, one verifier, one executor factory, one payload-kind range. |
| **The core** | The Broiler.VM execution core: its three packable assemblies and the numbered core contract version they carry. Core-owned terms — verified artifact, verified handle, guest-initiated load, artifact-provider capability, external suspension, deployment composition, feature manifest, core contract version, operation-result envelope — keep their core meanings. |
| **The specification** | The W3C WebAssembly core specification at one pinned, dated revision, together with its binary format, validation rules, execution semantics, and the appendices this profile implements against. A specification version name is never a conformance claim. |
| **Feature manifest** | The core's term, with this profile's content: the exact WebAssembly surface accepted by one version of this profile, minted as a `VmFeatureManifestId` under this profile's own ID. **A specification version alone is never a conformance claim**, and neither is a manifest name; a manifest claims only what its own retained oracle run shows. |
| **Manifest increment** | One further feature-manifest identity with a reviewed scope, its own corpus extension, and its own oracle run. An increment is not a milestone and closes none. This profile expects more increments than milestones, because the specification's own feature set is how its surface grows. |
| **The format version** | This profile's own integer, in the core's sense: the shape of the payload the descriptor admits. **It does not track the specification version.** The binary format's own version field has been `1` across every published specification version, so a format version derived from it would never move; the language surface is carried by the feature manifest, which is its correct home. Section 7 states what the format version does mean. |
| **Decoding** | Turning payload bytes into a structural module. The specification's first phase. In this component it is the first half of one verification, never a separate public step. |
| **Validation** | Type-checking a decoded module, including the single-pass algorithm over structured control flow. The specification's second phase. In this component it is the second half of the same verification. |
| **Linking** | Resolving a module's imports against a store and a host, and allocating its instance. The specification's third phase. In this component it happens at instantiation, and it is the one specification phase that is *not* verification. |
| **The store** | The specification's term for the mutable state holding every allocated instance, memory, table, global, and tag. **Where the store lives relative to a core instance state is the single most consequential open question in this document**, because the core offers exactly one instantiation shape and WebAssembly linking needs several instances to share one store. Section 11 enumerates the three possible answers, rejects one, and names the milestone that chooses between the other two. |
| **A trap** | The specification's term for a runtime abort. In this component a trap is a typed profile payload behind a profile fault. It is never a process failure, never a CLR exception crossing the core boundary, and never a core outcome category. |
| **DET / FUL** | The specification's *own* profiles: `DET` is its deterministic profile, `FUL` its full one. **This is a word collision with the core's "profile" and it is not resolvable by renaming either.** Wherever this document says *profile* unqualified it means the core's sense; the specification's sense is always written `DET` or `FUL`, or spelled out as "the specification's deterministic profile". Section 6 records that this component implements `DET`, and why that is a refinement rather than a subset. |
| **The oracle** | The specification's own conformance test suite, pinned at an immutable revision, run by this component's own harness, whose self-check proves that a failing test comes back as a failure before any shard is scored. |
| **The ratchet** | The first accepted per-assertion-family totals for a manifest. No later run of that manifest may regress against them. |
| **Deployment composition** | The core's term. Section 16 records that this profile mints exactly one label, and why another profile's three do not transfer to it. |

A release of this profile claims this profile: its accepted feature-manifest set, its accepted
format-version range, the core contract version it is built against, the specification revision it
was measured against, the composition it publishes and runs, and its deterministic exclusions. It
claims no instruction a manifest does not name and no capability a composition does not contain.
An unknown feature, an unsupported manifest, or an out-of-range format version is a deterministic
load failure, never a best-effort partial execution.

### Scope

This profile owns:

- its artifact shape and format-version range, and its feature manifests;
- decoding, structural validation, type and stack validation over structured control flow, and
  every profile-specific resource check, all of it inside the one verification entry point the
  core provides;
- its value, store, instance, frame, label, call, and trap model;
- import resolution and export projection: the explicit linker, its failure taxonomy, and the
  aliasing and lifetime rules for memories, tables, globals, and tags shared across instances;
- its typed normal-result and fault payloads and the projection accessors that expose them
  without adding a case to any core result enum;
- its host imports: their capability IDs, versions, signature IDs, kinds, reentrancy, thread
  affinity, and exception-translation modes, and the mapping from a WebAssembly import to one of
  them;
- its conformance harness, its pinned suite revision, its script-ingestion path, its scope
  manifests, its failure manifest, and its own regression suite for that machinery;
- its own overhead measurements, its own baseline register, and the honest limits on both; and
- its packages, its composition, its support table, and its assurance and human-review records.

The core owns, and this profile never re-implements: profile selection and the immutable catalog;
bounded byte reading, checked arithmetic, section framing, and allocation guards; the
verified-artifact handle, its identity, its leases, and its lifetime; the limit-precedence
algorithm across host ceilings, profile maxima, and artifact requests; the fifteen budget
dimensions and their metering; the lifecycle state machine, thread affinity, reentrancy,
cancellation, and idempotent disposal; guest-initiated-load mediation and its bounds; external
suspension; the profile-neutral operation-result envelopes; and the composition, trimming, and
Native AOT gates for the core boundary.

**One core primitive is in scope only in part, and that is a finding rather than a preference.**
The core's variable-length integer readers accept canonical encodings only; the specification
admits padded ones. Section 7 states the consequence, and it is why this profile decodes integers
with its own code over the core's byte primitives instead of calling the core's `TryReadVarUInt32`.

### Non-goals

- **A compiler.** This profile consumes artifacts that external toolchains already produce. There
  is no Broiler WebAssembly compiler, no lowering assembly, and no compiler sibling — the core's
  own roadmap says so — and section 5 records what follows for the assembly graph. The format
  package that exists to keep a compiler and an executor from depending on each other has nothing
  to separate here, and creating one anyway would be an assembly created to shorten a file.
- **The text format, in any product.** The specification's text format exists in this component
  only as a **test-only** ingestion path, because the conformance corpus is distributed as scripts
  written in it. A scan asserts it appears in no product package and in no published closure. Its
  absence from the product is the point; its presence in the harness is unavoidable.
- **A second execution arm.** This profile has one interpreter. It emits no IL, builds no
  expression tree, compiles no delegate, and contains no tiering path into dynamic code. There is
  no bytecode-to-IL promotion, no deoptimization, and no on-stack replacement, because there is no
  second tier for any of them to reach. A product closure containing an IL emitter is a release
  blocker, not a configuration.
- **A second validator.** Whatever validates a module is this profile's validator, reached through
  the core's one verification entry point. A build-time reimplementation that is merely supposed
  to agree with it is a security defect with a schedule attached.
- **Lazy validation.** The specification explicitly permits deferring a function body's validation
  until first invocation, with an invalid body then trapping. This profile declines that
  permission, because invariant 3 forbids a structural check migrating into execution and the
  core's stage matrix makes `InvalidArtifact` illegal at instantiation, invocation, and resume.
  Section 8 states the consequence, and it is a case where the strictly stricter reading is also
  the cheaper one.
- **A security sandbox claim.** Validation, bounded budgets, and a typed host boundary are
  correctness properties of this profile. They are not an isolation claim for untrusted modules,
  and no conformance total or benchmark result may be presented as one.
- **CLR interop.** No WebAssembly-reachable surface resolves a CLR type by name, constructs a
  generic type at run time, or enumerates CLR members. A host reaches guest code through typed,
  allowlisted, versioned capabilities and through nothing else.
- **WASI, the component model, or any embedding layer above the core specification.** Each is a
  separate specification with its own versioning and its own conformance story; each is
  expressible as host capabilities over this profile rather than inside it; and none is in any
  manifest this roadmap allocates. Naming them as out of scope is not a judgement about their
  value. It is a statement that this component's support table will not imply them.
- **The JavaScript API for WebAssembly.** `WebAssembly.Module`, `Instance`, `Memory`, `Table`, and
  `Global` belong to a *different* specification, and implementing them means crossing two core
  runtimes carrying two profiles. Section 17 works that boundary through rather than leaving a
  browser to discover it, and records it as this component's largest unpriced risk.
- **A debug wire protocol.** External suspension is a core lifecycle state; what a paused profile
  exposes is this profile's own surface, and a wire protocol is a separate component if it is ever
  wanted.
- **Filesystem, network, or module-registry ownership.** The host owns identity resolution,
  transport, content policy, and integrity checks. This profile asks; it never fetches.
- **A change to the core.** A WebAssembly requirement that the frozen contract cannot express is
  an amendment proposal or a recorded refusal (section 20). It is never a language-specific path
  added to the core's execution loop, and never a second core state machine.
- **Any performance claim about another engine.** This profile publishes its own overhead against
  its own controls. Fuel figures are not comparable across profiles and are never presented as if
  they were.

---

## 2. Engineering invariants

1. **Nothing runs that verification did not admit.** Every byte this profile executes came out of
   the core's verification entry point as an immutable, profile-bound handle. There is no second
   route, and at core contract version 1 there could not be one: verification takes a span and
   nothing else.
2. **Verification is total.** The verifier answers; it does not throw. Every rejection is one of
   the five verifier outcomes the core admits, carrying this profile's own diagnostic code and
   position. An exception escaping the verifier is a contract violation, not a rejection.
3. **A structural or type check happens at verification or it does not happen.** No decode, index,
   type, stack-consistency, or block-nesting rule migrates into first execution, and the
   specification's own permission to validate lazily is declined rather than exercised. A late
   check reported as a trap makes a malformed module indistinguishable from a program that
   trapped, and hollows out the corpus that is supposed to prove the boundary.
4. **Decoding and validation are two phases inside one verification, and their order is
   observable.** The specification requires decoding to complete before validation begins, so a
   module that is both malformed and invalid is reported malformed. The conformance suite tests
   exactly that distinction, which makes phase order a correctness property here and not an
   implementation detail.
5. **The executor answers in the core's vocabulary and no other.** Every step is one of the five
   execution-step kinds. Traps, uncaught exceptions, and link errors are typed payloads this
   profile owns; no profile code names a core outcome category, and adding an instruction never
   adds a core result case.
6. **No exception escapes into the core.** Every internal failure is caught at this profile's own
   adapter and converted. An escaped exception is a defect of this component even when the core
   survives it.
7. **Guest-controlled cost is charged proportionally.** An operation whose work grows with its
   input charges fuel as a declared monotone function of that input, at the declared granularity,
   with a retained fixture and an unsimplified control. `memory.copy`, `memory.fill`,
   `memory.init`, `table.copy`, `table.fill`, `table.init`, `array.copy`, `array.fill`, and
   `array.new_data` are each a single instruction with input-proportional work, and a flat charge
   on any of them means a bounded budget bounds nothing.
8. **The verified module is immutable; every instance is not.** The specification draws this line
   itself and the core requires it: everything reachable from a verified state must be immutable
   once verification returns and safe for unsynchronised concurrent readers. Memories, tables,
   globals, and every mutable cache belong to a store, never to a handle. Two runtimes sharing one
   verified handle share nothing mutable.
9. **The language surface grows only in reviewed increments.** Each increment mints one feature
   manifest, extends the retained corpus, and re-runs the oracle against the ratchet. No increment
   is justified by claiming an earlier manifest implies it, and none is justified by the fact that
   the specification happens to bundle its features into one version number.
10. **Unsupported surface is truthful.** Every instruction, type, or capability a manifest excludes
    has a named deterministic failure that the support table publishes. A shape-only stub does not
    satisfy a capability gate, and a specification version is not a language claim.
11. **Native AOT is demonstrated, not inferred.** Analyzer cleanliness and a trimmed build are
    inputs. The claimed composition publishes **and runs** its workload on every declared RID with
    trim and AOT warnings treated as errors, and its published closure is read off the published
    output.
12. **No evidence transfers.** No conformance result, benchmark, measurement, review decision, or
    Native AOT sample produced by any other component — the core included — is this component's
    evidence, and no gate here may cite one. Every claim starts at zero.
13. **The component is provable at every milestone.** Each milestone closes against something a
    reader can re-run: a corpus with recorded expected answers, a publish-and-run log with a
    closure report, a negative control that fails when injected and passes after revert. A gate
    that can only be closed by reading a document is a gate-design defect.
14. **This profile is the core's counterweight and behaves like one.** The core designed its
    contract against two languages and named this one as the check on whether a proposed feature is
    genuinely general or one language's need in disguise. Section 20 discharges that duty
    explicitly, and does so without a dependency edge, a citation, or a shared item identifier in
    either direction with any other profile component.

---

## 3. What the core already gives this profile, and what it refuses

The core is implemented, not paper. This section records what a profile author actually finds
there, so this roadmap plans against code rather than against prose. Nothing in it is a claim that
the core is accepted: every core milestone is in progress and unaccepted, its review record is
unsigned, and section 21 carries that as a dependency rather than assuming it away.

### The seven types this profile implements

| Type | What this profile owes it |
|---|---|
| `IVmProfileVerifier` | `Verify` over a descriptor, a payload span, a verification context, and a token, returning a `VmVerifierOutcome`. Plus three version integers — the authored core contract version, the built-against core contract version, and this profile's own verifier semantic version — and its profile ID. |
| `IVmProfileExecutor` | `Instantiate`, `Invoke`, and `Resume`, each returning a `VmExecutionStep` and each taking the operation's cancellation token; and `Unwind`, which **returns nothing** and takes a continuation plus one effective unwind allowance the core has already reduced to the tighter of the descriptor's abandon budget and the runtime's unwind budget — no token, no result. Plus its profile ID. One executor instance per runtime, created by the descriptor's factory from an `IVmExecutionEnvironment`. |
| `IVmVerifiedState` | The immutable decoded and validated module a successful verification produces. Opaque to the core; the whole of what execution may read. This is the specification's own module/instance split, and for once the core's requirement and the language's structure agree exactly. |
| `IVmInstanceState` | The mutable state instantiation produces. **Whether one of these is a whole store or one module instance within a runtime-scoped store is the open question of section 11**, and it is the only one of the seven types whose meaning this roadmap cannot yet fix. |
| `IVmProfileContinuation` | A captured, resumable suspension. Single-use, runtime-owned. Section 14 records that this profile declares no suspension at its first manifests, and why the type is implemented anyway. |
| `IVmProfilePayload` | Every value crossing back to the caller: returned values, traps, uncaught exceptions, link errors. Carries a `VmPayloadIdentity` whose kind IDs must lie inside the descriptor's declared range. |
| `IVmBoundedAllocationMeter` | The adapter that lets the core's bounded allocator charge this profile's allocations, because the core's own meter type is not public. Writing it is this profile's work, not the core's. |

The five verifier outcomes are `Verified`, `InvalidArtifact`, `ResourceExhaustion`, `Cancellation`,
and `UnsupportedProfile`. The five execution-step kinds are `Completed`, `Instantiated`,
`Suspended`, `Faulted`, and `ContractViolation`. There are no others, and this profile's whole
answer space is those two closed sets.

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
`ProfileDefaultExceedsProfileMaximum`, `GuestLoadDeclarationIncomplete`, `GuestLoadMaximumUnbounded`,
`VerifierWorkToFuelRateInvalid`, `MaxUnchargedWorkInvalid`, and `ChargingGranularityInvalid`. Each
one this profile's descriptor can provoke gets a named negative case; a refusal reported with the
wrong reason is a defect.

Two identity rules bind this component's names before any code exists. A profile ID is two to
eight dot-separated ASCII labels, and the first label `broiler` is reserved and paired with a
Broiler package identity — so `broiler.webassembly` is legitimate for this component and obliges a
`Broiler.*` package ID. A feature manifest ID must begin with its own profile's ID followed by a
dot and at least one further label, which makes `broiler.webassembly.<surface>` the shape of every
manifest this component ever mints. **The spelling of the profile ID is therefore not cosmetic**:
an abbreviation would be inherited by every manifest identity, every diagnostics namespace, and
every support-table row for the life of the component. WA-0 chooses between `broiler.webassembly`
and `broiler.wasm` and records the choice with that consequence stated.

### One structural gift the other intended profile does not get

**The artifact descriptor carries identity beside the bytes, not inside them.** A
`VmArtifactDescriptor` supplies the profile ID, exactly one format version, exactly one feature
manifest ID, the requested limits, and the caller's own identity; the payload span is separate.
Nothing in the contract requires the payload to restate any of it.

The consequence shapes section 7 and is worth stating plainly: **a bare `.wasm` file produced by
any external toolchain can be the payload verbatim, with no Broiler wrapper, no re-encoding, and
no envelope.** The module's own magic and version are checked by this profile's decoder as the
specification requires, and the core's identity requirements are satisfied entirely by the
descriptor the caller already had to construct. A browser that fetched a module hands those exact
bytes to verification.

### The fifteen budget dimensions, and what this profile intends to declare

`Fuel`, `WallClock`, `AllocatedBytes`, `LiveBytes`, `HostCalls`, `CallDepth`, `VerifierWork`,
`ArtifactBytes`, `SectionCount`, `DeclaredCount`, `StructuralDepth`, `NestedLoadDepth`,
`NestedLoadFanOut`, `NestedLoadBytes`, `LiveRuntimes`. The declaration matrix has no default row: a
dimension this profile does not charge says `NotApplicable`, the catalog refuses an incomplete
matrix, and it refuses `VerifierWork` declared inapplicable outright, because verification always
does work.

The intended matrix, which WA-1 fixes and later milestones may correct with a dated record but may
not drift:

| Dimension | Intended | What charges it |
|---|---|---|
| `Fuel` | Charged | Every instruction dispatched, plus the proportional families of invariant 7. |
| `WallClock` | Charged | Core-metered against the operation; this profile polls often enough for it to bite. |
| `AllocatedBytes` | Charged | Decode-time buffers; instance allocation; every `memory.grow` and `table.grow`. |
| `LiveBytes` | Charged | Linear memories and tables are the dominant retained cost of a WebAssembly instance, and a profile that did not report them would let a store grow without any ceiling noticing. Reported on growth, released on instance disposal. |
| `HostCalls` | Charged | Every call into an imported host function. |
| `CallDepth` | Charged | Every activation frame. Section 12 records that the default is measured, not chosen. |
| `VerifierWork` | Charged | Required by the catalog. Decode and validation work. |
| `ArtifactBytes` | Charged | Enforced by the core's reader over the payload. |
| `SectionCount` | Charged | WebAssembly sections are literal, so this dimension has a direct referent for once. Custom sections count. |
| `DeclaredCount` | Charged | Every vector length in the binary format — types, functions, locals, elements, data, fields, and the rest. |
| `StructuralDepth` | Charged | Section framing and block nesting, as a *high-water mark* rather than a running total — **which the core already supports and an earlier draft of this row said it did not.** The relevant members are not `TryCharge` alone: the metering surface is four members, and the retain/release pair exists precisely for the eight ceiling-class dimensions, of which this is one. Only a ceiling-class dimension releases; an allowance never refunds. So the discipline is charge on entry, release on exit, and the refusal lands exactly at the ceiling on a long function of many sequential shallow blocks — the failure mode the earlier draft feared is the one the pair prevents. This profile already applies the same discipline to `LiveBytes`, which is declared in this same table as reported on growth and released on disposal — so the earlier reading was internally inconsistent as well as wrong. Section framing is core-metered through the reader in any case. **WA-1 records the charge sites and the one caution below, not the question.** |
| `NestedLoadDepth` | NotApplicable, **default `Unconstrained`** | No guest-initiated loads at any manifest this roadmap allocates. The maximum may be whatever this profile likes - it binds this profile's artifacts alone. **The default is the half that reaches a neighbour**, and getting it wrong is a cross-profile defect: see below. |
| `NestedLoadFanOut` | NotApplicable, **default `Unconstrained`** | As above. |
| `NestedLoadBytes` | NotApplicable, **default `Unconstrained`** | As above. |
| `LiveRuntimes` | Charged | Core-metered; this profile adds nothing. |

**One caution the metering surface carries, and it lands on this profile harder than on any other.**
The retention report returns nothing, so a refusal on a ceiling-class dimension cannot be handed
back at the point of retention: it is latched, and the operation observes it at its next charge or
poll. **A ceiling-class dimension therefore cannot carry a guest-observable refusal.** Section 12
requires a refused `memory.grow` to be guest-observable and non-terminating — the module decides what
to do and the operation continues — so growth is admitted or refused on a `TryCharge` of
`AllocatedBytes`, and `LiveBytes` is reported for accounting only. If the aggregate `LiveBytes`
ceiling is what bites, the guest has already observed a *successful* grow and the operation aborts
at the next poll, which is precisely the outcome section 12 forbids. **WA-5 takes this as a numbered
decision before the memory representation is chosen**, and section 20 records the alternative
honestly: a refusable retention member would be general to any profile with host-visible retained
state, which is the counterweight test this profile applies to everyone else's asks.

**A maximum is a statement about this profile; a default is a statement about its neighbours.** A
profile hard maximum is **not** a statement of what this profile uses; the defaults are that. The
maximum is the most this profile would tolerate a host granting, and it binds **this profile's own
modules and nobody else's** — verification intersects the host's ceiling with the maxima of the
profile the artifact names, and with no other profile's.

**The default is the declaration that reaches other profiles.** A host that adopts profile defaults
rather than stating numbers gets the *tightest default in the catalog*, per dimension, because at
runtime creation no profile has been selected and there is no other safe answer. That matters more
here than anywhere else, because the composition this component exists to serve is a browser that
will also carry a JavaScript profile. Section 17 carries it as a named risk with a named owner.

**The sharpest case is the one this profile would get wrong by instinct, and it is about defaults.**
A dimension declared `NotApplicable` in the budget matrix is a statement about what this profile
*charges*. It is not a statement about the limit vectors, which are separate, and the default
resolution reads every descriptor in the catalog with no exemption for the inapplicable ones. So
writing `0` into the three guest-load **defaults** — the natural thing to write for "I do not use
this" — **hands a host that adopts defaults a ceiling of zero nested loads, and makes `eval` fail in
a JavaScript profile beside this one with a resource exhaustion naming a dimension this profile never
touches, in a verifier that has done nothing wrong.** What keeps a dimension unreachable is an import
list and a budget matrix, **not a zero ceiling**. **WA-0 publishes these three defaults as
`Unconstrained`, explicitly and with the reason recorded.**

*Corrected 2026-08-31.* Until then the same paragraph said all of this about the **maxima**, because
the core also clamped every runtime ceiling to the tightest maximum in the catalog. That clamp was a
defect against the core's own record, which always placed a profile maximum at verification against
the selected profile, and it has been removed. This profile's maxima are now its own business; the
obligation moved one column across to the defaults, which is a smaller exposure — a host that states
explicit ceilings never meets it at all — but not an absent one.

### What the core refuses to do for this profile

- It stores no values, inspects no frames, and knows no opcode. There is no shared value ABI to
  reach for and none is coming.
- It discovers nothing. No assembly load, no type lookup by name, no scan, no activator, no
  module-initializer ordering. A composition root names this profile's descriptor directly or the
  profile is not in the image.
- **It provides no argument channel.** An invocation request carries one UTF-8 entry-point name
  and nothing else. For a profile that compiles whole programs that is an inconvenience. For this
  one it is the central gap, because a WebAssembly module is *nothing but* exported entities with
  typed signatures, and the conformance suite is built end to end on invoking exported functions
  with arguments. Section 10 works it through, WA-1 picks an answer, and section 20 records what
  would have to be true for it to become an amendment.
- **It does provide a result channel.** A typed profile payload with projection accessors carries
  returned values back without adding a core result case, and it can carry several, so multi-value
  returns from an exported function are expressible today. The asymmetry between arguments and
  results is real, and it is the shape of the whole problem.
- It gives an executing profile no way to instantiate a further module through the core. This
  profile declares no guest-initiated loads, so the mediator is not on its path at all — but the
  refusal still binds, and section 11 records what it means for a store that must hold several
  linked modules.
- It offers no persisted envelope. Bounded outer-envelope parsing is admitted by the contract and
  implemented by no core milestone, so section 18 plans a code cache that does not exist yet and
  gates it accordingly.
- **It admits exactly one verification input form, and that is settled rather than open.** The byte
  round trip is mandatory and verification is whole-artifact and eager, so a handle means the whole
  module was decoded and validated. Each is reopened only by a numbered amendment. For this profile
  the round trip costs nothing — the bytes came from outside anyway — and the eager reading is the
  one invariant 3 already required. Section 20 records that this profile is therefore the
  counterweight that would *decline* both amendments rather than fund them.
- **Its variable-length integer readers accept canonical encodings only.** The core's own package
  description says "canonical LEB128", and the reader's source says over-long encodings are
  rejected "rather than accepted and truncated". The specification says the opposite. Section 7 is
  the whole treatment, and it is the first design decision this component has to take.
- It will not learn this profile's semantics. A requirement that cannot be expressed through the
  profile-facing checklist is an amendment or a refusal, never a special case.

---

## 4. No seed: what greenfield costs, and what it buys

The core roadmap fixes seeding conditions on the *other* intended first profile. It fixes none on
this one, because there is nothing to seed from: no Broiler component implements WebAssembly
execution, and the one assembly in the wider repository whose name contains the word is a browser
rendering back end that emits canvas calls and decodes nothing. **This component starts empty.**

A roadmap that left it at that would be hiding the interesting half. "No seed" is not a smaller
plan; it is a differently shaped one, and both directions are worth stating before the first
commit.

### 4.1 What it buys

- **No fork to police.** There is no snapshot identity to record recursively, no two-way boundary
  rule to enforce against a legacy component, no waited-on set, no snapshot stop condition, and no
  risk of a fix leaking across a fork. WA-0 is correspondingly smaller than it would otherwise be,
  and the architecture rule register loses a whole family of edges.
- **No inherited defect history and no inherited review debt.** Every unit in this component is
  written here, so the assurance system starts with an origin distribution of exactly one kind and
  the review queue grows only as fast as the work does. Nothing arrives unreviewed on day one
  because nothing arrives.
- **No amputation.** There is no dynamic-metaobject interface on a value base type to remove, no
  module-initializer bootstrap to delete, no reflective static constructor in the lowest layer of
  the graph, and no transitive project reference into an IL emitter to break. The Native AOT
  position is clean by construction rather than by surgery, and WA-0's gate can ask for a clean
  closure from the first compile instead of after an ingest.
- **No value-representation inheritance.** The value model is designed against the specification
  rather than negotiated against a body of existing library code that is already typed against
  something else. The specification's value set is small, closed, and fully specified.
- **The specification is the design.** Decoding, validation, and execution each have a normative
  description with a reference implementation and a conformance suite. There is no equivalent of
  reconstructing intent from a codebase.

### 4.2 What it costs

- **Everything is a first draft.** A copied parser has been run against real input for years. This
  profile's decoder will not have been run against anything until it is written, so the corpus and
  the fuzz targets are load-bearing earlier here than they would be for a fork, and WA-2 puts them
  before the interpreter rather than after it.
- **The surface is large, and its size is set by someone else.** The specification's current
  version standardises a large group of features at once, among them garbage collection, a typed
  reference system with subtyping and recursive type groups, exception handling, tail calls, a
  64-bit address space, multiple memories, and a 128-bit vector instruction family that is by
  itself comparable in size to the original instruction set. **This roadmap does not restate the
  counts**;
  section 26 pins the specification revision and the counts are read off its own index of
  instructions and index of types at that revision. What matters here is the consequence: the
  first manifest cannot be "the specification", and section 6 allocates manifests so that a
  release can be truthful long before the surface is complete.
- **The type system is the expensive part, and it is not obvious from the instruction list.**
  Typed references, subtyping, recursive type groups, and structural type canonicalization are a
  validation-time cost that a reader who has only seen the numeric core will underestimate. The
  suite has files devoted to type canonicalization and type equivalence for exactly that reason.
  Section 6 keeps that surface behind its own manifest and section 8 keeps it out of the first
  validator.
- **Conformance is unusually unforgiving, in a way that is a gift after the first month and a
  tax before it.** The suite is exhaustive, external, and adversarial; it grades decoding,
  validation, linking, trapping, and execution separately; and it will not accept a partial
  implementation quietly. There is no gentle on-ramp, which is why WA-4 builds the harness before
  the interpreter exists rather than after.
- **A greenfield component still has an assurance floor to build.** The annotation grammar, the
  generated review report, the fingerprint binding, the release-mode gate, the evidence-bundle
  contract, and the collection script are this component's own code and are not smaller for the
  tree being empty. WA-0 is that work and nothing else.

### 4.3 What is acquired rather than written

| Material | Disposition |
|---|---|
| The core's three packable assemblies | **Referenced as packages**, exactly two of them by the profile: the abstractions and the binary primitives. Never vendored, never copied, never sourced from a project reference across component boundaries. |
| The specification document | **Pinned by dated revision, retrieved, hashed, and archived.** Retrieving and archiving a third-party document is a *human* action; until someone performs it, the pin is provisional and carries a named exclusion in the ledger. WA-0 records the intended revision; WA-2 records the one actually taken. |
| The conformance test suite | **Pinned by immutable commit** and ingested by a **test-only** path. It is Apache-2.0 licensed material entering this repository, so section 4.4 applies to it. No file of it is referenced by a product project, and a scan asserts it appears in no published closure. |
| A text-format reader for the suite's scripts | **Written here, test-only.** The suite is distributed as scripts in the specification's text format, so something must read them. It is this component's code, it lives in the harness, and it is subject to the same no-product-reference rule as the corpus it reads. It is not a WebAssembly text-format implementation and does not claim to be one: it reads what the suite actually contains. |
| A binary encoder | **Written here, test-only.** The malformed corpus is generated, and generating a malformed module means being able to emit a well-formed one first. It exists to produce corpus entries and nothing else, and a scan asserts it is in no product closure. Its absence from the product is what keeps "no compiler" true. |
| Everything else | **Written here.** Decoder, validator, store, linker, interpreter, payloads, descriptor, host adapter, harness, corpus, fuzz targets, measurement lane. |

### 4.4 Licence, attribution, and one notice that must change

The conformance suite and the reference material are Apache-2.0, and ingesting them means carrying
the obligations of that licence: retain the notices, mark modified files as changed, and carry the
NOTICE content forward. This component's own licence and notice file satisfy that on its own terms.

One consequence reaches outside this component and must not be discovered at release time: **the
core component's third-party notice currently asserts that nothing it ships is vendored or
copied.** That assertion is scoped to the core's own packages and stays true — but only if it stays
scoped, and this component is about to place a large body of third-party test material in the same
repository tree. WA-0 carries an explicit item to confirm the scoping or to amend the notice, with
the release owner co-signing. An attribution obligation discovered during a publish is a stop.

A second consequence is smaller and easier to get wrong: **the suite is test material, and a
support table must never present a suite file as this component's own evidence of anything except
a run.** The corpus is inputs; the retained run is the evidence. Section 15 states the difference
where it can be enforced.

---

## 5. Package boundaries and the dependency graph

These names follow the pattern the core fixes for a profile and are hypotheses until WA-0 proves
the graph with project shells and an explicit assembly budget. No assembly is created to shorten a
file; each must enforce a dependency, AOT, deployment, ownership, test, or package boundary.

**The pivot argument does not transfer, and this is the section where that shows.** The core's
placement rule for a profile puts the format in its own assembly because a compiler and an
executor must agree on the bytecode and neither may depend on the other. This profile has no
compiler. Its format is the W3C binary format, its only encoder is test-only, and its only decoder
is inside the verifier. There is therefore nothing for a format assembly to hold apart, and
creating one would be creating an assembly to shorten a file. **The default here is one product
assembly**, and a split needs a justification that names the boundary it enforces.

| Logical boundary | Candidate assembly | Responsibility and dependency rule |
|---|---|---|
| Profile | `Broiler.VM.Profile.WebAssembly` | Descriptor, decoder, validator, verified module, store and instance model, linker, interpreter, host imports, payload projections. References exactly the two core assemblies and nothing else Broiler-owned. |
| Composition root | `Broiler.VM.Profile.WebAssembly.Composition.Execution` | The one named deployment composition. The only project that knows which profiles and capabilities an image contains. Non-packable unless the composition register advertises it. |
| Test-only | conformance host, script reader, corpus store and encoder, fuzz host, soak host, bench host | Never referenced by a product project and never present in a published closure. |

```text
Broiler.VM.Abstractions              ──→ (nothing)
Broiler.VM.Binary                    ──→ (nothing)
…Profile.WebAssembly                 ──→ Abstractions + Binary
composition root                     ──→ Broiler.VM.Runtime + the profile
conformance / corpus / fuzz hosts    ──→ the profile  (never referenced by any product project)
```

The rules the verified graph must retain, whatever the names become:

- the profile's Broiler.VM reference set is **exactly** the two core assemblies — no reference to
  the core runtime, no package reference to a third core package, no `InternalsVisibleTo` in
  either direction;
- **no edge in either direction reaches any other Broiler profile component**, asserted by an
  architecture rule with a passing witness and a negative control, including the inbound half.
  Two profiles in one browser image are composed by a composition root; they are not linked to
  each other, and section 17 depends on that staying true;
- no product project references a test project, a fixture, a corpus, or a conformance host, and no
  product project references the text-format reader or the binary encoder;
- every namespace matches its assembly;
- there is no aggregate profile-listing type anywhere. One would reference every profile assembly
  and defeat the exact-closure reports the composition depends on; and
- **a second product assembly is a dated decision, not a convenience.** If the vector instruction
  family or the garbage-collected type surface turns out to justify its own assembly on trimming
  grounds — an execution-only image that carries no vector interpreter is a real product — that is
  a decision with a measured closure difference attached, taken at the milestone that mints the
  manifest and not before.

---

## 6. Feature manifests: how the language surface is admitted

The core fixes a manifest's shape and identity; this profile fixes its content. Three rules make
that a gate rather than a label.

**One manifest, one reviewed scope, one oracle run.** A manifest is minted with an explicit list of
what it admits, an extension to the retained malformed corpus, and its own conformance run from an
exact commit against the pinned suite revision. A manifest with no retained run of its own is not
accepted, and the support table says so.

**Increments do not inherit, and a specification version is not an increment.** Manifest *n+1*
admits what its own scope names. It may not be justified by arguing that manifest *n* implies it,
and it may not be justified by the specification bundling several features under one version
number. The specification's versions are a publication convenience; this profile's manifests are
claims backed by runs.

**A manifest is refused, not degraded.** A module whose descriptor names a manifest this descriptor
does not accept is `InvalidArtifact` with reason `UnsupportedFeatureManifest`. There is no partial
acceptance and no fallback to a smaller manifest, and in particular **a module that decodes but
uses an instruction outside its declared manifest is rejected at validation** rather than trapping
at first execution.

### This profile implements the specification's deterministic profile

The specification defines its own profiles, and this component implements `DET`.

The reasoning is not about performance. `DET` fixes the two places the language is deliberately
underspecified: NaN payload propagation from floating-point instructions, and the behaviour of the
relaxed vector instructions. Both are places where `FUL` admits several answers.

- **A component whose whole method is retained corpora with recorded expected answers cannot hold
  a corpus whose expected answers legitimately vary.** A malformed-corpus entry compares an
  observed triple against a recorded one, byte for byte, across three publish modes. An
  execution corpus over `FUL` relaxed instructions would have no such triple to record.
- **`DET` is a refinement, not a subset.** Every behaviour `DET` fixes is one of the behaviours
  `FUL` already admits, so a `DET` implementation is a conforming `FUL` implementation. The
  conformance suite's own result patterns accommodate this: a canonical-NaN pattern and an
  arithmetic-NaN pattern are matched by a deterministic implementation, and the alternation
  pattern the suite uses for relaxed instructions is satisfied by any one of its arms.
- **It makes the relaxed vector surface scoreable at all.** Under `FUL` a relaxed instruction has
  no single right answer to assert; under `DET` it has one. That is what moves it from
  "excluded, because untestable here" to "implementable behind its own manifest".

Two consequences must be published rather than assumed. The support table names `DET` explicitly,
because a consumer choosing between engines on relaxed-vector throughput is choosing between
different behaviours and not merely different speeds. And **the `DET` position is itself a claim
that needs evidence**: a fixture asserts canonical positive NaN propagation from every
NaN-producing instruction family in the manifest, and the relaxed family's fixtures assert the
fixed arm rather than any arm.

### The intended allocation

WA-0 fixes this table; later milestones may extend it but may not silently widen it.

| Manifest | Admits | Earliest milestone |
|---|---|---|
| `broiler.webassembly.slice` | One type, one function, one export, integer arithmetic, `local.get`/`local.set`, `block`/`loop`/`br`/`br_if`/`return`, `unreachable`. No memory, no table, no global, no import, no float. **Deliberately not a module anyone would ship** — its purpose is to close the whole contract loop against a surface small enough to hold in the head. | WA-1 |
| `broiler.webassembly.core1` | The original standardised surface: the full numeric instruction set including floats, structured control flow, `call` and `call_indirect`, one linear memory with its loads and stores and `memory.grow`, one table, globals, imports and exports of all four kinds, the start function, element and data segments, and custom sections ignored correctly. | WA-6 opens it; WA-7 completes it with imports |
| `broiler.webassembly.core2` | The second standardised group **minus vectors**: sign-extension operators, non-trapping float-to-integer conversion, multi-value blocks and results, reference types with `funcref` and `externref`, bulk memory and table instructions, and the data count section. | WA-8 |
| `broiler.webassembly.vector` | The 128-bit vector instruction family. Its own manifest because it is, by instruction count, comparable to everything above it combined, and because an execution-only image that never needs it should be able to decline it truthfully. | WA-8, or excluded with a published failure |
| `broiler.webassembly.relaxed` | The relaxed vector instructions, under `DET` and only under `DET`. | After `…vector`; excluded by name until it has a run |
| `broiler.webassembly.tailcall` | `return_call` and `return_call_indirect`, and the frame-reuse obligation that makes them meaningful rather than merely accepted. | Increment |
| `broiler.webassembly.exceptions` | Tags, `throw`, the try/catch forms, and `exnref`, together with the unwinding interaction with host frames that section 12 fixes. | Increment |
| `broiler.webassembly.gc` | Typed references, subtyping, recursive type groups, structural type canonicalization, struct and array types, and the reference casts. **The largest single increment in the table**, and the one most likely to be re-scoped once the validator meets it. | Increment |
| `broiler.webassembly.memory64` | 64-bit address types for memories and tables. | Increment |
| `broiler.webassembly.multimemory` | More than one memory per module. | Increment |
| `broiler.webassembly.threads` | Shared memories and the atomic instruction family. **Excluded by name, and this exclusion is different from the others**: the core's thread-affinity enforcement reaches a profile only where the core can see a thread, and a profile that starts its own threads is invisible to it. Section 14 records what would have to be true first. | Not allocated |

**Nothing in this table is a schedule.** Its purpose is to fix the granularity at which the surface
may grow, so that the answer to "does this engine support WebAssembly?" is always a manifest set
with runs behind it rather than a version number.

---

## 7. The artifact, the decoder, and one disagreement with the core

### The artifact is a WebAssembly module, unwrapped

Format version 1 admits **a bare WebAssembly binary module as the entire payload**. No Broiler
magic, no Broiler framing, no outer envelope, no re-encoding. The identity the core needs travels
in the artifact descriptor beside the bytes, which section 3 records as a property of the shipped
contract rather than a hope.

This is worth defending, because the alternative is tempting and wrong. A Broiler wrapper would
make the corpus easier to version and would let the format version carry the manifest. It would
also mean that no artifact any external toolchain produces could be verified without a
transformation step, that a browser would re-encode every module it fetched on its critical path,
and that this component's malformed corpus would be testing a Broiler framing layer rather than
the specification's own binary format. The suite's malformed cases are cases about *this* format;
wrapping it would put a layer between the corpus and the thing the corpus is about.

**What the format version means, then, is the shape of the payload and nothing else.** It is
version 1 for a bare module. It moves if the specification's own binary version field ever moves,
or if a later format version admits a different payload shape — section 11 names one candidate.
It does **not** move when the language surface grows, because the feature manifest carries that,
and a format version that tracked specification versions would be a second, redundant, and
inevitably disagreeing version axis.

### The disagreement: canonical against padded variable-length integers

**The core's variable-length integer readers accept canonical encodings only. The specification
requires padded encodings to be accepted. These cannot both be satisfied by the same call.**

The core's position is explicit in two places that ship. Its published support table describes the
binary package as providing "canonical LEB128". Its reader's own source says that over-long
encodings "are rejected rather than accepted and truncated", and gives the reason: "Two encodings
of one value would make a byte-identical artifact check meaningless and would let a payload carry
a value past a length check that read it differently, so the canonical form is the only accepted
form." For a format the core also defines, that reasoning is correct.

The specification's position is equally explicit and goes the other way. Its integer grammar
permits an encoding to use up to a byte count derived from the width, with redundant continuation
bytes inside that budget; its own note is that *trailing zeros are still allowed*, and it gives an
8-bit example in which two distinct byte sequences both encode the same value. What is rejected is
exceeding the byte budget, and setting unused bits in the terminal byte. The suite has a file
devoted to exactly this, containing modules with padded encodings that must be **accepted** beside
modules with over-long ones that must be rejected as malformed — and production toolchains emit
padded immediates routinely, because emitting a fixed-width placeholder and patching it later is
how a single-pass code emitter works.

So a WebAssembly verifier built on `TryReadVarUInt32` would reject modules that clang produces.
That is not a tuning question; it is a wrong answer to a conformance test with a real-world
consumer behind it.

**The resolution, and it is taken here rather than deferred.** This profile decodes its own
variable-length integers, over the core's byte-level primitives, inside its own decoder. It calls
the core's `TryReadByte`, `TryReadBytes`, `TryReadUInt32LittleEndian` and its 64-bit counterpart —
which are the right members for the module's own version field and for float immediates —
`TryEnterSection`, `TryExitSection`, `TrySkipSectionBody`, and `TryChargeWork`, and it calls the
core's bounded allocator; it does **not** call the core's
`TryReadVarUInt32` or `TryReadVarUInt64`, and consequently it does not call `TryReadDeclaredCount`
either, because that member reads a canonical integer before comparing it against its bound.

Three obligations follow, and each is a WA-2 gate clause rather than a note:

1. **The bound-before-use ordering is re-derived, not inherited.** `TryReadDeclaredCount` exists so
   that a caller "cannot loop, size a buffer, or reserve capacity from a number that never passed
   its bound". This profile's own count reader must have exactly that property, and it must be
   asserted mechanically for every corpus entry including every failing one — not tested once.
   Losing this by re-implementing it is the single most likely way for this decision to go wrong,
   which is why it is written down beside the decision rather than after it.
2. **Signed integers are this profile's anyway.** The core has no signed variable-length reader at
   all, and the format needs three widths of them, including the 33-bit form block types use. So
   part of this code was always going to be written here; the decision above extends it rather
   than starting it.
3. **The deviation is recorded as a deviation, and it moves a budget dimension.** This component
   does not get to describe itself as using the core's variable-length readers, and its support
   table says which core primitives it uses and which it replaces. A component that quietly
   re-implements a core primitive and says nothing has created a second implementation of a
   security-relevant check, which is the same shape of defect as a second verifier. **The support
   table must also say which dimensions change side.** The core's own metering split is stated in
   three rows — unevadable, core-metered *when the profile routes through the binary package which
   it is obliged to route through*, and profile-charged obligation — and declining the guarded count
   reader moves `DeclaredCount` out of the second row into the third for this profile. `SectionCount`
   and `StructuralDepth` stay core-metered, because this profile still enters and exits sections
   through the core; `ArtifactBytes` stays unevadable, because the reader's own construction
   enforces it whatever the caller does. So exactly one dimension migrates, this profile names it,
   and the core's obligation row is made conditional rather than absolute — because as written it is
   already false for the first product profile that will exist.

Section 20 carries the amendment candidate this produces — a padding-tolerant reader in the core's
binary package, parameterized by a byte budget — and states its counterweight honestly: it is
general to any format that inherits this encoding, and it is *not* something the core needs for
itself. That is what makes it a proposal from a profile rather than a core defect.

### The second disagreement is smaller and entirely this profile's problem

**Section identifiers do not sort into section order.** The specification says so in its own words
— "section ids do not always correspond to the order of sections in the encoding of a module" —
and there are two live examples: the tag section carries a higher identifier than the data count
section and is ordered before it, and the data count section is ordered before the code section
whose indices it exists to bound.

A decoder that validates section order by comparing identifiers is therefore wrong, and wrong in a
way that a smoke test over a toolchain-produced module will not catch, because the common case
happens to be ordered. The order is a table, the table is derived from the pinned specification
revision, and a corpus entry exists for every adjacent pair the table forbids.

### What the decoder must reject, before any validation begins

- a payload whose magic or version field is not the specification's;
- malformed framing, truncation, and a section whose declared size does not match its content;
- an integer exceeding its byte budget, and an integer whose terminal byte sets unused bits —
  distinguished from each other, because the suite distinguishes them;
- a non-custom section appearing twice, or out of the specified order;
- a vector length that would exceed the effective declared-count ceiling, refused **before** it is
  used to loop or to size anything;
- a name that is not well-formed UTF-8, checked as the specification defines it rather than as the
  platform's decoder happens to behave;
- a data count section that disagrees with the data section, and a code section using a data index
  when no data count section is present;
- structural depth beyond the effective ceiling, and artifact bytes beyond it; and
- anything the pinned specification calls malformed, with this profile's own diagnostic code and a
  byte position.

**Those bullets do not all produce the same outcome category, and an earlier draft of this
paragraph said they did.** Six of them are `InvalidArtifact` with a decode reason, a diagnostic
code, and a byte position. Two are not: **a vector length beyond the effective declared-count
ceiling, and artifact bytes beyond theirs, are `ResourceExhaustion` naming one dimension and one
scope** — the module is well formed and this image declined to admit it, which is the same rule
section 8 applies to every other implementation limit and the same one the core rules for the whole
bounded-read status set. The distinction is not cosmetic here: every corpus entry pins its observed
⟨outcome, reason, diagnostic code⟩ triple and replays it across three publish modes, so an entry
recorded under the wrong category **passes** and encodes the wrong answer, and the published mapping
table would be built wrong from the start. The mapping is a published table bound in both
directions, with no invented reason and no aliasing, and it names the category per bullet.

### Three disciplines that make the list provable rather than aspirational

1. **A retained malformed corpus.** Every entry carries its bytes, its hash, and its expected
   outcome, reason, and diagnostic code, and every entry is replayed under JIT, trimmed, and
   Native AOT with the three tables compared byte for byte. The corpus grows at every milestone
   that grows the accepted surface, and it contains **control entries that verify successfully** —
   including a padded-integer entry that must be *accepted*, because a corpus in which nothing
   passes would not notice a decoder that rejects everything, and a corpus with no padded
   acceptance would not notice a regression back onto the core's canonical reader.
2. **Coverage-guided fuzzing over two surfaces**, kept apart because they fail differently: the
   decoder and validator over arbitrary bytes, and the interpreter over
   validated-but-adversarial modules. Every session retains its seed, its iteration budget, its
   runtime settings, and every minimized counterexample. **A counterexample is closed by a named
   regression, never by an allow-list entry.**
3. **Ordering assertions.** The effective ceilings are materialized before the first byte is read;
   a refusal happens before the allocation it would have authorised; a declared count is compared
   against its bound before it sizes anything or bounds a loop. These are asserted mechanically for
   every corpus entry including every failing one, because the ordering is the property and the
   answer alone does not show it.

---

## 8. Validation

### One verification, two phases, and the order is observable

The specification is a three-phase language — decode, validate, instantiate — and this component
maps the first two onto the core's single verification and the third onto instantiation. The
mapping is not a convenience: it is what the core's contract requires, because a verified handle
means the artifact is admitted and the stage matrix makes `InvalidArtifact` illegal at every later
stage.

Within that one verification, **decoding completes before validation begins**, and the ordering is
observable rather than internal. A module that is both malformed and invalid must be reported
malformed; the suite asserts it; and an implementation that fused the phases to save a pass would
report the wrong one. This is stated as invariant 4 because it is the kind of property that a
performance-minded rewrite silently destroys.

### The lazy-validation permission is declined, and that is cheaper as well as stricter

The specification's implementation-limitations appendix permits an implementation to defer a
function body's validation until its first invocation, with an invalid body then trapping, subject
to the body being fully validated before its execution begins. Engines take that permission for
start-up latency.

This profile does not, for three independent reasons and one bonus:

- **Invariant 3 forbids it.** A structural or type check that happens at first execution is a check
  reported as a trap, and a trap and a rejection are different answers to different questions.
- **The core forbids it.** `InvalidArtifact` is not a legal outcome at instantiation, invocation, or
  resume, and the core's own note explains why: a verified handle cannot later become invalid
  without creating a second verification point.
- **The core's verification is whole-artifact and eager anyway**, so nothing would be saved on the
  path that matters.
- **And it is easier.** Under eager validation, an `assert_invalid` case is a rejection at
  verification, full stop. Under lazy validation it is a rejection only if the offending function
  is reached, which turns a large family of conformance assertions into a question about
  reachability.

What is genuinely lost is start-up latency on a large module with a cold entry point. Section 18
records the measurement that would reopen it, and section 20 records that reopening it is an
amendment to the *core*, not a local decision, and that this profile would have to fund the
amendment with a number rather than an intuition.

### The algorithm is the specification's, and its shape matters

Validation of function bodies is the specification's own single-pass algorithm over three stacks —
a value-type stack, a control-frame stack, and an initialization stack — with each control frame
carrying its opcode, its start and end types, the value and initialization heights it was entered
at, and an unreachable flag.

Two properties of that algorithm are load-bearing here rather than incidental:

- **Unreachable code is type-checked polymorphically, not skipped.** After an unconditional branch
  the frame is marked unreachable and the value stack is truncated to the frame's height; popping
  from an unreachable frame at its own height yields a bottom type that satisfies any constraint.
  This is what lets dead code contain instructions with no valid operand and still validate, and
  it is a place implementations get quietly wrong in both directions — accepting genuinely invalid
  dead code, or rejecting valid dead code. The suite tests both directions extensively.
- **It is a single linear pass and it composes with decoding.** The algorithm is designed to run
  inside a decoder, which is what makes "decode then validate" implementable without materialising
  an intermediate tree per function. This component may fuse them *within one function body* as an
  implementation choice, provided the phase-order property above survives at module granularity —
  and WA-3 states which it did, because the two readings differ in what a partially decoded module
  reports.

### The implementation-limitations appendix is where the core's dimensions get their referents

The specification does not fix concrete limits; it enumerates the places an implementation may
impose one and says that exceeding them may be rejected with an implementation-specific error.
That enumeration maps almost row for row onto the core's budget dimensions, which is the clearest
evidence available that the core's fifteen dimensions were not invented for one language:

| Specification permits a limit on | This profile enforces it as |
|---|---|
| Number of types, functions, tables, memories, globals, tags, imports, exports, element and data segments, struct fields, parameters and results, locals | `DeclaredCount`, against the effective ceiling, before the count is used |
| Nesting depth of control instructions | `StructuralDepth`, subject to the WA-1 decision in section 3 |
| Module size, section size, function body size | `ArtifactBytes` and the section framing |
| Number of sections | `SectionCount` |
| Instructions in a function, instructions in a constant expression, `br_table` label count, `array.new_fixed` length | `DeclaredCount` and `VerifierWork` |
| Subtyping depth and recursive-type group size | `StructuralDepth` and `DeclaredCount`, at the manifest that admits them |
| Name length and character ranges | `DeclaredCount` and the UTF-8 rule |
| Allocated instances, instance sizes, stack frames, labels, values | `LiveBytes` and `CallDepth`, at execution |

Two things follow that a support table must say. **Every one of these limits makes this profile
reject a module the specification calls valid**, which is a legitimate and specification-sanctioned
outcome — and it is still a rejection, so it is `ResourceExhaustion` naming a dimension and a
scope, never `InvalidArtifact`. Confusing the two would tell a caller that its module is malformed
when the truth is that this host declined to spend the memory. And **the defaults are policy, not
correctness**: they are recorded, they are a host's to tighten, and the conformance run publishes
the effective vector it ran under, because a suite result obtained under generous ceilings does not
transfer to a product that ships tight ones.

### The diagnostic registry

Every rejection carries a stable code from a published, versioned registry, bound in **both**
directions: every code the profile can emit appears in the registry, and every code in the registry
is reachable from a named case. Each code maps onto exactly one core reason. Positions are byte
offsets into the payload, with a documented encoding, and they are stable across a rebuild.

**The registry has two carriers and the sentence above spans both, which is worth saying because the
core's do not.** A verification rejection travels as the core's own pair — this profile's stable
32-bit diagnostic code plus an opaque position record — on the verifier outcome. A trap, an uncaught
exception, or a link error is a `ProfileFault`, and the core's only channel for its detail is the
typed payload: the diagnostics record has nowhere to put it and the position field is populated on
the verification path alone. So a trap's position and code travel **in the payload**, not in the
core's diagnostics, and the registry says which carrier each code uses. Getting this wrong surfaces
as a design argument at WA-5 and WA-6, when the payload turns out to need a field the registry
assumed the core would carry.

**Positions travel in the core's position record**, which carries a section index, a byte offset,
and two profile-owned coordinates the core does not interpret. WA-3 states which of the four this
profile populates, what it puts in the two coordinates — the natural candidates are the section
identifier and the function index — and what a section index of `-1` means here. That sentence
exists because two profiles designing two position encodings against one shared record, neither
naming it, is how two incompatible conventions get built against one struct.

The registry is published at WA-3 and versioned from then on, because a diagnostic code that
changes meaning between releases silently invalidates every retained corpus entry that recorded it.
**The registry revision is therefore recorded with every retained corpus entry**, so an entry's code
can be dated; the core carries a revision for its own reason registry and none for a profile's, so
WA-3 states which field this profile uses for it or names it as an amendment candidate.

---

## 9. The value, store, and frame model

**This decision is taken before the interpreter is written, and it is a gate on entry to WA-5
rather than that milestone's first task.**

Unlike a language whose library is already typed against an existing value base type, this
profile's value set is closed, small, and fully specified, which makes the decision tractable — but
also makes an unrecorded answer inexcusable, because there is no legacy to blame it on.

What the decision must state, in both directions — what it buys and what it costs:

| Row | What must be decided and recorded |
|---|---|
| Numeric representation | How `i32`, `i64`, `f32`, and `f64` are held. The specification's own value set is untyped at run time within a validated module, because validation has already proved the types — so a single 64-bit slot is expressible and an eight-byte tagged form is not required. Whether the operand stack is typed, untyped, or split is the decision. |
| Vector representation | How `v128` is held once the vector manifest opens, and whether admitting it changes the slot width for every other value. A representation that is chosen for scalars and then widened for vectors is a rewrite; one that reserves width it does not use is a permanent cost. **This row is decided at WA-5 even though vectors arrive much later**, because it is the only row whose late answer invalidates the early ones. |
| Reference representation | How `funcref`, `externref`, and later the garbage-collected reference types are held, rooted, and kept distinct from numeric slots. An external reference is host-owned and crosses the core boundary as an opaque reference; section 13 fixes its lifetime. |
| Rooting and lifetime | Rooting for operand slots, locals, globals, table elements, and host references, and who owns each. |
| Call convention | How arguments and results are passed for a direct call, an indirect call, a host call, and a tail call, including how the frame-reuse obligation of a tail call is expressed before the tail-call manifest exists. |
| Frames and labels | Frame ownership, label representation for structured control flow, the native cost of one interpreter frame, and how that cost fixes the `CallDepth` default. |
| Trap propagation | How a trap leaves an instruction, unwinds frames, and becomes a payload, expressed explicitly rather than as an exception the dispatch loop happens to catch. Section 12 is the whole treatment. |
| Metering | Where every `Poll()` and every charge sits in the loop, and against which dimension. A representation that makes charging awkward is a representation with a hidden cost. |

Each row carries correctness fixtures and Native AOT representation probes retained beside it. **A
representation is not accepted because it looks compact**, and it is not accepted on a JIT
measurement alone.

### `CallDepth` is measured, not chosen

A recursing module must be refused as `ResourceExhaustion` naming `CallDepth`, on every claimed
RID, under Native AOT — **rather than terminating the process**. A stack overflow is not
translatable into a result, so claiming to handle deep recursion without a measured bound would be
an untruthful capability claim.

This profile has an unusually direct check on that claim: the conformance suite asserts it. Its
exhaustion assertions exist precisely to require that an engine survives runaway recursion and
reports it, and they are scored like every other assertion family. So the `CallDepth` default is
derived from a retained, reproducible measurement of native frame cost per interpreter frame on
each claimed RID, and the suite's own exhaustion cases are the proof on each.

The same discipline fixes `MaxUnchargedWork`, `ChargingGranularity`, and `CancellationPollBound`:
each is a number chosen from a measurement and recorded with it, not a round figure.

### Proportional charging

For every operation whose cost grows with its input, this profile declares a monotone
non-decreasing charging function and a granularity, and charges at least the ceiling of that
function over the granularity. Each family gets a retained fixture with an unsimplified control.

The families, named now because each is a **single instruction** and therefore the easiest possible
place for a flat charge to hide:

- `memory.copy`, `memory.fill`, `memory.init`, and `data.drop`;
- `table.copy`, `table.fill`, `table.init`, `table.grow`, and `elem.drop`;
- `memory.grow`, which charges allocation as well as work;
- `array.new`, `array.new_default`, `array.new_data`, `array.new_elem`, `array.copy`, `array.fill`,
  and `array.init_data`, when the garbage-collected manifest opens;
- `br_table`, whose label vector is guest-controlled at validation time; and
- the segment-initialisation work instantiation performs before any guest instruction runs, which
  is charged to the instantiating operation and is the one member of this list that is not an
  instruction at all.

**An operation family without a proportionality fixture does not ship in the increment**, and
`memory.fill` over a large memory is the canonical negative control: a flat charge passes a
functional test and fails this one.

---

## 10. Execution: mapping WebAssembly onto the core lifecycle

The core's lifecycle is fixed and this profile refines observable behaviour inside it. The mapping:

| Core stage | What this profile does |
|---|---|
| Catalog build | Supplies one descriptor through one static accessor. No aggregate listing type exists anywhere in the graph. |
| Runtime creation | The composition supplies ceilings, capabilities, and the external-suspension mode. The executor factory creates one executor per runtime from the execution environment. |
| Verification | Decodes and validates into an immutable `IVmVerifiedState` — the module, its types, its function bodies, its segments, and the ceilings computed for it. Owns or fully decodes its input: later mutation, disposal, or concurrent overwrite of the caller's buffer changes nothing. |
| Instantiation | **Links and allocates.** Resolves imports against the host and the store, allocates memories, tables, globals, and tags, initialises element and data segments, and runs the start function. Returns `Instantiated`, or `Faulted` carrying a link error or a start-function trap. |
| Invocation | Calls an exported function. Runs to `Completed` with a typed payload carrying the returned values, or `Faulted` with a typed trap or uncaught exception. |
| Resume | Not reached at any manifest this roadmap allocates. Implemented as the named invalid-state refusal, and section 14 records why the type is implemented anyway. |
| Unwind | Terminal. Releases memories, tables, and host references under the tighter of the abandon budget and the unwind budget, and **runs no guest code**. Section 14 records that this is simpler here than it would be for a language with user-visible finalisation, and that the simplicity is a property of the manifest set rather than a permanent one. |
| Disposal | Drains an in-flight step before releasing the artifact lease under it. This profile's obligation is that a step is interruptible often enough for the drain to succeed, which is what the cancellation poll bound is for. |

### The four failure phases, and why they land in three different places

The specification's failure taxonomy is finer than the core's stage boundaries, and the suite tests
each kind separately. Getting this table wrong is the most likely way to produce an engine that
passes functionally and fails conformance:

| Specification failure | Suite assertion | Where this profile answers | Core outcome |
|---|---|---|---|
| Malformed — the bytes do not decode | `assert_malformed` | Verification, decode phase | `InvalidArtifact`, with a decode reason and a byte position |
| Invalid — it decodes but does not type-check | `assert_invalid` | Verification, validation phase | `InvalidArtifact`, with a validation reason |
| Unlinkable — imports cannot be satisfied | `assert_unlinkable` | **Instantiation** | `ProfileFault` carrying a typed link error. It cannot be `InvalidArtifact`: the stage matrix forbids that outcome at instantiation, and it would be wrong anyway — the module is valid, the *environment* did not supply what it needs |
| Uninstantiable — linking succeeded, the start function trapped | `assert_trap` on an instantiation | Instantiation | `ProfileFault` carrying a typed trap. **No instance is published**, and a test asserts it |
| Trap during a call | `assert_trap` on an action | Invocation | `ProfileFault` carrying a typed trap |
| Uncaught exception reaching the embedder | `assert_exception` | Invocation | `ProfileFault` carrying a typed uncaught-exception payload, distinguishable from a trap, once the exceptions manifest opens |
| Resource exhaustion, including call-stack exhaustion | `assert_exhaustion` | Instantiation or invocation | `ResourceExhaustion` naming one dimension and one scope — **not** a profile fault, and not a trap |

Two consequences of the core's result vocabulary that this profile must live inside:

**A trap is not a core category.** It is a typed payload behind `ProfileFault`. The core's
categories describe what happened to the *operation*, not what the module computed, and this
profile adds no case to them.

**A host exception is a host failure, unless it is cancellation or exhaustion.** The core's
translation precedence applies: a cancellation exception carrying the operation's own token is
cancellation; an exhausted meter at the moment of the catch is resource exhaustion; anything else
is a host failure naming the capability. The handler matrix is tested in both directions across
the boundary, and once the exceptions manifest opens, a host failure crossing WebAssembly frames
must not be catchable by a WebAssembly `catch_all` — because a host failure is not a WebAssembly
exception and making it catchable would let guest code swallow a capability fault.

### The entry-point problem, which for this profile is the central one

An invocation request carries one UTF-8 entry-point name and nothing else. There is no argument
channel. The result channel exists and is adequate: a typed profile payload carries returned
values, several of them, so multi-value returns are expressible today.

**For this profile the asymmetry is not an inconvenience; it is the gap between "runs modules" and
"runs modules anyone can use."** A WebAssembly module has no notion of a program to run. It exports
functions with typed signatures, and calling one with arguments is the entire interface. The
conformance suite is built on it end to end: its actions invoke an export by name with a list of
typed constants and compare typed results.

Three answers exist, and **WA-1 picks one and records it with its consequences**:

1. **Encode arguments into the entry-point text.** The entry point is UTF-8 bytes this profile
   interprets, and the core carries them verbatim without decoding, re-encoding, or trimming them
   — a property its own contract asserts. So this profile may define an entry-point encoding: a
   name alone, or a name followed by a typed argument list in a specified textual form. This works
   today, needs no amendment, is fully testable, and is honestly ugly. Its real costs are that
   floating-point arguments must round-trip exactly through text, that a byte-array argument would
   be absurd, and that every embedder must implement the encoder.
2. **Invert the direction with a host capability.** The module imports a host function that returns
   its arguments; the entry point is a wrapper the embedder supplies. This keeps the invocation
   surface clean and moves the problem into the capability channel, which carries a span of 64-bit
   integers and one 64-bit result. It works for the numeric types, does not work for references,
   and requires the *module* to cooperate — which the conformance suite's modules will not.
3. **Propose an amendment.** A typed argument vector on the invocation request.

**The recommendation this roadmap carries into WA-1 is (1), with (3) opened against measured
evidence rather than distaste.** The reason is that (1) is sufficient for the conformance harness,
which is the consumer that must exist first, and it is available now against a contract that is
frozen. Section 20 states the amendment's counterweight, and it is the strongest in this document:
**this profile is the counterweight the core designed the contract against, and it is the profile
that needs an argument channel most.** A gap that binds the language with no parser, no text
format, and no dynamic loads is not one language's need in disguise.

### One further consequence, stated because it is easy to miss

The entry-point name is UTF-8 bytes, and a WebAssembly export name is an arbitrary well-formed
UTF-8 name — which may contain any character, including the ones any encoding scheme in answer (1)
would want to use as separators. The encoding must therefore be unambiguous over the full name
space rather than over the names that happen to appear in test modules, and a corpus entry exists
for an export whose name contains the separator. An encoding that works until someone exports a
function named `f(i32)` is a defect with a schedule attached.

---

## 11. The store, instances, and linking

This is the section where the core contract and WebAssembly semantics meet most sharply, so it
states the problem exactly before it states any answer.

### The problem

WebAssembly's third phase allocates a module instance **into a store**, and a store holds every
instance, memory, table, global, and tag in an embedding. Instances in one store can import each
other's exports; instances in different stores cannot see each other at all. That is not an
optimisation, it is the specification's own structure, and the conformance suite depends on it:
its scripts register an instance under a name and then instantiate further modules that import
from it, and a whole file of the suite is devoted to linking.

The core offers exactly one instantiation shape. `Instantiate` takes a verified artifact and
returns a fresh `IVmInstanceState`; there is no overload that instantiates into an existing one,
and there is no path from an executing profile to a nested core instantiation. Confirmed against
the shipped runtime rather than inferred: the two `Instantiate` overloads differ only in whether
limit overrides are supplied.

So a design must say where the store lives, and **there are exactly three places it can live.**

### The three readings

| Reading | The store is | What an artifact is | Cost |
|---|---|---|---|
| **A — one instance, one store** | Whatever one `IVmInstanceState` holds | One module, bare | Simplest and wrong for anything real. Two modules can never link, the suite's linking files cannot run at all, and the profile could never host a toolchain that emits more than one module. It is recorded here only to be rejected explicitly, because it is what an implementer arrives at by default. |
| **B — one artifact, one link set** | Whatever one `IVmInstanceState` holds, but an artifact carries several modules | A container: N modules plus a link plan | Entirely within contract, verified as one unit, and deterministic — the handle means *this whole set links*. But it needs a second format version and a Broiler-invented container, which is the thing section 7 argued against; and a browser that instantiates modules as it fetches them cannot use it. |
| **C — one runtime, one store** | Executor-scoped: the executor is created once per runtime and holds the store; each `IVmInstanceState` is one module instance's handle into it | One module, bare | Semantically the specification's own shape, keeps bare payloads, and supports incremental instantiation. But the store now outlives every individual instance, disposal order becomes a real design problem, and **there is no contract channel that names an instance for a later module to import from.** |

### What this roadmap fixes now, and what WA-5 decides

**Reading A is rejected here.** No milestone may deliver it, because a profile that cannot link
cannot run the suite and cannot host a real toolchain, and discovering that at WA-5 would mean
rewriting the store.

**WA-5 chooses between B and C, as a numbered decision with its consequence stated in both
directions**, and it chooses before the linker is written rather than during it. The inputs are
recorded now so the decision is not taken on taste:

- **The naming channel is the crux of C.** An import names a module and a field; something must
  map that module name onto an instance. The contract offers no member for it. Three candidates
  exist and each has a defect worth stating: the artifact descriptor's caller identity is a
  string the caller supplies and the verifier can read, but the core documents it as a diagnostic
  field that is "never parsed", and building semantics on a field the core may tighten is a defect
  with a schedule; a custom section in the module could carry a registration name, but that is a
  Broiler extension inside a standard format and any module carrying it becomes non-portable in
  one direction; and the entry-point channel of section 10 could carry a registration command,
  which folds this problem into the one WA-1 is already solving. **The third is the least bad and
  is the one WA-5 should cost first**, precisely because it reuses a channel this profile must
  build anyway.
- **B's container is not as offensive as it first looks.** Format version 1 stays a bare module,
  format version 2 adds the container, and the two coexist under one descriptor's version range.
  A browser uses version 1 and reading C's incremental path; a toolchain shipping a linked set of
  modules uses version 2. **The readings are not exclusive**, and the honest possibility that WA-5
  should evaluate is that this profile wants both: C for the store's shape, B for the case where a
  set really is one deployable unit.
- **The suite forces the issue and dates it.** WA-4 stands the harness up and WA-5 is the first
  milestone that can score a linking file. A decision deferred past WA-5 is a decision the
  conformance run makes by failing.

### What is fixed regardless of the reading

- **A memory, table, global, or tag is owned by the store, not by the instance that declared it.**
  The specification says so, and it is why an imported memory survives the disposal of the instance
  that exported it. Whichever reading wins, the ownership model is the store's and a test asserts
  that disposing an exporting instance does not invalidate an importing one's view.
- **Linking is refused as a whole or not at all.** A partially linked instance is never published.
  A test asserts that after a refused link there is no instance to find, mirroring the core's own
  no-partial-binding rule for host capabilities.
- **The link failure taxonomy is this profile's and it is published.** Unknown module, unknown
  field, kind mismatch, signature mismatch, limits mismatch on an imported memory or table,
  mutability mismatch on an imported global, and type mismatch on an imported tag are each a named
  diagnostic with its own case, because the suite distinguishes them and a single "unlinkable"
  answer would score identically while telling a developer nothing.
- **Host imports are checked twice, deliberately, and the two checks answer different questions.**
  At verification, the profile reads the registered capability descriptors from the verification
  context and refuses a module naming a host import the composition does not carry — that is
  `InvalidArtifact` with `UnsatisfiedHostAssumption`, and it means *this image can never run this
  module*. At instantiation, the linker resolves the actual bindings — and a failure there means
  *this store does not have it right now*. The first is a property of the image; the second is a
  property of the moment.
- **A shareable handle is only shareable where its assumptions hold.** Because verification records
  the capability assumptions it checked, the core refuses a handle presented to a runtime whose
  capability set differs, with `SharedHandleCapabilityAssumptionMismatch`. For this profile that is
  a feature and a constraint at once: two browser realms with the same imports share one verified
  module; two with different imports do not, however identical the bytes. Section 18's cache key
  must say so.
- **No byte source but the caller.** This profile declares no guest-initiated loads, and an
  architecture rule asserts the profile assembly reaches no filesystem, socket, embedded resource,
  or byte-returning host object. There is no dynamic module loading instruction in the language and
  there must be none in the implementation.

---

## 12. Traps, exhaustion, and why neither is a process failure

A trap is the specification's word for an abort that unwinds to the embedder. The list is closed
and every member is reachable from guest input: `unreachable`; integer division by zero; the one
signed-division overflow case; a float-to-integer conversion of a NaN or an out-of-range value;
an out-of-bounds memory access; an out-of-bounds table access; an indirect call through a null
element; an indirect call whose target signature does not match; and, once the relevant manifests
open, a null reference dereference, an out-of-bounds array access, and a failed cast.

Three properties are gates rather than notes:

**A trap is a value, not an exception.** It leaves the interpreter as a typed payload behind
`ProfileFault`, carrying its kind and the position that produced it. Whether the interpreter
*implements* unwinding with a CLR exception is a WA-6 decision with a measurement attached — it is
the single most common place an interpreter pays for a rarely-taken path — but the answer is
invisible at the boundary, and a scan asserts nothing derived from a CLR exception type crosses it.

**Exhaustion is not a trap, and conflating them fails conformance.** The suite has separate
assertion families, and a call-stack overflow is exhaustion. It maps onto `ResourceExhaustion`
naming `CallDepth` and a scope. An engine that reported it as a trap would score wrong on the
family that exists to check exactly this, and would also be telling a host that a module misbehaved
when the truth is that a ceiling bit.

**`memory.grow` and `table.grow` fail by returning, not by trapping.** They answer `-1` when the
allocation is refused. So a refused growth is *guest-observable* and must not become a core
resource-exhaustion outcome — the operation continues, and the module decides what to do. This is
the one place where a budget refusal is correctly invisible to the host, and a test asserts both
halves: that the allowance was not spent, and that the operation completed normally with the
module observing the failure. Getting this wrong in the other direction — turning a refused grow
into an aborted operation — would break a large class of real modules, which allocate
speculatively and handle the refusal.

**And the specification is explicit that growth stays non-deterministic even under `DET`**, in
order to be able to indicate resource exhaustion. So this profile's `DET` claim in section 6 does
not extend to memory growth, and the support table says so rather than implying a determinism it
does not have.

---

## 13. Memories, tables, globals, and the host boundary

### Memories

A linear memory is the largest thing this profile allocates and the main reason `LiveBytes` is
declared. Four properties are fixed here:

- **A memory is reported, grown, and released through the meter.** Allocation on instantiation,
  growth on `memory.grow`, release on store disposal. A memory that is allocated without being
  reported is a ceiling that does not exist.
- **Bounds checks are not optional and not deferred.** Every access is checked, and the check is
  where the bulk of the interpreter's per-instruction cost will sit. Section 19's measurement lane
  exists partly to publish that cost honestly rather than to hide it.
- **The representation decision names its own limits.** Whether a memory is a managed array, a
  pinned buffer, or a reserved virtual range with guard pages is a WA-6 decision with Native AOT
  and per-RID consequences, and a virtual-reservation strategy that works on one platform and not
  another is a claim about RIDs, not about the profile.
- **A successful growth invalidates every view a host holds over that memory.** Growth may
  reallocate, so any span, pointer, or buffer the embedder was handed before it is stale afterwards,
  and the rule is that it is *invalid* rather than merely stale — the embedder re-acquires or fails.
  The JavaScript API models exactly this as detaching the buffer, so a representation chosen without
  the rule is a representation that cannot express the boundary section 17 prices. This is stated
  here rather than in section 17 because it is this profile's rule and only its consequence is the
  embedder's.
- **Shared memories are excluded by name**, with their manifest unallocated. Section 14 says why.

### Tables and globals

A table is a vector of references with a declared element type and declared limits; a global is one
value with a mutability flag. Both are store-owned and both may be imported. The interesting cases
are the ones the suite tests and an implementation forgets: an imported table whose declared limits
must be *compatible with* rather than equal to the importer's declaration; an imported global whose
mutability must match exactly; and the segment-initialisation rule below, which is the one an
implementer is most likely to get wrong by reasoning about it.

### Segment initialisation is atomic per segment and not across segments

Both halves are tested by the suite and neither is what a reader guesses.

One segment is bounds-checked before any of it is applied, so **a failing segment writes nothing**.
But segments are applied in order, and a segment already applied **stays applied** when a later one
traps: the suite's linking file asserts that an in-bounds segment is visible after a following
out-of-bounds one has trapped. So "all or nothing" is right at the segment level and wrong at the
module level.

This is spelled out here rather than left to the specification because an implementation that chose
the intuitive whole-module rollback would pass every functional test anyone would write by hand,
and would fail the suite. Both halves get their own case, and the rule is copied from the pinned
revision rather than derived.

It matters most for an **imported** memory or table, because that is where the writes outlive the
failed instantiation: no instance is published, but the memory the segments wrote into belongs to
the store and keeps what it was given.

### External references and the host boundary

**The host boundary is typed, versioned, and refused at binding.** Every import names one exact
capability ID, one exact version, and one signature ID; a mismatch is refused when the runtime is
created, never at first call. Kind, reentrancy, thread affinity, and exception translation are
declared per capability. A failed required import leaves no partially bound runtime. An unbound
optional import has its branch exercised.

The channel itself is narrow and its narrowness shapes the manifests:

- **The value channel carries a span of 64-bit integers in and one 64-bit integer out.** `i32` and
  `i64` fit. `f32` and `f64` fit by exact bit pattern, and a fixture asserts the round trip
  preserves NaN payloads rather than passing through a floating-point register that might quiet
  them — which is the kind of thing that only fails under one RID's calling convention.
- **`v128` does not fit in one slot.** A host import taking or returning a vector needs two, and
  the vector manifest either splits it and documents the encoding or excludes vector-typed host
  imports by name. Splitting is the better answer and it is a decision with a published encoding,
  not an implementation detail.
- **A multi-result host import cannot be expressed at all.** The channel returns one value.
  Multi-value results from *guest* functions are fine — the result payload carries several — but a
  host function returning two values has nowhere to put the second. Section 20 carries this as an
  amendment candidate with a strong counterweight, and until then a multi-result host import is a
  named deterministic link failure rather than a silent truncation.
- **An `externref` is a `VmOpaqueRef`.** The core provides one: a runtime-scoped, generation-stamped
  handle with named refusals for a foreign or stale reference. That is the correct shape and this
  profile uses it rather than inventing one. Two obligations follow: an `externref` never leaves
  its runtime, and a table holding external references is reported to the meter as retaining them
  so that a store cannot pin unbounded host state invisibly.
- **A `funcref` crossing to the host has no channel and is excluded** — and the reason is narrower
  than an earlier draft of this bullet gave. It is *not* that no callback direction exists: section
  17 records that cross-runtime reentry is legal and is exactly how a host calls back into a guest.
  The reason is that **the capability channel this profile binds carries values, not callable
  references**, and manufacturing a callable host object out of a store-owned function reference
  would mean this profile publishing a projection whose identity, lifetime, and reentrancy rules it
  would then own on the embedder's behalf. Once the reference-types manifest opens, an exported
  `funcref` is representable inside the store and in a result payload as an opaque profile-owned
  reference, and is not projectable as a callable host object. **Stable identity across the seam —
  the same export handed out twice being recognisably the same thing — is the embedder's rule and is
  written in no document today**; WA-6's export projection records that it does not foreclose one.

No CLR type crosses the boundary in either direction. Arguments and results are the core's transfer
types, and diagnostics carry identity and position without carrying host state.

---

## 14. Suspension, threads, and what this profile does not declare

### It declares no suspension, and that is a statement about the manifests

At every manifest this roadmap allocates, WebAssembly execution runs to completion or to a trap.
There is no `yield`, no `await`, and no instruction that parks a frame. So the descriptor declares
`NotDeclared` for asynchronous instantiation and for external suspension, `Resume` answers the
named invalid-state reason, and `Unwind` releases store resources and runs no guest code.

Three things follow that are worth stating so nobody has to rediscover them:

- **`IVmProfileContinuation` is still implemented.** The core requires the type and the executor
  must answer `Resume` correctly, so the refusals are code and are tested. A profile that declares
  nothing still has to refuse correctly.
- **The frame model is designed to be capturable anyway.** This costs nothing today and is the one
  thing that cannot be retrofitted: a frame model that lives on the CLR stack cannot later be moved
  to the heap without rewriting the interpreter. Section 9's frame row says so, and WA-6's exit
  gate asks for the design rather than the implementation.
- **`Unwind` is simple here, and its simplicity is temporary.** WebAssembly has no user-visible
  finalisation, so terminal unwinding releases memories, tables, and host references and stops. The
  exceptions manifest does not change that — an unwind still runs no guest handler. A future
  stack-switching surface would.

### What would change it

The specification family has a stack-switching direction, and the browser embedding has a
JavaScript promise integration built on it. Either would make a WebAssembly frame parkable and
would make this section's declarations wrong. Neither is in any manifest here, and the roadmap
records the trigger rather than the schedule: **a manifest that admits a parking instruction is a
manifest that requires the suspension design, the routing decision, and the whole of the core's
suspension gate — and it re-opens WA-6's frame model.** That is a milestone-sized change and it is
named so it cannot arrive as an increment.

### Threads are excluded, and this exclusion is different from the others

Shared memories and the atomic instruction family are excluded by name, and the reason is not
scope. It is that the core's thread-affinity enforcement reaches a profile only where the core can
see a thread — its own published support table records affinity as *partial* for exactly this
reason, and says that a profile which starts its own threads is invisible to it.

A shared-memory WebAssembly implementation is a profile whose guest state is reachable from threads
the core did not create. Three things would have to be true before this component could admit it
truthfully, and the roadmap names them rather than leaving "later" to do the work:

1. the store's own concurrency model is designed and tested, including what a `memory.grow` on a
   shared memory does to another thread's view;
2. the metering story is answered, because `IVmMeter` is called from whichever thread is executing
   and the core's affinity rules were not written for a profile with its own; and
3. the core is asked whether a profile-owned thread is admissible at all — which is an amendment
   question, not a local one.

Until then, the support table names the exclusion and its deterministic failure: a module declaring
a shared memory or an atomic instruction is refused at validation, under a manifest that does not
admit it.

---

## 15. The conformance oracle

An engine that grades itself is not evidence. This profile builds the harness before it builds the
interpreter, and the harness's first job is not to score anything — it is to prove that a failing
test comes back as a failure.

This component has one structural advantage over a profile whose surface is a language with no
external suite, and it should be spent deliberately: **the suite grades the verifier before an
interpreter exists.** The malformed and invalid assertion families need no execution at all. So
WA-4 stands the harness up and scores those two families against WA-2's decoder and WA-3's
validator, and the first real conformance number this component publishes is about the boundary
that matters most, at a point where there is nothing to run.

**The method, stated so it can be built from this document.**

- **A pinned suite revision, resolved once.** An immutable commit, resolved before any shard
  starts, cached under a key containing it, and verified by re-reading the checked-out revision. A
  branch name is not a pin.
- **The ingestion path is test-only and is asserted to be.** The suite is distributed as scripts in
  the specification's text format. Reading them means a text-format reader, and that reader is the
  single largest piece of code in this component that must never appear in a product. A scan
  asserts it is absent from every product package and every published closure, and a negative
  control adds a reference to it and observes the scan fail.
- **Script semantics are implemented, not approximated.** The script language has module
  definitions in text and in binary form, registration of an instance under a name, actions that
  invoke an export or read a global, and assertions over each failure family. **Registration and
  the two module forms are not optional extras** — they are what section 11's decision has to
  serve, and a harness that skipped them would score a subset while reporting a total.
- **The host module the suite imports is supplied and its shape is recorded.** The suite's modules
  import printing functions, a table, a memory, and globals from a host module the script
  environment is expected to provide. The printing functions are genuine host capabilities; the
  table, memory, and globals are expressible as a WebAssembly module this harness synthesises. WA-4
  records which are which, because a reader needs to know how much of the harness is exercising the
  host boundary and how much is exercising the store. **One trap to check against the pinned
  revision rather than to assume away**: this host module's printing functions have historically
  been overloaded by signature under one name, and a capability table keyed on the import name alone
  would collapse them. WA-4 reads the actual names and signatures off the pin and keys the mapping
  on both, or records that the pin no longer needs it.
- **Content-independent sharding.** A test's shard is a stable hash of its normalized path modulo
  the shard count, so shard membership does not move when the selection changes and a shard's
  history stays comparable.
- **Selection as a recorded pipeline.** Discovery, then known-incorrect exclusion, then manifest
  scope filtering, then per-file selectability. The candidate count and the pre-sharding selected
  count are emitted separately from each shard's executed count, which is what lets the merge prove
  the shards covered the whole selection rather than a subset.
- **Per-assertion-family totals.** Malformed, invalid, unlinkable, uninstantiable, trap, exception,
  exhaustion, and return each report their own selected, executed, passed, failed, skipped, and
  timed-out counts. **A single aggregate percentage is not a result this component publishes**,
  because the families measure different things and an engine can be excellent at one and absent at
  another. A family that selects files and executes none is a named configuration failure, not a
  small total.
- **The self-check runs before every shard.** Deliberately broken fixtures with declared verdicts
  are run against the built profile, **and at least one control fixture that must pass.** A
  mismatch stops the run. A negative control injects a scoring regression, observes the mismatch,
  and reverts.
- **The malformed-before-invalid ordering is checked as its own fixture.** A module that is both
  gets one right answer, and an implementation that fused the phases scores it wrong. This is a
  self-check fixture rather than an ordinary case, because it is a property of the harness's
  interpretation as much as of the engine's.
- **Configuration failures are a closed, named set and each is a failure**: inconsistent shard
  configuration, missing suite revision, empty selection, no executed tests, and a manifest scope
  naming a file the suite does not contain. Removing one shard's report must produce incomplete
  coverage, not a smaller total.
- **The failure manifest is a queue, not an allow-list.** A path leaves it only after a minimal
  repository regression exists, the focused reproduction passes, the affected shard passes, and the
  record is updated. A hand-written entry that a run does not confirm does not survive.
- **The harness has its own regression suite**, run before any shard starts, with the crash
  classifier tested against recorded output. A measurement tool nobody tests is a measurement
  nobody can read.
- **The ratchet.** The first accepted per-family totals for a manifest are the floor. No later run
  of that manifest regresses against them.
- **The effective limit vector is published with every run.** A conformance total obtained under
  generous ceilings is not the total a product with tight ones would get, and section 8 makes that
  a real difference rather than a theoretical one.

Two things this section deliberately refuses. **No total, manifest entry, known-gap entry, or
triage finding from any other component is carried across** — this component starts at zero. And
**a differential against another engine is a cross-check, never the oracle**: two implementations
agreeing on the same wrong answer is still a failure, and another engine's movement may invalidate
an attribution but never accept one.

---

## 16. Deployment compositions, Native AOT, and the browser embedding

**This profile mints exactly one composition label, and the reason is structural rather than
modest.** A composition label describes *when source is compiled*. This profile compiles nothing,
so there is only one answer:

| Label | Contains at run time | What its Native AOT gate proves |
|---|---|---|
| `execution-only` | Decoder, validator, store, linker, interpreter, host adapter | That the accepted manifest set verifies and executes under Native AOT on every claimed RID |

The three-label pattern a language with a compiler needs does not transfer, and inventing a second
label here would be inventing a distinction this component cannot demonstrate. If a future decision
splits the product into more than one assembly — section 5 names the vector family and the
garbage-collected surface as the only plausible candidates — that produces further *closures*, not
further composition kinds, and each closure is evidenced on its own.

The gate itself is unchanged from the core's: the composition's closure is read off its own
published output, contains exactly the assemblies its register row declares, and contains no test,
reflection, dynamic-code, or IL-emission assembly. **A linker annotation without execution is
insufficient**, and no publish is evidence for another RID.

### The browser is the smallest closure here, not the largest

For a language whose source arrives as text, a browser composition must link a tokenizer and a
lowering, and its Native AOT gate proves the *larger* closure. For this profile the inverse holds,
and it is the clearest practical benefit of consuming a format rather than a language:

**A browser fetches WebAssembly bytes and hands them to verification.** There is nothing to compile,
no text format on the path, no lowering in the image. The browser composition is the
`execution-only` composition, unmodified. The closure a page exercises is the closure a
precompiled-artifact host exercises, so there is one thing to evidence rather than two, and the
evidence transfers between the two consumers because it is the same image.

**The host keeps its own seam.** An embedder already talks to WebAssembly through its own interface
in terms of bytes, an import object, and an instance. That interface does not change: an adapter
behind it verifies, instantiates, and invokes. The embedder never handles this profile's internal
types, and swapping the engine behind the seam stays a bounded change.

**The division of labour is strict.** The host owns fetch, identity, content policy, integrity
checks, and the event loop; this profile never fetches anything. There is no guest-initiated-load
path to police because the language has no instruction that would use one — which is why section 3
records the mediator as absent from this profile's design rather than as refused by its
composition.

---

## 17. The cross-profile boundary: the JavaScript API for WebAssembly

This section exists because the component that will consume this profile first is a browser, a
browser reaches WebAssembly through JavaScript, and **nothing in the core makes that free.** No
milestone here delivers it and no manifest admits it. What this section refuses to do is leave it
unpriced.

### What the boundary actually is

`WebAssembly.Module`, `WebAssembly.Instance`, `WebAssembly.Memory`, `WebAssembly.Table`, and
`WebAssembly.Global` are defined by a separate specification, in terms of JavaScript objects, with
a defined coercion between JavaScript values and WebAssembly values in both directions. Implementing
them means two core runtimes, carrying two profiles, exchanging values.

The core's rule for this is explicit and it is a refusal: **share mechanism, never share
semantics.** A shared value representation, frame layout, or opcode set is named in the core's own
table as something it exists not to own. So there is no cross-profile value channel and none is
coming, and a component that wanted one would be asking the core to grow the
lowest-common-denominator model its invariants exist to prevent.

**Two frozen facts settle the route, and neither is obvious from the core's non-goal alone.** The
non-goal excludes "an implied invocation bridge between two profiles", which reads to anyone who
stops there as forbidding the whole thing. It is narrower than that:

- **A guest-initiated load may not name another profile.** The provider must answer with an artifact
  of the profile that asked; a different profile is a provider contract breach, reported as a host
  failure with its own reason. So `WebAssembly.instantiate` called from the other profile is **not**
  a mediated load carrying a different descriptor, it can never become one, and an amendment to the
  mediator is the wrong thing to ask for. This forecloses the design most implementers would try
  first, and it forecloses it by construction rather than by policy.
- **Cross-runtime reentry is legal and depth-bounded.** A host object bridging two independent
  runtimes was admitted deliberately, because forbidding it outright would break exactly this case
  for no safety gain. That is the route: one profile calls out through a host capability, the
  embedder converts, and the other profile's runtime is invoked. **The chain is bounded by aggregate
  call depth, which is only a bound when both runtimes were created under one shared parent** — so a
  two-profile composition root creates one, and a composition that does not has no bound on the
  chain at all.

Neither fact is written in either profile's roadmap today; both are recorded here because the first
team to build the seam will otherwise discover the first one by having a provider refuse them, and
the second one by not having it.

### The consequence, stated plainly

**The seam is the embedder's, and it is a real cost rather than a formality.** A JavaScript
`WebAssembly.Instance` is a host object owned by the embedder; calling one of its exports is the
embedder receiving a call from the JavaScript profile, converting arguments into this profile's
transfer types, invoking this profile's runtime, and converting results back. Four consequences
follow, and each is a thing a browser team will meet:

1. **Every call crosses the host twice.** A JavaScript-to-WebAssembly call is not a call; it is two
   host-boundary transits and a conversion in each direction. That is the correct price for two
   profiles that share no semantics, and it is a price that shows up in exactly the benchmark
   people run first.
2. **`WebAssembly.Memory` is the hard case, not the function call.** The JavaScript API exposes a
   memory as a byte buffer that JavaScript reads and writes directly and that reflects the guest's
   writes immediately. The core's transfer types are integers, byte spans, and opaque references —
   none of which is a *shared mutable region*. Making a memory visible to a JavaScript profile
   means either the embedder mediating every access, which is unusably slow, or a shared buffer
   both profiles reach, which is shared semantics by another name. **This is the largest unpriced
   risk in this component and it does not belong to this component to solve.** One rule inside it
   *is* this component's and section 13 now carries it: **a successful `memory.grow` invalidates any
   view a host holds over that memory**, which the JavaScript API models as detaching the buffer. It
   is stated here because a memory representation chosen without it is a representation that cannot
   express it, and WA-5 takes that decision.
3. **Two profiles in one catalog reach each other through their defaults.** A maximum binds only
   the profile an artifact names, so this profile's maxima constrain a JavaScript profile beside it
   not at all. But a host that adopts profile defaults rather than stating ceilings gets the tightest
   in the catalog, so the two components' *default* vectors are coupled whether or not anyone
   intended it, and a stingy default here is felt over there. That is a coordination obligation
   between two independently owned components, smaller than the one this point used to describe -
   a host stating explicit ceilings never meets it - but real. **WA-0 records this profile's defaults
   with that consequence stated**, and the browser composition - wherever it lives - owns the
   reconciliation. Until 2026-08-31 this point said the maxima clamped each other too; that was a
   defect in the core, not a property of the contract, and it has been removed.
4. **The trap-to-exception mapping is the embedder's, and the exception-to-trap mapping is
   worse.** A WebAssembly trap surfacing into JavaScript becomes a JavaScript error object; a
   JavaScript exception thrown from an imported function must unwind WebAssembly frames. This
   profile's half of that is section 12's host-failure rule, and the other half is not this
   profile's at all.

### What this roadmap commits to

- **It commits to owning nothing of it**, and to saying so in the support table rather than letting
  a reader infer that a WebAssembly profile in a browser image implies a working JavaScript API.
- **It commits to not foreclosing it.** Where a design choice inside this profile would make the
  boundary harder — a memory representation that cannot be handed out as a contiguous region, an
  export projection that cannot enumerate, an opaque reference model with no stable identity — the
  choice is recorded with that consequence noted at the milestone that takes it. WA-6's memory
  representation row and WA-7's export projection each carry that clause.
- **It commits to naming the owner.** A browser integration is a consumer of two profile components
  and belongs to whichever component composes them. This roadmap's obligation is to make the price
  visible before that component exists, not to pay it.

---

## 18. Persistence and the code cache

**No milestone here delivers persistence, and the reason is not scheduling.** The core admits a
bounded persisted envelope by contract and implements none, and no core milestone approves one. A
profile-owned cache format written against a core envelope that does not exist would be a second
serialization path with nothing to hold it to the first.

What this roadmap does instead is fix the design so it stays reachable, at no cost today:

- **The cache key is named now, and it is the handle's identity minus the terms that cannot
  survive a process.** Module bytes identity — the artifact content hash, which is the core's own
  key field — the format version, the feature manifest identity and version, **the descriptor
  revision**, the verifier semantic version, the core contract version, **this profile's declared
  hard-maximum vector**, and **the per-import capability tuple for every import the artifact binds**.
  That last term is not optional: section 11 records that the core refuses a shared handle whose
  capability assumptions differ, so a cache that ignored them would produce entries the core then
  rejects — the good failure, but only because the core checks. It is also **seven fields, not
  two**: capability ID, version, signature ID, kind, reentrancy, exception-translation mode, and
  whether an optional import was bound. The last three change the legal control flow at a call site,
  so a key that omits them collides two compiled variants onto one entry, which is a correctness
  defect in the cache rather than a performance one. The tuple covers imports the artifact **binds**;
  registered-but-unimported capabilities are excluded, so an unrelated composition change does not
  invalidate every entry. This profile cites the core's tuple rather than restating it.

  **The *effective* limit vector is deliberately not in the key**, and an earlier draft of this row
  had it. It is part of the handle's in-process identity — which is why two runtimes with different
  ceilings do not share a handle — but it is a timing-dependent, process-local quantity, and
  persisting it would produce a key that never recurs. Correctness does not depend on it, for the
  reason the next bullet already gives: loading always re-validates, and re-validation recomputes
  the vector, so a persisted module never carries a ceiling decision forward. What the key needs is
  the composition-invariant half — **this profile's declared hard maxima**, which is what actually
  varies between two images and does recur.
- **Nothing warmed or process-local is ever serializable.** No object references, no delegates, no
  process-local identities, no warmed caches, no specialized opcodes that have become
  authoritative, no host handles. That is a property of how the verified module is designed, and
  invariant 8's no-mutable-state-reachable-from-a-handle rule — pinned by the handle-immutability
  structural scan in WA-5's exit gate — is what keeps it true before there is a writer to violate
  it.
- **Loading always re-validates.** Outer-envelope compatibility never implies payload compatibility,
  and interpreting old bytes under new semantics is prohibited. A checksum detects corruption; it
  does not authenticate code.
- **The reopening trigger is a measurement, not an argument.** WA-10 measures verification
  throughput per byte and cold-start cost across a range of module sizes. If a host's latency
  budget is missed by a stated margin, the persistence question reopens against that number with
  the core, as a joint gate.

**One neighbouring question is already answered and this profile plans against the answer.** At core
contract version 1 the byte round trip is mandatory. For this profile that costs nothing: the bytes
always came from outside, so there is no in-process producer to bypass serialization for. This
component therefore has no reason to want that amendment, which section 20 records — because a
counterweight that stays silent when it agrees is only half a counterweight.

---

## 19. Measurement discipline

Every figure this component publishes obeys the same rules, and the rules are stricter than the
figures are interesting. **The rules are the core's, restated here only because a reader of this
document must be able to check a figure without leaving it** — the authority is the core's baseline
register, and where the two ever differ the register wins. One thing this component adds that a
restatement alone would lose: a **declared repetition count**, fixed at WA-1 and published with
every bundle, because "retained repetitions" is a release gate nobody can fail without a number.

1. **A control that is the same workload minus the thing being measured.** A difference between two
   different programs is a comparison, not an attribution.
2. **Interleaved lanes.** Candidate and control alternate inside each repetition rather than running
   as two blocks, so a machine that gets slower slows both.
3. **An A/A lane.** The candidate is measured a second time, identically. A candidate-versus-control
   difference smaller than the A/A difference is reported **below resolution**, not as a result.
4. **Every repetition retained**, with no outlier policy and no statistical model. The spread
   between repetitions is most of what a single figure hides.
5. **A condition checked before and after every lane.** The operation must still do what its name
   says. A measurement whose module quietly trapped is the most dangerous output a harness can
   produce: it is fast, it is stable, and it is a number for the abort path.
6. **An immutable manifest written before either arm runs**, carrying the commit, the clean-tree
   assertion or the retained patch, the resolved dependency graph, the SDK and runtime identity,
   the pinned specification revision, and the effective limit vector.
7. **Effective, not requested, configuration.** Each measured child reports its actual RID, process
   architecture, GC mode, and tiering state, and the arm fails on a mismatch.
8. **Exactly one evidence class per bundle**, declared up front, with exactly one predeclared
   decision. A bundle that proves the harness works accepts nothing, even when every number in it
   moves the right way.

Three things this component will not do. **No benchmarking framework**, because a framework's
warmup, pilot, and outlier policies would be part of every published figure and invisible in this
repository. **No cross-profile fuel comparison**, because fuel is this profile's own unit and means
nothing beside another's. **No comparison against any other WebAssembly engine**, in either
direction, at any point — and this refusal will be under more pressure here than it would be
elsewhere, because comparable engines are numerous, public, and easy to run. An interpreter with no
compiled tier has a known shape of result, and publishing it beside a compiling engine's would be
comparing two different products under one word.

---

## 20. Amendments, and this profile's duty as the counterweight

The core's amendment procedure exists because a contract frozen before its first profile will meet
something it cannot express. Recording the candidates now is cheap; discovering them during an
implementation is not. Each of these is a **proposal or a refusal**, never a workaround inside the
core's execution loop.

**And this profile carries a second duty that the other does not.** The core's own roadmap names it
the counterweight: the profile with no parser, no text format, and no guest-initiated loads, whose
job is to make the difference visible between a feature that is genuinely general and one language's
need wearing a general shape. A counterweight that only ever asks for things is not one. So the
table below records what this profile **needs**, and the section after it records what this profile
**does not need** — because the second list is the one that makes the first list mean something.

### What this profile would ask for

| Candidate | Why it is needed | Strength |
|---|---|---|
| **An argument channel on invocation** — arguments only, deliberately | An invocation request carries one entry-point name. A WebAssembly module is a set of exported functions with typed signatures and nothing else, and the conformance suite is built end to end on calling them with arguments. Section 10's answer (1) works and is a text encoding of a typed call. **The scope is arguments and not results**: the typed payload already carries results and several of them, so multi-value returns are expressible today, and filing argument and result as one amendment would put two differently-scoped versions of one capability into the register — which is how a capability gets approved at the wrong width. | **The strongest in this document.** A profile with no parser, no text format, no dynamic loads, and no notion of a program still needs it — which is precisely the test the core wrote this profile in to apply. **The other intended profile graded the same capability weak**, reasoning from a browser that compiles a *program* rather than a call; that reasoning stops holding the moment it hosts this profile, since an export call is a typed call whose arguments originate over there. The two gradings must be reconciled to one scope and one strength **before either component records its entry-point encoding**, because the core's procedure requires each amendment record to state the other profile's position and that answer is currently unanswerable. Opened against WA-1's recorded encoding and the cost that encoding actually imposes, not against distaste for it. |
| **A padding-tolerant variable-length integer reader in the core's binary package** | The core's readers accept canonical encodings only; the specification requires padded ones inside a byte budget; production toolchains emit padded immediates. Section 7 resolves it locally by decoding integers in this profile. | **Moderate, and honestly so.** It is general to any format that inherits this encoding, and there are several. It is *not* something the core needs for its own envelope, and the local resolution works. Opened only if a second profile meets the same wall, which is exactly the extraction gate's own standard. |
| **Multi-result host capabilities** | The capability channel returns one 64-bit value. A WebAssembly host import with two results has nowhere to put the second, so it is a named link failure. | **Moderate.** Any profile whose calling convention admits multiple results meets it. Until then this profile refuses the import deterministically rather than truncating, and the refusal is published. |
| **A wider value slot on the capability channel** | `v128` does not fit in a 64-bit slot. Splitting works and needs a published encoding; a wider slot would not. | **Weak.** This is one type in one instruction family. Recorded so it is not mistaken for the previous row. |
| **A charging hook for work done inside a host capability** | Wall clock covers a slow capability; it does not cover one that allocates on this profile's behalf. | **Strong: general**, and this profile reaches it by the same route any other would. |
| **A persisted envelope** | Section 18. | **Strong: general**, and already admitted by contract. It needs a gate rather than an amendment. |
| **A refusable retention member on the metering surface** | Section 3: the retention report returns nothing, so a ceiling-class dimension cannot carry a guest-observable refusal, while section 12 requires a refused `memory.grow` to be exactly that. The local resolution — admit growth on a charge, report retention for accounting only — works, and this row exists because the alternative should be visible rather than rediscovered. | **Moderate, and general.** Any profile with host-visible retained state that the language can ask to grow meets it, which is the counterweight test passing. Opened only if WA-5's memory-representation decision shows the local resolution costs something real. |

### What this profile does **not** need, and says so

This is the counterweight discharged, and each row is a case where a general-looking requirement is
one language's shape:

| Candidate a language profile might raise | This profile's answer |
|---|---|
| **An in-process producer input form — compiling straight to a verified handle, skipping the byte round trip** | **Not needed, and this profile would not co-sign it.** There is no in-process producer. Every byte arrives from outside the trust boundary, so serialization is not a critical-path cost here — it is the input. A profile that meets this wall meets it because it compiles its own source in-process, which is a property of that profile and not of the contract. |
| **Lazy per-section or per-function verification** | **Not needed, and actively declined.** The specification *offers* this profile the permission and section 8 refuses it, because a deferred check is a check reported as a trap. If a latency measurement ever justified reopening it, this profile would still want the whole-module answer for the malformed and invalid families, so the amendment would have to preserve exactly what invariant 3 asks for. Recorded as a refusal rather than a silence. |
| **Streaming or incremental verification** | **Wanted eventually, needed by nobody yet.** A browser does stream WebAssembly bytes and would like to validate as they arrive, so this profile is not indifferent — but it has no measurement, and the core already carries a registered amendment shape. Reopened against WA-10's throughput figures, not against the observation that browsers stream. |
| **Nested instantiation through the mediator** | **Not needed.** The language has no dynamic module-loading instruction. Section 11's problem is about *linking*, which happens at instantiation, and none of its three readings requires the core to instantiate anything nested. A profile that needs nested instantiation needs it because its language can ask for code while running, which is the exact property this profile does not have. |
| **A shared cross-profile value channel** | **Refused.** Section 17 states the cost of not having one and still refuses it, because it is shared semantics and the core's rule against that is right. The price is paid at the embedder's seam, where it is visible. |
| **Asynchronous instantiation** | **Not needed at any allocated manifest.** Instantiation runs a start function that either completes or traps. Section 14 names what would change it and makes that a milestone rather than an increment. |
| **External suspension** | **Not declared.** A debugger for this profile is a separate component, and declaring a capability nothing exercises would make the descriptor claim something the evidence does not show. |

The rule that governs all of it: **a design that can only be hosted by a second core state machine
is refused.** Exactly one core state machine and one core contract version exist in a product graph
at any time.

**Two procedural facts, so the counterweight duty is discharged without overreaching it.** A
refusal in the table above is **recorded, not blocking**. The core's procedure asks whether the
other intended profile could use a capability, is unaffected, or refuses it, and this profile's
answers are the third kind — but a profile with a veto over a core amendment would be a
profile-to-profile dependency established by governance rather than by reference, which is exactly
what the extraction gate's fourth condition exists to prevent. Saying "this profile would not
co-sign it" is a counterweight answer for the record and not a decision. And the procedure is
currently **unexecutable**: no amendment has been minted, and the minting and both co-signing roles
are held by one person, so no co-signature would be independent. Every row here is filed and held
rather than scheduled, and none is admissible until it names a merged or approved capability.

---

## 21. Milestones

The [status ledger](roadmap.status.md) is the authority for what has been accepted. This section
states planned work and objective exit gates only.

Three things run through every milestone and are stated once. **The core is implemented, not
accepted**, so WA-0 and WA-1 build against implemented contracts while WA-2 onward additionally
depend on the core contract being accepted — a gate this component does not hold. **Owner and
reviewer roles are named per milestone**; where one person holds several, the non-independence is
recorded as a limit on what these gates prove, not resolved by assertion. And because there is no
seed, every milestone's fourth row states **what it deliberately does not do**, which is the scope
control a copied codebase gets for free from the shape of what it copied.

### WA-0 — Boundary, placement, identity, and the assurance floor

- **Owner:** profile architecture owner, with the core's topology owner co-signing placement and
  the release owner co-signing the licence position.
- **Next action:** Decide and record, each as a dated decision with a registered rule and a passing
  witness: where this component lives relative to the core and the aggregate repository; the
  profile ID and the `Broiler.*` package identity it obliges, **with the manifest-namespace
  consequence of the spelling stated**; the assembly topology of section 5 and the single-assembly
  default; the feature manifest allocation of section 6 and the `DET` position; the one composition
  label; **the fifteen profile hard maxima and the fifteen defaults, with section 17's cross-profile
  consequence stated in the record, and with the three guest-load *defaults* published as
  `Unconstrained` and the reason recorded** — a dimension declared inapplicable in the budget matrix
  is a statement about what this profile charges and not a licence to write a zero into the vector a
  neighbour adopts. The maxima need no such care: they bind this profile's own modules alone; the nullable and unsafe-code
  positions; and the intended
  specification revision and suite revision, marked provisional until a human has retrieved,
  hashed, and archived them. Stand up this component's own assurance system — annotation grammar,
  exemption predicate, generated review report, fingerprint binding, release-mode gate — and its own
  evidence-bundle contract and collection script. Publish the licence and third-party notice, and
  confirm or amend the core's standing third-party claim.
- **Dependencies:** Named ownership. No dependency on any core milestone's acceptance.
- **Objective exit gate:** An acyclic shell graph builds Release with zero warnings; architecture
  rules express every forbidden edge — including **both halves** of the no-edge-to-another-profile
  rule and the no-product-reference-to-the-harness rule — each with a passing witness and a
  negative control that fails when injected and passes after revert; a scan asserts no source file,
  project file, or build item resolves outside the component root, and an unresolvable build item is
  **reported rather than skipped**; **a two-profile catalog test composes this descriptor beside a
  second profile that declares guest-initiated loads and proves that this profile's maxima reach that
  profile's work not at all, while its adopted defaults do**, with a negative control that writes a
  zero into one guest-load *default*, adopts defaults rather than stating ceilings, and observes the
  neighbour's nested load refused with a resource exhaustion naming a dimension this profile does not
  use; the public API baseline mechanism exists and compares in both
  directions, with an injected member failing it and a deleted member failing it too; the assurance
  generator is a fixed point — a regeneration moves no byte — and a negative control proves it
  refuses to write a reviewer identifier no source line carries; the release-mode gate names each
  blocking declaration individually rather than counting them; the evidence-collection script exists
  and this milestone's own bundle was produced by it; the licence and notice carry the Apache-2.0
  text and the attribution the ingested suite will require; and the core's third-party claim is
  confirmed scoped or amended, with the release owner's co-signature recorded.
- **Deliberately not done:** No product code. No decoder, no descriptor, no project that references
  a core package. A milestone that stands up an assurance system and also writes a verifier cannot
  demonstrate that the assurance system caught anything.

### WA-1 — The whole contract loop on the smallest module that is still WebAssembly

- **Owner:** profile contract owner, with release and AOT review of the composition root.
- **Next action:** Mint `broiler.webassembly.slice` and define format version 1 as a bare module.
  Write the decoder for the slice's subset of the grammar over the core's byte primitives, with
  this profile's own variable-length integer readers and its own bound-before-use count reader.
  Write the validator for the slice. Implement all seven core-facing types, including the
  allocation-meter adapter and the bounds projection. Fill every descriptor row in one full-arity
  construction, with the rows section 9 owns marked **provisional** and each naming the milestone
  that will settle it. **Take the `StructuralDepth` declaration decision of section 3 and record
  it.** **Take the entry-point decision of section 10 and record its encoding**, including its
  behaviour on an export name containing the encoding's own separator. Write the test-only binary
  encoder that generates corpus entries. Stand up the `execution-only` composition root with a
  closure self-report mode.
- **Dependencies:** WA-0. Deliberately **not** core acceptance: the point of this milestone is to
  find contract defects against a surface small enough to read in an afternoon.
- **Objective exit gate:** The named composition **publishes and runs** on every claimed RID — the
  set named here, non-vacuously — under JIT, trimmed self-contained, and Native AOT with trim and
  AOT warnings treated as errors, executing a verified module to its expected answer in every mode,
  each closure report containing exactly the declared assemblies and no test, reflection,
  dynamic-code, or IL-emission assembly. **Each of the five verifier outcomes** is produced by a
  named retained corpus case, the invalid-artifact case carrying a diagnostic code and a byte
  position and the exhaustion case naming one dimension and one scope. **Each of the five
  execution-step kinds** is produced by a named test, including a contract violation from a
  deliberately non-conforming variant; `Suspended` is declared unreachable from this profile with
  the refusal tested rather than an out-of-manifest instruction minted to reach it. The descriptor
  is admitted by a catalog build, and named negative cases produce each catalog refusal this
  descriptor can provoke. A module whose descriptor names an absent profile answers
  `UnsupportedProfile` / `ProfileNotInCatalog` **with no payload byte examined**; one naming an
  unaccepted manifest answers `UnsupportedFeatureManifest`; one naming an out-of-range format
  version answers `UnsupportedProfileFormatVersion`. A second profile composed in the same catalog
  proves a foreign payload is dropped rather than projected, and every payload kind this profile can
  mint lies inside its declared range. **A padded variable-length integer is accepted and an
  over-long one is rejected, each by its own corpus entry**, and a control test proves the accepted
  case would fail if the core's canonical reader were substituted. A case proves the interpreter
  sizes its operand stack from a bound **computed at validation and stored on the verified state**,
  never from a number the payload chose. A case mutates, disposes, and concurrently overwrites the
  caller's payload buffer after verification returns, and neither the verified state nor the result
  changes. The entry-point encoding round-trips a module export whose name contains the separator.
  The slice corpus replays identically twice with no residue, contains at least one successful
  control entry, and the verifier throws on none of it.
- **Deliberately not done:** No memory, no table, no global, no import, no float, no linking. The
  binary encoder is test-only from its first line and is never referenced by a product project.

### WA-2 — The decoder, the integer decision, and the malformed corpus

- **Owner:** verification-boundary owner.
- **Next action:** Complete the decoder over the whole binary grammar the allocated manifests will
  need, including every section, the section-order table derived from the pinned revision, UTF-8
  name validation as the specification defines it, the data count section's relationship to the
  code and data sections, and custom sections correctly ignored. Grow the malformed corpus from
  slice scope to grammar scope, with a sweep that truncates the canonical module at every offset and
  a sweep that inverts every byte. Stand up the decode fuzz target. **Retrieve, hash, and archive
  the specification revision** and record the pin.
- **Dependencies:** WA-1, plus one external gate: **the core contract accepted**, which is open
  today and is recorded in the ledger as a named blocker with its holder and unblock condition.
- **Objective exit gate:** Every corpus entry produces its recorded outcome, reason, and diagnostic
  code on JIT, trimmed, and Native AOT hosts, the verifier throws on none, control entries verify
  successfully, and a repeat leaves no residue; **a mutated corpus entry proves the replay detects a
  changed observed triple**; the ordering assertions hold for every entry including every failing
  one — ceilings materialized before the first byte, refusal before the allocation it would have
  authorised, every declared count compared against its bound before it sizes a buffer or bounds a
  loop; **the section-order table is derived from the pinned revision rather than from section
  identifiers, and a corpus entry exists for every adjacent pair it forbids, including the tag and
  data-count pairs whose identifiers and order disagree**; a name that is not well-formed UTF-8 is
  rejected with its own diagnostic, including the cases the platform's own decoder would accept;
  a scan asserts the profile assembly contains no call to the core's canonical variable-length
  readers and no second implementation of the count-bound comparison; the decode fuzz session
  retains its corpus identity, its iteration budget with a stated floor, its runtime settings, and
  every minimized counterexample, and any counterexample is closed by a **named regression, never an
  allow-list entry**; the specification pin is recorded with its hash and the human action that took
  it, or the exclusion is named; and the trim and AOT analyzers are force-enabled with **zero
  warnings anywhere in the reference closure** rather than none attributed to the project.
- **Deliberately not done:** No type checking. A decoder that also validates cannot demonstrate that
  a malformed module is reported malformed rather than invalid, which is invariant 4's whole
  content.

### WA-3 — Validation as one verification stage, and the diagnostic registry

- **Owner:** verification-boundary owner.
- **Next action:** Implement the specification's single-pass validation algorithm over the value,
  control, and initialization stacks, with the polymorphic treatment of unreachable code. Implement
  every module-level validation rule the allocated manifests need. Publish and version the
  diagnostic-code registry and the byte-position encoding, **stating which of the core position
  record's four fields this profile populates and which carrier — the verifier outcome's code and
  position pair, or the typed payload — each registry code travels on**. Record the decision on
  whether decode and validate are fused within one function body, with the module-granularity
  phase-order property stated either way. Extend the corpus to invalid modules, **and add a nesting
  corpus**: the decoder and the validator walk attacker-controlled nesting, and the guest-frame depth
  bound does not reach either of them.
- **Dependencies:** WA-2.
- **Objective exit gate:** The diagnostic-code registry is published, versioned, and bound in
  **both** directions — every emittable code appears in it, every code in it is reachable from a
  named case; every validation rule the manifest requires is produced by a named case and maps onto
  exactly one core invalid-artifact reason with no invented or aliased reason; **a module that is
  both malformed and invalid is reported malformed, by a named case that fails when the phases are
  fused at module granularity**; unreachable code validates polymorphically, proved in both
  directions by a case that must be accepted and a case that must be rejected, each drawn from the
  behaviour the specification's algorithm defines rather than from an implementation's habit;
  an implementation-limit refusal is `ResourceExhaustion` naming a dimension and a scope and **not**
  `InvalidArtifact`, by its own case per limit the profile enforces — **including the two decoder
  families section 7 reclassifies**, the over-ceiling vector length and the over-ceiling artifact
  size, each with a corpus entry recording the exhaustion triple rather than an invalid-artifact
  one; **a nesting corpus generated to the effective `StructuralDepth` ceiling and one level beyond
  it is refused as `ResourceExhaustion` naming a dimension rather than terminating the process, on
  every claimed RID under Native AOT** — a stack overflow is uncatchable and kills the host, so "the
  validator throws on nothing" cannot observe it and this clause is what does; each module is decoded
  at most once during verification, asserted by a case; the validator throws on nothing across the
  whole corpus; and the corpus's invalid half replays identically on all three publish modes.
- **Deliberately not done:** No execution and no store. A validator whose milestone also delivers an
  interpreter will have its lazy-validation temptation resolved by convenience rather than by
  section 8's argument.

### WA-4 — The oracle, standing before the interpreter exists

- **Owner:** conformance owner, with the verification-boundary owner for the scoring of the two
  families this milestone can run.
- **Next action:** Pin the suite revision. Build the script reader, the harness, the self-check, the
  sharding, the merge, the scope manifests, and the audit command. Implement the script commands
  the malformed and invalid families need, and implement registration and the two module forms even
  though nothing yet consumes them, so that WA-6 does not discover the harness cannot express its
  own tests. Run the malformed and invalid families and set the ratchet for them.
- **Dependencies:** WA-2 and WA-3 for something to score. **Not** WA-5: this milestone exists
  because those two families need no execution.
- **Objective exit gate:** The suite revision is pinned to an immutable commit, resolved once before
  any shard starts and verified by re-reading the checked-out revision; **the self-check runs
  against the built profile before every shard** and every deliberately broken fixture returns its
  declared verdict alongside at least one passing control, with a negative control that injects a
  scoring regression, observes the mismatch, and reverts; **the malformed-before-invalid ordering is
  a self-check fixture in its own right**; the malformed and invalid families run to completion and
  publish their own totals from an exact commit, an exact suite revision, and a published effective
  limit vector, and **that run sets the ratchet for those two families and for no others**; removing
  one shard's report reports incomplete coverage rather than a smaller total, a configuration field
  differing between shards reports a named inconsistency, and an empty selection and an all-skipped
  selection are each named configuration failures; the failure manifest is proved to be a queue by a
  case where a listed path still fails and a case where a hand-written entry does not survive; the
  harness, merge, audit, and scope tooling each carry their own regression tests run before any
  shard starts; and **a scan asserts the script reader, the corpus store, the encoder, and every
  suite file appear in no product package and in no closure report**, with a negative control that
  adds a product reference to the script reader and observes the scan fail.
- **Deliberately not done:** No aggregate percentage is published, then or ever. The two families
  scored here are reported as themselves.

### WA-5 — The value model, the store, and the interpreter

- **Owner:** profile runtime owner.
- **Next action:** Take the section 9 decision as a numbered decision stating its consequence in
  both directions, **before any interpreter source is written**, with all eight rows including the
  vector-width row that only matters later. Implement the store for a single module: memories,
  tables, globals, and their metering. Implement the interpreter over the numeric and control
  surface, memory loads and stores, `memory.grow`, and `call` and `call_indirect` within one module.
  Implement traps as typed payloads. Place every poll and every charge. Measure native frame cost
  per interpreter frame on each claimed RID and derive the `CallDepth` default from it. Choose the
  uncharged-work bound, the charging granularity, and the cancellation poll bound from measurement.
  Catch every internal exception at this profile's own adapter.
- **Dependencies:** WA-3. The ABI decision is a **gate on entry**, not this milestone's first task.
- **Objective exit gate:** The numbered ABI decision exists with all eight rows, with fixtures and
  Native AOT representation probes retained; every executor answer is one of the five step kinds and
  a scan asserts no profile code names a core outcome category; **every trap in the closed list is
  produced by a named case and arrives as a typed payload behind a profile fault**, with the
  position it was produced at; **`memory.grow` and `table.grow` refusal is guest-observable**, proved
  by a case in which the growth is refused, the operation completes normally, the module observes
  the negative answer, and the allowance was not spent; **a call-stack exhaustion is
  `ResourceExhaustion` naming `CallDepth` and its scope rather than terminating the process**, on
  every claimed RID under Native AOT, with the `CallDepth` default derived from a retained,
  reproducible frame-cost measurement per RID; **no exception escapes the interpreter** across the
  corpus and the fuzz corpus; a deliberately non-polling variant completes as a profile fault with
  the poll-bound reason and the runtime poisoned to accept only disposal; **a proportionality
  fixture exists for each named operation family of section 9**, each with an unsimplified control,
  each showing fuel charged as a monotone non-decreasing function of input magnitude and at least
  the declared ceiling, with the declared function and granularity recorded — and `memory.fill` over
  a large memory is a named negative control that a flat charge fails; a deliberately non-charging
  variant is detected and reported as a contract violation; two runtimes read one shareable handle
  concurrently with no synchronisation and a **structural scan** asserts no memory, table, global,
  or mutable cache is reachable from a handle, with the scan's mechanism and its residual stated;
  the memory representation decision names its own per-RID limits and does not foreclose section
  17's boundary; and the `assert_return`, `assert_trap`, and `assert_exhaustion` families are scored
  for the single-module subset of the suite with their own ratchets.
- **Deliberately not done:** No imports, no linking, no second module in a store. Section 11's
  decision is not taken here — it is taken at WA-6 with the linker in front of it.

### WA-6 — Linking: imports, exports, the store decision, and host capabilities

- **Owner:** profile runtime owner with the host-capability owner.
- **Next action:** **Take the section 11 decision** between the link-set artifact and the
  runtime-scoped store, as a numbered decision with the naming channel resolved and its consequence
  stated in both directions, before the linker is written. Implement import resolution and export
  projection for all four import kinds. Implement the link failure taxonomy with a distinct
  diagnostic per failure. Implement the two-point host-import check: capability assumptions at
  verification, bindings at instantiation. Implement `externref` over the core's opaque reference.
  Synthesise the suite's host module: its printing functions as host capabilities, its table,
  memory, and globals as a module. Declare host capability imports in the descriptor.
- **Dependencies:** WA-5 for the store and the interpreter, WA-4 for the harness that scores it.
- **Objective exit gate:** The numbered store decision exists with the naming channel resolved and
  a retained cost for the option not taken; **the linking and unlinkable families of the suite are
  scored with their own ratchets, non-vacuously**, and the harness's registration and dual module
  forms are exercised rather than merely present; each link failure kind has its own diagnostic and
  its own case, and a single aggregate unlinkable answer is proved absent by a case per kind;
  **a refused link publishes no instance**, asserted by a case that finds no instance after the
  refusal; **a start-function trap publishes no instance**, asserted separately, and is
  distinguishable from a link failure by its payload kind; **the host boundary is proved at binding
  time** — a capability whose version, signature ID, or kind does not match a declared import is
  refused when the runtime is created and not at first call, each mismatch by its own named case; a
  failed required import leaves no partially bound runtime; the unbound branch of at least one
  optional import is exercised; a module naming a host import the composition does not carry is
  refused **at verification** as `InvalidArtifact` / `UnsatisfiedHostAssumption`, and a module whose
  binding fails at instantiation is refused there — **the two are proved to be different answers by
  a case that produces each from the same module in two compositions**; a scan asserts every
  argument and result crossing the boundary is one of the core's transfer types and no CLR type
  crosses it; a fixture asserts a float host argument round-trips by exact bit pattern including a
  NaN payload; **a multi-result host import is a named deterministic link failure rather than a
  truncation**, by its own case; an `externref` presented to a second runtime is refused, and a
  table of external references reports its retention to the meter; and a handle verified under one
  capability set is refused by a runtime with a different one, with the core's own reason.
- **Deliberately not done:** No new language surface. This milestone adds no instruction; it makes
  the ones WA-5 delivered reachable from more than one module.

### WA-7 — `broiler.webassembly.core1` complete, and the embedding seam

- **Owner:** profile runtime owner with the API owner for the seam.
- **Next action:** Complete the first full manifest: element and data segments with their
  initialisation ordering, the start function, imported and exported memories, tables, and globals
  with their compatibility rules, custom sections ignored correctly, and every remaining edge the
  suite names. Harden the entry-point encoding of WA-1 into the production channel, with its
  encoder published as part of the support surface. Write the embedding seam the browser will use
  and record its shape. Mint `broiler.webassembly.core1` and run the whole suite scope for it.
- **Dependencies:** WA-6.
- **Objective exit gate:** `broiler.webassembly.core1` is declared and a module naming an unaccepted
  manifest is refused; **the whole suite scope for this manifest runs and publishes per-family
  totals from an exact commit, an exact suite revision, and a published effective limit vector**,
  with the failure manifest regenerated from that run and no family regressed against its ratchet;
  **segment initialisation is atomic per segment and not across segments**, by two cases — a failing
  segment writes nothing, and a segment applied before a later one traps stays applied and is
  observable on an imported memory afterwards, with a negative control proving a whole-module
  rollback fails the second; an imported memory or table whose limits are merely compatible is
  accepted and one whose limits are incompatible is refused, each by its own case; an imported
  global whose mutability differs is refused; a custom section in every legal position is ignored
  without affecting any other answer, including an unknown custom section between two known ones;
  the entry-point encoder is published, its grammar is specified, and a case proves it unambiguous
  over an export name containing its separator; the embedding seam is exercised by a consumer that
  reaches this profile through the seam alone; and the composition still publishes and runs on every
  claimed RID with warnings as errors and an unchanged closure shape.
- **Deliberately not done:** No second standardised group. A milestone that completes one manifest
  and opens the next cannot report which one its numbers describe.

### WA-8 — The second standardised group, and the vector family

- **Owner:** profile runtime owner.
- **Next action:** Mint `broiler.webassembly.core2` and implement its surface: sign-extension
  operators, non-trapping conversions, multi-value blocks and results, reference types, bulk memory
  and table instructions, and the data count section's execution-side consequences. Then decide
  whether the vector family is implemented in this component or excluded with a published failure,
  **and take the assembly-split question of section 5 against a measured closure difference rather
  than against an intuition.** Extend the corpus, the fuzz corpora, and the proportionality fixtures
  to every new instruction family.
- **Dependencies:** WA-7.
- **Objective exit gate:** Each manifest minted here has its own reviewed scope, its own corpus
  extension, and **its own retained oracle run against its own ratchet**, and none is justified by
  claiming an earlier manifest implies it; every new instruction adds corpus entries covering its
  structural, index, and type rejections; **every new proportional family has its proportionality
  fixture before it ships**, `table.fill` and `memory.init` being the named ones; multi-value
  results are carried by the result payload and a case proves more than one value returns; the
  reference-type manifest proves a `funcref` is not projectable as a callable host object; if the
  vector family is implemented, the vector-width row of the WA-5 ABI decision is shown to have held
  without a representation change, and if it is excluded, the exclusion is published with its
  deterministic failure; if an assembly split is taken, the closure difference is measured and
  retained and both closures publish and run; the earlier manifests' totals are re-run and
  unregressed; and **this milestone supplies this profile's half of the extraction-gate comparison
  and records that it supplied it** — the file paths and source revision of this component's
  validator, and a correspondence table against the other implementation, if one has merged. It
  records **no verdict**: a verdict changes the core graph and is the core architecture owner's, and
  the record can only be filed in the core's own set because no identifier from another profile
  component may appear in this document. If no second product profile's verifier has merged, the
  milestone records that the first gate condition is unsatisfied and names what would satisfy it.
- **Deliberately not done:** No garbage-collected type surface, no exceptions, no tail calls, no
  64-bit addressing, no multiple memories. Each is an increment with its own scope, and section 6
  says the largest of them is likely to be re-scoped once the validator meets it — which is a
  statement this roadmap makes now rather than an excuse it makes later.

### WA-9 — Adversarial input, aggregate budgets, and soak

- **Owner:** profile security owner with the fuzz-corpus owner.
- **Next action:** Grow the malformed corpus to the full accepted surface. **Fuzz both
  untrusted-input surfaces** — the decoder and validator over arbitrary bytes, and the interpreter
  over validated-but-adversarial modules — with recorded seeds, budgets, and runtime settings.
  Design and implement retained-bytes reporting over the store and state the limits of what it
  measures. Run a soak over recycled runtimes. Exercise sibling runtimes under one aggregate budget.
- **Dependencies:** WA-5 through WA-8.
- **Objective exit gate:** Every entry in the full corpus produces its recorded outcome, reason, and
  diagnostic code on JIT, trimmed, and Native AOT hosts, the verifier throws on none, control
  entries verify successfully, and a repeat leaves no residue; a **mutated corpus entry** proves the
  replay detects a changed observed triple; each fuzz session retains its corpus identity, its
  iteration budget with a stated floor, its runtime settings, and **every minimized
  counterexample**, and any counterexample is closed by a **named regression, never an allow-list
  entry**; **the interpreter fuzz target is generated from validated modules**, so that what it
  exercises is the interpreter rather than the validator a second time, and the generator's own
  validity is asserted; a soak over a recorded number of lifecycle cycles across recycled runtimes
  reaches a stated heap plateau, a disposed store leaves no memory, table, or opaque reference
  retained, and a disposed runtime leaves no per-thread state, each with a named regression that
  fails when the fix is reverted; two runtimes under one aggregate budget together spend no more
  than the parent's allowance, disposing a parent with live children is refused, sealing drains, and
  **no test asserts which sibling observes a shared-parent exhaustion**; and every negative control
  in this milestone's bundle fails when injected and passes after revert, with the running count
  recorded.
- **Deliberately not done:** No new manifest. A milestone whose subject is adversarial input does
  not also widen the surface being attacked.

### WA-10 — Baselines, packaging, the support table, and the release gate

- **Owner:** release owner with the package, security, API, performance, and documentation owners.
- **Next action:** Stand up the controlled measurement lane and take this component's own baselines
  under section 19, including verification throughput per byte across a range of module sizes and
  cold-start cost — the two figures section 18 names as the reopening trigger for persistence.
  Resolve WA-0's packaging decision into a shipped identity or a stated refusal. Publish the support
  table and the composition register. Claim a RID only where a retained bundle published and ran the
  composition on it. Run the release gate that refuses the tree while any relevant unit lacks a
  human decision.
- **Dependencies:** WA-4 and WA-9 for evidence, WA-7 for the composition, WA-0 for the packaging
  ruling, and **a named human reading every relevant unit** — the largest single-owner task in the
  programme, decomposed and scheduled rather than assumed.
- **Objective exit gate:** Every published figure declares exactly one evidence class and returns
  exactly one predeclared decision, with an immutable manifest written before either arm ran, a
  comparable control, an A/A lane result, every repetition retained, and each measured child's
  effective configuration reported — and a candidate-versus-control difference smaller than the A/A
  difference is reported **below resolution**, not as a result; the baseline register and the
  retained log agree in both directions, asserted by a rule; the support table names the core
  contract version **implemented** and the minimum **accepted** as two separate integers, plus the
  accepted format-version range, the accepted manifest set, the pinned specification revision, the
  pinned suite revision, and the conformance manifest identity and version; **it names the
  specification's deterministic profile as implemented, and names memory growth as the place that
  determinism does not reach**; it names **which core primitives this profile uses and which it
  replaces**, because section 7 makes that a deviation rather than a detail; it uses a vocabulary
  that never reads as a bare yes, gives every row an evidence cell naming a rule or a retained
  artifact, names a deterministic failure or an exclusion for every unimplemented capability —
  threads, shared memory, multi-result host imports, and the JavaScript API among them —
  distinguishes what the contract admits from what this profile implements from what the composition
  provides, and closes with a section stating what the table does not say; **the accepted manifest
  set contains no manifest whose oracle totals show it failing, and no aggregate percentage appears
  anywhere**; the composition register and the checkout agree in both directions; every claimed RID
  has a retained publish-and-run bundle with its closure report, and every unclaimed one is listed
  with its reason; a pristine consumer restores and runs from a source containing only this
  component's packages with upstream feeds unreachable, and a rollback to the previous package set
  runs unchanged; the release gate refuses on each of its conditions, naming each blocker by its
  declaration, with a negative control proving the generator cannot invent a reviewer; a named human
  decision exists on **every** relevant unit before the first publish; every suppression is
  inventoried with an owner and a reachability argument; and no figure, total, claim, or platform
  result from any other component appears anywhere.
- **Deliberately not done:** No comparison against any other WebAssembly engine, in either
  direction, however easy it would be to run one.

---

## 22. Delivery order

```text
     WA-0  boundary, placement, identity, assurance floor, evidence contract
        │        no product code
        │
        └→ WA-1  the whole contract loop on a slice module, publish-and-run
             │        ←── the entry-point and StructuralDepth decisions land here
             │
             └→ WA-2  the decoder, the integer decision, the malformed corpus
                  │        ←── (core contract accepted): external gate, held by
                  │            the core, open today — it binds WA-2 onward and
                  │            binds neither WA-0 nor WA-1
                  │        ←── the specification revision is pinned (a human action)
                  │
                  └→ WA-3  validation, the diagnostic registry
                       │
                       ├→ WA-4  the oracle — scores malformed and invalid
                       │    │      with no interpreter in existence
                       │    │      ←── the suite revision is pinned
                       │    │
                       └→ WA-5  the ABI decision; the store; the interpreter
                            │
                            └→ WA-6  linking, host imports, the store decision
                                 │      (needs WA-4's harness to be scored)
                                 │
                                 └→ WA-7  core1 complete; the embedding seam
                                      │
                                      └→ WA-8  core2; the vector family
                                           │
                                           └→ WA-9  corpus, fuzz, soak, agents
                                                │
                                                └→ WA-10 baselines, packaging,
                                                     │    support table,
                                                     │    release gate
                                                     │
                                                     └→ (an advertised composition:
                                                         a release decision)

Manifest increments — tail calls, exceptions, the garbage-collected surface,
64-bit addressing, multiple memories — re-enter WA-8's loop: each mints one
further feature-manifest identity, extends the retained corpus, re-runs the
oracle against its own ratchet, and closes no milestone.
```

What this ordering does and does not imply:

- **Read the two arrow kinds differently.** A `└→` edge is milestone precedence. A `←──` annotation
  marks an input or an external gate entering at that node and constrains nothing above it.
- **Nothing here waits on a core milestone's *evidence*, and no gate here closes a core gate.** WA-0
  and WA-1 depend on the core being *implemented*, which is why the acceptance gate hangs off WA-2
  rather than off the root.
- **WA-4 and WA-5 fork, and that fork is the point.** Both are gated on WA-3 and on nothing else,
  they are different skills with different owners, and the fork is what lets the conformance suite
  grade the verifier while the interpreter is being written. A team that serialises them loses the
  main structural advantage this profile has over a language with no external oracle.
- **WA-6 needs both arms to have landed.** It is the join, and it is where the store decision is
  taken with a linker in front of it and a harness behind it.
- **Two milestones carry the bulk of the cost**, and an eleven-milestone diagram should not be read
  as eleven equal steps: WA-5, which is the ABI plus the store plus the interpreter, and WA-8,
  whose vector half is by instruction count comparable to everything before it.
- **Two decisions need no code and may be opened early**, against WA-1 rather than waiting on the
  acceptance gate: the value and frame ABI of section 9, and the store reading of section 11. A team
  that reaches the acceptance gate after WA-1 should have prepared work rather than a hard stop.
- **Manifest increments are not milestones.** Each mints one identity with a reviewed scope, extends
  the corpus, and re-runs the oracle. The admission criterion is section 6's allocation table, not a
  judgement made per commit, and in particular **not the fact that the specification ships its
  features in one version.**

---

## 23. Test and evidence matrix

| Area | Required tests/evidence | Failure that blocks release |
|---|---|---|
| Dependency architecture | acyclic graph asserted against a checked-in manifest in both directions; exact profile reference set read from project text and from metadata; no edge to another profile component in either direction, inbound recording its branch; no product reference to the script reader, the corpus store, or the encoder; no dynamic loading, reflection invocation, IL emit, reflective member write, or module initializer; no aggregate profile-listing type; namespace-matches-assembly scan; per-clause witnesses | any forbidden project or assembly edge, an unresolvable build item cleared as a pass, a product project reaching test-only ingestion code, undeclared dynamic loading, a registered rule with no witness |
| Identity and registration | descriptor admitted; one named negative case per catalog refusal the descriptor can provoke; identity grammar bounds; reserved-namespace and package-identity pairing; manifest namespace containment; payload-kind range containment; permutation of registration orders producing byte-identical catalog encodings | a descriptor admitted that should be refused, a refusal reported with the wrong reason, a payload kind outside the declared range, an encoding that depends on declaration order |
| Decoding | five verifier outcomes each by a named case; retained malformed corpus with expected-and-observed triples and successful control entries, **including a padded-integer entry that must be accepted**; double replay with no residue; mutated-entry detection; ordering assertions — ceilings before the first byte, refusal before allocation, bound before declared-count use; section-order table derived from the pinned revision with a case per forbidden adjacency; UTF-8 name validation including the cases a platform decoder accepts; caller-buffer mutation, disposal, and concurrent overwrite after return; scan for absence of the core's canonical integer readers | invalid input decodes, a decoder throws, a spec-legal padded module is rejected, a section-order check keyed on section identifiers, a declared count sizing an allocation before its bound comparison, a corpus in which nothing verifies successfully |
| Validation | every rule by a named case mapping to exactly one core reason; **malformed-before-invalid reported correctly, with a case that fails when the phases are fused**; polymorphic unreachable-code handling proved in both directions; implementation limits reported as resource exhaustion naming a dimension, never as invalid artifact; single-decode assertion; registry bound in both directions | a late check reported as a trap, a limit refusal reported as a malformed module, dead code accepted that the algorithm rejects or rejected that it accepts, an emittable diagnostic absent from the registry |
| Value model and store | numbered ABI decision with all eight rows, fixtures, and AOT representation probes; handle-immutability structural scan plus concurrent read; store-owned memory, table, and global lifetime with an exporting instance disposed under an importing one; meter reporting on allocation, growth, and release | mutable state reachable from a handle, a memory allocated without being reported, an importing instance invalidated by an exporter's disposal |
| Execution and traps | five step kinds each by a named case; every trap in the closed list by its own case with a position; **growth refusal guest-observable with the allowance unspent**; exhaustion as `ResourceExhaustion` naming `CallDepth` rather than a trap and rather than a process termination, per claimed RID under Native AOT; no exception escaping; poll-bound breach poisoning the runtime; a proportionality fixture per named family with its declared function, granularity, and an unsimplified control | a trap reported as a core category, a call-stack overflow terminating the process or reported as a trap, a refused `memory.grow` aborting the operation, an operation family shipping without a proportionality fixture, a flat charge passing as proportional |
| Linking and host boundary | numbered store decision with the option not taken costed; a case per link failure kind; refused link publishing no instance; start trap publishing no instance and distinguishable from a link failure; two-point host-import check proved to give different answers from one module in two compositions; binding-time signature, version, and kind refusals; no partial binding; unbound-optional branch exercised; transfer-type closure; float bit-pattern round trip including a NaN payload; multi-result import refused deterministically; opaque-reference non-shareability and retention reporting; capability-assumption mismatch on a shared handle | a partially linked instance, an instance published after a start trap, a mismatch discovered at first call, a CLR type crossing the boundary, a truncated multi-result import, an external reference crossing runtimes |
| Conformance | pinned suite revision; self-check with failing **and** passing fixtures before every shard, plus an injected-and-reverted scoring regression; the malformed-before-invalid self-check fixture; per-family totals with per-family ratchets; published effective limit vector per run; registration and both module forms exercised; merge configuration-failure kinds; failure manifest as a queue; the harness's own regression suite; ingestion-path absence scan with a negative control | a failing test reported as a pass, a family selecting files and executing none, a green run with zero executed tests, a regression against a family's ratchet, an aggregate percentage published, a claimed manifest whose totals show it failing |
| Native AOT | publish-and-run per claimed RID, warnings as errors, closure report attached and read off the published output; suppressions inventoried with owner and reachability | an AOT claim derived from a property, an analyzer, or a non-AOT publish; a closure containing a test, ingestion, reflection, or dynamic-code assembly; one RID's publish cited for another |
| Packaging and consumers | package count and identity; produced metadata declaring no foreign dependency; pristine-feed consumer restore-and-run through the embedding seam; exercised rollback | a package that resolves a dependency from the internet, a packable identity outside the dated budget, a rollback that does not run |
| Composed-profile safety | a two-profile catalog test with a neighbour that declares guest-initiated loads, proving this profile's maxima reach it not at all and its adopted defaults do; the fifteen maxima and fifteen defaults published, the defaults recorded as the neighbour-facing half; the three guest-load defaults published `Unconstrained` with a negative control that zeroes one, adopts defaults, and observes the neighbour refused | a default set so tight that a neighbour adopting it is strangled, a maximum mistaken for a neighbour-facing declaration, or a composition hosting two profiles closing a gate with no such test |
| Assurance and review | generator fixed point; refusal-to-invent-a-reviewer negative control; per-declaration blocker naming; review-mark vocabulary; **origin distribution published, and expected to be uniform** — a unit in this component that is not written here is a finding, because there is no seed to explain it | a generated artifact differing from what the generator would write, a reviewer identifier no source line carries, a stale fingerprint at publish, an unreviewed relevant unit at release, an unexplained non-local origin |
| Licence and attribution | licence and notices carrying the ingested suite's attribution; modified files marked; the core's standing third-party claim confirmed scoped or amended with the release owner's co-signature | an attribution obligation discovered during a publish, a standing claim elsewhere falsified by what this component's tree contains |
| Measurement | evidence class declared; immutable pre-run manifest carrying the specification and suite pins and the effective limit vector; comparable control; A/A lane; every repetition; effective-configuration attestation; register bound to log in both directions | a figure without a control, an envelope widened after seeing a candidate, an effective-versus-requested mismatch, a cross-profile or cross-engine comparison |

Generated results are evidence artifacts, not substitutes for pinned manifests and durable
summaries. Every accepted bundle records source revision, clean or dirty inputs, SDK and runtime,
publish properties, core contract version, specification revision, suite revision, effective limit
vector, RID and device, effective GC/JIT/AOT state, commands, and raw outputs — and every bundle
states its negative-control count, which grows.

---

## 24. Release gates

A `Broiler.VM.Profile.WebAssembly` preview or stable release must satisfy all applicable gates:

1. **Support truth:** the support table names the implemented and minimum-accepted core contract
   versions as two separate integers, the accepted format-version range, the accepted manifest set,
   the pinned specification revision, and the pinned suite revision; it names the deterministic
   profile as implemented and names memory growth as outside that determinism; it names which core
   primitives this profile replaces and why; every unimplemented capability has a named
   deterministic failure or a named exclusion, threads, shared memory, multi-result host imports and
   the JavaScript API among them; composition label, contract admission, and implemented feature are
   kept apart per row; no row reads as a bare yes; and no figure from any other component appears.
2. **Graph and registration:** the graph is acyclic and matches its manifest; the profile reference
   set is exactly the two core assemblies; no edge reaches another profile component in either
   direction; no product project reaches the ingestion path; registration is static and typed, with
   no reflection, dynamic loading, IL emit, or module initializer anywhere in a product closure.
3. **Correctness and safety:** the malformed and invalid corpora replay with zero unexplained
   differences on all three publish modes; the verifier throws on nothing; every fuzz counterexample
   is closed by a named regression; verification is separable from execution and there is exactly
   one validator; and **no structural or type check happens after verification.**
4. **Lifecycle and results:** the step-kind mapping holds; no exception escapes into the core; no
   core outcome category or reason code is added; the four failure phases land where section 10's
   table says; a call-stack overflow is reported and not fatal; the terminal unwind and the release
   order are observed.
5. **Linking and the host boundary:** every declared import binds by exact capability ID, version,
   signature ID, and kind when the runtime is created, or the runtime is refused; no
   required-import failure leaves a partially bound runtime; every optional import has its unbound
   branch exercised; only the core's transfer types cross the boundary; every capability declares a
   translation mode whose precedence is proved; and a refused link or a trapping start function
   publishes no instance.
6. **Native AOT:** the advertised composition publishes **and runs** on its declared matrix with
   trim and AOT warnings treated as errors, closure reports attached, suppressions reviewed and
   scoped. *A linker annotation without execution is insufficient.*
7. **Packages and consumers:** the packable set matches its dated budget; produced metadata declares
   no foreign dependency; a pristine consumer restores and runs through the embedding seam;
   rollback is exercised.
8. **Conformance:** a release-candidate run of the pinned suite exists from an exact commit with
   retained artifacts and a published effective limit vector; no family is regressed against its
   ratchet; every claimed manifest has its own totals; the failure manifest is generated from that
   run; and **no aggregate percentage is published.**
9. **Measurement honesty:** this profile's own overhead is published with its method and its limits;
   no claim is made without a predeclared rule, a comparable control, an A/A lane, and retained
   repetitions; fuel figures are never compared across profiles and no figure is cited from any
   other component or engine.
10. **Human review:** no package is published, no RID is claimed, no support table is issued, and no
    milestone moves to accepted until a named human has recorded a decision on every relevant code
    unit, bound to that declaration's fingerprint.
11. **Licence and attribution:** this component's licence and notices carry the ingested suite's
    attribution, modified files are marked as changed, and no standing third-party claim elsewhere
    is falsified by what this component ships or by what its tree contains.
12. **Operations:** diagnostics, cancellation, rollback, format-version rejection, specification-
    and suite-revision drift, **vulnerability response**, and recertification owners are each named.
    This component ships a decoder, a validator, and an interpreter over bytes the world produces;
    a release with no named holder for a report about any of them is not a release.

Recertification is required when the SDK or runtime, core contract version, package graph, host
capability surface, Native AOT settings, RID matrix, cache identity, resource defaults, pinned
specification revision, pinned suite revision, or representative workload changes — and, per
affected record, the ledger states what recertifies unchanged, what must be re-collected, and what
is superseded.

---

## 25. Risks and stop conditions

| Risk | Mitigation / stop condition |
|---|---|
| The decoder is built on the core's canonical variable-length readers, and the engine rejects modules that real toolchains produce. | Section 7 takes the decision before the first decoder line; a corpus entry with a padded encoding must **verify successfully**, and a control test proves it would fail if the core's reader were substituted; a scan asserts the profile assembly contains no call to those readers. **Stop: a spec-legal module rejected as malformed is a correctness defect, not a strictness setting, and it blocks the milestone.** |
| The count-bound ordering is lost while re-implementing what `TryReadDeclaredCount` provided. | The ordering is asserted mechanically for every corpus entry including every failing one, not tested once; the assertion is a WA-2 gate clause rather than a note. **Stop: a declared count that sizes a buffer or bounds a loop before clearing its bound is the defect this whole discipline exists to prevent, and it stops the milestone wherever it is found.** |
| Section order is validated by comparing section identifiers, which is correct for every module a toolchain happens to emit and wrong by the specification. | The order is a table derived from the pinned revision, with a corpus entry per forbidden adjacency, including the two pairs whose identifiers and order disagree. **Stop: an order check keyed on identifiers is a defect even while every test passes.** |
| Function-body validation drifts into first execution, because the specification permits it and every performance instinct wants it. | Invariant 3, the core's stage matrix, and a WA-3 case that reports malformed-before-invalid and fails when the phases are fused. **Stop: a late check reported as a trap is a release blocker, because it makes an invalid module indistinguishable from a trapping one and silently hollows out the corpus.** |
| The store decision is taken late or implicitly, and the linker is written against a shape that cannot express the suite's registration. | Section 11 enumerates three readings and rejects one outright; WA-6 takes the decision as a numbered record with the naming channel resolved **before the linker is written**; WA-4 implements registration and both module forms before anything consumes them. **Stop: no linker source lands while the decision is open, and a decision deferred past WA-6 is a decision the conformance run makes by failing.** |
| A trap and a resource exhaustion are conflated, or a refused `memory.grow` is turned into an aborted operation. | Section 10's four-phase table with a case per row; a named case proving growth refusal is guest-observable with the allowance unspent. **Stop: either conflation is an untruthful answer to a question the suite asks directly, and it fails the family that exists to ask it.** |
| Deep recursion terminates the process rather than being refused. | `CallDepth` derived from a retained frame-cost measurement per RID; the suite's own exhaustion family is the proof on each. **Stop: a stack overflow is not translatable into a result, so claiming to handle deep recursion without a measured bound would be an untruthful capability claim; a process termination blocks the milestone.** |
| Guest-controlled superlinear cost is not charged proportionally, so a bounded budget bounds nothing — and here the offenders are *single instructions*. | Per-family declared monotone charging functions with a declared granularity and a ceiling floor, each with a retained fixture and an unsimplified control; `memory.fill` over a large memory as the named negative control. **Stop: an operation family without a proportionality fixture does not ship in the increment.** |
| The test-only ingestion path — a text-format reader, a binary encoder, and a large third-party corpus — leaks into a product closure. | Architecture rules with both halves, a scan over every published closure, and a negative control that adds the reference and observes the failure. **Stop: a product closure containing the ingestion path falsifies the "no text format, no compiler" claim that is most of this profile's value as a counterweight.** |
| The support table implies a working JavaScript API because a browser image contains this profile. | Section 17 states the boundary and its cost; the support table names the JavaScript API as not provided, with its exclusion. **Stop: an untruthful support claim is a stop condition; a difficult or slow milestone is not.** |
| This profile's declared **defaults** silently constrain a second profile composed beside it in a browser, wherever the host adopts defaults rather than stating ceilings. | WA-0 records the defaults with the cross-profile consequence stated in the decision itself; section 17 names the reconciliation as belonging to whichever component composes both. **Stop: a guest-load default of zero is a composition defect, and it is caught by a two-profile catalog test rather than by a reader.** The maxima half of this risk was retired on 2026-08-31, when the core removed a catalog-wide maximum clamp its own record never authorised; a maximum now binds only the modules of the profile that declared it. |
| An aggregate conformance percentage is published, and a strong verifier hides an absent execution surface — or the reverse. | Per-family totals with per-family ratchets, and a release gate that forbids an aggregate. **Stop: a single percentage is not a result this component publishes, at any point, for any audience.** |
| Determinism is claimed more broadly than it holds. | Section 6 states `DET` and section 12 states that memory growth stays non-deterministic under it, with the support table carrying both. **Stop: a determinism claim that a growth refusal falsifies is an untruthful support claim.** |
| The extraction gate is never answered, and two verifiers duplicate a worklist and a fixpoint forever. | **This profile may be the second verifier**, so the core's four conditions become answerable for the first time when its validator exists *and a second product profile's has merged* — which is a claim about a schedule this component does not hold, so it is stated as a condition and not as an assumption. WA-8 **supplies this profile's half** — file paths, source revision, correspondence table — and records that it supplied it, or records that the first condition is unsatisfied and names what would satisfy it. It records no verdict: the verdict is the core architecture owner's, it changes the core graph, and the record can only live in the core's own ADR set because this document may carry no identifier from another profile component. **Stop: the verdict may be either and an unsatisfied first condition is not a failure — but an unrecorded state is, and an extraction that creates a profile-to-profile dependency is refused whatever it saves.** |
| The value and frame ABI decision is taken late or implicitly, and the interpreter lands against a representation that the vector family then invalidates. | The decision is numbered, states its consequence in both directions, and is a gate on entry to WA-5 rather than that milestone's first task; **its vector-width row is the one whose late answer invalidates the others**, so it is answered with the rest and not deferred to WA-8. **Stop: no interpreter source lands while the decision is open, and a vector-width answer taken at WA-8 re-scopes WA-5 rather than extending it.** |
| The oracle reports a failure as a pass, or a green run means nothing. | Failing **and** passing self-check fixtures run before every shard, with an injected-and-reverted scoring regression; the malformed-before-invalid ordering as its own self-check fixture; per-family totals; configuration failures rather than green results; a ratchet no later run may regress. **Stop: a self-check mismatch stops the run, a green run with zero executed tests is never a pass, and a regression against the ratchet fails the milestone.** |
| A WebAssembly requirement maps onto no row of the core's profile checklist, and pressure builds to work around it inside the core. | Section 20's proposals, each naming the driving capability, the profile-owned design tried and rejected, and the counterweight answer — or a recorded refusal, which is recorded and not blocking. **Stop: a design that can only be hosted by a second core state machine is refused; exactly one core state machine and one core contract version exist in the product graph at any time, and no language-specific path is added to the core's execution loop.** |
| Mutable state becomes reachable from a shared handle, so two runtimes sharing one module collide. | The verified module is immutable by construction and the specification agrees; the handle-immutability structural scan with its mechanism and residual stated; concurrent unsynchronised reads by two runtimes. **Stop: any such reachability is a defect, not a tuning option, and the milestone does not close over it.** |
| A shared aggregate parent is treated as isolation for multi-tenant modules. | Section 3's clamping note and WA-9's aggregate exercise; a shared parent is a channel, not isolation. **Stop: an isolation claim over a shared parent is an untruthful support claim, and no test may assert which sibling observes a shared-parent exhaustion.** |
| The manifest set drifts upward one increment at a time, because the specification ships features in bundles and a version number looks like a unit. | Each increment mints one identity with a reviewed scope, extends the corpus, and re-runs the oracle against its own ratchet; the accepted set is published in every support claim. **Stop: an increment published without its own retained oracle run and corpus extension is not accepted, and no increment may be justified by a specification version number.** |
| Owner and reviewer are the same person, so no gate here is independently confirmed. | Roles are named per milestone; where one person holds several, the non-independence is recorded as a residual limit on what these gates prove rather than resolved by assertion. **Stop: a vacant role stops the point that requires it; a role held by nobody does not pass to whoever is available.** |
| The programme stalls on a precondition this component does not control. | The core-acceptance blocker is recorded with its holder and its unblock condition; the decisions that need no code are opened against WA-1. **Stop: a milestone blocked by a named external dependency is recorded blocked with its holder and its unblock condition — lack of scheduling is not a blocker, and an unaccepted contract is.** |

Stop or re-scope a milestone when the graph is cyclic; a product closure reaches dynamic code, the
ingestion path, or another profile component; a validator cannot produce an immutable bounded
module before execution; trusted policy can be weakened by module input; a second core state machine
is maintained for one language; the declared Native AOT composition cannot publish and run; or the
named ownership or maintenance ceiling is absent. **A difficult or slow milestone is not itself a
stop condition; an untruthful support claim is.**

---

## 26. Specification and platform references

This roadmap records immutable revisions for implementation and release evidence. The moving links
below are discovery entry points, **not substitutes for the pinned manifests**.

- **The WebAssembly core specification**, pinned by dated revision identifier, retrieved, hashed, and
  archived. Retrieving, hashing, and archiving a third-party document is a **human action**: until
  someone performs it the pin is provisional and carries a named exclusion in the ledger. WA-0
  records the intended revision; WA-2 records the pin that was actually taken. Every count this
  roadmap declines to state — instructions, types, section identifiers, trap kinds — is read off
  that revision's own indexes rather than transcribed here.
  <https://webassembly.github.io/spec/core/>
- **The conformance test suite revision**, the immutable commit resolved once before any shard
  starts, never a branch name, together with the scope manifests mapping this component's manifests
  to suite path prefixes. <https://github.com/WebAssembly/testsuite>
- **The specification's appendices** that this roadmap depends on by name: the validation algorithm,
  the implementation limitations, and the profiles appendix that defines `DET` and `FUL`. Each is
  pinned with the revision above.
- **Any embedding specification in scope for a claimed composition**, pinned the same way. None is
  in scope today; section 1 records the JavaScript API as a non-goal and section 17 records why it
  is still this document's business to price.
- [.NET Native AOT deployment and limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [.NET Native AOT warning guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings)
- [.NET trimming options and analysis](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options)

**No reference here resolves into any other Broiler profile component**, and no specification
reference belonging to another language appears. A profile's own specification references belong in
that profile's roadmap.
