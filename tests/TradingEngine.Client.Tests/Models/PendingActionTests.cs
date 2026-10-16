using TradingEngine.Client.Models;
using TradingEngine.Client.Services;
using TradingEngine.Contracts.MonitoringRules;

namespace TradingEngine.Client.Tests.Models;

[TestFixture]
public sealed class PendingActionTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-10-05T09:00:00Z");

    private static MonitoringRuleRevisionDto Draft(Guid id)
    {
        return new MonitoringRuleRevisionDto(
            id,
            "draft",
            null,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "synthetic-user",
            false,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    [Test]
    public void ResolveInputs_ScheduleWithPastStart_ReturnsError()
    {
        PendingAction action = new(PendingActionKind.Schedule, Draft(Guid.NewGuid()), "tok-1")
        {
            From = new UtcTimeField(null),
            To = new UtcTimeField(null)
        };
        action.From.Set("2020-01-02T00:00:00");

        ApiError? error = action.ResolveInputs(Now, out string? fromIso, out string? toIso);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Code, Is.EqualTo("schedule.effective_from_past"));
            Assert.That(fromIso, Is.Not.Null);
            Assert.That(toIso, Is.Null);
        });
    }

    [Test]
    public void ResolveInputs_ScheduleWithFutureStart_Succeeds()
    {
        PendingAction action = new(PendingActionKind.Schedule, Draft(Guid.NewGuid()), "tok-1")
        {
            From = new UtcTimeField(null),
            To = new UtcTimeField(null)
        };
        action.From.Set("2030-01-02T00:00:00");

        ApiError? error = action.ResolveInputs(Now, out string? fromIso, out string? toIso);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(fromIso, Does.StartWith("2030-01-02"));
            Assert.That(toIso, Is.Null);
        });
    }

    [Test]
    public void ResolveInputs_ApplyWithProposedStart_RequiresAcknowledgement()
    {
        PendingAction action = new(PendingActionKind.Apply, Draft(Guid.NewGuid()), "tok-1")
        {
            To = new UtcTimeField(null),
            ProposedEffectiveFrom = Now + TimeSpan.FromDays(1)
        };

        ApiError? error = action.ResolveInputs(Now, out _, out _);
        Assert.That(error!.Code, Is.EqualTo("apply.proposal_unacknowledged"));

        action.ProposalReplacementAcknowledged = true;
        error = action.ResolveInputs(Now, out string? fromIso, out string? toIso);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(fromIso, Is.Null);
            Assert.That(toIso, Is.Null);
        });
    }

    [Test]
    public void ResolveInputs_ApplyWithoutProposedStart_DoesNotRequireAcknowledgement()
    {
        PendingAction action = new(PendingActionKind.Apply, Draft(Guid.NewGuid()), "tok-1")
        {
            To = new UtcTimeField(null)
        };

        ApiError? error = action.ResolveInputs(Now, out _, out _);

        Assert.Multiple(() =>
        {
            Assert.That(action.RequiresProposalAcknowledgement, Is.False);
            Assert.That(error, Is.Null);
        });
    }
}
