using Azure;
using Azure.Communication.Email;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;
using EmailSendResult = TradingEngine.Application.Notifications.EmailSendResult;

namespace TradingEngine.Infrastructure.Notifications;

public sealed class AcsEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly EmailNotificationOptions _options;

    public AcsEmailSender(EmailNotificationOptions options)
        : this(new EmailClient(options.ConnectionString), options)
    {
    }

    internal AcsEmailSender(EmailClient client, EmailNotificationOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<EmailSendResult> SendAsync(
        RecommendationEmail email,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        EmailMessage message = new(
            _options.SenderAddress,
            new EmailRecipients(
                _options.RecipientAddresses
                    .Select(address => new EmailAddress(address))
                    .ToList()),
            new EmailContent(email.Subject)
            {
                PlainText = email.PlainTextBody
            });

        try
        {
            EmailSendOperation operation = await _client.SendAsync(
                WaitUntil.Started,
                message,
                operationId,
                cancellationToken);

            return new EmailSendResult(
                EmailSendOutcome.Accepted,
                operation.Id,
                null);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested is false)
        {
            return new EmailSendResult(
                EmailSendOutcome.Ambiguous,
                null,
                "timeout");
        }
        catch (RequestFailedException exception)
        {
            return new EmailSendResult(
                Classify(exception),
                null,
                ErrorKind(exception));
        }
        catch (Exception)
        {
            return new EmailSendResult(
                EmailSendOutcome.Ambiguous,
                null,
                "unexpected");
        }
    }

    public async Task<EmailOperationProbe> GetOperationStatusAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        try
        {
            EmailSendOperation operation = new(operationId, _client);
            await operation.UpdateStatusAsync(cancellationToken);

            string providerStatus = operation.HasValue
                ? operation.Value.Status.ToString()
                : "Running";

            return new EmailOperationProbe(
                MapStatus(operation),
                providerStatus,
                null);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return new EmailOperationProbe(
                EmailOperationStatus.Unknown,
                null,
                "operation_unknown");
        }
        catch (RequestFailedException exception) when (
            exception.Status is >= 200 and < 300)
        {
            return new EmailOperationProbe(
                EmailOperationStatus.Rejected,
                exception.ErrorCode ?? "Failed",
                "provider_rejected");
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested is false)
        {
            return new EmailOperationProbe(
                EmailOperationStatus.ProbeFailed,
                null,
                "timeout");
        }
        catch (RequestFailedException exception)
        {
            return new EmailOperationProbe(
                EmailOperationStatus.ProbeFailed,
                null,
                ErrorKind(exception));
        }
        catch (Exception)
        {
            return new EmailOperationProbe(
                EmailOperationStatus.ProbeFailed,
                null,
                "unexpected");
        }
    }

    private static EmailOperationStatus MapStatus(EmailSendOperation operation)
    {
        if (operation.HasCompleted is false)
        {
            return EmailOperationStatus.InFlight;
        }

        if (operation.HasValue is false)
        {
            return EmailOperationStatus.ProbeFailed;
        }

        EmailSendStatus status = operation.Value.Status;
        if (status == EmailSendStatus.Succeeded)
        {
            return EmailOperationStatus.Succeeded;
        }

        if (status == EmailSendStatus.Failed || status == EmailSendStatus.Canceled)
        {
            return EmailOperationStatus.Rejected;
        }

        return EmailOperationStatus.InFlight;
    }

    private static EmailSendOutcome Classify(RequestFailedException exception)
    {
        if (exception.Status == 0 || exception.Status >= 500)
        {
            return EmailSendOutcome.Ambiguous;
        }

        if (exception.Status is 408 or 429)
        {
            return EmailSendOutcome.TransientFailure;
        }

        return EmailSendOutcome.TerminalFailure;
    }

    private static string ErrorKind(RequestFailedException exception)
    {
        return exception.Status == 0
            ? "transport"
            : $"http_{exception.Status}";
    }
}
