# Repository Agent Instructions

These instructions apply to the entire repository and have high priority.

## Public-scope boundary

This is a public repository. Read and comply with [PUBLIC_SCOPE.md](PUBLIC_SCOPE.md) before proposing, generating, committing or publishing changes.

Never add proprietary trading strategy, meaningful parameters, private research, real portfolio information, credentials, account identifiers, live environment values, restricted market data or raw production data.

If any content might be outside the public scope, stop and ask Jason before writing it. Do not infer or reconstruct private strategy from other context.

## Data integrity and execution safety

- Use only synthetic or properly anonymised examples and test fixtures.
- No test, sample, development or CI path may submit a real-money order.
- Stub or Demo execution must be the default safe state.
- Preserve concurrency, idempotency, reconciliation and validation safeguards.
- Do not silently rewrite or delete audit and execution history.
- Do not create destructive migrations or data repair operations without explicit approval, tests and a recovery plan.
- Never weaken safety checks merely to make a build, test or demonstration pass.
- Review the complete diff for accidental disclosure before every public commit or pull request.

The public codebase may contain generic interfaces and harmless reference strategies. Real strategy implementations and live configuration belong in a separate private repository or deployment context.

## Coding standards

Read and comply with [CODING_STANDARDS.md](CODING_STANDARDS.md) before editing C# production or test code.

The following rules are non-negotiable:

- Use explicit types for every local and `foreach` variable. Never use `var`.
- Do not add code comments. Express intent through naming and extracted methods.
- Prefer private methods over local functions.
- Do not nest ternary (`?:`) expressions. Use guard clauses, `if`/`else` or a `switch` expression for multiple cases.
- Write boolean negation as `is false` rather than the `!` operator.
- Keep one class, interface, record or enum per file.
- Separate interface method declarations with a blank line.
- Name tests `MethodUnderTest_Condition_ExpectedResult`.
- Structure tests as Arrange, Act and Assert without section comments.
- Use NUnit `Assert.That` assertions without description or reason strings.
- Keep tests deterministic and obtain time through an injected clock outside Domain.

Before completing a change, run `dotnet build TradingEngine.sln` and `dotnet test TradingEngine.sln`, then review the complete diff against these instructions and the public-scope boundary.

## Pull request size and stacked story delivery

This convention applies to AI agents and human contributors. Readability and a coherent review take priority over a mechanical file limit.

- Prefer about **10 changed files per PR** and normally aim for **no more than 20**. These are guidelines, not hard limits or automatic CI failures.
- Count the incremental diff against that PR's actual base branch, including tests, migrations, generated files and documentation. Report the total and distinguish mechanical/generated changes where helpful; do not hide files from the count.
- Larger PRs are reasonable for repetitive/mechanical edits, generated output, established conventions, or an indivisible coherent change. Explain the exception and why splitting would make review worse in the PR description. Apply judgement without requesting approval solely because the guideline is exceeded.
- Before implementing a story, propose coherent review-sized slices, their dependencies and approximate file counts. Reassess as scope grows. Do not bundle unrelated cleanup, postpone necessary tests, combine types into one file, or otherwise weaken the coding standards to meet the target.
- Each slice must build and pass its relevant checks on top of its declared ancestors. Keep intermediate states safe to merge and deploy, using additive changes and disabled/incomplete feature entry points where appropriate. Include the tests and migration/configuration changes needed by that slice.

### Stack naming and bases

For a multi-PR story, use a common descriptive stem and numbered suffixes `_1`, `_2`, `_3`, etc. in both branch names and PR titles. GitHub PR numbers remain GitHub-assigned; the suffix is the story's sequence number.

Example branches: `story/130-weekly-signals_1`, `story/130-weekly-signals_2`.
Example titles: `#130 Weekly signals: tracking and calculation _1`, `#130 Weekly signals: persistence and queries _2`.

- Create `_1` from `main` and target `main`.
- Create `_2` from `_1` and target the `_1` branch; create `_3` from `_2` and target `_2`, continuing as needed. An independent change can target `main` directly; do not invent dependencies just to create a stack.
- Include the story link, ordered stack links, current position, immediate dependency/base, incremental file count and scope in each PR description. Keep the story's stack checklist current as PRs are opened, amended and merged.
- Use `Refs #<story>` for intermediate PRs. Only the final completing PR should use `Closes #<story>`, and only when the story's completion requirements are met; otherwise close the story after the remaining acceptance verification.
- Merge in dependency order into `main`. Once a predecessor lands, update/restack its successor onto the resulting `main`, retarget the successor PR to `main`, and verify its incremental diff and checks before merging. Do not merge a successor into an unmerged predecessor simply to clear the stack. Keep dependent branches attached to their immediate predecessor until their turn.
- Do not assume retargeting alone removes ancestor changes after a squash or rebase merge. Preserve the old dependency boundary and transplant only the successor's own commits when restacking is necessary, so already-landed work is not replayed. Verify any tooling's automatic restack. Avoid deleting a predecessor branch until dependent PRs have been updated.

### Corrections to earlier slices

When later work exposes a prerequisite or design correction, put the fix in the earliest appropriate **still-open** PR, then propagate it through dependent branches and rerun the affected checks. Use follow-up commits by default and explain material scope changes in the PRs. Recheck the diff/file count against each actual base and refresh reviews when their reviewed content changed.

Once a PR has merged, use a new forward fix on `main` or an appropriate remaining PR; never rewrite merged history. Keep changes by other contributors intact. Prefer history-preserving propagation; any authorised history rewrite must use an explicit expected remote head/force-with-lease, never an unguarded force push.

The completion report must identify the stack order, links, checks and any size exceptions. Repository instructions and the PR template make this a planning and review requirement; the file-count guideline is intentionally not a hard gate.

## Commit and push approval

Agents must not run `git commit`, `git commit --amend`, `git push`, `git merge`, `git rebase`, `git reset` or any other command that writes history locally or to a remote, and must not create, edit, close or merge a pull request, without Jason's explicit approval for that specific action.

Before requesting approval, present `git status`, the complete diff, test results and the proposed commit message or PR text, then wait. Local, uncommitted working-tree changes are permitted.

Within an already authorised story stack, Jason's standing instruction permits corrective follow-up commits to earlier open PRs and propagation into their dependants. This does not authorise unrelated changes, merging PRs, or history rewrites beyond the permission already granted for the task.
