using OpenTelemetry;
using OpenTelemetry.Logs;

namespace TradingEngine.Api.Diagnostics;

internal sealed class SensitiveDataLogProcessor : BaseProcessor<LogRecord>
{
    private const string OriginalTypeAttribute = "exception.original_type";
    private const string SanitizedDetailsAttribute = "exception.sanitized_details";

    public override void OnEnd(LogRecord logRecord)
    {
        List<string> sensitiveValues = ScrubAttributes(logRecord);
        ScrubRenderedText(logRecord, sensitiveValues);
        ScrubException(logRecord, sensitiveValues);
    }

    private static List<string> ScrubAttributes(LogRecord logRecord)
    {
        List<string> sensitiveValues = [];
        IReadOnlyList<KeyValuePair<string, object?>>? attributes = logRecord.Attributes;
        if (attributes is null)
        {
            return sensitiveValues;
        }

        bool changed = false;
        List<KeyValuePair<string, object?>> scrubbed = new(attributes.Count);
        foreach (KeyValuePair<string, object?> attribute in attributes)
        {
            if (SensitiveTextRedactor.IsSensitiveKey(attribute.Key))
            {
                AddIfString(sensitiveValues, attribute.Value);
                scrubbed.Add(new KeyValuePair<string, object?>(
                    attribute.Key,
                    SensitiveTextRedactor.RedactedMarker));
                changed = true;
            }
            else if (attribute.Value is string text
                && SensitiveTextRedactor.ContainsSensitiveValue(text))
            {
                sensitiveValues.Add(text);
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

        return sensitiveValues;
    }

    private static void ScrubRenderedText(
        LogRecord logRecord,
        IReadOnlyCollection<string> sensitiveValues)
    {
        if (logRecord.FormattedMessage is not null
            && SensitiveTextRedactor.ContainsSensitiveValue(
                logRecord.FormattedMessage, sensitiveValues))
        {
            logRecord.FormattedMessage = SensitiveTextRedactor.Redact(
                logRecord.FormattedMessage, sensitiveValues);
        }

        if (logRecord.Body is not null
            && SensitiveTextRedactor.ContainsSensitiveValue(logRecord.Body, sensitiveValues))
        {
            logRecord.Body = SensitiveTextRedactor.Redact(logRecord.Body, sensitiveValues);
        }
    }

    private static void ScrubException(
        LogRecord logRecord,
        IReadOnlyCollection<string> sensitiveValues)
    {
        Exception? exception = logRecord.Exception;
        if (exception is null
            || SensitiveTextRedactor.ContainsSensitiveValue(
                exception.ToString(), sensitiveValues) is false)
        {
            return;
        }

        Exception sanitized = SanitizeException(exception, sensitiveValues);
        logRecord.Exception = sanitized;
        logRecord.Attributes = AppendSanitizedDetails(
            logRecord.Attributes,
            sanitized);
    }

    private static IReadOnlyList<KeyValuePair<string, object?>> AppendSanitizedDetails(
        IReadOnlyList<KeyValuePair<string, object?>>? attributes,
        Exception sanitized)
    {
        List<KeyValuePair<string, object?>> extended = attributes is null
            ? new List<KeyValuePair<string, object?>>()
            : new List<KeyValuePair<string, object?>>(attributes);

        string originalType = sanitized is TelemetrySanitizedException single
            ? single.OriginalTypeName
            : ((TelemetrySanitizedAggregateException)sanitized).OriginalTypeName;
        string diagnosticText = sanitized is TelemetrySanitizedException singleText
            ? singleText.SanitizedDiagnosticText
            : ((TelemetrySanitizedAggregateException)sanitized).SanitizedDiagnosticText;

        extended.Add(new KeyValuePair<string, object?>(OriginalTypeAttribute, originalType));
        extended.Add(new KeyValuePair<string, object?>(SanitizedDetailsAttribute, diagnosticText));

        return extended;
    }

    private static Exception SanitizeException(
        Exception exception,
        IReadOnlyCollection<string> sensitiveValues)
    {
        string sanitizedMessage = SensitiveTextRedactor.Redact(
            exception.Message,
            sensitiveValues);
        string sanitizedDiagnosticText = SensitiveTextRedactor.Redact(
            exception.ToString(),
            sensitiveValues);
        string originalTypeName = exception.GetType().FullName
            ?? exception.GetType().Name;

        if (exception is AggregateException aggregate)
        {
            List<Exception> sanitizedInner = [];
            foreach (Exception inner in aggregate.InnerExceptions)
            {
                sanitizedInner.Add(SanitizeException(inner, sensitiveValues));
            }

            return new TelemetrySanitizedAggregateException(
                originalTypeName,
                sanitizedMessage,
                sanitizedDiagnosticText,
                sanitizedInner);
        }

        Exception? sanitizedInnerException = exception.InnerException is null
            ? null
            : SanitizeException(exception.InnerException, sensitiveValues);

        return new TelemetrySanitizedException(
            originalTypeName,
            sanitizedMessage,
            sanitizedDiagnosticText,
            sanitizedInnerException);
    }

    private static void AddIfString(List<string> sensitiveValues, object? value)
    {
        if (value is string text && string.IsNullOrEmpty(text) is false)
        {
            sensitiveValues.Add(text);
        }
    }
}
