using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NodaTime;
using TradingEngine.Application.Listings;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerListingConfigurationStore : IListingConfigurationStore
{
    private readonly TradingEngineDbContext _context;

    public SqlServerListingConfigurationStore(TradingEngineDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Result<ListingConfigurationSnapshot>> FindAsync(
        string exchange,
        string symbol,
        CancellationToken cancellationToken)
    {
        ListingConfigurationRow? row = await _context.ListingConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Exchange == exchange && candidate.Symbol == symbol,
                cancellationToken);

        if (row is null)
        {
            return InstrumentListingErrors.NotFound;
        }

        return Snapshot(row);
    }

    public async Task<Result<ListingConfigurationSnapshot>> SaveAsync(
        ListingConfigurationUpsert upsert,
        byte[]? expectedConcurrencyToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upsert);

        ListingConfigurationRow? row = await _context.ListingConfigurations
            .SingleOrDefaultAsync(
                candidate => candidate.Exchange == upsert.Exchange
                    && candidate.Symbol == upsert.Symbol,
                cancellationToken);

        if (row is null)
        {
            return await InsertAsync(upsert, expectedConcurrencyToken, cancellationToken);
        }

        return await UpdateAsync(row, upsert, expectedConcurrencyToken, cancellationToken);
    }

    private async Task<Result<ListingConfigurationSnapshot>> InsertAsync(
        ListingConfigurationUpsert upsert,
        byte[]? expectedConcurrencyToken,
        CancellationToken cancellationToken)
    {
        if (expectedConcurrencyToken is not null)
        {
            return InstrumentListingErrors.ConcurrentChange;
        }

        Result<InstrumentListing> listing = InstrumentListing.Configure(
            upsert.Exchange,
            upsert.Symbol,
            upsert.Provider,
            upsert.ProviderInstrumentId,
            upsert.ProviderSymbol,
            upsert.ProviderExchangeName,
            upsert.DisplayName,
            upsert.NativeQuoteCurrency,
            upsert.Source,
            upsert.ConfirmedBy,
            PersistedInstant.Require(upsert.ConfirmedAt, nameof(upsert)));
        if (listing.IsFailure)
        {
            return listing.Error;
        }

        ListingConfigurationRow row = ToRow(listing.Value);
        _context.ListingConfigurations.Add(row);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return InstrumentListingErrors.ConcurrentChange;
        }

        return Snapshot(row);
    }

    private async Task<Result<ListingConfigurationSnapshot>> UpdateAsync(
        ListingConfigurationRow row,
        ListingConfigurationUpsert upsert,
        byte[]? expectedConcurrencyToken,
        CancellationToken cancellationToken)
    {
        if (expectedConcurrencyToken is null)
        {
            return InstrumentListingErrors.ConcurrencyTokenRequired;
        }

        if (row.RowVersion.SequenceEqual(expectedConcurrencyToken) is false)
        {
            return InstrumentListingErrors.ConcurrentChange;
        }

        if (string.Equals(
                row.NativeQuoteCurrency,
                upsert.NativeQuoteCurrency,
                StringComparison.Ordinal) is false)
        {
            return InstrumentListingErrors.UnitConflict
                .WithExtension("confirmedCurrency", row.NativeQuoteCurrency)
                .WithExtension("requestedCurrency", upsert.NativeQuoteCurrency);
        }

        Result<InstrumentListing> listing = Restore(row);
        Result refreshed = listing.Value.RefreshProviderMetadata(
            upsert.ProviderInstrumentId,
            upsert.ProviderSymbol,
            upsert.ProviderExchangeName,
            upsert.DisplayName,
            upsert.ConfirmedBy,
            PersistedInstant.Require(upsert.ConfirmedAt, nameof(upsert)));
        if (refreshed.IsFailure)
        {
            return refreshed.Error;
        }

        EntityEntry<ListingConfigurationRow> entry = _context.Entry(row);
        entry.Property(nameof(ListingConfigurationRow.RowVersion)).OriginalValue =
            expectedConcurrencyToken;

        InstrumentListing value = listing.Value;
        row.Provider = value.Provider;
        row.ProviderInstrumentId = value.ProviderInstrumentId;
        row.ProviderSymbol = value.ProviderSymbol;
        row.ProviderExchangeName = value.ProviderExchangeName;
        row.DisplayName = value.DisplayName;
        row.ConfirmedBy = value.ConfirmedBy;
        row.LastChangedAt = value.LastChangedAt;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return InstrumentListingErrors.ConcurrentChange;
        }

        return Snapshot(row);
    }

    private static ListingConfigurationRow ToRow(InstrumentListing listing)
    {
        return new ListingConfigurationRow
        {
            Exchange = listing.Exchange,
            Symbol = listing.Symbol,
            Provider = listing.Provider,
            ProviderInstrumentId = listing.ProviderInstrumentId,
            ProviderSymbol = listing.ProviderSymbol,
            ProviderExchangeName = listing.ProviderExchangeName,
            DisplayName = listing.DisplayName,
            NativeQuoteCurrency = listing.NativeQuoteCurrency,
            UnitSource = listing.Source.ToString(),
            ConfirmedBy = listing.ConfirmedBy,
            ConfirmedAt = listing.ConfirmedAt,
            LastChangedAt = listing.LastChangedAt
        };
    }

    private static Result<ListingConfigurationSnapshot> Snapshot(
        ListingConfigurationRow row)
    {
        Result<InstrumentListing> listing = Restore(row);
        return new ListingConfigurationSnapshot(listing.Value, row.RowVersion);
    }

    private static Result<InstrumentListing> Restore(ListingConfigurationRow row)
    {
        if (Enum.TryParse(row.UnitSource, out ListingUnitSource source) is false)
        {
            throw new InvalidDataException(
                $"The persisted listing unit source '{row.UnitSource}' for " +
                $"'{row.Exchange}/{row.Symbol}' is not a defined value.");
        }

        Result<InstrumentListing> listing = InstrumentListing.Restore(
            row.Exchange,
            row.Symbol,
            row.Provider,
            row.ProviderInstrumentId,
            row.ProviderSymbol,
            row.ProviderExchangeName,
            row.DisplayName,
            row.NativeQuoteCurrency,
            source,
            row.ConfirmedBy,
            row.ConfirmedAt,
            row.LastChangedAt);

        if (listing.IsFailure)
        {
            throw new InvalidDataException(
                $"The persisted listing configuration for " +
                $"'{row.Exchange}/{row.Symbol}' is invalid " +
                $"({listing.Error.Code}): {listing.Error.Description}");
        }

        return listing;
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }
}
