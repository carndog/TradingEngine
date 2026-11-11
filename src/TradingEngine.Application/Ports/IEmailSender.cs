using TradingEngine.Application.Notifications;

namespace TradingEngine.Application.Ports;

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(
        RecommendationEmail email,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<EmailOperationProbe> GetOperationStatusAsync(
        string operationId,
        CancellationToken cancellationToken);
}
