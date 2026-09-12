using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class ChartConditionTests
{
    [Test]
    public void Create_WithValidValues_ReturnsCondition()
    {
        ChartCondition condition = ChartCondition.Create(
            ChartConditionType.BuyZone,
            ChartAnalysisIdentifier.From("publish-signal"));

        Assert.Multiple(() =>
        {
            Assert.That(condition.Type, Is.EqualTo(ChartConditionType.BuyZone));
            Assert.That(condition.ActionId.Value, Is.EqualTo("publish-signal"));
        });
    }

    [Test]
    public void Create_WithNullActionId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => ChartCondition.Create(ChartConditionType.BuyZone, null!));
    }

    [Test]
    public void Create_WithUndefinedConditionType_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChartCondition.Create(
                (ChartConditionType)999,
                ChartAnalysisIdentifier.From("publish-signal")));
    }
}
