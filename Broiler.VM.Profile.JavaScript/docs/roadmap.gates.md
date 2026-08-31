# Broiler.VM.Profile.JavaScript roadmap — gates and evidence

**This file is part of the [Broiler.VM.Profile.JavaScript roadmap](roadmap.md)**, which
[names every file](roadmap.md#how-this-roadmap-is-split). It carries sections 17, 21–24:
the measurement rules, the evidence matrix, the release gates, the stop conditions and the
references.
**Section numbers are global and do not change when a section moves**, so
this file holding 17 before 21 is intentional rather than an error.

The [evidence ledger](roadmap.status.md) is the authority for what has been accepted.

---

## 17. Measurement discipline

Every figure this component publishes obeys the same rules, and the rules are stricter than the
figures are interesting. **The rules are the core's, restated here only because a reader of this
document must be able to check a figure without leaving it** — the authority is the core's baseline
register, and where the two ever differ the register wins. Two things this component adds that a
restatement alone would lose: a **declared repetition count**, fixed at JS-1 and published with
every bundle, because "retained repetitions" is a release gate nobody can fail without a number;
and the discipline of reading the core's own record of *what measuring found*, since two of the
defects its measurement lane caught were correctness defects no behavioural test saw, and one of
them was in the guest-load fan-out counter this profile's mediator depends on. **JS-8 reads that
record before designing the mediator adapter.**

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

## 21. Test and evidence matrix

| Area | Required tests/evidence | Failure that blocks release |
|---|---|---|
| Dependency architecture | acyclic graph asserted against a checked-in manifest in both directions; exact profile reference set read from project text and from metadata; no edge to a legacy component in either direction, inbound recording its branch; no dynamic loading, reflection invocation, IL emit, reflective member write, or module initializer; no aggregate profile-listing type; namespace-matches-assembly scan; per-clause witnesses | any forbidden project or assembly edge, an unresolvable build item cleared as a pass, undeclared dynamic loading, a namespace that does not match its assembly, a registered rule with no witness |
| Identity and registration | descriptor admitted; one named negative case per catalog refusal the descriptor can provoke; identity grammar bounds; reserved-namespace and package-identity pairing; manifest namespace containment; payload-kind range containment; permutation of registration orders producing byte-identical catalog encodings | a descriptor admitted that should be refused, a refusal reported with the wrong reason, a payload kind outside the declared range, an encoding that depends on declaration order |
| Format and verifier safety | five verifier outcomes each by a named case; retained malformed corpus with expected-and-observed triples and successful control entries; double replay with no residue; ordering assertions — ceilings before the first byte, refusal before allocation, bound before declared-count use; capability-absent and capability-throws verification; caller-buffer mutation, disposal, and concurrent overwrite after return; bounded-read statuses mapped; corpus extended at every format-growing milestone and replayed through the nested path; coverage-guided fuzzing with minimized regressions | invalid input executes, a verifier throws, a late check is reported as a language fault, a declared count sizes an allocation before its bound comparison, a corpus in which nothing verifies successfully, a fuzz counterexample closed by an allow-list entry, **a ceiling breach recorded as an invalid artifact or a framing failure recorded as exhaustion** |
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
| Licence and attribution | upstream derivation carried; modified files marked as changed; the ingested conformance suite's own attribution; the aggregate-repository notice gaining a row in the same change that introduces the copied tree; the core's standing third-party claim confirmed scoped or amended with the release owner co-signing | a notice that omits a copied or ingested tree, an attribution obligation discovered during a publish, a standing claim elsewhere falsified by what this component ships **or by what its tree contains** |
| Composed-profile safety | a two-profile catalog test with a deliberately hostile neighbour, proving a neighbour's maxima do not reach this profile's artifacts and its adopted defaults do; this profile's fifteen maxima and fifteen defaults published, with the defaults recorded as the neighbour-facing half | a default set so tight that a neighbour adopting it is strangled, a maximum mistaken for a neighbour-facing declaration, or a composition hosting two profiles closing a gate with no such test |
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
   set, the conformance manifest identity and version, **the pinned language-specification edition
   and suite revision by immutable identifier**, and **the list of surfaces this profile declares
   varying rather than fixed**; every unimplemented capability has a named deterministic failure or
   a named exclusion — **including the `WebAssembly` host-object surface, which is named as not
   provided rather than left to be inferred from a second profile in the image**; composition
   label, contract admission, and implemented feature are kept apart per row; no row reads as a
   bare yes; and no figure from any other component appears.
2. **Graph and registration:** the graph is acyclic and matches its manifest; the profile
   reference set is exactly the two core assemblies plus whatever the core's placement ruling
   admits for this component's own siblings; **no edge reaches a legacy component and no edge
   reaches another Broiler.VM profile component, in either direction, each with both halves
   witnessed**; registration is static and typed, with no reflection, dynamic loading, IL emit, or
   module initializer anywhere in a product closure.
3. **Correctness and safety:** the malformed corpus replays with zero unexplained differences on
   all three publish modes; the verifier throws on nothing; every fuzz counterexample is closed by
   a named regression; verification is separable from execution and there is exactly one verifier;
   **no structural, index, stack-consistency or handler-nesting check happens after verification**,
   and **no ceiling breach is reported as a malformed artifact**.
4. **Lifecycle and results:** the step-kind mapping holds; no exception escapes into the core; no
   core outcome category or reason code is added; every pause is a suspension holding no thread;
   **a call-stack overflow is reported as a resource exhaustion naming a dimension and is not
   fatal, on every claimed RID under Native AOT**; the terminal unwind and the release order are
   observed.
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
   the failure manifest is generated from that run; the effective limit vector each run was
   obtained under is published beside its totals; and **no aggregate percentage is published**,
   because the host modes and manifests measure different things and this component can be
   excellent at one and absent at another.
10. **Measurement honesty:** this profile's own overhead is published with its method and its
   limits; no claim is made without a predeclared rule, a comparable control, an A/A lane, and
   retained repetitions; fuel figures are never compared across profiles and no figure is cited
   from any other component.
11. **Human review:** no package is published, no RID is claimed, no support table is issued, and
    no milestone moves to accepted until a named human has recorded a decision on every relevant
    code unit, bound to that declaration's fingerprint.
12. **Licence and attribution:** this component's licence and notices carry the upstream
    derivation and the ingested conformance suite's own attribution, modified files are marked as
    changed, the aggregate-repository notice names this component in the same change that
    introduces its copied tree, and no standing third-party claim elsewhere is falsified by what
    this component ships **or by what its tree contains**.
13. **Operations:** diagnostics, cancellation, rollback, format-version rejection, corpus and
    suite-revision drift, **vulnerability response**, and recertification owners are each named.
    This component ships a parser and an interpreter over untrusted input; a release with no named
    holder for a report about either is not a release.

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
| This profile's declared **defaults** silently constrain a second profile composed beside it in a browser, or a neighbour's constrain this profile, wherever the host adopts defaults rather than stating ceilings. | JS-0 records the fifteen maxima and fifteen defaults with [section 3](roadmap.md#3-what-the-core-already-gives-this-profile-and-what-it-refuses)'s split stated inside the decision — maxima bind this profile's own artifacts, defaults are catalog-wide; a two-profile catalog test with a deliberately hostile neighbour catches it rather than a reader; [section 15](roadmap.md#15-deployment-compositions-native-aot-and-the-browser-embedding) names the reconciliation as belonging to whichever component composes both. **Stop: `eval` refused with a resource exhaustion naming a dimension this profile did not breach is a defect in the composition, not in the guest.** The maxima half of this risk was retired on 2026-08-31 when the core removed a catalog-wide clamp its own record never authorised. |
| The extraction gate is never answered, and the same mechanism — a diagnostic registry, a conformance harness, an assurance floor, a bounded-read projection — is written twice and diverges on the first edit. | This profile supplies its half of the comparison when a second product profile's implementation has merged: file paths, source revision, and a correspondence table, recorded at the milestone that owns the mechanism. It records **no verdict**, because a verdict changes the core graph and because no identifier from another profile component may appear here. Where the second implementation has not merged, it records that the first condition is unsatisfied and names what would satisfy it. **Stop: the verdict is the core architecture owner's and may be either, and an unsatisfied first condition is not a failure — but an unrecorded state is.** |
| An aggregate conformance percentage is published, and a strong parser hides an absent library surface — or the reverse. | Per-host-mode and per-manifest totals with their own ratchets, the effective limit vector published beside them, and a release gate that forbids an aggregate. **Stop: a single percentage is not a result this component publishes, at any point, for any audience.** |
| Determinism is claimed more broadly than it holds, and a corpus entry pins an answer the specification lets vary. | [Section 6](roadmap.md#6-feature-manifests-how-the-language-surface-is-admitted) names every surface this profile fixes and every surface it declares varying, the support table carries the list, and no corpus entry is written over an unlisted varying surface. **Stop: a determinism claim that a legitimate variation falsifies is an untruthful support claim, and a corpus entry over a varying surface is a test that fails for a reason that is not a defect.** |
| The support table implies a working `WebAssembly` namespace because a browser image contains this profile beside another one. | [Section 15](roadmap.md#15-deployment-compositions-native-aot-and-the-browser-embedding) states the boundary, its two frozen facts, and its unowned costs; the support table names the JavaScript API for WebAssembly as not provided, with its exclusion. **Stop: an untruthful support claim is a stop condition; a difficult or slow milestone is not.** |
| A published claim is untruthful — a composition label read as a capability claim, contract admission read as an implemented feature, or an execution-only publish promoted into evidence for a compiler-bearing one. | The support table separates the three facts per row, each with its own evidence cell; a composition label describes when source is compiled; no publish is cited for another kind. **Stop: a difficult or slow milestone is not itself a stop condition; an untruthful support claim is.** |
| Unreviewed copied units accumulate faster than anyone reads them, and a passing suite over them reads as assurance. | Annotation at ingest with a ported origin; a generator that refuses to invent a reviewer; decisions bound to fingerprints so a changed unit reports stale; a release gate naming each blocker by its declaration; the origin distribution published. **Stop: no publish, no claimed RID, no support table, and no accepted milestone while any relevant unit lacks a decision.** |
| Guest-controlled superlinear cost is not charged proportionally, so a bounded budget bounds nothing. | Per-family declared monotone charging functions with a declared granularity and a ceiling floor, each with a retained fixture and an unsimplified control; an uncharged-work breach is a profile fault that poisons the runtime. **Stop: an operation family without a proportionality fixture does not ship in the increment.** |
| A deeply nested program terminates the process at parse, validation, or lowering time, where `CallDepth` does not reach. | An explicit compile-time depth bound or a worklist rewrite, with a nesting corpus that must be refused; the seed's segmentation mitigation adopted only with the worklist named as a deferred risk. **Stop: a stack overflow is not translatable and claiming to handle it would be an untruthful capability claim; a process termination on a nesting case blocks the milestone.** |
| Dynamic code hides inside the standard library, where an emitter-reference scan does not look — a compiled-mode regular expression emitting and retaining a method per pattern. | A separate metadata test with its own witness for the compiled-mode call site, independent of the emitter-reference scan; routing through the from-scratch matcher. **Stop: if the matcher cannot carry the pinned surface, ship interpreted-only and record the consequence; do not reintroduce the compiled path.** |
| A JavaScript requirement maps onto no row of the core's profile checklist, and pressure builds to work around it inside the core. | [Section 18](roadmap.md#18-amendments-this-profile-expects-to-ask-of-the-core)'s amendment proposals, each naming the driving capability, the profile-owned design tried and rejected, and the counterweight check — or a recorded refusal. **Stop: a design that can only be hosted by a second core state machine is refused; exactly one core state machine and one core contract version exist in the product graph at any time, and no language-specific path is added to the core's execution loop.** |
| Placement or assembly topology is assumed rather than decided, and the layout is illegal under rules that are active today. | Placement, the profile-ID and package-ID pairing, and the assembly topology are dated decisions with the core's topology owner co-signing, each enforced by a registered rule with a witness. **Stop: no product code lands while placement is open, and no milestone assumes a sibling layout works today.** |
| The programme stalls indefinitely on preconditions this component does not control. | The waited-on set is itemised per open item with a stated reason; a snapshot-as-is date or commit-count budget is recorded with a named owner; decisions needing no copied code are opened against JS-1. **Stop: a milestone blocked by a named external dependency is recorded blocked with its holder and its unblock condition — lack of scheduling is not a blocker, and an unaccepted contract is.** |
| Mutable optimization state becomes reachable from a shared handle, or is keyed process-globally so two runtimes collide. | Program-relative slots owned and reclaimed with the program, function, or runtime; the same-slot-index eviction test; the two-runtime key and shape isolation test with its named falsifier; nothing warmed or process-local is serialized. **Stop: any such reachability is a defect, not a tuning option, and the milestone does not close over it.** |
| A shared aggregate parent is treated as isolation for multi-tenant agents. | [Section 13](roadmap.md#13-realms-agents-and-the-host-boundary) states the channel property; hosts requiring isolation must not share a parent; no test asserts which sibling observes a shared-parent exhaustion. **Stop: an isolation claim over a shared parent is an untruthful support claim.** |
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
  ledger. JS-0 records the intended edition; JS-3a records the pin that was actually taken.
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
