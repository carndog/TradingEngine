namespace TradingEngine.Api.Diagnostics;

internal sealed class TelemetrySanitizedException : Exception
{
    public TelemetrySanitizedException(
        string originalTypeName,
        string sanitizedMessage,
        string sanitizedDiagnosticText,
        Exception? sanitizedInnerException)
        : base(sanitizedMessage, sanitizedInnerException)
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
