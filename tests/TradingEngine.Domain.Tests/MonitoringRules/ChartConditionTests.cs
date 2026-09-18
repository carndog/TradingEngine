using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class ChartConditionTests
{
    [Test]
    public void Create_WithValidValues_ReturnsCondition()
    {
        Result<ChartCondition> result = ChartCondition.Create(
            ChartConditionType.BuyZone,
            ChartAnalysisIdentifier.From("publish-signal").Value);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Type, Is.EqualTo(ChartConditionType.BuyZone));
            Assert.That(result.Value.ActionId.Value, Is.EqualTo("publish-signal"));
        });
    }

    [Test]
    public void Create_WithNullActionId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => ChartCondition.Create(ChartConditionType.BuyZone, null!));
    }

    [Test]
    public void Create_WithUndefinedConditionType_ReturnsConditionTypeUndefinedError()
    {
        Result<ChartCondition> result = ChartCondition.Create(
            (ChartConditionType)999,
            ChartAnalysisIdentifier.From("publish-signal").Value);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.ConditionTypeUndefined));
    }
}
