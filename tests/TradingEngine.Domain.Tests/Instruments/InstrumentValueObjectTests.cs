using TradingEngine.Domain.Instruments;

namespace TradingEngine.Domain.Tests.Instruments;

[TestFixture]
public sealed class InstrumentValueObjectTests
{
    [Test]
    public void WatchedInstrumentId_rejects_an_empty_identifier()
    {
        Assert.Throws<ArgumentException>(() => WatchedInstrumentId.From(Guid.Empty));
    }

    [TestCase("")]
    [TestCase("US")]
    [TestCase("EURO")]
    [TestCase("G8P")]
    public void CurrencyCode_rejects_an_invalid_value(string value)
    {
        Assert.Throws<ArgumentException>(() => CurrencyCode.From(value));
    }

    [Test]
    public void BrokerInstrumentCode_rejects_whitespace_within_the_code()
    {
        Assert.Throws<ArgumentException>(() => BrokerInstrumentCode.From("DEMO 1"));
    }

    [Test]
    public void ExchangeCode_rejects_unsupported_characters()
    {
        Assert.Throws<ArgumentException>(() => ExchangeCode.From("X/TEST"));
    }
}
