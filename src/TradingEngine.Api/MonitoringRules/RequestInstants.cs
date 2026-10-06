using NodaTime;
using NodaTime.Text;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.MonitoringRules;

internal static class RequestInstants
{
    private static readonly OffsetDateTimePattern Pattern = OffsetDateTimePattern.ExtendedIso;

    internal static Result<Instant> Required(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MonitoringRuleErrors.InstantInvalid;
        }

        ParseResult<OffsetDateTime> parsed = Pattern.Parse(value.Trim());

        return parsed.Success
            ? Result<Instant>.Success(parsed.Value.ToInstant())
            : MonitoringRuleErrors.InstantInvalid;
    }

    internal static (Instant? Instant, Error? Error) Optional(string? value)
    {
        if (value is null)
        {
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, MonitoringRuleErrors.InstantInvalid);
        }

        Result<Instant> parsed = Required(value);

        return parsed.IsFailure
            ? (null, parsed.Error)
            : (parsed.Value, null);
    }
}
