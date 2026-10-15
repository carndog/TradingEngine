using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Application.WatchedInstruments.Delete;
using TradingEngine.Application.Tests.WatchedInstruments.Register;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Delete;

[TestFixture]
public sealed class BulkDeleteWatchedInstrumentsHandlerTests
{
    private static readonly Guid FirstId = Guid.Parse("b1000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondId = Guid.Parse("b1000000-0000-0000-0000-000000000002");

    [Test]
    public async Task HandleAsync_WithValidItems_PassesItemsAndClockInstantToStore()
    {
        Instant now = Instant.FromUtc(2026, 10, 5, 9, 30);
        FakeClock clock = new(now);
        CapturingWatchedInstrumentStore store = new();
        BulkDeleteWatchedInstrumentsHandler handler = new(clock, store);
        WatchedInstrumentDeleteItem[] items =
        [
            new(FirstId, [1, 2, 3]),
            new(SecondId, [4, 5, 6])
        ];

        Result<WatchedInstrumentBulkDeleteOutcome> result = await handler.HandleAsync(
            new BulkDeleteWatchedInstruments(items),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(store.RequestedDelete, Is.Not.Null);
            Assert.That(store.RequestedDelete!.Value.DeletedAt, Is.EqualTo(now));
            Assert.That(
                store.RequestedDelete.Value.Items.Select(item => item.InstrumentId),
                Is.EqualTo(new[] { FirstId, SecondId }));
            Assert.That(result.Value.DeletedIds, Is.EqualTo(new[] { FirstId, SecondId }));
            Assert.That(result.Value.AlreadyDeletedIds, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WithEmptyBatch_ReturnsValidationError()
    {
        CapturingWatchedInstrumentStore store = new();
        BulkDeleteWatchedInstrumentsHandler handler = new(SystemClock.Instance, store);

        Result<WatchedInstrumentBulkDeleteOutcome> result = await handler.HandleAsync(
            new BulkDeleteWatchedInstruments([]),
            CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error.Code, Is.EqualTo("watched_instrument.delete_items_required"));
            Assert.That(store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithOversizedBatch_ReturnsValidationError()
    {
        CapturingWatchedInstrumentStore store = new();
        BulkDeleteWatchedInstrumentsHandler handler = new(SystemClock.Instance, store);
        WatchedInstrumentDeleteItem[] items = Enumerable.Range(0, 101)
            .Select(index => new WatchedInstrumentDeleteItem(
                Guid.Parse($"b2000000-0000-0000-0000-{index:D12}"),
                [1]))
            .ToArray();

        Result<WatchedInstrumentBulkDeleteOutcome> result = await handler.HandleAsync(
            new BulkDeleteWatchedInstruments(items),
            CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error.Code, Is.EqualTo("watched_instrument.delete_items_too_large"));
            Assert.That(store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithDuplicateIds_ReturnsErrorIdentifyingIds()
    {
        CapturingWatchedInstrumentStore store = new();
        BulkDeleteWatchedInstrumentsHandler handler = new(SystemClock.Instance, store);
        WatchedInstrumentDeleteItem[] items =
        [
            new(FirstId, [1]),
            new(SecondId, [2]),
            new(FirstId, [3])
        ];

        Result<WatchedInstrumentBulkDeleteOutcome> result = await handler.HandleAsync(
            new BulkDeleteWatchedInstruments(items),
            CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error.Code, Is.EqualTo("watched_instrument.delete_items_duplicate"));
            Assert.That(
                result.Error.Extensions!["instrumentIds"],
                Is.EqualTo(new[] { FirstId }));
            Assert.That(store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithEmptyGuid_ReturnsValidationError()
    {
        CapturingWatchedInstrumentStore store = new();
        BulkDeleteWatchedInstrumentsHandler handler = new(SystemClock.Instance, store);
        WatchedInstrumentDeleteItem[] items =
        [
            new(FirstId, [1]),
            new(Guid.Empty, [2])
        ];

        Result<WatchedInstrumentBulkDeleteOutcome> result = await handler.HandleAsync(
            new BulkDeleteWatchedInstruments(items),
            CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error.Code, Is.EqualTo("watched_instrument.delete_items_invalid"));
            Assert.That(store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithMissingToken_ReturnsPreconditionErrorIdentifyingIds()
    {
        CapturingWatchedInstrumentStore store = new();
        BulkDeleteWatchedInstrumentsHandler handler = new(SystemClock.Instance, store);
        WatchedInstrumentDeleteItem[] items =
        [
            new(FirstId, [1]),
            new(SecondId, null)
        ];

        Result<WatchedInstrumentBulkDeleteOutcome> result = await handler.HandleAsync(
            new BulkDeleteWatchedInstruments(items),
            CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error.Code, Is.EqualTo("watched_instrument.delete_token_required"));
            Assert.That(
                result.Error.Extensions!["instrumentIds"],
                Is.EqualTo(new[] { SecondId }));
            Assert.That(store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenStoreFails_PropagatesError()
    {
        CapturingWatchedInstrumentStore store = new()
        {
            DeleteResult = WatchedInstrumentErrors.DeleteItemsStale
                .WithExtension("instrumentIds", new[] { SecondId })
        };
        BulkDeleteWatchedInstrumentsHandler handler = new(SystemClock.Instance, store);
        WatchedInstrumentDeleteItem[] items =
        [
            new(FirstId, [1]),
            new(SecondId, [2])
        ];

        Result<WatchedInstrumentBulkDeleteOutcome> result = await handler.HandleAsync(
            new BulkDeleteWatchedInstruments(items),
            CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("watched_instrument.delete_items_stale"));
    }
}
