# Broiler.VM.Profile.WebAssembly roadmap — gates and evidence

**This file is part of the [Broiler.VM.Profile.WebAssembly roadmap](roadmap.md)**, which
[names every file](roadmap.md#how-this-roadmap-is-split). It carries sections 19, 23–26:
the measurement rules, the evidence matrix, the release gates, the stop conditions and the
references.
**Section numbers are global and do not change when a section moves**, so
this file holding 19 before 23 is intentional rather than an error.

The [evidence ledger](roadmap.status.md) is the authority for what has been accepted.

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
   core outcome category or reason code is added; the four failure phases land where
   [section 10](roadmap.md#10-execution-mapping-webassembly-onto-the-core-lifecycle)'s table says;
   a call-stack overflow is reported and not fatal; the terminal unwind and the release order are
   observed.
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
| The decoder is built on the core's canonical variable-length readers, and the engine rejects modules that real toolchains produce. | [Section 7](roadmap.md#7-the-artifact-the-decoder-and-one-disagreement-with-the-core) takes the decision before the first decoder line; a corpus entry with a padded encoding must **verify successfully**, and a control test proves it would fail if the core's reader were substituted; a scan asserts the profile assembly contains no call to those readers. **Stop: a spec-legal module rejected as malformed is a correctness defect, not a strictness setting, and it blocks the milestone.** |
| The count-bound ordering is lost while re-implementing what `TryReadDeclaredCount` provided. | The ordering is asserted mechanically for every corpus entry including every failing one, not tested once; the assertion is a WA-2 gate clause rather than a note. **Stop: a declared count that sizes a buffer or bounds a loop before clearing its bound is the defect this whole discipline exists to prevent, and it stops the milestone wherever it is found.** |
| Section order is validated by comparing section identifiers, which is correct for every module a toolchain happens to emit and wrong by the specification. | The order is a table derived from the pinned revision, with a corpus entry per forbidden adjacency, including the two pairs whose identifiers and order disagree. **Stop: an order check keyed on identifiers is a defect even while every test passes.** |
| Function-body validation drifts into first execution, because the specification permits it and every performance instinct wants it. | Invariant 3, the core's stage matrix, and a WA-3 case that reports malformed-before-invalid and fails when the phases are fused. **Stop: a late check reported as a trap is a release blocker, because it makes an invalid module indistinguishable from a trapping one and silently hollows out the corpus.** |
| The store decision is taken late or implicitly, and the linker is written against a shape that cannot express the suite's registration. | [Section 11](roadmap.md#11-the-store-instances-and-linking) enumerates three readings and rejects one outright; WA-6 takes the decision as a numbered record with the naming channel resolved **before the linker is written**; WA-4 implements registration and both module forms before anything consumes them. **Stop: no linker source lands while the decision is open, and a decision deferred past WA-6 is a decision the conformance run makes by failing.** |
| A trap and a resource exhaustion are conflated, or a refused `memory.grow` is turned into an aborted operation. | [Section 10](roadmap.md#10-execution-mapping-webassembly-onto-the-core-lifecycle)'s four-phase table with a case per row; a named case proving growth refusal is guest-observable with the allowance unspent. **Stop: either conflation is an untruthful answer to a question the suite asks directly, and it fails the family that exists to ask it.** |
| Deep recursion terminates the process rather than being refused. | `CallDepth` derived from a retained frame-cost measurement per RID; the suite's own exhaustion family is the proof on each. **Stop: a stack overflow is not translatable into a result, so claiming to handle deep recursion without a measured bound would be an untruthful capability claim; a process termination blocks the milestone.** |
| Guest-controlled superlinear cost is not charged proportionally, so a bounded budget bounds nothing — and here the offenders are *single instructions*. | Per-family declared monotone charging functions with a declared granularity and a ceiling floor, each with a retained fixture and an unsimplified control; `memory.fill` over a large memory as the named negative control. **Stop: an operation family without a proportionality fixture does not ship in the increment.** |
| The test-only ingestion path — a text-format reader, a binary encoder, and a large third-party corpus — leaks into a product closure. | Architecture rules with both halves, a scan over every published closure, and a negative control that adds the reference and observes the failure. **Stop: a product closure containing the ingestion path falsifies the "no text format, no compiler" claim that is most of this profile's value as a counterweight.** |
| The support table implies a working JavaScript API because a browser image contains this profile. | [Section 17](roadmap.md#17-the-cross-profile-boundary-the-javascript-api-for-webassembly) states the boundary and its cost; the support table names the JavaScript API as not provided, with its exclusion. **Stop: an untruthful support claim is a stop condition; a difficult or slow milestone is not.** |
| This profile's declared **defaults** silently constrain a second profile composed beside it in a browser, wherever the host adopts defaults rather than stating ceilings. | WA-0 records the defaults with the cross-profile consequence stated in the decision itself; [section 17](roadmap.md#17-the-cross-profile-boundary-the-javascript-api-for-webassembly) names the reconciliation as belonging to whichever component composes both. **Stop: a guest-load default of zero is a composition defect, and it is caught by a two-profile catalog test rather than by a reader.** The maxima half of this risk was retired on 2026-08-31, when the core removed a catalog-wide maximum clamp its own record never authorised; a maximum now binds only the modules of the profile that declared it. |
| An aggregate conformance percentage is published, and a strong verifier hides an absent execution surface — or the reverse. | Per-family totals with per-family ratchets, and a release gate that forbids an aggregate. **Stop: a single percentage is not a result this component publishes, at any point, for any audience.** |
| Determinism is claimed more broadly than it holds. | [Section 6](roadmap.md#6-feature-manifests-how-the-language-surface-is-admitted) states `DET` and [section 12](roadmap.md#12-traps-exhaustion-and-why-neither-is-a-process-failure) states that memory growth stays non-deterministic under it, with the support table carrying both. **Stop: a determinism claim that a growth refusal falsifies is an untruthful support claim.** |
| The extraction gate is never answered, and two verifiers duplicate a worklist and a fixpoint forever. | **This profile may be the second verifier**, so the core's four conditions become answerable for the first time when its validator exists *and a second product profile's has merged* — which is a claim about a schedule this component does not hold, so it is stated as a condition and not as an assumption. WA-8 **supplies this profile's half** — file paths, source revision, correspondence table — and records that it supplied it, or records that the first condition is unsatisfied and names what would satisfy it. It records no verdict: the verdict is the core architecture owner's, it changes the core graph, and the record can only live in the core's own ADR set because this document may carry no identifier from another profile component. **Stop: the verdict may be either and an unsatisfied first condition is not a failure — but an unrecorded state is, and an extraction that creates a profile-to-profile dependency is refused whatever it saves.** |
| The value and frame ABI decision is taken late or implicitly, and the interpreter lands against a representation that the vector family then invalidates. | The decision is numbered, states its consequence in both directions, and is a gate on entry to WA-5 rather than that milestone's first task; **its vector-width row is the one whose late answer invalidates the others**, so it is answered with the rest and not deferred to WA-8. **Stop: no interpreter source lands while the decision is open, and a vector-width answer taken at WA-8 re-scopes WA-5 rather than extending it.** |
| The oracle reports a failure as a pass, or a green run means nothing. | Failing **and** passing self-check fixtures run before every shard, with an injected-and-reverted scoring regression; the malformed-before-invalid ordering as its own self-check fixture; per-family totals; configuration failures rather than green results; a ratchet no later run may regress. **Stop: a self-check mismatch stops the run, a green run with zero executed tests is never a pass, and a regression against the ratchet fails the milestone.** |
| A WebAssembly requirement maps onto no row of the core's profile checklist, and pressure builds to work around it inside the core. | [Section 20](roadmap.md#20-amendments-and-this-profiles-duty-as-the-counterweight)'s proposals, each naming the driving capability, the profile-owned design tried and rejected, and the counterweight answer — or a recorded refusal, which is recorded and not blocking. **Stop: a design that can only be hosted by a second core state machine is refused; exactly one core state machine and one core contract version exist in the product graph at any time, and no language-specific path is added to the core's execution loop.** |
| Mutable state becomes reachable from a shared handle, so two runtimes sharing one module collide. | The verified module is immutable by construction and the specification agrees; the handle-immutability structural scan with its mechanism and residual stated; concurrent unsynchronised reads by two runtimes. **Stop: any such reachability is a defect, not a tuning option, and the milestone does not close over it.** |
| A shared aggregate parent is treated as isolation for multi-tenant modules. | [Section 3](roadmap.md#3-what-the-core-already-gives-this-profile-and-what-it-refuses)'s clamping note and WA-9's aggregate exercise; a shared parent is a channel, not isolation. **Stop: an isolation claim over a shared parent is an untruthful support claim, and no test may assert which sibling observes a shared-parent exhaustion.** |
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
  in scope today; [section 1](roadmap.md#1-terminology-and-support-claims) records the JavaScript
  API as a non-goal and
  [section 17](roadmap.md#17-the-cross-profile-boundary-the-javascript-api-for-webassembly)
  records why it is still this document's business to price.
- [.NET Native AOT deployment and limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [.NET Native AOT warning guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings)
- [.NET trimming options and analysis](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options)

**No reference here resolves into any other Broiler profile component**, and no specification
reference belonging to another language appears. A profile's own specification references belong in
that profile's roadmap.
