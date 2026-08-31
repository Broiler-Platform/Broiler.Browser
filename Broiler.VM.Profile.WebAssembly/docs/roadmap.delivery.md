# Broiler.VM.Profile.WebAssembly roadmap — delivery

**This file is part of the [Broiler.VM.Profile.WebAssembly roadmap](roadmap.md)**, which
[names every file](roadmap.md#how-this-roadmap-is-split). It carries sections 21–22:
the milestones and the order they are delivered in. **Section numbers are global and do not
change when a section moves**, so a reference written to any section below still resolves here.

The [evidence ledger](roadmap.status.md) is the authority for what has been accepted.

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
- **Next action:** Decide and record, each as a dated decision with a registered rule and a
  passing witness: where this component lives relative to the core and the aggregate repository;
  the profile ID and the `Broiler.*` package identity it obliges, **with the manifest-namespace
  consequence of the spelling stated**; the assembly topology of
  [section 5](roadmap.md#5-package-boundaries-and-the-dependency-graph) and the single-assembly
  default; the feature manifest allocation of
  [section 6](roadmap.md#6-feature-manifests-how-the-language-surface-is-admitted) and the `DET`
  position; the one composition label; **the fifteen profile hard maxima and the fifteen defaults,
  with [section 17](roadmap.md#17-the-cross-profile-boundary-the-javascript-api-for-webassembly)'s
  cross-profile consequence stated in the record, and with the three guest-load *defaults*
  published as `Unconstrained` and the reason recorded** — a dimension declared inapplicable in
  the budget matrix is a statement about what this profile charges and not a licence to write a
  zero into the vector a neighbour adopts. The maxima need no such care: they bind this profile's
  own modules alone; the nullable and unsafe-code positions; and the intended specification
  revision and suite revision, marked provisional until a human has retrieved, hashed, and
  archived them. Stand up this component's own assurance system — annotation grammar, exemption
  predicate, generated review report, fingerprint binding, release-mode gate — and its own
  evidence-bundle contract and collection script. Publish the licence and third-party notice, and
  confirm or amend the core's standing third-party claim. **The annotation grammar, the exemption
  predicate, the review-state machine, the fingerprint definition and the release-gate semantics
  are repository policy, not this component's inventions**: they are recorded in the platform's
  `CODE-ASSURANCE.md` and the policy it names, and this milestone implements them rather than
  redefining them. Record the deviation if any of the
  five has to differ, because a second implementation that quietly diverges is what the core's
  extraction gate exists to catch.
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
  construction, with the rows [section 9](roadmap.md#9-the-value-store-and-frame-model) owns
  marked **provisional** and each naming the milestone that will settle it. **Take the
  `StructuralDepth` declaration decision of
  [section 3](roadmap.md#3-what-the-core-already-gives-this-profile-and-what-it-refuses) and
  record it.** **Take the entry-point decision of
  [section 10](roadmap.md#10-execution-mapping-webassembly-onto-the-core-lifecycle) and record its
  encoding**, including its behaviour on an export name containing the encoding's own separator.
  Write the test-only binary encoder that generates corpus entries. Stand up the `execution-only`
  composition root with a closure self-report mode.
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
  named case; every validation rule the manifest requires is produced by a named case and maps
  onto exactly one core invalid-artifact reason with no invented or aliased reason; **a module
  that is both malformed and invalid is reported malformed, by a named case that fails when the
  phases are fused at module granularity**; unreachable code validates polymorphically, proved in
  both directions by a case that must be accepted and a case that must be rejected, each drawn
  from the behaviour the specification's algorithm defines rather than from an implementation's
  habit; an implementation-limit refusal is `ResourceExhaustion` naming a dimension and a scope
  and **not** `InvalidArtifact`, by its own case per limit the profile enforces — **including the
  two decoder families
  [section 7](roadmap.md#7-the-artifact-the-decoder-and-one-disagreement-with-the-core)
  reclassifies**, the over-ceiling vector length and the over-ceiling artifact size, each with a
  corpus entry recording the exhaustion triple rather than an invalid-artifact one; **a nesting
  corpus generated to the effective `StructuralDepth` ceiling and one level beyond it is refused
  as `ResourceExhaustion` naming a dimension rather than terminating the process, on every claimed
  RID under Native AOT** — a stack overflow is uncatchable and kills the host, so "the validator
  throws on nothing" cannot observe it and this clause is what does; each module is decoded at
  most once during verification, asserted by a case; the validator throws on nothing across the
  whole corpus; and the corpus's invalid half replays identically on all three publish modes.
- **Deliberately not done:** No execution and no store. A validator whose milestone also delivers
  an interpreter will have its lazy-validation temptation resolved by convenience rather than by
  [section 8](roadmap.md#8-validation)'s argument.

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
- **Next action:** Take the [section 9](roadmap.md#9-the-value-store-and-frame-model) decision as
  a numbered decision stating its consequence in both directions, **before any interpreter source
  is written**, with all eight rows including the vector-width row that only matters later.
  Implement the store for a single module: memories, tables, globals, and their metering.
  Implement the interpreter over the numeric and control surface, memory loads and stores,
  `memory.grow`, and `call` and `call_indirect` within one module. Implement traps as typed
  payloads. Place every poll and every charge. Measure native frame cost per interpreter frame on
  each claimed RID and derive the `CallDepth` default from it. Choose the uncharged-work bound,
  the charging granularity, and the cancellation poll bound from measurement. Catch every internal
  exception at this profile's own adapter.
- **Dependencies:** WA-3. The ABI decision is a **gate on entry**, not this milestone's first task.
- **Objective exit gate:** The numbered ABI decision exists with all eight rows, with fixtures and
  Native AOT representation probes retained; every executor answer is one of the five step kinds
  and a scan asserts no profile code names a core outcome category; **every trap in the closed
  list is produced by a named case and arrives as a typed payload behind a profile fault**, with
  the position it was produced at; **`memory.grow` and `table.grow` refusal is guest-observable**,
  proved by a case in which the growth is refused, the operation completes normally, the module
  observes the negative answer, and the allowance was not spent; **a call-stack exhaustion is
  `ResourceExhaustion` naming `CallDepth` and its scope rather than terminating the process**, on
  every claimed RID under Native AOT, with the `CallDepth` default derived from a retained,
  reproducible frame-cost measurement per RID; **no exception escapes the interpreter** across the
  corpus and the fuzz corpus; a deliberately non-polling variant completes as a profile fault with
  the poll-bound reason and the runtime poisoned to accept only disposal; **a proportionality
  fixture exists for each named operation family of
  [section 9](roadmap.md#9-the-value-store-and-frame-model)**, each with an unsimplified control,
  each showing fuel charged as a monotone non-decreasing function of input magnitude and at least
  the declared ceiling, with the declared function and granularity recorded — and `memory.fill`
  over a large memory is a named negative control that a flat charge fails; a deliberately
  non-charging variant is detected and reported as a contract violation; two runtimes read one
  shareable handle concurrently with no synchronisation and a **structural scan** asserts no
  memory, table, global, or mutable cache is reachable from a handle, with the scan's mechanism
  and its residual stated; the memory representation decision names its own per-RID limits and
  does not foreclose section 17's boundary; and the `assert_return`, `assert_trap`, and
  `assert_exhaustion` families are scored for the single-module subset of the suite with their own
  ratchets.
- **Deliberately not done:** No imports, no linking, no second module in a store.
  [Section 11](roadmap.md#11-the-store-instances-and-linking)'s decision is not taken here — it is
  taken at WA-6 with the linker in front of it.

### WA-6 — Linking: imports, exports, the store decision, and host capabilities

- **Owner:** profile runtime owner with the host-capability owner.
- **Next action:** **Take the [section 11](roadmap.md#11-the-store-instances-and-linking)
  decision** between the link-set artifact and the runtime-scoped store, as a numbered decision
  with the naming channel resolved and its consequence stated in both directions, before the
  linker is written. Implement import resolution and export projection for all four import kinds.
  Implement the link failure taxonomy with a distinct diagnostic per failure. Implement the
  two-point host-import check: capability assumptions at verification, bindings at instantiation.
  Implement `externref` over the core's opaque reference. Synthesise the suite's host module: its
  printing functions as host capabilities, its table, memory, and globals as a module. Declare
  host capability imports in the descriptor.
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
  operators, non-trapping conversions, multi-value blocks and results, reference types, bulk
  memory and table instructions, and the data count section's execution-side consequences. Then
  decide whether the vector family is implemented in this component or excluded with a published
  failure, **and take the assembly-split question of
  [section 5](roadmap.md#5-package-boundaries-and-the-dependency-graph) against a measured closure
  difference rather than against an intuition.** Extend the corpus, the fuzz corpora, and the
  proportionality fixtures to every new instruction family.
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
  64-bit addressing, no multiple memories. Each is an increment with its own scope, and
  [section 6](roadmap.md#6-feature-manifests-how-the-language-surface-is-admitted) says the
  largest of them is likely to be re-scoped once the validator meets it — which is a statement
  this roadmap makes now rather than an excuse it makes later.

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
- **Next action:** Stand up the controlled measurement lane and take this component's own
  baselines under [section 19](roadmap.gates.md#19-measurement-discipline), including verification
  throughput per byte across a range of module sizes and cold-start cost — the two figures
  [section 18](roadmap.md#18-persistence-and-the-code-cache) names as the reopening trigger for
  persistence. Resolve WA-0's packaging decision into a shipped identity or a stated refusal.
  Publish the support table and the composition register. Claim a RID only where a retained bundle
  published and ran the composition on it. Run the release gate that refuses the tree while any
  relevant unit lacks a human decision.
- **Dependencies:** WA-4 and WA-9 for evidence, WA-7 for the composition, WA-0 for the packaging
  ruling, and **a named human reading every relevant unit** — the largest single-owner task in the
  programme, decomposed and scheduled rather than assumed.
- **Objective exit gate:** Every published figure declares exactly one evidence class and returns
  exactly one predeclared decision, with an immutable manifest written before either arm ran, a
  comparable control, an A/A lane result, every repetition retained, and each measured child's
  effective configuration reported — and a candidate-versus-control difference smaller than the
  A/A difference is reported **below resolution**, not as a result; the baseline register and the
  retained log agree in both directions, asserted by a rule; the support table names the core
  contract version **implemented** and the minimum **accepted** as two separate integers, plus the
  accepted format-version range, the accepted manifest set, the pinned specification revision, the
  pinned suite revision, and the conformance manifest identity and version; **it names the
  specification's deterministic profile as implemented, and names memory growth as the place that
  determinism does not reach**; it names **which core primitives this profile uses and which it
  replaces**, because
  [section 7](roadmap.md#7-the-artifact-the-decoder-and-one-disagreement-with-the-core) makes that
  a deviation rather than a detail; it uses a vocabulary that never reads as a bare yes, gives
  every row an evidence cell naming a rule or a retained artifact, names a deterministic failure
  or an exclusion for every unimplemented capability — threads, shared memory, multi-result host
  imports, and the JavaScript API among them — distinguishes what the contract admits from what
  this profile implements from what the composition provides, and closes with a section stating
  what the table does not say; **the accepted manifest set contains no manifest whose oracle
  totals show it failing, and no aggregate percentage appears anywhere**; the composition register
  and the checkout agree in both directions; every claimed RID has a retained publish-and-run
  bundle with its closure report, and every unclaimed one is listed with its reason; a pristine
  consumer restores and runs from a source containing only this component's packages with upstream
  feeds unreachable, and a rollback to the previous package set runs unchanged; the release gate
  refuses on each of its conditions, naming each blocker by its declaration, with a negative
  control proving the generator cannot invent a reviewer; a named human decision exists on
  **every** relevant unit before the first publish; every suppression is inventoried with an owner
  and a reachability argument; and no figure, total, claim, or platform result from any other
  component appears anywhere.
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
  acceptance gate: the value and frame ABI of
  [section 9](roadmap.md#9-the-value-store-and-frame-model), and the store reading of
  [section 11](roadmap.md#11-the-store-instances-and-linking). A team that reaches the acceptance
  gate after WA-1 should have prepared work rather than a hard stop.
- **Manifest increments are not milestones.** Each mints one identity with a reviewed scope,
  extends the corpus, and re-runs the oracle. The admission criterion is
  [section 6](roadmap.md#6-feature-manifests-how-the-language-surface-is-admitted)'s allocation
  table, not a judgement made per commit, and in particular **not the fact that the specification
  ships its features in one version.**
