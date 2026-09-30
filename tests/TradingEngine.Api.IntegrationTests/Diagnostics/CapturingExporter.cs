using OpenTelemetry;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed class CapturingExporter<T> : BaseExporter<T>
    where T : class
{
    private readonly List<T> _items = [];
    private readonly object _gate = new();

    public IReadOnlyList<T> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }
    }

    public override ExportResult Export(in Batch<T> batch)
    {
        lock (_gate)
        {
            foreach (T item in batch)
            {
                _items.Add(item);
            }
        }

        return ExportResult.Success;
    }
}
