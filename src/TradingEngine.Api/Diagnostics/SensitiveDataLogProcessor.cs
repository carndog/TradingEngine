using OpenTelemetry;
using OpenTelemetry.Logs;

namespace TradingEngine.Api.Diagnostics;

internal sealed class SensitiveDataLogProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord logRecord)
    {
        if (logRecord.FormattedMessage is not null
            && SensitiveTextRedactor.ContainsSensitiveValue(logRecord.FormattedMessage))
        {
            logRecord.FormattedMessage = SensitiveTextRedactor.Redact(logRecord.FormattedMessage);
        }

        if (logRecord.Body is not null
            && SensitiveTextRedactor.ContainsSensitiveValue(logRecord.Body))
        {
            logRecord.Body = SensitiveTextRedactor.Redact(logRecord.Body);
        }

        ScrubAttributes(logRecord);
        ScrubException(logRecord);
    }

    private static void ScrubAttributes(LogRecord logRecord)
    {
        IReadOnlyList<KeyValuePair<string, object?>>? attributes = logRecord.Attributes;
        if (attributes is null)
        {
            return;
        }

        bool changed = false;
        List<KeyValuePair<string, object?>> scrubbed = new(attributes.Count);
        foreach (KeyValuePair<string, object?> attribute in attributes)
        {
            if (SensitiveTextRedactor.IsSensitiveKey(attribute.Key))
            {
                scrubbed.Add(new KeyValuePair<string, object?>(
                    attribute.Key,
                    SensitiveTextRedactor.RedactedMarker));
                changed = true;
            }
            else if (attribute.Value is string text
                && SensitiveTextRedactor.ContainsSensitiveValue(text))
            {
                scrubbed.Add(new KeyValuePair<string, object?>(
                    attribute.Key,
                    SensitiveTextRedactor.Redact(text)));
                changed = true;
            }
            else
            {
                scrubbed.Add(attribute);
            }
        }

        if (changed)
        {
            logRecord.Attributes = scrubbed;
        }
    }

    private static void ScrubException(LogRecord logRecord)
    {
        if (logRecord.Exception is null
            || !SensitiveTextRedactor.ContainsSensitiveValue(logRecord.Exception.ToString()))
        {
            return;
        }

        logRecord.Exception = SanitizeException(logRecord.Exception);
    }

    private static TelemetrySanitizedException SanitizeException(Exception exception)
    {
        return new TelemetrySanitizedException(
            $"{exception.GetType().FullName}: {SensitiveTextRedactor.Redact(exception.Message)}",
            exception.InnerException is null ? null : SanitizeException(exception.InnerException));
    }
}
