namespace TradingEngine.Api.Diagnostics;

internal sealed class TelemetrySanitizedAggregateException : AggregateException
{
    public TelemetrySanitizedAggregateException(
        string originalTypeName,
        string sanitizedMessage,
        string sanitizedDiagnosticText,
        IReadOnlyList<Exception> sanitizedInnerExceptions)
        : base(sanitizedMessage, sanitizedInnerExceptions)
    {
        OriginalTypeName = originalTypeName;
        SanitizedDiagnosticText = sanitizedDiagnosticText;
    }

    public string OriginalTypeName { get; }

    public string SanitizedDiagnosticText { get; }

    public override string ToString()
    {
        return SanitizedDiagnosticText;
    }
}
