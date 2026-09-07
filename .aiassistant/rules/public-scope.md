# Public Scope and Data Integrity

This is a public repository. Apply this rule to every AI-assisted change.

Read and comply with [PUBLIC_SCOPE.md](../../PUBLIC_SCOPE.md).

Do not generate or publish proprietary strategy logic, meaningful trading parameters, private research, real portfolio or account information, credentials, live environment values, restricted market data or raw production data.

Use synthetic or anonymised data. Keep Stub or Demo execution as the default, and never allow tests, examples, development or CI to submit real-money orders.

Preserve validation, optimistic concurrency, idempotency, reconciliation and traceable audit history. Do not perform destructive migrations or data repairs without explicit approval, tests and a recovery plan.

If information might be outside the public scope, stop and ask Jason before writing it.
