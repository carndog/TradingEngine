using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class ChartAnalysisIdentifierTests
{
    [Test]
    public void From_WithValidValue_ReturnsIdentifier()
    {
        Result<ChartAnalysisIdentifier> result = ChartAnalysisIdentifier.From("support-a.1");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Value, Is.EqualTo("support-a.1"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void From_WithMissingValue_ReturnsIdentifierRequiredError(string? value)
    {
        Result<ChartAnalysisIdentifier> result = ChartAnalysisIdentifier.From(value!);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.IdentifierRequired));
    }

    [Test]
    public void From_WithValueExceedingMaximumLength_ReturnsError()
    {
        Result<ChartAnalysisIdentifier> result = ChartAnalysisIdentifier.From(
            "a" + new string('b', 64));

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.IdentifierExceedsMaximumLength));
    }

    [TestCase("Support-A")]
    [TestCase("1support")]
    [TestCase("-support")]
    [TestCase("support_a")]
    [TestCase("support a")]
    public void From_WithInvalidCharacters_ReturnsIdentifierInvalidCharactersError(string value)
    {
        Result<ChartAnalysisIdentifier> result = ChartAnalysisIdentifier.From(value);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.IdentifierInvalidCharacters));
    }
}
