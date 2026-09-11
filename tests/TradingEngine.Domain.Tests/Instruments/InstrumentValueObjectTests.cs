using TradingEngine.Domain.Instruments;

namespace TradingEngine.Domain.Tests.Instruments;

[TestFixture]
public sealed class InstrumentValueObjectTests
{
    [Test]
    public void From_WhenWatchedInstrumentIdIsEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => WatchedInstrumentId.From(Guid.Empty));
    }

    [TestCase("")]
    [TestCase("US")]
    [TestCase("EURO")]
    [TestCase("G8P")]
    public void From_WhenQuoteCurrencyCodeIsInvalid_ThrowsArgumentException(string value)
    {
        Assert.Throws<ArgumentException>(() => QuoteCurrencyCode.From(value));
    }

    [Test]
    public void From_WhenSymbolContainsWhitespace_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => InstrumentSymbol.From("DEMO 1"));
    }

    [Test]
    public void From_WhenExchangeCodeContainsUnsupportedCharacters_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ExchangeCode.From("X/TEST"));
    }
}
