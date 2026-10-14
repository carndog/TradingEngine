using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments.Delete;

public sealed class BulkDeleteWatchedInstrumentsHandler
{
    internal const int MaximumItems = 100;

    private readonly IClock _clock;
    private readonly IWatchedInstrumentStore _store;

    public BulkDeleteWatchedInstrumentsHandler(
        IClock clock,
        IWatchedInstrumentStore store)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<Result<WatchedInstrumentBulkDeleteOutcome>> HandleAsync(
        BulkDeleteWatchedInstruments command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Items.Count == 0)
        {
            return WatchedInstrumentErrors.DeleteItemsRequired;
        }

        if (command.Items.Count > MaximumItems)
        {
            return WatchedInstrumentErrors.DeleteItemsTooLarge;
        }

        Guid[] duplicateIds = command.Items
            .GroupBy(item => item.InstrumentId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            return WatchedInstrumentErrors.DeleteItemsDuplicate
                .WithExtension("instrumentIds", duplicateIds);
        }

        Guid[] invalidIds = command.Items
            .Where(item => item.InstrumentId == Guid.Empty)
            .Select(item => item.InstrumentId)
            .ToArray();
        if (invalidIds.Length > 0)
        {
            return WatchedInstrumentErrors.DeleteItemsInvalid;
        }

        Guid[] missingTokenIds = command.Items
            .Where(item => item.ExpectedConcurrencyToken is null
                || item.ExpectedConcurrencyToken.Length == 0)
            .Select(item => item.InstrumentId)
            .ToArray();
        if (missingTokenIds.Length > 0)
        {
            return WatchedInstrumentErrors.DeleteTokenRequired
                .WithExtension("instrumentIds", missingTokenIds);
        }

        Instant deletedAt = PersistedInstant.Require(
            _clock.GetCurrentInstant(),
            nameof(command));

        return await _store.DeleteInstrumentsAsync(
            command.Items,
            deletedAt,
            cancellationToken);
    }
}
