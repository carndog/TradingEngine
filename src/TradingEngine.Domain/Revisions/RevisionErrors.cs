using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Revisions;

public static class RevisionErrors
{
    public static readonly Error IdRequired = Error.Validation(
        "revision.id_required",
        "A revision identifier cannot be empty.");

    public static readonly Error CreatedByRequired = Error.Validation(
        "revision.created_by_required",
        "A revision requires the identity that created it.");

    public static readonly Error DuplicateId = Error.Conflict(
        "revision.duplicate_id",
        "A revision with the same identifier already exists on this timeline.");

    public static readonly Error NotFound = Error.NotFound(
        "revision.not_found",
        "No revision with the requested identifier exists on this timeline.");

    public static readonly Error NotDraft = Error.Conflict(
        "revision.not_draft",
        "The revision is committed to the effective timeline and cannot be treated as a draft.");

    public static readonly Error NotCommitted = Error.Conflict(
        "revision.not_committed",
        "The revision is a draft and has no committed effective period.");

    public static readonly Error InvalidPeriod = Error.Validation(
        "revision.invalid_period",
        "An effective period must end after it begins.");

    public static readonly Error Backdated = Error.Validation(
        "revision.backdated",
        "An effective boundary cannot precede the current instant.");

    public static readonly Error PeriodBegun = Error.Conflict(
        "revision.period_begun",
        "The revision's effective period has begun and its definition and start can no longer change.");

    public static readonly Error StartConflict = Error.Conflict(
        "revision.start_conflict",
        "Another revision already begins at the requested instant.");

    public static readonly Error UncoveredStart = Error.Conflict(
        "revision.uncovered_start",
        "The committed range must start at or after the timeline's coverage origin.");

    public static readonly Error NoChangeRequested = Error.Validation(
        "revision.no_change_requested",
        "A revision amendment must supply a replacement definition, a new effective start, or both.");

    public static readonly Error CoverageOriginProtected = Error.Conflict(
        "revision.coverage_origin_protected",
        "The revision holding the coverage origin cannot be rescheduled or removed.");

    public static readonly Error RestoredDraftInvalid = Error.Validation(
        "revision.restored_draft_invalid",
        "A restored draft cannot carry a revision number or an effective end boundary.");

    public static readonly Error RestoredCommittedInvalid = Error.Validation(
        "revision.restored_committed_invalid",
        "A restored committed revision requires a positive revision number and cannot carry a proposal.");

    public static readonly Error RestoredSequenceInvalid = Error.Validation(
        "revision.restored_sequence_invalid",
        "Restored committed revisions must be adjacent in start order, numbered to match their position, and end open-ended.");
}
