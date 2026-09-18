using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Tests.Results;

[TestFixture]
public sealed class ResultTests
{
    [Test]
    public void Success_WhenCreated_ExposesNoError()
    {
        Result result = Result.Success();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsFailure, Is.False);
        });
    }

    [Test]
    public void Error_WhenResultIsSuccessful_ThrowsInvalidOperationException()
    {
        Result result = Result.Success();

        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Test]
    public void Failure_WhenCreated_ExposesError()
    {
        Error error = Error.Validation("test.code", "A test failure.");

        Result result = Result.Failure(error);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(error));
        });
    }

    [Test]
    public void Failure_WhenErrorIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Test]
    public void SuccessOfT_WhenCreated_ExposesNonNullValue()
    {
        Result<string> result = Result<string>.Success("value");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsFailure, Is.False);
            Assert.That(result.Value, Is.EqualTo("value"));
        });
    }

    [Test]
    public void SuccessOfT_WhenValueIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string>.Success(null!));
    }

    [Test]
    public void FailureOfT_WhenCreated_ExposesError()
    {
        Error error = Error.Conflict("test.conflict", "A conflicting failure.");

        Result<string> result = Result<string>.Failure(error);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(error));
        });
    }

    [Test]
    public void Value_WhenResultIsFailed_ThrowsInvalidOperationException()
    {
        Result<string> result = Result<string>.Failure(
            Error.Validation("test.code", "A test failure."));

        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Test]
    public void Error_WhenResultOfTIsSuccessful_ThrowsInvalidOperationException()
    {
        Result<string> result = Result<string>.Success("value");

        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Test]
    public void Error_WhenCreated_PreservesStableCodeTypeAndDescription()
    {
        Error validation = Error.Validation("test.validation", "A validation failure.");
        Error conflict = Error.Conflict("test.conflict", "A conflict failure.");
        Error notFound = Error.NotFound("test.not_found", "A not-found failure.");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Code, Is.EqualTo("test.validation"));
            Assert.That(validation.Description, Is.EqualTo("A validation failure."));
            Assert.That(validation.Type, Is.EqualTo(ErrorType.Validation));
            Assert.That(conflict.Type, Is.EqualTo(ErrorType.Conflict));
            Assert.That(notFound.Type, Is.EqualTo(ErrorType.NotFound));
        });
    }
}
