using Microsoft.Extensions.Logging;
using OpenTelemetry;
using TradingEngine.Api.Diagnostics;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class SensitiveDataLogProcessorTests
{
    private const string SyntheticCredential = "synthetic-credential-7e2a";
    private const string Category = "TradingEngine.Tests.SensitiveDataLog";

    private CapturingLogRecordExporter _exported = null!;
    private ILoggerFactory _loggerFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _exported = new CapturingLogRecordExporter();
        CapturingLogRecordExporter exported = _exported;
        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.ParseStateValues = true;
                options
                    .AddProcessor(new SensitiveDataLogProcessor())
                    .AddProcessor(new SimpleLogRecordExportProcessor(exported));
            }));
    }

    [TearDown]
    public void TearDown()
    {
        _loggerFactory.Dispose();
    }

    [Test]
    public void OnEnd_WhenSensitiveAttributeValueRendersInFormattedMessage_RedactsRenderedMessageAndBody()
    {
        ILogger logger = _loggerFactory.CreateLogger(Category);

        logger.LogError("Probe credential {Token}", SyntheticCredential);

        CapturedLogRecord record = LastRecord();

        Assert.Multiple(() =>
        {
            Assert.That(record.FormattedMessage, Does.Not.Contain(SyntheticCredential));
            Assert.That(record.FormattedMessage, Does.Contain("Probe credential"));
            Assert.That(record.Body, Does.Not.Contain(SyntheticCredential));
            Assert.That(
                record.Attributes!.Any(attribute =>
                    attribute.Key == "Token"
                    && attribute.Value?.ToString() == SensitiveTextRedactor.RedactedMarker),
                Is.True);
        });
    }

    [Test]
    public void OnEnd_WhenMessageContainsQuotedCredential_RedactsRenderedMessage()
    {
        ILogger logger = _loggerFactory.CreateLogger(Category);

        logger.LogError($"connection failed: password=\"{SyntheticCredential}\"");

        CapturedLogRecord record = LastRecord();

        Assert.That(record.FormattedMessage, Does.Not.Contain(SyntheticCredential));
    }

    [Test]
    public void OnEnd_WhenThrownSensitiveExceptionLogged_PreservesTypeNameAndOriginalFrames()
    {
        ILogger logger = _loggerFactory.CreateLogger(Category);
        InvalidOperationException original = ThrowSensitiveInvalidOperation();

        logger.LogError(original, "Diagnostics exception probe.");

        CapturedLogRecord record = LastRecord();

        Assert.Multiple(() =>
        {
            Assert.That(record.Exception, Is.TypeOf<TelemetrySanitizedException>());
            Assert.That(record.Exception, Is.Not.SameAs(original));
            Assert.That(record.Exception!.Message, Does.Not.Contain(SyntheticCredential));
            Assert.That(record.Exception.ToString(), Does.Contain(nameof(InvalidOperationException)));
            Assert.That(record.Exception.ToString(), Does.Contain(nameof(ThrowSensitiveInvalidOperation)));
            Assert.That(record.Exception.ToString(), Does.Not.Contain(SyntheticCredential));
        });
    }

    [Test]
    public void OnEnd_WhenAggregateHasMultipleSensitiveBranches_PreservesEveryBranch()
    {
        ILogger logger = _loggerFactory.CreateLogger(Category);
        AggregateException original = ThrowSensitiveAggregate();

        logger.LogError(original, "Diagnostics aggregate probe.");

        CapturedLogRecord record = LastRecord();
        Exception? sanitized = record.Exception;

        Assert.Multiple(() =>
        {
            Assert.That(sanitized, Is.InstanceOf<AggregateException>());
            Assert.That(sanitized, Is.Not.SameAs(original));
            Assert.That(
                (sanitized as AggregateException)!.InnerExceptions,
                Has.Count.EqualTo(2));
            Assert.That(sanitized.ToString(), Does.Contain(nameof(InvalidOperationException)));
            Assert.That(sanitized.ToString(), Does.Contain(nameof(ArgumentException)));
            Assert.That(sanitized.ToString(), Does.Not.Contain(SyntheticCredential));
        });
    }

    [Test]
    public void OnEnd_WhenExceptionSafe_PassesThroughUnmodified()
    {
        ILogger logger = _loggerFactory.CreateLogger(Category);
        InvalidOperationException original = ThrowSafeInvalidOperation();

        logger.LogError(original, "Diagnostics safe exception probe.");

        CapturedLogRecord record = LastRecord();

        Assert.Multiple(() =>
        {
            Assert.That(record.Exception, Is.SameAs(original));
            Assert.That(record.Exception!.StackTrace, Does.Contain(nameof(ThrowSafeInvalidOperation)));
        });
    }

    [Test]
    public void OnEnd_WhenExceptionSanitized_DoesNotMutateOriginalException()
    {
        ILogger logger = _loggerFactory.CreateLogger(Category);
        InvalidOperationException original = ThrowSensitiveInvalidOperation();

        logger.LogError(original, "Diagnostics exception probe.");
        _ = LastRecord();

        Assert.Multiple(() =>
        {
            Assert.That(original.Message, Does.Contain(SyntheticCredential));
            Assert.That(original.StackTrace, Does.Contain(nameof(ThrowSensitiveInvalidOperation)));
        });
    }

    private static InvalidOperationException ThrowSensitiveInvalidOperation()
    {
        try
        {
            throw new InvalidOperationException(
                $"authentication failed, token={SyntheticCredential}");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private static AggregateException ThrowSensitiveAggregate()
    {
        try
        {
            throw new AggregateException(
                $"batch failed, apikey={SyntheticCredential}",
                new InvalidOperationException($"first branch token={SyntheticCredential}"),
                new ArgumentException($"second branch secret={SyntheticCredential}"));
        }
        catch (AggregateException exception)
        {
            return exception;
        }
    }

    private static InvalidOperationException ThrowSafeInvalidOperation()
    {
        try
        {
            throw new InvalidOperationException("safe diagnostic failure");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private CapturedLogRecord LastRecord()
    {
        return _exported.Items.Last(record => record.CategoryName == Category);
    }
}
