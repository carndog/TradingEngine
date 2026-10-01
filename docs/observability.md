# Observability for the development API

This document describes the Application Insights and App Service diagnostics added under issue #36: how telemetry is collected, where to inspect startup, request, dependency and deployment failures, and how to verify the deployed development API is observable.

## Approach

The API uses the **Azure Monitor OpenTelemetry Distro for ASP.NET Core** (`Azure.Monitor.OpenTelemetry.AspNetCore`), Microsoft's recommended and supported integration for ASP.NET Core applications per [Enable OpenTelemetry in Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-enable) and the [Azure Monitor Distro client library for .NET](https://learn.microsoft.com/dotnet/api/overview/azure/monitor.opentelemetry.aspnetcore-readme).

This is the smallest supported integration: one package provides ASP.NET Core request instrumentation, HttpClient and (vendored) SqlClient dependency instrumentation, and the Azure Monitor exporters for traces, metrics and logs. The classic `Microsoft.ApplicationInsights.AspNetCore` SDK remains supported but is the legacy path for new applications. The App Service codeless Application Insights agent is **not** enabled — the in-process distro is the single collection path, so there is no duplicate collection from overlapping SDK and agent configurations.

All observability code lives in `src/TradingEngine.Api` (the composition root). Domain, Application and Contracts take no Azure or telemetry dependencies.

## What is collected

| Signal | Mechanism |
| --- | --- |
| Incoming requests (`requests`) | ASP.NET Core instrumentation in the distro |
| Outbound dependencies (`dependencies`), including SQL calls through EF Core / `Microsoft.Data.SqlClient` | Vendored SqlClient instrumentation in the distro |
| Unexpected exceptions (`exceptions`) | `ILogger` records that carry an `Exception` are exported as exception telemetry; exception events on spans are disabled (`RecordException = false`) because their tags cannot be scrubbed after the fact — the log path is sanitized instead (see [Sensitive-data handling](#sensitive-data-handling) for what the sanitized form preserves and loses) |
| Application start (`customEvents`) | `StartupTelemetryHostedService` emits one `TradingEngine.Api.Started` custom event per process start via `ILogger` using the `microsoft.custom_event.name` attribute |
| Metrics and `ILogger` traces | Azure Monitor exporters configured by `UseAzureMonitor` |

Requests, dependencies and exceptions produced by one API call share the OpenTelemetry trace context, so they correlate under `operation_Id` in Application Insights.

The distro's trace-based log sampler (`EnableTraceBasedLogsSampler`, on by default) exports an `ILogger` record emitted inside a trace only when that trace is sampled; records with no trace context are always exported. With `SamplingRatio = 1.0` every request trace is sampled, so this has no effect on the development API today, but lowering the ratio would also drop the logs of unsampled requests.

## Structured build identity

- Every request, dependency, `ILogger` trace and exception record carries `application.version`, `application.commit` and `deployment.environment` in `customDimensions` (applied by `TelemetryEnrichmentProcessor` for spans and `TelemetryEnrichmentLogProcessor` for log records).
- The OpenTelemetry resource sets `service.name` (`cloud_RoleName` in the portal) and `service.version` to the same informational version the `/version` endpoint reports, which includes the commit SHA for CI-built deployments.
- The startup event repeats `Application`, `Version`, `Commit` and `Environment` as structured properties.

## Configuration

| Name | Where | Purpose |
| --- | --- | --- |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Web App app setting, set by `infra/modules/app-service.bicep` from the Application Insights resource reference | Enables Azure Monitor export. The value is resolved during deployment, never committed, and passed as a `@secure` parameter. It is not a deployment output, so it never appears in workflow summaries or deployment history. |
| `applicationInsightsRetentionDays` | `infra/main.bicep` parameter (`30` in `dev.bicepparam`) | Retention for Application Insights data; 30 days is the supported minimum. |
| `logAnalyticsDailyDataCapGb` | `infra/main.bicep` parameter (`'0.1'` in `dev.bicepparam`) | Daily ingestion cap on the Log Analytics workspace. |

When `APPLICATIONINSIGHTS_CONNECTION_STRING` is absent the API skips the OpenTelemetry/Azure Monitor pipeline entirely: local development, tests and CI run with no Azure credentials and no Application Insights resource. The startup event is still written to the standard `ILogger` output, so it appears in App Service application logs. Exporter delivery problems are handled inside the OpenTelemetry pipeline and never fail API operations.

### Local telemetry (optional)

To exercise the export path locally, set the environment variable or a user secret named `APPLICATIONINSIGHTS_CONNECTION_STRING` to a development Application Insights connection string before `dotnet run`. Do not commit a real connection string.

## Sensitive-data handling

- `SensitiveDataTelemetryProcessor` runs before export on every span: it removes `db.statement`/`db.query.text` (SQL command text — the SqlClient instrumentation's statement capture stays disabled anyway), removes `url.query`, strips query strings and fragments from `url.full`/`http.url`, and drops all `http.request.header.*` tags so authorization headers, cookies and the `X-Database-Probe-Key` header can never be exported.
- `SensitiveDataLogProcessor` runs before export on every `ILogger` record. Structured attributes whose key names a credential (`token`, `password`, `authorization`, `cookie`, `apikey`, `connectionstring`, …) are replaced with `<redacted>`, and their raw values are then removed from the rendered message and body as well, so `LogError("Probe credential {Token}", value)` exports neither the attribute value nor the rendered text. Free text is additionally scanned for `key=value` / `key: value` credential pairs — unquoted values up to the next whitespace or `;`, or complete `"…"`/`'…'` quoted values including any whitespace or `;` inside the quotes — plus `Bearer` tokens and embedded chart-analysis documents.
- **Redaction guarantees and limits.** Redaction is pattern-based: it reliably removes values that are attached to a recognisable key or token prefix, or that were logged through a sensitive-named structured attribute. It cannot recognise an arbitrary proprietary value that appears in free text with no key, so such values must be kept out of log messages and exception messages at the source — which is how document-derived data is handled: `ChartAnalysisDefinitionXmlSerializer` reports invalid price attributes by element and attribute name only (no `FormatException` carrying the raw input is retained), schema-validation failures report error count and position without XML content, and `appsettings.json` disables the `Microsoft.EntityFrameworkCore.Database.Command` log category so EF Core SQL text and parameter values are never logged or exported.
- **Exceptions carrying sensitive text.** When an exception's `ToString()` contains a sensitive value, the processor exports a `TelemetrySanitizedException` (or `TelemetrySanitizedAggregateException`, preserving every `AggregateException` branch) in its place. The application's own exception object is never mutated. The replacement carries the redacted message and the redacted original `ToString()` — including the original type name, stack frames and inner-exception chain — and the log record gains two properties, `exception.original_type` and `exception.sanitized_details`, so the original classification and frames survive in `customDimensions`. **Limitation of the pinned SDK**: the Azure Monitor exporter (`Azure.Monitor.OpenTelemetry.Exporter` 1.8.3) derives `exceptions.type`, `problemId` and `parsedStack` from the live `System.Exception` it is handed, and provides no supported hook to supply them independently. Sanitized exceptions therefore appear in Application Insights with `type = TradingEngine.Api.Diagnostics.TelemetrySanitizedException`, an empty `parsedStack`, and group under that type rather than the original one; use `customDimensions["exception.original_type"]` for classification and `customDimensions["exception.sanitized_details"]` for the frames. Exceptions without sensitive text are exported unchanged, with native type, grouping and parsed stack.
- Request and response bodies are never captured by the instrumentation, so registration payloads — including monitoring-rule data — do not reach telemetry. `Result`-based validation failures return problem details and do not produce exception records.
- Connection strings, instrumentation keys and probe keys are never logged or committed; the probe key and SQL connection string are deploy-time secrets exactly as before.

## Azure resources (development)

| Resource | Name pattern | Notes |
| --- | --- | --- |
| Log Analytics workspace | `log-tradingengine-dev` | `PerGB2018` pay-as-you-go, 30-day retention, daily ingestion cap `0.1` GB |
| Application Insights | `appi-tradingengine-dev` | Workspace-based (`IngestionMode = LogAnalytics`), `kind = web`, 30-day retention |
| Web App app setting | `APPLICATIONINSIGHTS_CONNECTION_STRING` | Set from the component's `ConnectionString` property during deployment |
| Web App `logs` config | `Microsoft.Web/sites/config` `logs` | `httpLogs.fileSystem` 7 days / 35 MB; `detailedErrorMessages` and `applicationLogs.fileSystem` are also set but both are Windows App Service features — on the Linux plan they have no effect and container console output is collected by the platform instead |

## Retention, sampling and cost

- **Retention**: 30 days on both the workspace and the Application Insights component — the minimum supported value.
- **Sampling**: `SamplingRatio` is `1.0` (all traces kept). Development traffic is tiny, so full trace fidelity aids diagnosis; the ratio can be lowered in `TelemetryServiceCollectionExtensions` if volume grows.
- **Daily cap**: `0.1` GB/day on the Log Analytics workspace is a safeguard against unexpected ingestion spikes, **not a cost bound and not a guaranteed monthly maximum**. Per [Set daily cap on Log Analytics workspace](https://learn.microsoft.com/azure/azure-monitor/logs/daily-cap), the cap cannot stop collection at precisely the configured level — excess data is expected, can be large under high ingestion rates, and **is still billed**. Once reached, billable collection stops for the rest of the 24-hour period and the API is effectively unmonitored until collection resumes at the workspace's reset hour, which differs per workspace and cannot be configured. Treat reaching the cap as an incident; subscription cost alerts remain the mechanism for spend notification.
- **App Service logs**: `applicationLogs.fileSystem` and `detailedErrorMessages` are Windows App Service features and have no effect on the Linux plan. Linux application output goes to stdout/stderr and is collected by the platform: it is visible live in **Log stream** (`az webapp log tail`), and `az webapp log download` / Kudu **Diagnostic dump** returns the contents of `/home/LogFiles`, which on Linux includes the Docker host and container console logs (one set per instance) alongside the HTTP logs retained by `httpLogs.fileSystem` (`retentionInMb = 35`, `retentionInDays = 7`). Azure Monitor export is the durable, queryable path for application logs.
- **Live Metrics** is enabled for interactive verification and adds negligible cost.

## Where to inspect failures

| Question | Location |
| --- | --- |
| Did the process start? | Application Insights → **Logs**: `customEvents | where name == "TradingEngine.Api.Started"`; also App Service → **Log stream** for the console `ILogger` line |
| Did a request run and how did it end? | Application Insights → **Logs**: `requests`; **Transaction search** |
| What SQL ran inside a request? | `dependencies` joined on `operation_Id` |
| Was an exception thrown? | `exceptions` joined on `operation_Id`; also **Failures** blade |
| Why did deployment fail? | GitHub Actions workflow run log (build/test/publish/deploy steps); App Service → **Deployment Center → Logs**; `az webapp log deployment show`; subscription **Activity Log** and `az deployment sub show -n <name>` for the Bicep deployment |
| Container/platform problems? | App Service → **Log stream** (application stdout/stderr on Linux); `az webapp log download` (`/home/LogFiles`: container console output plus HTTP logs) |

Deployment logs are platform records of the publish/deploy action; application telemetry is what the running process emits. Check both — a failed zip deploy produces deployment logs but no telemetry, while a crashed process produces application logs and the startup event may be absent.

## Useful KQL (Application Insights → Logs)

Startup event for a specific deployment commit:

```kusto
customEvents
| where name == "TradingEngine.Api.Started"
| where customDimensions.Commit == "<commit-sha>"
| order by timestamp desc
```

Recent requests to `/health`:

```kusto
requests
| where url has "/health"
| project timestamp, name, resultCode, duration, operation_Id
| order by timestamp desc
```

Everything inside one request, correlated:

```kusto
union requests, dependencies, exceptions
| where operation_Id == "<operation-id-from-requests>"
| order by timestamp asc
```

SQL dependencies:

```kusto
dependencies
| where type contains "sql" or data has "SELECT"
| project timestamp, name, data, target, duration, resultCode, operation_Id
| order by timestamp desc
```

Unexpected exceptions:

```kusto
exceptions
| project timestamp, operation_Id, type, outerMessage, operation_Name
| order by timestamp desc
```

Build identity on telemetry rows:

```kusto
requests
| project timestamp, name,
    application_Version,
    customDimensions["application.version"],
    customDimensions["application.commit"],
    customDimensions["deployment.environment"]
| order by timestamp desc
```

Sensitive-value spot check (replace the placeholder with a synthetic value sent during verification — it must return no rows):

```kusto
search "synthetic-probe-key"
```

## Post-deployment verification checklist

The infrastructure workflow (monitoring resources, app setting) and the application workflow (API publish) run independently when `main` changes; neither waits for the other. Run this checklist only after **both** have completed successfully for the commit under test — telemetry from an application deployed before the app setting existed will be absent, and a startup event from an earlier build will not match the new commit:

1. **Startup telemetry** — `customEvents` contains `TradingEngine.Api.Started` whose `Commit` matches the deployed commit SHA (compare with `GET /version`).
2. **`/health` request telemetry** — call `GET /health` anonymously; a `requests` row appears within a couple of minutes with `resultCode = 200` and the `application.version`/`application.commit`/`deployment.environment` custom dimensions. Live Metrics shows it immediately.
3. **Authenticated request + SQL dependency** — call `GET /api/watched-instruments/{id}` with an owner token for a synthetic instrument; the request row and a `mssql`/`SQL` `dependencies` row share `operation_Id`. The dependency row carries server/database/duration, never statement text or parameter values.
4. **Deliberate handled failure** — `POST /api/watched-instruments` with a synthetic payload whose `monitoringState` is an unsupported value returns `400` with `code = watched_instrument.monitoring_state_undefined`. The `requests` row shows `resultCode = 400` and **no** matching `exceptions` row — evidence of a handled validation failure, not unexpected-exception capture.
5. **Version, environment and correlation fields** — the build-identity query above returns `application.version`, `application.commit` and `deployment.environment = Production` (App Service environment name) on the rows.
6. **Application and deployment logs** — App Service → **Log stream** (or `az webapp log tail`) shows the Linux stdout application output including the startup log line; `az webapp log download` returns `/home/LogFiles` with the container console output and HTTP logs; deployment history is visible in Deployment Center and via `az webapp log deployment show`.
7. **Sensitive-data absence** — the `search` query for the synthetic probe key / synthetic header value returns no rows; `dependencies` rows contain no SQL statement text.
