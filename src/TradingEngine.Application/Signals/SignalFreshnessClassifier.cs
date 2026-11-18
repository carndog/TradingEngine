using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Signals;

public sealed class SignalFreshnessClassifier(IMarketSessionPolicy sessions, PriceCollectionOptions? options = null)
{
    public string Classify(SignalEvidence signal, Instant cutoff)
    {
        if (signal.Quote is null)
        {
            return "missing";
        }

        int interval = signal.SamplingIntervalSeconds;
        if (interval < 1 || cutoff < Instant.FromUtc(2000, 1, 15, 0, 0)
            || (options is not null && options.SamplingPolicy != SamplingPolicyNames.PerInstrument))
        {
            return "unknown_cadence";
        }

        long seconds = cutoff.ToUnixTimeSeconds();
        Instant candidate = Instant.FromUnixTimeSeconds(seconds - seconds % interval - interval);
        Instant earliest = cutoff.Minus(Duration.FromDays(14));
        for (int checkedSlots = 0; candidate >= earliest && checkedSlots < 20160; checkedSlots++)
        {
            ExchangeSessionStatus status = sessions.GetStatus(signal.Exchange, candidate);
            if (status is ExchangeSessionStatus.Unconfigured or ExchangeSessionStatus.CalendarUnavailable)
            {
                return "unknown_calendar";
            }

            if (status == ExchangeSessionStatus.Open)
            {
                if (signal.Quote.ScheduledFor < candidate || signal.Quote.ObservedAt < candidate)
                {
                    return "stale_last_known";
                }

                return sessions.GetStatus(signal.Exchange, cutoff) switch
                {
                    ExchangeSessionStatus.Open => "current",
                    ExchangeSessionStatus.Closed => "latest_session",
                    _ => "unknown_calendar"
                };
            }

            candidate = candidate.Minus(Duration.FromSeconds(interval));
        }

        return "unknown_expected_collection";
    }
}
