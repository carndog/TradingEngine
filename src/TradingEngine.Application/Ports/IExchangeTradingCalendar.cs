using NodaTime;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Ports;

public interface IExchangeTradingCalendar
{
    ExchangeTradingDay GetDay(string exchange, LocalDate date);
}
