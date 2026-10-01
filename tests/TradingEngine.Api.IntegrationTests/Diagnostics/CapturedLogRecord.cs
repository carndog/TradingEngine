using System.Diagnostics;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed record CapturedLogRecord(
    string? CategoryName,
    string? FormattedMessage,
    string? Body,
    IReadOnlyList<KeyValuePair<string, object?>>? Attributes,
    Exception? Exception,
    ActivityTraceId TraceId,
    ActivitySpanId SpanId);
