using NodaTime;
using NodaTime.Text;
using TradingEngine.Application.MonitoringRules;
using TradingEngine.Application.MonitoringRules.Drafts;
using TradingEngine.Application.MonitoringRules.Lifecycle;
using TradingEngine.Application.MonitoringRules.Read;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Api.MonitoringRules;

internal static class MonitoringRuleEndpoints
{
    private const string GroupPath = "/api/watched-instruments/{instrumentId:guid}/monitoring-rule";

    public static RouteGroupBuilder MapMonitoringRuleEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        RouteGroupBuilder group = routes.MapGroup(GroupPath);
        group.MapGet("", GetTimelineAsync);
        group.MapGet("/revisions/{revisionId:guid}", GetRevisionAsync);
        group.MapGet("/revisions/applicable", GetApplicableAsync);
        group.MapGet("/drafts", GetDraftsAsync);
        group.MapPost("/drafts", CreateDraftAsync);
        group.MapPut("/drafts/{draftId:guid}", EditDraftAsync);
        group.MapDelete("/drafts/{draftId:guid}", DeleteDraftAsync);
        group.MapPost("/drafts/{draftId:guid}/apply", ApplyDraftAsync);
        group.MapPost("/drafts/{draftId:guid}/schedule", ScheduleDraftAsync);
        group.MapPut("/revisions/{revisionId:guid}", EditRevisionAsync);
        group.MapDelete("/revisions/{revisionId:guid}", RemoveRevisionAsync);

        return group;
    }

    private static async Task<IResult> GetTimelineAsync(
        Guid instrumentId,
        GetMonitoringRuleTimelineHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new GetMonitoringRuleTimeline(instrumentId),
            cancellationToken);
        if (snapshot.IsFailure)
        {
            return ApiProblemDetails.Problem(snapshot.Error);
        }

        SetConcurrencyHeaders(context, snapshot.Value.ConcurrencyToken);

        return TypedResults.Ok(ToTimelineResponse(snapshot.Value));
    }

    private static async Task<IResult> GetRevisionAsync(
        Guid instrumentId,
        Guid revisionId,
        GetMonitoringRuleTimelineHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new GetMonitoringRuleTimeline(instrumentId),
            cancellationToken);
        if (snapshot.IsFailure)
        {
            return ApiProblemDetails.Problem(snapshot.Error);
        }

        Revision<ChartAnalysisDefinition>? revision = snapshot.Value.Rule
            .FindRevision(revisionId);
        if (revision is null)
        {
            return ApiProblemDetails.Problem(RevisionErrors.NotFound);
        }

        SetConcurrencyHeaders(context, snapshot.Value.ConcurrencyToken);

        return TypedResults.Ok(new MonitoringRuleRevisionResponse(
            FormatToken(snapshot.Value.ConcurrencyToken),
            ToDto(revision)));
    }

    private static async Task<IResult> GetApplicableAsync(
        Guid instrumentId,
        string? at,
        GetApplicableMonitoringRuleRevisionHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        (Instant? instant, Error? parseError) = ParseOptionalInstant(at);
        if (parseError is not null)
        {
            return ApiProblemDetails.Problem(parseError);
        }

        Result<MonitoringRuleApplicableRevision> resolved = await handler.HandleAsync(
            new GetApplicableMonitoringRuleRevision(instrumentId, instant),
            cancellationToken);
        if (resolved.IsFailure)
        {
            return ApiProblemDetails.Problem(resolved.Error);
        }

        SetConcurrencyHeaders(context, resolved.Value.Snapshot.ConcurrencyToken);

        return TypedResults.Ok(new MonitoringRuleRevisionResponse(
            FormatToken(resolved.Value.Snapshot.ConcurrencyToken),
            ToDto(resolved.Value.Revision)));
    }

    private static async Task<IResult> GetDraftsAsync(
        Guid instrumentId,
        GetMonitoringRuleTimelineHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new GetMonitoringRuleTimeline(instrumentId),
            cancellationToken);
        if (snapshot.IsFailure)
        {
            return ApiProblemDetails.Problem(snapshot.Error);
        }

        SetConcurrencyHeaders(context, snapshot.Value.ConcurrencyToken);

        return TypedResults.Ok(ToDraftsResponse(snapshot.Value));
    }

    private static async Task<IResult> CreateDraftAsync(
        Guid instrumentId,
        MonitoringRuleDraftRequest? request,
        CreateMonitoringRuleDraftHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ApiProblemDetails.Problem(MonitoringRuleErrors.RequestRequired);
        }

        if (request.Definition is null)
        {
            return ApiProblemDetails.Problem(MonitoringRuleErrors.DefinitionRequired);
        }

        Result<ChartAnalysisDefinition> definition = ToDefinition(request.Definition);
        if (definition.IsFailure)
        {
            return ApiProblemDetails.Problem(definition.Error);
        }

        (byte[]? token, Error? tokenError) = ExpectedToken(context.Request);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new CreateMonitoringRuleDraft(
                instrumentId,
                definition.Value,
                request.ChangeReason,
                ToProposal(request.ProposedPeriod),
                token,
                RequestActor.Resolve(context)),
            cancellationToken);

        return CommitResponse(context, snapshot);
    }

    private static async Task<IResult> EditDraftAsync(
        Guid instrumentId,
        Guid draftId,
        MonitoringRuleDraftRequest? request,
        EditMonitoringRuleDraftHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ApiProblemDetails.Problem(MonitoringRuleErrors.RequestRequired);
        }

        if (request.Definition is null)
        {
            return ApiProblemDetails.Problem(MonitoringRuleErrors.DefinitionRequired);
        }

        Result<ChartAnalysisDefinition> definition = ToDefinition(request.Definition);
        if (definition.IsFailure)
        {
            return ApiProblemDetails.Problem(definition.Error);
        }

        (byte[]? token, Error? tokenError) = ExpectedToken(context.Request);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new EditMonitoringRuleDraft(
                instrumentId,
                draftId,
                definition.Value,
                request.ChangeReason,
                ToProposal(request.ProposedPeriod),
                token),
            cancellationToken);

        return CommitResponse(context, snapshot);
    }

    private static async Task<IResult> DeleteDraftAsync(
        Guid instrumentId,
        Guid draftId,
        DeleteMonitoringRuleDraftHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        (byte[]? token, Error? tokenError) = ExpectedToken(context.Request);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new DeleteMonitoringRuleDraft(instrumentId, draftId, token),
            cancellationToken);

        return CommitResponse(context, snapshot);
    }

    private static async Task<IResult> ApplyDraftAsync(
        Guid instrumentId,
        Guid draftId,
        ApplyMonitoringRuleDraftHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        (byte[]? token, Error? tokenError) = ExpectedToken(context.Request);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new ApplyMonitoringRuleDraft(instrumentId, draftId, token),
            cancellationToken);

        return CommitResponse(context, snapshot);
    }

    private static async Task<IResult> ScheduleDraftAsync(
        Guid instrumentId,
        Guid draftId,
        ScheduleMonitoringRuleRequest? request,
        ScheduleMonitoringRuleDraftHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ApiProblemDetails.Problem(MonitoringRuleErrors.RequestRequired);
        }

        if (request.EffectiveFrom is null)
        {
            return ApiProblemDetails.Problem(MonitoringRuleErrors.InstantInvalid);
        }

        (byte[]? token, Error? tokenError) = ExpectedToken(context.Request);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new ScheduleMonitoringRuleDraft(
                instrumentId,
                draftId,
                Instant.FromDateTimeOffset(request.EffectiveFrom.Value),
                request.EffectiveTo is null
                    ? null
                    : Instant.FromDateTimeOffset(request.EffectiveTo.Value),
                token),
            cancellationToken);

        return CommitResponse(context, snapshot);
    }

    private static async Task<IResult> EditRevisionAsync(
        Guid instrumentId,
        Guid revisionId,
        EditMonitoringRuleRevisionRequest? request,
        EditMonitoringRuleRevisionHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ApiProblemDetails.Problem(MonitoringRuleErrors.RequestRequired);
        }

        ChartAnalysisDefinition? definition = null;
        if (request.Definition is not null)
        {
            Result<ChartAnalysisDefinition> mapped = ToDefinition(request.Definition);
            if (mapped.IsFailure)
            {
                return ApiProblemDetails.Problem(mapped.Error);
            }

            definition = mapped.Value;
        }

        (byte[]? token, Error? tokenError) = ExpectedToken(context.Request);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new EditMonitoringRuleRevision(
                instrumentId,
                revisionId,
                definition,
                request.ChangeReason,
                request.EffectiveFrom is null
                    ? null
                    : Instant.FromDateTimeOffset(request.EffectiveFrom.Value),
                token),
            cancellationToken);

        return CommitResponse(context, snapshot);
    }

    private static async Task<IResult> RemoveRevisionAsync(
        Guid instrumentId,
        Guid revisionId,
        RemoveMonitoringRuleRevisionHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        (byte[]? token, Error? tokenError) = ExpectedToken(context.Request);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new RemoveMonitoringRuleRevision(instrumentId, revisionId, token),
            cancellationToken);

        return CommitResponse(context, snapshot);
    }

    private static IResult CommitResponse(
        HttpContext context,
        Result<MonitoringRuleSnapshot> snapshot)
    {
        if (snapshot.IsFailure)
        {
            return ApiProblemDetails.Problem(snapshot.Error);
        }

        SetConcurrencyHeaders(context, snapshot.Value.ConcurrencyToken);

        return TypedResults.Ok(ToTimelineResponse(snapshot.Value));
    }

    private static void SetConcurrencyHeaders(HttpContext context, byte[] token)
    {
        context.Response.Headers.ETag = $"\"{FormatToken(token)}\"";
    }

    private static string FormatToken(byte[] token)
    {
        return Convert.ToBase64String(token);
    }

    private static (byte[]? Token, Error? Error) ExpectedToken(HttpRequest request)
    {
        string? header = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return (null, null);
        }

        string value = header.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value.Substring(2).Trim();
        }

        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            value = value.Substring(1, value.Length - 2);
        }

        try
        {
            byte[] decoded = Convert.FromBase64String(value);
            return decoded.Length == 0
                ? (null, MonitoringRuleErrors.ConcurrencyTokenInvalid)
                : (decoded, null);
        }
        catch (FormatException)
        {
            return (null, MonitoringRuleErrors.ConcurrencyTokenInvalid);
        }
    }

    private static (Instant? Instant, Error? Error) ParseOptionalInstant(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        ParseResult<Instant> parsed = InstantPattern.ExtendedIso.Parse(value);
        return parsed.Success
            ? (parsed.Value, null)
            : (null, MonitoringRuleErrors.InstantInvalid);
    }

    private static Result<ChartAnalysisDefinition> ToDefinition(
        MonitoringRuleDefinitionDto dto)
    {
        return ChartAnalysisMapping.ToDefinition(
            dto.PriceScale,
            dto.SupportZones,
            dto.ResistanceZones);
    }

    private static RevisionProposal? ToProposal(RevisionPeriodDto? period)
    {
        if (period is null)
        {
            return null;
        }

        return new RevisionProposal(
            period.EffectiveFrom is null
                ? null
                : Instant.FromDateTimeOffset(period.EffectiveFrom.Value),
            period.EffectiveTo is null
                ? null
                : Instant.FromDateTimeOffset(period.EffectiveTo.Value));
    }

    private static MonitoringRuleTimelineResponse ToTimelineResponse(
        MonitoringRuleSnapshot snapshot)
    {
        return new MonitoringRuleTimelineResponse(
            snapshot.Rule.Id,
            snapshot.Rule.WatchedInstrumentId,
            FormatToken(snapshot.ConcurrencyToken),
            ToOffset(snapshot.Rule.CoverageOrigin),
            snapshot.Rule.Revisions.Select(ToDto).ToArray());
    }

    private static MonitoringRuleDraftsResponse ToDraftsResponse(
        MonitoringRuleSnapshot snapshot)
    {
        return new MonitoringRuleDraftsResponse(
            snapshot.Rule.Id,
            snapshot.Rule.WatchedInstrumentId,
            FormatToken(snapshot.ConcurrencyToken),
            snapshot.Rule.Drafts.Select(ToDto).ToArray());
    }

    private static MonitoringRuleRevisionDto ToDto(
        Revision<ChartAnalysisDefinition> revision)
    {
        return new MonitoringRuleRevisionDto(
            revision.Id,
            revision.IsDraft ? "draft" : "committed",
            revision.RevisionNumber,
            new DateTimeOffset(revision.CreatedAt.ToDateTimeUtc()),
            revision.CreatedBy,
            revision.ChangeReason,
            ToOffset(revision.EffectivePeriod?.EffectiveFrom),
            ToOffset(revision.EffectivePeriod?.EffectiveTo),
            ToOffset(revision.Proposal?.EffectiveFrom),
            ToOffset(revision.Proposal?.EffectiveTo),
            ChartAnalysisMapping.ToDto(revision.Definition));
    }

    private static DateTimeOffset? ToOffset(Instant? instant)
    {
        return instant is null ? null : new DateTimeOffset(instant.Value.ToDateTimeUtc());
    }
}
