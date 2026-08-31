# Broiler Platform Code Assurance

**Status: one component has adopted the assurance system. Nothing in this repository has been
reviewed by a human.**

This is the platform-level aggregate required by the
[Broiler Code Assurance and Human Review Policy](#the-policy). It reports what each component's
own generated report says, and nothing else.

Read this table carefully, because two of its columns say very different things:

- **Adopted** is whether the component carries per-unit assurance metadata at all. A component
  that has not adopted the system reports nothing, and an empty row is *not* a good result.
- **Human reviewed** is the fraction of that component's relevant code units that a named human
  has approved against the fingerprint of the version they approved.

A component that has not adopted the system is **not** at 0% - it is unmeasured, which is a
weaker position, not a stronger one.

## Components

| Component | Adopted | Relevant units | Human reviewed | Max security risk | Max IP risk |
|---|---|---:|---:|---|---|
| [Broiler.VM](Broiler.VM/CODE-ASSURANCE.md) | **yes** | 716 | **0 (0%)** | High | Low |
| Broiler.CSS | no | - | - | - | - |
| Broiler.DOM | no | - | - | - | - |
| Broiler.Graphics | no | - | - | - | - |
| Broiler.HTML | no | - | - | - | - |
| Broiler.Input | no | - | - | - | - |
| Broiler.JS | no | - | - | - | - |
| Broiler.Layout | no | - | - | - | - |
| Broiler.Media | no | - | - | - | - |
| Broiler.UI | no | - | - | - | - |

`src/` - the browser heads, the shared chrome and HtmlBridge - has not adopted the system either.

## What this table is not

It is not a security assessment, and no row of it may be quoted as one. The assurance system
records **whether a human has certified a specific version of an implementation**. Every relevant
unit in the one component that has adopted it is `HUMAN_PENDING`, so what the table currently
records is the *absence* of review, measured precisely.

The separate [human review summary](HUMAN_REVIEW.md) is where review decisions live. It is
`PENDING` for this repository and for two of its dependency components, and two further
components carry no review record at all.

## Release policy

Under the policy, an official release fails on any unresolved assurance state - `PENDING`,
`STALE`, an unknown reviewer, a missing review, a fingerprint mismatch, or an invalid review
transition. Every relevant unit in `Broiler.VM` is `PENDING`, so **no component here is eligible
for an official release**, and the nine components that have not adopted the system cannot be
assessed against that gate at all.

This sits alongside, and does not replace, the rule recorded in `Broiler.VM`'s status ledger that
human review gates a release rather than a development step. Both point the same way: development
may proceed unreviewed; nothing ships unreviewed.

## Adoption

`Broiler.VM` is the reference implementation. Its scanner, fingerprinter and generator live inside
its own architecture-test assembly rather than in a shared tool, because that component's
[ADR 0001](Broiler.VM/docs/adr/0001-component-topology-and-dependency-graph.md) caps its project
count, and every test-only project that record permits before VM-3 is now spent - the two VM-1 was
allowed and the fuzz target host VM-2 was allowed. A platform-wide tool that
every component could share does not exist yet, so this table is hand-maintained from the
components' generated reports - which is exactly the kind of hand-maintained summary the policy
warns against, and is recorded here rather than hidden.

**Two more components are about to build the same thing, and that is now known rather than
latent.** The roadmaps for `Broiler.VM.Profile.JavaScript` and `Broiler.VM.Profile.WebAssembly`
each require, as their first milestone's exit gate, an assurance system with the same parts in
the same words: an annotation grammar, an exemption predicate, a generated review report, a
fingerprint binding, a release-mode gate that names each blocking declaration individually, a
generator proved to be a fixed point, and a negative control proving it refuses to invent a
reviewer. Seven of the nine clauses are identical between the two. None of it names a language.

That is three implementations of one repository-level policy, and the reason it is not simply
extracted is worth stating rather than leaving as an accident of project budgets. The rule
`Broiler.VM` applies to shared code requires **two or more product profiles to already implement
a behaviour in merged code** before a shared component is opened; one implementation exists, so
the condition fails, and under that rule the duplication is documented and kept rather than
pre-empted. **This paragraph is that documentation.** The trigger is named: when a second
component's assurance implementation exists and the two can be compared from real code, open a
repository-level test-only tool and amend `Broiler.VM`'s project budget to permit it. Until then,
a second component copying these mechanisms should copy them deliberately and say so, because a
copy nobody recorded is the thing that makes an eventual extraction expensive.

**One thing this file asserts that the repository does not contain.** The section below names
`BROILER-CODE-ASSURANCE.md` as the authority. **That file is not in this repository.** Either it
lives outside it, in which case this file should say where, or it has never been written, in
which case the authority for every row above is a document nobody can read. Naming the gap is the
point; resolving it is the owner's, and it cannot be resolved by inventing the policy here.

## The policy

The authority is the owner's `BROILER-CODE-ASSURANCE.md`. `Broiler.VM` records how it fitted the
policy to a component with no CI lane, and what the fitting cost, in
[its VM-1 evidence bundle](Broiler.VM/docs/evidence/vm-1/README.md) - in particular that its two modes
run as a test rather than in CI, that four rounds of adversarial attack produced 46 defeats every
one of which was in coverage, and that an assessment is a comment and therefore moves no
fingerprint when it is downgraded.
