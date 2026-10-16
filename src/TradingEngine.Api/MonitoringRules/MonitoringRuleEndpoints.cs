using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NodaTime;
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

        ConcurrencyTokens.SetConcurrencyHeaders(context, snapshot.Value.ConcurrencyToken);

        return TypedResults.Ok(ToTimelineResponse(snapshot.Value, RequestActor.Resolve(context)));
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

        ConcurrencyTokens.SetConcurrencyHeaders(context, snapshot.Value.ConcurrencyToken);

        return TypedResults.Ok(new MonitoringRuleRevisionResponse(
            ConcurrencyTokens.FormatToken(snapshot.Value.ConcurrencyToken),
            ToDto(revision, RequestActor.Resolve(context))));
    }

    private static async Task<IResult> GetApplicableAsync(
        Guid instrumentId,
        string? at,
        GetApplicableMonitoringRuleRevisionHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        (Instant? instant, Error? parseError) = RequestInstants.Optional(at);
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

        ConcurrencyTokens.SetConcurrencyHeaders(context, resolved.Value.Snapshot.ConcurrencyToken);

        return TypedResults.Ok(new MonitoringRuleRevisionResponse(
            ConcurrencyTokens.FormatToken(resolved.Value.Snapshot.ConcurrencyToken),
            ToDto(resolved.Value.Revision, RequestActor.Resolve(context))));
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

        ConcurrencyTokens.SetConcurrencyHeaders(context, snapshot.Value.ConcurrencyToken);

        return TypedResults.Ok(ToDraftsResponse(snapshot.Value, RequestActor.Resolve(context)));
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

        (RevisionProposal? proposal, Error? proposalError) = ToProposal(request.ProposedPeriod);
        if (proposalError is not null)
        {
            return ApiProblemDetails.Problem(proposalError);
        }

        (byte[]? token, Error? tokenError) = ConcurrencyTokens.ExpectedToken(context.Request, MonitoringRuleErrors.ConcurrencyTokenInvalid);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Guid draftId = Guid.NewGuid();
        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new CreateMonitoringRuleDraft(
                instrumentId,
                draftId,
                definition.Value,
                request.ChangeReason,
                proposal,
                token,
                RequestActor.Resolve(context)),
            cancellationToken);

        return CreatedDraftResponse(context, snapshot, draftId);
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

        (RevisionProposal? proposal, Error? proposalError) = ToProposal(request.ProposedPeriod);
        if (proposalError is not null)
        {
            return ApiProblemDetails.Problem(proposalError);
        }

        (byte[]? token, Error? tokenError) = ConcurrencyTokens.ExpectedToken(context.Request, MonitoringRuleErrors.ConcurrencyTokenInvalid);
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
                proposal,
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
        (byte[]? token, Error? tokenError) = ConcurrencyTokens.ExpectedToken(context.Request, MonitoringRuleErrors.ConcurrencyTokenInvalid);
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
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApplyMonitoringRuleRequest? request,
        ApplyMonitoringRuleDraftHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        (Instant? effectiveTo, Error? toError) = RequestInstants.Optional(request?.EffectiveTo);
        if (toError is not null)
        {
            return ApiProblemDetails.Problem(toError);
        }

        (byte[]? token, Error? tokenError) = ConcurrencyTokens.ExpectedToken(context.Request, MonitoringRuleErrors.ConcurrencyTokenInvalid);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new ApplyMonitoringRuleDraft(instrumentId, draftId, effectiveTo, token),
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

        Result<Instant> effectiveFrom = RequestInstants.Required(request.EffectiveFrom);
        if (effectiveFrom.IsFailure)
        {
            return ApiProblemDetails.Problem(effectiveFrom.Error);
        }

        (Instant? effectiveTo, Error? toError) = RequestInstants.Optional(request.EffectiveTo);
        if (toError is not null)
        {
            return ApiProblemDetails.Problem(toError);
        }

        (byte[]? token, Error? tokenError) = ConcurrencyTokens.ExpectedToken(context.Request, MonitoringRuleErrors.ConcurrencyTokenInvalid);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new ScheduleMonitoringRuleDraft(
                instrumentId,
                draftId,
                effectiveFrom.Value,
                effectiveTo,
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

        (Instant? effectiveFrom, Error? fromError) = RequestInstants.Optional(request.EffectiveFrom);
        if (fromError is not null)
        {
            return ApiProblemDetails.Problem(fromError);
        }

        (byte[]? token, Error? tokenError) = ConcurrencyTokens.ExpectedToken(context.Request, MonitoringRuleErrors.ConcurrencyTokenInvalid);
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
                effectiveFrom,
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
        (byte[]? token, Error? tokenError) = ConcurrencyTokens.ExpectedToken(context.Request, MonitoringRuleErrors.ConcurrencyTokenInvalid);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<MonitoringRuleSnapshot> snapshot = await handler.HandleAsync(
            new RemoveMonitoringRuleRevision(instrumentId, revisionId, token),
            cancellationToken);

        return CommitResponse(context, snapshot);
    }

    private static IResult CreatedDraftResponse(
        HttpContext context,
        Result<MonitoringRuleSnapshot> snapshot,
        Guid draftId)
    {
        if (snapshot.IsFailure)
        {
            return ApiProblemDetails.Problem(snapshot.Error);
        }

        Revision<ChartAnalysisDefinition>? created = snapshot.Value.Rule.FindRevision(draftId);
        if (created is null)
        {
            return ApiProblemDetails.Problem(RevisionErrors.NotFound);
        }

        ConcurrencyTokens.SetConcurrencyHeaders(context, snapshot.Value.ConcurrencyToken);

        return TypedResults.Ok(new MonitoringRuleRevisionResponse(
            ConcurrencyTokens.FormatToken(snapshot.Value.ConcurrencyToken),
            ToDto(created, RequestActor.Resolve(context))));
    }

    private static IResult CommitResponse(
        HttpContext context,
        Result<MonitoringRuleSnapshot> snapshot)
    {
        if (snapshot.IsFailure)
        {
            return ApiProblemDetails.Problem(snapshot.Error);
        }

        ConcurrencyTokens.SetConcurrencyHeaders(context, snapshot.Value.ConcurrencyToken);

        return TypedResults.Ok(ToTimelineResponse(snapshot.Value, RequestActor.Resolve(context)));
    }

    private static Result<ChartAnalysisDefinition> ToDefinition(
        MonitoringRuleDefinitionDto dto)
    {
        return ChartAnalysisMapping.ToDefinition(
            dto.PriceScale,
            dto.SupportZones,
            dto.ResistanceZones);
    }

    private static (RevisionProposal? Proposal, Error? Error) ToProposal(RevisionPeriodDto? period)
    {
        if (period is null)
        {
            return (null, null);
        }

        (Instant? from, Error? fromError) = RequestInstants.Optional(period.EffectiveFrom);
        if (fromError is not null)
        {
            return (null, fromError);
        }

        (Instant? to, Error? toError) = RequestInstants.Optional(period.EffectiveTo);
        if (toError is not null)
        {
            return (null, toError);
        }

        return (new RevisionProposal(from, to), null);
    }

    private static MonitoringRuleTimelineResponse ToTimelineResponse(
        MonitoringRuleSnapshot snapshot,
        string actor)
    {
        return new MonitoringRuleTimelineResponse(
            snapshot.Rule.Id,
            snapshot.Rule.WatchedInstrumentId,
            ConcurrencyTokens.FormatToken(snapshot.ConcurrencyToken),
            ToOffset(snapshot.Rule.CoverageOrigin),
            snapshot.Rule.Revisions.Select(revision => ToDto(revision, actor)).ToArray());
    }

    private static MonitoringRuleDraftsResponse ToDraftsResponse(
        MonitoringRuleSnapshot snapshot,
        string actor)
    {
        return new MonitoringRuleDraftsResponse(
            snapshot.Rule.Id,
            snapshot.Rule.WatchedInstrumentId,
            ConcurrencyTokens.FormatToken(snapshot.ConcurrencyToken),
            snapshot.Rule.Drafts.Select(draft => ToDto(draft, actor)).ToArray());
    }

    private static MonitoringRuleRevisionDto ToDto(
        Revision<ChartAnalysisDefinition> revision,
        string actor)
    {
        return new MonitoringRuleRevisionDto(
            revision.Id,
            revision.IsDraft ? "draft" : "committed",
            revision.RevisionNumber,
            new DateTimeOffset(revision.CreatedAt.ToDateTimeUtc()),
            revision.CreatedBy,
            RequestActor.IsVerifiedMatch(actor, revision.CreatedBy),
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
