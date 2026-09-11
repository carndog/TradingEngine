# TradingEngine

TradingEngine is a public .NET project for building the generic infrastructure of a price-monitoring and trading system. It starts with instrument administration and read-only monitoring, then develops toward a message-driven Azure architecture, safe demo execution and historical replay.

The project deliberately separates reusable engineering from proprietary trading strategy. Strategy algorithms, meaningful parameters, private research, live configuration and sensitive operational information are not stored in this repository.

## Initial product

The first usable version will provide:

- A REST API for maintaining instruments, exchanges and monitoring status.
- A keyboard-focused web client for configuring watched instruments.
- Revision-controlled monitoring rules, including support and resistance regions, stored and validated by the API.
- Scheduled price collection that respects provider sampling and rate limits.
- Read-only signal detection and recommendation alerts.
- Stubbed or demo-only order execution until the relevant safety controls have been proven.

## Technical direction

The planned architecture includes:

- ASP.NET Core and .NET services.
- Entity Framework Core with Azure SQL as the operational system of record.
- Relational storage for core entities such as instruments, observations, signals, trade intents and executions.
- Revision-controlled monitoring-rule definitions stored as XML in Azure SQL, preserving the timeline of changes.
- Azure Functions and Service Bus for the asynchronous processing pipeline.
- Managed Identity and Azure Key Vault for service authentication and secrets.
- Application Insights for logging, tracing and operational monitoring.
- Idempotency, concurrency control, reconciliation and auditable state changes.
- Infrastructure as code and automated build, test and deployment workflows.

Large historical datasets may later use separate archive storage, but they are not the canonical configuration store.

## Development

The solution targets .NET 10 LTS and C# 14. The `global.json` accepts the latest installed .NET 10 feature band while excluding preview SDKs and .NET 11.

From the repository root:

```bash
dotnet restore TradingEngine.sln
dotnet build TradingEngine.sln --configuration Release --no-restore
dotnet test TradingEngine.sln --configuration Release --no-build
dotnet run --project src/TradingEngine.Api
```

The API exposes two deployment-safe smoke-test endpoints:

- `GET /health`
- `GET /version`

For local requests in Rider, open `src/TradingEngine.Api/TradingEngine.Api.http`.

See [Solution boundaries and dependency rules](docs/architecture.md) for the Hexagonal Architecture conventions enforced by the architecture tests.

## Delivery roadmap

Work is organised into the following milestones:

- **M0 — Planning and project foundation**
- **M1 — Configuration foundation**
- **M2 — Read-only signal detection**
- **M3 — Messaging pipeline**
- **M4 — Demo execution**
- **M5 — Historical replay and backtesting**
- **M6 — Controlled go-live preparation**

See the [Trading Engine project](https://github.com/users/carndog/projects/1) and the repository milestones for the current plan and progress.

## Safety and public scope

Stub or demo execution is the safe default. Tests, examples, development environments and CI workflows must never submit real-money orders. Repository data must be synthetic or appropriately anonymised.

Any use of paid data services, provisioned Azure resources or live execution requires an explicit, reviewed decision. Live credentials, account identifiers, real allocations and proprietary strategy belong in a separate private repository or deployment context.

Read the [Public Scope and Data Integrity Policy](PUBLIC_SCOPE.md) before contributing or using an AI coding agent.

## Legacy implementation

The previous implementation may be consulted as reference material while rebuilding individual capabilities. This project starts afresh; legacy source code will not be imported wholesale into the public repository.

## Licence

This repository does not currently grant an open-source licence. Apache License 2.0 may be considered once the public infrastructure has matured.
