using TradingEngine.Domain.Results;

namespace TradingEngine.Application.PriceCollection;

public sealed record MarketSessionDecision(
    ExchangeSessionStatus Status,
    string? ReasonCode = null,
    string? Description = null,
    string? CalendarVersion = null,
    string? Notice = null)
{
    public Error? BlockingError
    {
        get
        {
            if (Status == ExchangeSessionStatus.Open)
            {
                return null;
            }

            if (Status == ExchangeSessionStatus.Unconfigured)
            {
                return PriceCollectionErrors.ExchangeSessionUnconfigured;
            }

            return Error.PreconditionFailed(
                ReasonCode ?? PriceCollectionErrors.ExchangeSessionUnconfigured.Code,
                Description ?? PriceCollectionErrors.ExchangeSessionUnconfigured.Description)
                .WithExtension("calendarVersion", CalendarVersion);
        }
    }

    public static MarketSessionDecision FromStatus(ExchangeSessionStatus status)
    {
        return status switch
        {
            ExchangeSessionStatus.Open => new(status),
            ExchangeSessionStatus.Closed => new(status, "price_collection.session_closed",
                "The exchange session is currently closed."),
            ExchangeSessionStatus.CalendarUnavailable => new(status, "price_collection.calendar_unavailable",
                "Verified exchange calendar coverage is unavailable."),
            _ => new(status, PriceCollectionErrors.ExchangeSessionUnconfigured.Code,
                PriceCollectionErrors.ExchangeSessionUnconfigured.Description)
        };
    }

    public static bool IsClosedError(string code)
    {
        return code is "price_collection.session_closed" or "price_collection.holiday"
            or "price_collection.exceptional_closure" or "price_collection.outside_replacement_session";
    }
}
