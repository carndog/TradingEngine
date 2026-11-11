using System.Net;
using Azure.Communication.Email;
using Azure.Core.Pipeline;
using TradingEngine.Application.Notifications;
using TradingEngine.Infrastructure.Notifications;
using EmailSendResult = TradingEngine.Application.Notifications.EmailSendResult;

namespace TradingEngine.Infrastructure.Tests.Notifications;

[TestFixture]
public sealed class AcsEmailSenderTests
{
    private const string OperationId = "b1c2d3e4-f5a6-4b7c-8d9e-0f1a2b3c4d5e";

    private ScriptedHttpMessageHandler _handler = null!;
    private AcsEmailSender _sender = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new ScriptedHttpMessageHandler();
        HttpClient httpClient = new(_handler);
        EmailClientOptions options = new()
        {
            Transport = new HttpClientTransport(httpClient)
        };
        EmailClient client = new(
            "endpoint=https://synthetic.communication.azure.com/;" +
            "accesskey=c3ludGhldGljLWtleQ==",
            options);
        _sender = new AcsEmailSender(
            client,
            new EmailNotificationOptions
            {
                ConnectionString = "synthetic",
                SenderAddress = "DoNotReply@synthetic.azurecomm.net",
                RecipientAddresses = ["devnull@synthetic.example"]
            });
    }

    [Test]
    public async Task SendAsync_WhenAccepted_ReturnsPersistedOperationId()
    {
        EnqueueSendAccepted();

        EmailSendResult result = await _sender.SendAsync(
            Email(),
            Guid.Parse(OperationId),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(EmailSendOutcome.Accepted));
            Assert.That(result.ProviderOperationId, Is.EqualTo(OperationId));
            Assert.That(SendRequestCount(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ProbeLifecycle_WhenRunningThenSucceeded_KeepsPollingWithoutResend()
    {
        EnqueueSendAccepted();
        EnqueueOperationStatus("Running");
        EnqueueOperationStatus("Succeeded");

        EmailSendResult send = await _sender.SendAsync(
            Email(),
            Guid.Parse(OperationId),
            CancellationToken.None);
        EmailOperationProbe running = await _sender.GetOperationStatusAsync(
            send.ProviderOperationId!,
            CancellationToken.None);
        EmailOperationProbe succeeded = await _sender.GetOperationStatusAsync(
            send.ProviderOperationId!,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(send.Outcome, Is.EqualTo(EmailSendOutcome.Accepted));
            Assert.That(running.Status, Is.EqualTo(EmailOperationStatus.InFlight));
            Assert.That(
                succeeded.Status,
                Is.EqualTo(EmailOperationStatus.Succeeded));
            Assert.That(SendRequestCount(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ProbeLifecycle_WhenAcceptedThenFailed_ResolvesRejectedWithoutResend()
    {
        EnqueueSendAccepted();
        EnqueueOperationStatus("Failed");

        EmailSendResult send = await _sender.SendAsync(
            Email(),
            Guid.Parse(OperationId),
            CancellationToken.None);
        EmailOperationProbe probe = await _sender.GetOperationStatusAsync(
            send.ProviderOperationId!,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(probe.Status, Is.EqualTo(EmailOperationStatus.Rejected));
            Assert.That(SendRequestCount(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ProbeLifecycle_WhenAcceptedThenCanceled_ResolvesRejectedWithoutResend()
    {
        EnqueueSendAccepted();
        EnqueueOperationStatus("Canceled");

        EmailSendResult send = await _sender.SendAsync(
            Email(),
            Guid.Parse(OperationId),
            CancellationToken.None);
        EmailOperationProbe probe = await _sender.GetOperationStatusAsync(
            send.ProviderOperationId!,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(probe.Status, Is.EqualTo(EmailOperationStatus.Rejected));
            Assert.That(SendRequestCount(), Is.EqualTo(1));
        });
    }

    private void EnqueueSendAccepted()
    {
        _handler.Enqueue(
            HttpStatusCode.Accepted,
            $"{{\"id\":\"{OperationId}\",\"status\":\"NotStarted\"}}");
    }

    private void EnqueueOperationStatus(string status)
    {
        _handler.Enqueue(
            HttpStatusCode.OK,
            $"{{\"id\":\"{OperationId}\",\"status\":\"{status}\"}}");
    }

    private int SendRequestCount()
    {
        return _handler.Requests.Count(
            request => request.Method == HttpMethod.Post);
    }

    private static RecommendationEmail Email()
    {
        return new RecommendationEmail(
            "synthetic subject",
            "synthetic body");
    }
}
