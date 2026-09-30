using OpenTelemetry;
using OpenTelemetry.Logs;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed class CapturingLogRecordExporter : BaseExporter<LogRecord>
{
    private readonly List<CapturedLogRecord> _items = [];
    private readonly object _gate = new();

    public IReadOnlyList<CapturedLogRecord> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }
    }

    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        lock (_gate)
        {
            foreach (LogRecord record in batch)
            {
                _items.Add(new CapturedLogRecord(
                    record.CategoryName,
                    record.FormattedMessage,
                    record.Body,
                    record.Attributes is null
                        ? null
                        : record.Attributes.ToArray(),
                    record.Exception,
                    record.TraceId,
                    record.SpanId));
            }
        }

        return ExportResult.Success;
    }
}
