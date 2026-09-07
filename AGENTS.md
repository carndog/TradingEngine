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
