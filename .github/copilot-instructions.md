# GitHub Copilot Repository Instructions

This repository is public. Treat the public-scope boundary as a high-priority requirement.

Read and comply with [PUBLIC_SCOPE.md](../PUBLIC_SCOPE.md) before proposing or generating changes.

- Never add proprietary strategy logic, meaningful trading parameters, private research, real allocations, credentials, account identifiers, live environment values, restricted market data or raw production information.
- Use synthetic or properly anonymised data for examples and tests.
- Never enable real-money execution from tests, samples, development defaults or CI; Stub or Demo must remain the safe default.
- Preserve validation, concurrency, idempotency, reconciliation and audit-history safeguards.
- Do not create destructive migrations or data repair operations without explicit approval, tests and a recovery plan.
- Do not weaken safety controls merely to make a test or demonstration pass.
- Review generated code and documentation for accidental public disclosure.
- If public/private classification is uncertain, stop and ask Jason before writing the content.

Generic infrastructure, contracts and harmless reference strategies are public-safe. Real strategy implementations, meaningful parameters and live configuration belong in a separate private repository or deployment context.
