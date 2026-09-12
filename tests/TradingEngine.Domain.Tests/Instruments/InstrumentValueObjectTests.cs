using TradingEngine.Domain.Instruments;

namespace TradingEngine.Domain.Tests.Instruments;

[TestFixture]
public sealed class InstrumentValueObjectTests
{
    [Test]
    public void From_WhenWatchedInstrumentIdIsEmpty_ThrowsDomainRuleViolationException()
    {
        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => WatchedInstrumentId.From(Guid.Empty));

        Assert.That(exception!.Rule, Is.EqualTo(WatchedInstrumentIdRule.Empty));
    }

    [Test]
    public void From_WhenQuoteCurrencyCodeIsMissing_ThrowsDomainRuleViolationException()
    {
        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => QuoteCurrencyCode.From(""));

        Assert.That(exception!.Rule, Is.EqualTo(QuoteCurrencyCodeRule.Required));
    }

    [TestCase("US")]
    [TestCase("EURO")]
    [TestCase("G8P")]
    public void From_WhenQuoteCurrencyCodeIsInvalid_ThrowsDomainRuleViolationException(string value)
    {
        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => QuoteCurrencyCode.From(value));

        Assert.That(exception!.Rule, Is.EqualTo(QuoteCurrencyCodeRule.InvalidFormat));
    }

    [Test]
    public void From_WhenSymbolContainsWhitespace_ThrowsDomainRuleViolationException()
    {
        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => InstrumentSymbol.From("DEMO 1"));

        Assert.That(exception!.Rule, Is.EqualTo(InstrumentSymbolRule.ContainsWhitespace));
    }

    [Test]
    public void From_WhenExchangeCodeContainsUnsupportedCharacters_ThrowsDomainRuleViolationException()
    {
        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => ExchangeCode.From("X/TEST"));

        Assert.That(exception!.Rule, Is.EqualTo(ExchangeCodeRule.InvalidCharacters));
    }
}
