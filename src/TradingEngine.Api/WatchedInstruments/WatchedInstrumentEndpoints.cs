using Microsoft.AspNetCore.Http.HttpResults;
using NodaTime;
using TradingEngine.Application.Listings;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Application.WatchedInstruments.Delete;
using TradingEngine.Application.WatchedInstruments.Read;
using TradingEngine.Application.WatchedInstruments.Register;
using TradingEngine.Application.WatchedInstruments.Search;
using TradingEngine.Application.WatchedInstruments.Settings;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Application.WatchedInstruments.Verify;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.WatchedInstruments;

internal static class WatchedInstrumentEndpoints
{
    private const string GroupPath = "/api/watched-instruments";
    private const int SearchQueryMaximumLength = 128;
    private const int MaximumPageSize = 100;

    public static RouteGroupBuilder MapWatchedInstrumentEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        RouteGroupBuilder group = routes.MapGroup(GroupPath);
        group.MapPost("", RegisterAsync);
        group.MapPost("/verify", VerifyAsync);
        group.MapGet("", SearchAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/{id:guid}/settings", GetSettingsAsync);
        group.MapPut("/{id:guid}/settings", PutSettingsAsync);
        group.MapPut("/{id:guid}/listing", PutListingAsync);
        group.MapPost("/bulk-delete", BulkDeleteAsync);

        return group;
    }

    private static async Task<IResult> VerifyAsync(
        VerifyInstrumentRequest? request,
        VerifyInstrumentHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.User.Identity?.IsAuthenticated is not true)
        {
            return ApiProblemDetails.Unauthorized("authentication.principal_required", "Sign in to verify an instrument.");
        }

        if (request is null)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.RequestRequired);
        }

        Result<InstrumentVerification> verified = await handler.HandleAsync(new VerifyInstrument(
            request.Symbol ?? string.Empty, request.Exchange ?? string.Empty,
            request.QuoteCurrency ?? string.Empty, request.NativeQuoteCurrency), cancellationToken);
        if (verified.IsFailure)
        {
            return ApiProblemDetails.Problem(verified.Error);
        }

        InstrumentVerification result = verified.Value;
        VerificationQuoteDto? quote = result.Quote is null ? null : new(
            result.Quote.Bid, result.Quote.Ask, result.Quote.Instrument.QuoteCurrency,
            result.Quote.RetrievedAt.ToDateTimeOffset(), result.Quote.ProviderObservedAt?.ToDateTimeOffset(),
            result.Quote.Timeliness.ToString().ToLowerInvariant());
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new InstrumentVerificationResponse(
            result.Identity.Symbol, result.Identity.Exchange, result.Identity.QuoteCurrency,
            FormatResolutionState(result.ResolutionState), result.ResolutionDetailCode,
            result.Listing?.Provider, result.Listing?.ProviderSymbol, result.Listing?.ProviderExchangeName,
            result.Listing?.ProviderExchangeId, result.Listing?.DisplayName, result.NativeQuoteCurrency,
            result.ReadyForMonitoring, result.ReadinessDetailCode, FormatSessionStatus(result.Session.Status),
            result.Session.ReasonCode, quote, result.QuoteDetailCode));
    }

    private static async Task<Results<Created<WatchedInstrumentResponse>, ProblemHttpResult>> RegisterAsync(
        RegisterWatchedInstrumentRequest? request,
        RegisterWatchedInstrumentHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.RequestRequired);
        }

        Result<ChartAnalysisDefinition> definition = ChartAnalysisMapping.ToDefinition(
            request.PriceScale,
            request.SupportZones,
            request.ResistanceZones);
        if (definition.IsFailure)
        {
            return ApiProblemDetails.Problem(definition.Error);
        }

        Result<MonitoringState> monitoringState = MapMonitoringState(request.MonitoringState);
        if (monitoringState.IsFailure)
        {
            return ApiProblemDetails.Problem(monitoringState.Error);
        }

        RegisterWatchedInstrument command = new(
            request.Symbol ?? string.Empty,
            request.Exchange ?? string.Empty,
            request.QuoteCurrency ?? string.Empty,
            request.SamplingIntervalSeconds,
            monitoringState.Value,
            definition.Value,
            RequestActor.Resolve(context),
            request.NativeQuoteCurrency);

        Result<WatchedInstrumentRegistrationOutcome> registered =
            await handler.HandleAsync(command, cancellationToken);
        if (registered.IsFailure)
        {
            return ApiProblemDetails.Problem(registered.Error);
        }

        WatchedInstrumentResponse response = ToResponse(
            registered.Value.Instrument,
            definition.Value) with
        {
            Listing = ToListingDto(registered.Value.Listing)
        };

        return TypedResults.Created($"{GroupPath}/{registered.Value.Instrument.Id}", response);
    }

    private static async Task<Results<Ok<WatchedInstrumentResponse>, ProblemHttpResult>> GetAsync(
        Guid id,
        GetWatchedInstrumentHandler handler,
        CancellationToken cancellationToken)
    {
        Result<WatchedInstrumentConfiguration> configuration = await handler.HandleAsync(
            new GetWatchedInstrument(id),
            cancellationToken);

        if (configuration.IsFailure)
        {
            return ApiProblemDetails.Problem(configuration.Error);
        }

        return TypedResults.Ok(ToResponse(
            configuration.Value.Instrument,
            configuration.Value.Definition));
    }

    private static async Task<IResult> SearchAsync(
        string? q,
        int? page,
        int? pageSize,
        SearchWatchedInstrumentsHandler handler,
        CancellationToken cancellationToken)
    {
        int requestedPage = page ?? 1;
        int requestedPageSize = pageSize ?? 25;
        string? query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        if (requestedPage < 1)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.PageInvalid);
        }

        if (requestedPageSize is < 1 or > MaximumPageSize)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.PageSizeOutOfRange);
        }

        if ((long)(requestedPage - 1) * requestedPageSize > int.MaxValue)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.PageOutOfRange);
        }

        if (query is not null && query.Length > SearchQueryMaximumLength)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.SearchQueryTooLong);
        }

        Result<WatchedInstrumentPage> result = await handler.HandleAsync(
            new SearchWatchedInstruments(query, requestedPage, requestedPageSize),
            cancellationToken);
        if (result.IsFailure)
        {
            return ApiProblemDetails.Problem(result.Error);
        }

        WatchedInstrumentPage found = result.Value;

        return TypedResults.Ok(new WatchedInstrumentPageResponse(
            found.Items.Select(ToSummaryDto).ToArray(),
            found.Page,
            found.PageSize,
            found.TotalCount));
    }

    private static async Task<IResult> GetSettingsAsync(
        Guid id,
        GetWatchedInstrumentSettingsHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<WatchedInstrumentSettingsSnapshot> settings = await handler.HandleAsync(
            new GetWatchedInstrumentSettings(id),
            cancellationToken);
        if (settings.IsFailure)
        {
            return ApiProblemDetails.Problem(settings.Error);
        }

        ConcurrencyTokens.SetConcurrencyHeaders(context, settings.Value.ConcurrencyToken);

        return TypedResults.Ok(ToSettingsResponse(settings.Value));
    }

    private static async Task<IResult> PutSettingsAsync(
        Guid id,
        UpdateWatchedInstrumentSettingsRequest? request,
        UpdateWatchedInstrumentSettingsHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.RequestRequired);
        }

        Result<MonitoringState> monitoringState = MapMonitoringState(request.MonitoringState);
        if (monitoringState.IsFailure)
        {
            return ApiProblemDetails.Problem(monitoringState.Error);
        }

        PriceDisplayUnit? displayUnit = null;
        if (request.PriceDisplayUnit is not null)
        {
            Result<PriceDisplayUnit> mappedUnit = MapPriceDisplayUnit(request.PriceDisplayUnit);
            if (mappedUnit.IsFailure)
            {
                return ApiProblemDetails.Problem(mappedUnit.Error);
            }

            displayUnit = mappedUnit.Value;
        }

        (byte[]? token, Error? tokenError) = ConcurrencyTokens.ExpectedToken(
            context.Request,
            WatchedInstrumentErrors.ConcurrencyTokenInvalid);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<WatchedInstrumentSettingsSnapshot> saved = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                id,
                monitoringState.Value,
                request.SamplingIntervalSeconds,
                token,
                request.PriceDisplayDecimalPlaces,
                displayUnit),
            cancellationToken);
        if (saved.IsFailure)
        {
            return ApiProblemDetails.Problem(saved.Error);
        }

        ConcurrencyTokens.SetConcurrencyHeaders(context, saved.Value.ConcurrencyToken);

        return TypedResults.Ok(ToSettingsResponse(saved.Value));
    }

    private static async Task<IResult> PutListingAsync(
        Guid id,
        ConfirmListingUnitRequest? request,
        ConfirmListingUnitHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.RequestRequired);
        }

        (byte[]? token, Error? tokenError) = ConcurrencyTokens.ExpectedToken(
            context.Request,
            InstrumentListingErrors.ConcurrencyTokenInvalid);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<ListingStatusInfo> confirmed = await handler.HandleAsync(
            new ConfirmListingUnit(
                id,
                request.NativeQuoteCurrency,
                token,
                RequestActor.Resolve(context)),
            cancellationToken);
        if (confirmed.IsFailure)
        {
            return ApiProblemDetails.Problem(confirmed.Error);
        }

        return TypedResults.Ok(ToListingDto(confirmed.Value));
    }

    private static async Task<IResult> BulkDeleteAsync(
        BulkDeleteWatchedInstrumentsRequest? request,
        BulkDeleteWatchedInstrumentsHandler handler,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Items is null)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.RequestRequired);
        }

        List<WatchedInstrumentDeleteItem> items = new(request.Items.Count);
        List<Guid> malformedIds = [];
        foreach (BulkDeleteWatchedInstrumentItemDto item in request.Items)
        {
            if (item is null)
            {
                malformedIds.Add(Guid.Empty);
                continue;
            }

            if (ConcurrencyTokens.TryParseToken(item.ConcurrencyToken, out byte[] token) is false)
            {
                malformedIds.Add(item.InstrumentId);
                continue;
            }

            byte[]? expected = token.Length == 0 ? null : token;
            items.Add(new WatchedInstrumentDeleteItem(item.InstrumentId, expected));
        }

        if (malformedIds.Count > 0)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.DeleteTokenInvalid
                .WithExtension("instrumentIds", malformedIds.ToArray()));
        }

        Result<WatchedInstrumentBulkDeleteOutcome> outcome = await handler.HandleAsync(
            new BulkDeleteWatchedInstruments(items),
            cancellationToken);
        if (outcome.IsFailure)
        {
            return ApiProblemDetails.Problem(outcome.Error);
        }

        return TypedResults.Ok(new BulkDeleteWatchedInstrumentsResponse(
            outcome.Value.DeletedIds,
            outcome.Value.AlreadyDeletedIds));
    }

    private static Result<MonitoringState> MapMonitoringState(string? value)
    {
        return value switch
        {
            "configured" => MonitoringState.Configured,
            "monitored" => MonitoringState.Monitored,
            _ => WatchedInstrumentErrors.MonitoringStateUndefined
        };
    }

    private static WatchedInstrumentResponse ToResponse(
        WatchedInstrument instrument,
        ChartAnalysisDefinition definition)
    {
        return new WatchedInstrumentResponse(
            instrument.Id,
            instrument.Symbol,
            instrument.Exchange,
            instrument.QuoteCurrency,
            FormatMonitoringState(instrument.MonitoringState),
            instrument.SamplingIntervalSeconds,
            ToOffset(instrument.CreatedAt),
            ToOffset(instrument.LastChangedAt),
            definition.PriceScale,
            ChartAnalysisMapping.ToDto(definition).SupportZones!,
            ChartAnalysisMapping.ToDto(definition).ResistanceZones!);
    }

    private static WatchedInstrumentSummaryDto ToSummaryDto(WatchedInstrumentSummary summary)
    {
        return new WatchedInstrumentSummaryDto(
            summary.Id,
            summary.Symbol,
            summary.Exchange,
            summary.QuoteCurrency,
            FormatMonitoringState(summary.MonitoringState),
            summary.SamplingIntervalSeconds,
            ConcurrencyTokens.FormatToken(summary.ConcurrencyToken));
    }

    private static WatchedInstrumentSettingsResponse ToSettingsResponse(
        WatchedInstrumentSettingsSnapshot snapshot)
    {
        WatchedInstrument instrument = snapshot.Instrument;

        return new WatchedInstrumentSettingsResponse(
            instrument.Id,
            instrument.Symbol,
            instrument.Exchange,
            instrument.QuoteCurrency,
            FormatMonitoringState(instrument.MonitoringState),
            instrument.SamplingIntervalSeconds,
            ToOffset(instrument.CreatedAt),
            ToOffset(instrument.LastChangedAt),
            ConcurrencyTokens.FormatToken(snapshot.ConcurrencyToken),
            snapshot.Display.DecimalPlaces,
            FormatPriceDisplayUnit(snapshot.Display.Unit),
            snapshot.Listing is null ? null : ToListingDto(snapshot.Listing),
            snapshot.Collection is null ? null : ToCollectionDto(snapshot.Collection));
    }

    private static ListingDto ToListingDto(ListingStatusInfo info)
    {
        return new ListingDto(
            info.ResolutionState is null ? null : FormatResolutionState(info.ResolutionState.Value),
            info.ResolutionDetailCode,
            info.Listing?.Provider,
            info.Listing?.ProviderInstrumentId,
            info.Listing?.ProviderSymbol,
            info.Listing?.ProviderExchangeName,
            info.Listing?.DisplayName,
            FormatUnitState(info.UnitState),
            info.NativeQuoteCurrency,
            info.UnitSource is null ? null : FormatUnitSource(info.UnitSource.Value),
            info.DeploymentConfigured,
            info.ConcurrencyToken is null
                ? null
                : ConcurrencyTokens.FormatToken(info.ConcurrencyToken));
    }

    private static CollectionStatusDto ToCollectionDto(CollectionStatusInfo info)
    {
        return new CollectionStatusDto(
            FormatCollectionStatus(info.Status),
            info.ReasonCode,
            info.Reason,
            info.LastObservedAt is null ? null : ToOffset(info.LastObservedAt.Value),
            info.LastAttemptAt is null ? null : ToOffset(info.LastAttemptAt.Value),
            info.LastAttemptOutcome?.ToString(),
            info.LastAttemptErrorCode);
    }

    private static string FormatSessionStatus(ExchangeSessionStatus status) => status switch
    {
        ExchangeSessionStatus.Open => "open",
        ExchangeSessionStatus.Closed => "closed",
        ExchangeSessionStatus.CalendarUnavailable => "calendar-unavailable",
        _ => "unconfigured"
    };

    private static string FormatResolutionState(ListingResolutionState state)
    {
        return state switch
        {
            ListingResolutionState.Resolved => "resolved",
            ListingResolutionState.NotFound => "not-found",
            ListingResolutionState.Ambiguous => "ambiguous",
            ListingResolutionState.UnsupportedVenue => "unsupported-venue",
            _ => "unavailable"
        };
    }

    private static string FormatUnitState(ListingUnitState state)
    {
        return state switch
        {
            ListingUnitState.Confirmed => "confirmed",
            ListingUnitState.Deployment => "deployment",
            ListingUnitState.Conflict => "conflict",
            _ => "unconfirmed"
        };
    }

    private static string FormatUnitSource(ListingUnitSource source)
    {
        return source switch
        {
            ListingUnitSource.ProviderMetadata => "provider-metadata",
            ListingUnitSource.Deployment => "deployment",
            _ => "administrator"
        };
    }

    private static string FormatCollectionStatus(InstrumentCollectionStatus status)
    {
        return status switch
        {
            InstrumentCollectionStatus.NotMonitored => "not-monitored",
            InstrumentCollectionStatus.CollectorDisabled => "collector-disabled",
            InstrumentCollectionStatus.UnsupportedVenue => "unsupported-venue",
            InstrumentCollectionStatus.NativeUnitUnconfirmed => "native-unit-unconfirmed",
            InstrumentCollectionStatus.NativeUnitConflict => "native-unit-conflict",
            InstrumentCollectionStatus.SessionClosed => "session-closed",
            InstrumentCollectionStatus.CalendarUnavailable => "calendar-unavailable",
            InstrumentCollectionStatus.AwaitingFirstObservation => "awaiting-first-observation",
            _ => "collecting"
        };
    }

    private static Result<PriceDisplayUnit> MapPriceDisplayUnit(string value)
    {
        return value switch
        {
            "major" => PriceDisplayUnit.Major,
            "native" => PriceDisplayUnit.Native,
            _ => PriceDisplayErrors.UnitUndefined
        };
    }

    private static string FormatPriceDisplayUnit(PriceDisplayUnit unit)
    {
        return unit switch
        {
            PriceDisplayUnit.Major => "major",
            PriceDisplayUnit.Native => "native",
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown price display unit.")
        };
    }

    private static string FormatMonitoringState(MonitoringState state)
    {
        return state switch
        {
            MonitoringState.Configured => "configured",
            MonitoringState.Monitored => "monitored",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown monitoring state.")
        };
    }

    private static DateTimeOffset ToOffset(Instant instant)
    {
        return new DateTimeOffset(instant.ToDateTimeUtc());
    }
}
