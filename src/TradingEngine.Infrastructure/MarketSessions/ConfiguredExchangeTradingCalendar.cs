using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NodaTime;
using NodaTime.Text;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Infrastructure.MarketSessions;

public sealed class ConfiguredExchangeTradingCalendar : IExchangeTradingCalendar, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfiguredExchangeTradingCalendar> _logger;
    private readonly IDisposable _subscription;
    private readonly System.Threading.Lock _gate = new();
    private readonly Dictionary<string, string> _lastWarnings = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, ExchangeCalendarDefinition> _definitions;
    private bool _refreshFailed;

    public ConfiguredExchangeTradingCalendar(
        IConfiguration configuration, ILogger<ConfiguredExchangeTradingCalendar> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _definitions = Load();
        LogVersions(_definitions);
        _subscription = ChangeToken.OnChange(configuration.GetReloadToken, Reload);
    }

    public ExchangeTradingDay GetDay(string exchange, LocalDate date)
    {
        lock (_gate)
        {
            if (_definitions.TryGetValue(exchange, out ExchangeCalendarDefinition? calendar) is false)
            {
                return new ExchangeTradingDay(TradingDayKind.Unavailable, "No verified calendar is configured.");
            }

            string? notice = _refreshFailed ? "Calendar refresh failed; retaining the last verified dataset." : null;
            if (date < calendar.Start || date > calendar.End)
            {
                Warn(exchange, calendar.Version, date, "Calendar coverage is unavailable for this date.");
                return new ExchangeTradingDay(TradingDayKind.Unavailable,
                    "The date is outside verified calendar coverage.", calendar.Version, calendar.TimeZone, Notice: notice);
            }

            if (Period.Between(date, calendar.End, PeriodUnits.Days).Days <= 60)
            {
                notice = $"Calendar coverage ends on {LocalDatePattern.Iso.Format(calendar.End)}. {notice}".Trim();
                Warn(exchange, calendar.Version, date, notice);
            }

            ExchangeTradingDay day = calendar.Days.TryGetValue(date, out ExchangeTradingDay? exception)
                ? exception
                : new ExchangeTradingDay(TradingDayKind.Normal, "normal", calendar.Version, calendar.TimeZone);
            return day with { Notice = notice };
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }

    private void Warn(string exchange, string version, LocalDate date, string notice)
    {
        string key = $"{version}:{LocalDatePattern.Iso.Format(date)}:{notice}";
        if (_lastWarnings.GetValueOrDefault(exchange) != key)
        {
            _lastWarnings[exchange] = key;
            _logger.LogWarning("Exchange calendar {Exchange} version {CalendarVersion}: {CalendarNotice}",
                exchange, version, notice);
        }
    }

    private void LogVersions(IReadOnlyDictionary<string, ExchangeCalendarDefinition> definitions)
    {
        foreach (KeyValuePair<string, ExchangeCalendarDefinition> entry in definitions)
        {
            _logger.LogInformation("Exchange calendar {Exchange} loaded version {CalendarVersion}, coverage {Start} through {End}.",
                entry.Key, entry.Value.Version, entry.Value.Start, entry.Value.End);
        }
    }

    private void Reload()
    {
        try
        {
            IReadOnlyDictionary<string, ExchangeCalendarDefinition> replacement = Load();
            lock (_gate)
            {
                _definitions = replacement;
                _refreshFailed = false;
                _lastWarnings.Clear();
            }

            LogVersions(replacement);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException)
        {
            lock (_gate)
            {
                _refreshFailed = true;
            }

            _logger.LogError("Exchange calendar refresh rejected; the last verified dataset remains in use within its coverage.");
        }
    }

    private IReadOnlyDictionary<string, ExchangeCalendarDefinition> Load()
    {
        string? configured = _configuration["TradingCalendars:DefinitionsJson"];
        List<ExchangeCalendarOptions> options;
        if (configured is null)
        {
            using Stream stream = typeof(ConfiguredExchangeTradingCalendar).Assembly.GetManifestResourceStream(
                "TradingEngine.Infrastructure.MarketSessions.exchange-calendars.json")
                ?? throw new InvalidDataException("The packaged exchange calendar dataset is missing.");
            options = JsonSerializer.Deserialize<List<ExchangeCalendarOptions>>(stream, JsonOptions)
                ?? throw new InvalidDataException("The exchange calendar dataset is empty.");
        }
        else
        {
            options = JsonSerializer.Deserialize<List<ExchangeCalendarOptions>>(configured, JsonOptions)
                ?? throw new InvalidDataException("The exchange calendar dataset is empty.");
        }

        if (options.Count == 0 || options.Any(option => option is null))
        {
            throw new InvalidDataException("The exchange calendar dataset requires non-null definitions.");
        }

        Dictionary<string, ExchangeCalendarDefinition> definitions = new(StringComparer.OrdinalIgnoreCase);
        foreach (ExchangeCalendarOptions option in options)
        {
            if (string.IsNullOrWhiteSpace(option.Exchange) || string.IsNullOrWhiteSpace(option.Version)
                || option.Version.Length > 80 || DateTimeZoneProviders.Tzdb.GetZoneOrNull(option.TimeZone) is null
                || option.Sources.Count == 0 || option.Sources.Any(source =>
                    Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) is false || uri.Scheme != "https"))
            {
                throw new InvalidDataException("Calendar identity, version, time zone and HTTPS source metadata are required.");
            }

            LocalDate start = ParseDate(option.CoverageStart);
            LocalDate end = ParseDate(option.CoverageEnd);
            ParseDate(option.VerifiedOn);
            if (end < start)
            {
                throw new InvalidDataException("Calendar coverage must have an ordered date range.");
            }

            Dictionary<LocalDate, ExchangeTradingDay> days = ParseDays(option.Exceptions, option, start, end, false);
            foreach (KeyValuePair<LocalDate, ExchangeTradingDay> entry in ParseDays(option.Overrides, option, start, end, true))
            {
                days[entry.Key] = entry.Value;
            }

            if (definitions.TryAdd(option.Exchange.Trim().ToUpperInvariant(),
                new ExchangeCalendarDefinition(option.Version, option.TimeZone, start, end, days)) is false)
            {
                throw new InvalidDataException("An exchange calendar is configured more than once.");
            }
        }

        return definitions;
    }

    private static Dictionary<LocalDate, ExchangeTradingDay> ParseDays(
        IReadOnlyCollection<CalendarDayOptions> options, ExchangeCalendarOptions calendar,
        LocalDate start, LocalDate end, bool overrides)
    {
        if (options.Any(option => option is null))
        {
            throw new InvalidDataException("Calendar day entries cannot be null.");
        }

        Dictionary<LocalDate, ExchangeTradingDay> days = [];
        foreach (CalendarDayOptions option in options)
        {
            LocalDate date = ParseDate(option.Date);
            if (date < start || date > end || string.IsNullOrWhiteSpace(option.Reason)
                || (option.Closed && option.Windows.Count > 0)
                || (option.Closed is false && option.Windows.Count == 0)
                || option.Windows.Any(window => window is null))
            {
                throw new InvalidDataException("Calendar exceptions require a covered date, reason and either closure or windows.");
            }

            List<TradingDayWindow> windows = option.Windows.Select(ParseWindow).OrderBy(window => window.Open).ToList();
            for (int index = 1; index < windows.Count; index++)
            {
                if (windows[index].Open < windows[index - 1].Close)
                {
                    throw new InvalidDataException("Calendar replacement windows must not overlap.");
                }
            }

            ExchangeTradingDay day = new(option.Closed ? TradingDayKind.Closed : TradingDayKind.Replacement,
                overrides && option.Closed ? "exceptional_closure" : option.Reason,
                calendar.Version, calendar.TimeZone, windows.AsReadOnly());
            if (days.TryAdd(date, day) is false)
            {
                throw new InvalidDataException("A calendar date has duplicate or conflicting exceptions.");
            }
        }

        return days;
    }

    private static TradingDayWindow ParseWindow(CalendarWindowOptions option)
    {
        ParseResult<LocalTime> open = LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(option.Open);
        ParseResult<LocalTime> close = LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(option.Close);
        if (open.Success is false || close.Success is false || open.Value >= close.Value)
        {
            throw new InvalidDataException("Replacement windows must be ordered within one exchange-local date.");
        }

        return new TradingDayWindow(open.Value, close.Value);
    }

    private static LocalDate ParseDate(string value)
    {
        ParseResult<LocalDate> parsed = LocalDatePattern.Iso.Parse(value);
        if (parsed.Success is false)
        {
            throw new InvalidDataException("Calendar dates must use valid ISO local dates.");
        }

        return parsed.Value;
    }
}
