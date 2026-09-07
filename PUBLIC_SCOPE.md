# Public Scope and Data Integrity Policy

**Status:** Governing project policy  
**Priority:** High  
**Applies to:** All code, issues, pull requests, commit messages, documentation, diagrams, examples, configuration, test data and generated artefacts intended for this public repository.

## Purpose

This public repository exists to demonstrate the generic .NET and Azure engineering of the Trading Engine. It must not disclose proprietary trading strategy, valuable parameters, private research, live operational information or sensitive data.

When information might fall outside the public scope, keep it private and ask Jason before publishing it.

## Public-safe content

- Generic architecture, interfaces and message contracts
- EF Core and Azure SQL infrastructure
- Azure Functions, Service Bus, observability and deployment templates
- Generic idempotency, reconciliation and risk-control mechanisms
- Synthetic or anonymised examples and test data
- Harmless reference strategies that do not contain proprietary logic or meaningful parameters
- Demo and stub execution paths with safe defaults

## Must remain private

- Genuine strategy algorithms, decision logic or signal derivation
- Meaningful thresholds, weights, timing rules or instrument-selection methods
- Private backtest research or results that reveal strategy behaviour
- Current holdings, allocations, position sizing or account-specific risk limits
- Credentials, tokens, portfolio or account identifiers, private endpoints and live environment values
- Real approval procedures or operational details that would weaken security
- Restricted market data or any data that cannot legally be redistributed
- Private incident details, raw production logs or unrestricted broker payloads

## Data integrity and execution safety

- Use synthetic or properly anonymised data in source control, examples and tests.
- No test, sample, development or CI path may submit a real-money order.
- Stub or Demo execution must remain the default safe state.
- Do not silently rewrite or delete audit, signal or execution history; corrections must remain traceable.
- Preserve concurrency, idempotency and reconciliation protections.
- Reject malformed or unsupported configuration before persistence.
- Treat destructive database migrations, history rewrites and data repair scripts as high risk; require explicit approval, tests and a recovery plan.
- Never log or commit secrets, unrestricted payloads or private operational data.
- Do not weaken safety checks merely to make a test or demonstration pass.

## Agent requirements

Before creating or modifying public repository content:

1. Classify the proposed content as public-safe or private.
2. Review the complete diff for strategy, secret, identity, live-operation and restricted-data exposure.
3. Use synthetic placeholders instead of real values.
4. Stop and ask Jason if the classification is uncertain.
5. Do not infer, reconstruct or publish proprietary strategy from private context.
6. Preserve this policy unless Jason explicitly changes it.

## Repository boundary

The public repository contains generic infrastructure, contracts and harmless reference implementations.

Proprietary strategies, meaningful parameters, private research and live environment configuration belong in a separate private repository or private deployment context.
