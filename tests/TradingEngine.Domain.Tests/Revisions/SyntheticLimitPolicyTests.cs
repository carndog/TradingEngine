using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using static TradingEngine.Domain.Tests.Revisions.SyntheticTimelines;

namespace TradingEngine.Domain.Tests.Revisions;

[TestFixture]
public sealed class SyntheticLimitPolicyTests
{
    [Test]
    public void ApplyNow_WithDraftEditedToInvalidDefinition_ReturnsPolicyErrorAndLeavesStateUnchanged()
    {
        SyntheticLimitPolicy policy = new();
        Revision<SyntheticLimitDefinition> current = policy.CreateDraft(Id(1), Limit(10), October(1)).Value;
        policy.ApplyNow(current.Id, October(1));
        Revision<SyntheticLimitDefinition> draft = policy.CreateDraft(Id(2), Limit(20), October(4)).Value;
        policy.EditDraft(draft.Id, Limit(0));

        Result result = policy.ApplyNow(draft.Id, October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(SyntheticLimitPolicy.LimitNotPositive));
            Assert.That(draft.IsDraft, Is.True);
            Assert.That(draft.Definition.Limit, Is.EqualTo(0));
            Assert.That(policy.Drafts, Is.EqualTo(new[] { draft }));
            Assert.That(policy.Revisions, Is.EqualTo(new[] { current }));
            Assert.That(current.EffectivePeriod!.IsOpenEnded, Is.True);
            Assert.That(policy.EffectiveAt(October(5)), Is.SameAs(current));
        });
    }

    [Test]
    public void Schedule_WithInvalidDefinition_ReturnsPolicyErrorBeforeTemporalValidation()
    {
        SyntheticLimitPolicy policy = new();
        Revision<SyntheticLimitDefinition> draft = policy.CreateDraft(Id(1), Limit(-5), October(4)).Value;

        Result result = policy.Schedule(draft.Id, October(3), October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(SyntheticLimitPolicy.LimitNotPositive));
            Assert.That(policy.Revisions, Is.Empty);
        });
    }

    [Test]
    public void ApplyNow_WithDraftCorrectedAfterFailure_CommitsOnRetry()
    {
        SyntheticLimitPolicy policy = new();
        Revision<SyntheticLimitDefinition> draft = policy.CreateDraft(Id(1), Limit(0), October(4)).Value;
        policy.ApplyNow(draft.Id, October(5));
        policy.EditDraft(draft.Id, Limit(15));

        Result result = policy.ApplyNow(draft.Id, October(5, 1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(draft.IsDraft, Is.False);
            Assert.That(draft.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(5, 1)));
            Assert.That(policy.EffectiveAt(October(5, 1))!.Definition.Limit, Is.EqualTo(15));
        });
    }
}
