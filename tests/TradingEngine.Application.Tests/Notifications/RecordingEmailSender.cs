using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;

namespace TradingEngine.Application.Tests.Notifications;

internal sealed class RecordingEmailSender : IEmailSender
{
    public List<RecordedSend> Sends { get; } = [];

    public List<string> Probes { get; } = [];

    public Queue<EmailSendResult> SendResults { get; } = new();

    public Queue<EmailOperationProbe> ProbeResults { get; } = new();

    public Func<RecommendationEmail, Guid, Task<EmailSendResult>>? SendHook { get; set; }

    public Task<EmailSendResult> SendAsync(
        RecommendationEmail email,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        Sends.Add(new RecordedSend(email, operationId));
        if (SendHook is not null)
        {
            return SendHook(email, operationId);
        }

        EmailSendResult result = SendResults.Count > 0
            ? SendResults.Dequeue()
            : new EmailSendResult(EmailSendOutcome.Accepted, operationId.ToString("D"), null);

        return Task.FromResult(result);
    }

    public Task<EmailOperationProbe> GetOperationStatusAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        Probes.Add(operationId);
        EmailOperationProbe probe = ProbeResults.Count > 0
            ? ProbeResults.Dequeue()
            : new EmailOperationProbe(EmailOperationStatus.Unknown, null, null);

        return Task.FromResult(probe);
    }
}
