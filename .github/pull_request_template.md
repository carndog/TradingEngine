## Summary

What changed, why, and what is usable after this slice?

## Linked issue

Refs #

Use `Closes #` only for the final completing PR when the story's completion requirements are met. Intermediate PRs must not close the story.

## Review size and stack

Follow [the repository convention](https://github.com/carndog/TradingEngine/blob/main/AGENTS.md#pull-request-size-and-stacked-story-delivery): prefer about 10 changed files and normally no more than 20; justified larger changes are welcome.

- Incremental changed files against this PR's actual base:
- Size exception, if needed (identify repetitive/generated changes and explain why this grouping helps review):
- Stack position and ordered PR links (or standalone):
- Immediate dependency / target branch:
- This slice's scope and safe intermediate deployment behaviour:

For a stack, use `_1`, `_2`, etc. in branch names and PR titles. Keep these fields and the story checklist current after restacking or changing an earlier PR.

## Verification

- [ ] Relevant tests pass on this slice and its declared ancestors
- [ ] This slice's acceptance criteria have been checked
- [ ] The incremental diff, base and stack order have been checked; any affected descendants have been updated
- [ ] No credentials or secrets have been committed
- [ ] Documentation or ADRs have been updated where required

## Risks and operational impact

- Cost impact:
- Security impact:
- Data or licensing impact:
- Deployment considerations:
