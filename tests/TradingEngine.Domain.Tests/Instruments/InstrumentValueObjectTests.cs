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
    public void From_WhenCurrencyCodeIsInvalid_ThrowsArgumentException(string value)
    {
        Assert.Throws<ArgumentException>(() => CurrencyCode.From(value));
    }

    [Test]
    public void From_WhenBrokerCodeContainsWhitespace_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => BrokerInstrumentCode.From("DEMO 1"));
    }

    [Test]
    public void From_WhenExchangeCodeContainsUnsupportedCharacters_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ExchangeCode.From("X/TEST"));
    }
}
