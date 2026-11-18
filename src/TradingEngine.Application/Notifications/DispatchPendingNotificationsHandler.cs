using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;

namespace TradingEngine.Application.Notifications;

public sealed class DispatchPendingNotificationsHandler
{
    private readonly INotificationIntentStore _store;
    private readonly IEmailSender _sender;
    private readonly RecommendationEmailRenderer _renderer;
    private readonly IClock _clock;
    private readonly NotificationOptions _options;

    public DispatchPendingNotificationsHandler(
        INotificationIntentStore store,
        IEmailSender sender,
        RecommendationEmailRenderer renderer,
        IClock clock,
        NotificationOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<DispatchRunResult> HandleAsync(
        DispatchPendingNotifications command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Instant startedAt = PersistedInstant.Require(
            _clock.GetCurrentInstant(),
            nameof(command));
        Guid invocationId = Guid.CreateVersion7();
        Instant now = _clock.GetCurrentInstant();

        IReadOnlyList<PendingNotification> intents =
            await _store.ListDispatchableAsync(
                _options.MaxIntentsPerRun,
                now,
                cancellationToken);

        List<IntentDispatchResult> items = [];
        EmailDeliveryDispatcher dispatcher = new(new NotificationDeliveryStoreAdapter(_store), _sender, _clock,
            _options.ClaimLeaseSeconds, _options.MaxDispatchAttempts);
        foreach (PendingNotification intent in intents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EmailDispatchResult result = await dispatcher.DispatchAsync(
                new EmailDeliveryWork(intent.IntentId, () => _renderer.Render(intent)), cancellationToken);
            items.Add(new IntentDispatchResult(intent.IntentId, intent.InstrumentId, intent.ScheduledFor,
                intent.MonitoringRuleRevisionId, intent.CollectionInvocationId, result.Outcome, result.ErrorKind));
        }

        return new DispatchRunResult(
            invocationId,
            startedAt,
            _clock.GetCurrentInstant(),
            items);
    }
}
