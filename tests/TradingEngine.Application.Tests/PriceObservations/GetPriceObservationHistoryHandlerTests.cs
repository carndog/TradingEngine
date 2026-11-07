using NodaTime;
using TradingEngine.Application.PriceObservations;
using TradingEngine.Application.PriceObservations.Read;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.PriceObservations;

[TestFixture]
public sealed class GetPriceObservationHistoryHandlerTests
{
    private static readonly Guid InstrumentId = Guid.Parse("e1030000-0000-0000-0000-000000000001");

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(101)]
    public async Task HandleAsync_PageSizeOutOfRange_ReturnsValidationWithoutReading(int pageSize)
    {
        RecordingPriceObservationHistoryReader reader = new();
        GetPriceObservationHistoryHandler handler = new(reader);

        Result<PriceObservationHistory> result = await handler.HandleAsync(
            new GetPriceObservationHistory(InstrumentId, null, pageSize),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(PriceObservationErrors.PageSizeOutOfRange));
            Assert.That(reader.CallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task HandleAsync_CursorFinerThanStoredPrecision_ReturnsValidationWithoutReading()
    {
        RecordingPriceObservationHistoryReader reader = new();
        GetPriceObservationHistoryHandler handler = new(reader);
        Instant cursor = Instant.FromUtc(2026, 10, 30, 13, 45).PlusNanoseconds(1);

        Result<PriceObservationHistory> result = await handler.HandleAsync(
            new GetPriceObservationHistory(InstrumentId, cursor, 25),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(PriceObservationErrors.CursorInvalid));
            Assert.That(reader.CallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task HandleAsync_CursorBeforeStoredRange_ReturnsValidationWithoutReading()
    {
        RecordingPriceObservationHistoryReader reader = new();
        GetPriceObservationHistoryHandler handler = new(reader);
        Instant cursor = Instant.FromUtc(1, 1, 1, 0, 0).Minus(Duration.FromDays(1));

        Result<PriceObservationHistory> result = await handler.HandleAsync(
            new GetPriceObservationHistory(InstrumentId, cursor, 25),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(PriceObservationErrors.CursorInvalid));
            Assert.That(reader.CallCount, Is.EqualTo(0));
        });
    }

    [TestCase(1)]
    [TestCase(100)]
    public async Task HandleAsync_ValidInput_PassesQueryAndCancellationToReader(int pageSize)
    {
        RecordingPriceObservationHistoryReader reader = new();
        PriceObservationHistory expected = new(InstrumentId, null, [], pageSize, null, null);
        reader.Result = expected;
        GetPriceObservationHistoryHandler handler = new(reader);
        Instant cursor = Instant.FromUtc(2026, 10, 30, 13, 45).PlusTicks(1234567);
        using CancellationTokenSource source = new();

        Result<PriceObservationHistory> result = await handler.HandleAsync(
            new GetPriceObservationHistory(InstrumentId, cursor, pageSize),
            source.Token);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.SameAs(expected));
            Assert.That(reader.LastInstrumentId, Is.EqualTo(InstrumentId));
            Assert.That(reader.LastBefore, Is.EqualTo(cursor));
            Assert.That(reader.LastPageSize, Is.EqualTo(pageSize));
            Assert.That(reader.LastCancellationToken, Is.EqualTo(source.Token));
        });
    }

    [Test]
    public async Task HandleAsync_UnknownInstrument_ReturnsReaderNotFound()
    {
        RecordingPriceObservationHistoryReader reader = new();
        GetPriceObservationHistoryHandler handler = new(reader);

        Result<PriceObservationHistory> result = await handler.HandleAsync(
            new GetPriceObservationHistory(InstrumentId, null, 25),
            CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(PriceObservationErrors.InstrumentNotFound));
    }
}
