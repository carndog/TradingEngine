using NodaTime;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Tests.Instruments;

[TestFixture]
public sealed class WatchedInstrumentTests
{
    private static readonly Guid InstrumentId = Guid.Parse("1788c81b-2d9b-4686-ab26-b62685d7bda0");
    private static readonly Instant CreatedAt = Instant.FromUtc(2026, 1, 2, 9, 30);

    [Test]
    public void Create_WithValidValues_ReturnsConfiguredInstrument()
    {
        Result<WatchedInstrument> result = CreateInstrument();

        Assert.That(result.IsSuccess, Is.True);
        WatchedInstrument instrument = result.Value;

        Assert.Multiple(() =>
        {
            Assert.That(instrument.Id, Is.EqualTo(InstrumentId));
            Assert.That(instrument.Symbol, Is.EqualTo("DEMO-1"));
            Assert.That(instrument.Exchange, Is.EqualTo("XTEST"));
            Assert.That(instrument.QuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(instrument.MonitoringState, Is.EqualTo(MonitoringState.Configured));
            Assert.That(instrument.SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(instrument.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(instrument.LastChangedAt, Is.EqualTo(CreatedAt));
        });
    }

    [Test]
    public void Create_WithMixedCaseInputs_NormalizesToUppercase()
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            "  demo-1  ",
            "xtest",
            "gbp",
            60,
            CreatedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Symbol, Is.EqualTo("DEMO-1"));
            Assert.That(result.Value.Exchange, Is.EqualTo("XTEST"));
            Assert.That(result.Value.QuoteCurrency, Is.EqualTo("GBP"));
        });
    }

    [Test]
    public void Create_WithEmptyId_ReturnsIdRequiredError()
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            Guid.Empty,
            "DEMO-1",
            "XTEST",
            "GBP",
            60,
            CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.IdRequired));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithMissingSymbol_ReturnsSymbolRequiredError(string? symbol)
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            symbol,
            "XTEST",
            "GBP",
            60,
            CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.SymbolRequired));
    }

    [Test]
    public void Create_WithSymbolContainingWhitespace_ReturnsError()
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            "DEMO 1",
            "XTEST",
            "GBP",
            60,
            CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.SymbolContainsWhitespace));
    }

    [Test]
    public void Create_WithSymbolExceedingMaximumLength_ReturnsError()
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            new string('A', 65),
            "XTEST",
            "GBP",
            60,
            CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.SymbolExceedsMaximumLength));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithMissingExchange_ReturnsExchangeRequiredError(string? exchange)
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            "DEMO-1",
            exchange,
            "GBP",
            60,
            CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.ExchangeRequired));
    }

    [Test]
    public void Create_WithExchangeContainingUnsupportedCharacters_ReturnsError()
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            "DEMO-1",
            "X/TEST",
            "GBP",
            60,
            CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.ExchangeInvalidCharacters));
    }

    [Test]
    public void Create_WithExchangeExceedingMaximumLength_ReturnsError()
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            "DEMO-1",
            new string('X', 21),
            "GBP",
            60,
            CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.ExchangeExceedsMaximumLength));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithMissingQuoteCurrency_ReturnsQuoteCurrencyRequiredError(string? quoteCurrency)
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            "DEMO-1",
            "XTEST",
            quoteCurrency,
            60,
            CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.QuoteCurrencyRequired));
    }

    [TestCase("GBP")]
    [TestCase("USD")]
    [TestCase("USDT")]
    [TestCase("USDC")]
    public void Create_WithFiatOrCryptoQuoteCurrency_ReturnsSuccess(string quoteCurrency)
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            "DEMO-1",
            "XTEST",
            quoteCurrency,
            60,
            CreatedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.QuoteCurrency, Is.EqualTo(quoteCurrency));
    }

    [TestCase("US")]
    [TestCase("TOOLONGQUOTE1")]
    [TestCase("G-P")]
    [TestCase("G P")]
    public void Create_WithInvalidQuoteCurrency_ReturnsQuoteCurrencyInvalidFormatError(string quoteCurrency)
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            "DEMO-1",
            "XTEST",
            quoteCurrency,
            60,
            CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.QuoteCurrencyInvalidFormat));
    }

    [TestCase(1)]
    [TestCase(3600)]
    public void Create_WithSamplingIntervalWithinRange_ReturnsSuccess(int samplingIntervalSeconds)
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            "DEMO-1",
            "XTEST",
            "GBP",
            samplingIntervalSeconds,
            CreatedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.SamplingIntervalSeconds, Is.EqualTo(samplingIntervalSeconds));
    }

    [TestCase(0)]
    [TestCase(3601)]
    [TestCase(-1)]
    public void Create_WithSamplingIntervalOutsideRange_ReturnsError(int samplingIntervalSeconds)
    {
        Result<WatchedInstrument> result = WatchedInstrument.Create(
            InstrumentId,
            "DEMO-1",
            "XTEST",
            "GBP",
            samplingIntervalSeconds,
            CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.SamplingIntervalOutOfRange));
    }

    [Test]
    public void StartMonitoring_WhenConfigured_ChangesStateIntervalAndTimestamp()
    {
        WatchedInstrument instrument = CreateInstrument().Value;
        Instant changedAt = CreatedAt + Duration.FromMinutes(5);

        Result result = instrument.StartMonitoring(300, changedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(instrument.MonitoringState, Is.EqualTo(MonitoringState.Monitored));
            Assert.That(instrument.SamplingIntervalSeconds, Is.EqualTo(300));
            Assert.That(instrument.LastChangedAt, Is.EqualTo(changedAt));
        });
    }

    [Test]
    public void StartMonitoring_WhenAlreadyMonitored_ReturnsConflictError()
    {
        WatchedInstrument instrument = CreateInstrument().Value;
        instrument.StartMonitoring(60, CreatedAt);

        Result result = instrument.StartMonitoring(120, CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.AlreadyMonitored));
    }

    [Test]
    public void StartMonitoring_WhenTimestampPrecedesLatestChange_ReturnsError()
    {
        WatchedInstrument instrument = CreateInstrument().Value;

        Result result = instrument.StartMonitoring(300, CreatedAt - Duration.FromNanoseconds(1));

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.ChangePrecedesLatestChange));
    }

    [Test]
    public void StopMonitoring_WhenMonitored_ReturnsConfiguredState()
    {
        WatchedInstrument instrument = CreateInstrument().Value;
        Instant startedAt = CreatedAt + Duration.FromMinutes(1);
        Instant stoppedAt = startedAt + Duration.FromMinutes(1);
        instrument.StartMonitoring(60, startedAt);

        Result result = instrument.StopMonitoring(stoppedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(instrument.MonitoringState, Is.EqualTo(MonitoringState.Configured));
            Assert.That(instrument.LastChangedAt, Is.EqualTo(stoppedAt));
        });
    }

    [Test]
    public void StopMonitoring_WhenNotMonitored_ReturnsConflictError()
    {
        WatchedInstrument instrument = CreateInstrument().Value;

        Result result = instrument.StopMonitoring(CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.NotMonitored));
    }

    [Test]
    public void ChangeSamplingInterval_WhenDifferent_UpdatesIntervalAndTimestamp()
    {
        WatchedInstrument instrument = CreateInstrument().Value;
        Instant changedAt = CreatedAt + Duration.FromMinutes(1);

        Result result = instrument.ChangeSamplingInterval(120, changedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(instrument.SamplingIntervalSeconds, Is.EqualTo(120));
            Assert.That(instrument.LastChangedAt, Is.EqualTo(changedAt));
        });
    }

    [Test]
    public void ChangeSamplingInterval_WhenUnchanged_ReturnsConflictError()
    {
        WatchedInstrument instrument = CreateInstrument().Value;

        Result result = instrument.ChangeSamplingInterval(60, CreatedAt);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.SamplingIntervalUnchanged));
    }

    [Test]
    public void ChangeSamplingInterval_WhenTimestampPrecedesLatestChange_ReturnsError()
    {
        WatchedInstrument instrument = CreateInstrument().Value;

        Result result = instrument.ChangeSamplingInterval(
            120,
            CreatedAt - Duration.FromNanoseconds(1));

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.ChangePrecedesLatestChange));
    }

    private static Result<WatchedInstrument> CreateInstrument()
    {
        return WatchedInstrument.Create(
            InstrumentId,
            "demo-1",
            "xtest",
            "gbp",
            60,
            CreatedAt);
    }
}
