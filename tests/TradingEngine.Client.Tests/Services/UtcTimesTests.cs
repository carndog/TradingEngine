namespace TradingEngine.Client.Tests.Services;

[TestFixture]
public sealed class UtcTimesTests
{
    [Test]
    public void ToIsoUtc_FractionalValue_PreservesSevenFractionalDigits()
    {
        DateTimeOffset value = DateTimeOffset.Parse("2026-10-07T10:11:12.1234567Z");

        string? iso = UtcTimes.ToIsoUtc(value);

        Assert.That(iso, Is.EqualTo("2026-10-07T10:11:12.1234567Z"));
    }

    [Test]
    public void ToInputValue_WholeSecondValue_OmitsFractionalDigits()
    {
        DateTimeOffset value = DateTimeOffset.Parse("2026-10-07T10:11:12Z");

        string input = UtcTimes.ToInputValue(value);

        Assert.That(input, Is.EqualTo("2026-10-07T10:11:12"));
    }

    [Test]
    public void ToInputValue_FractionalValue_PreservesFractionalDigits()
    {
        DateTimeOffset value = DateTimeOffset.Parse("2026-10-07T10:11:12.9999999Z");

        string input = UtcTimes.ToInputValue(value);

        Assert.That(input, Is.EqualTo("2026-10-07T10:11:12.9999999"));
    }

    [Test]
    public void TryParseInput_WhitespaceValue_ReturnsTrueWithNull()
    {
        bool parsed = UtcTimes.TryParseInput("   ", out DateTimeOffset? result);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(result, Is.Null);
        });
    }

    [Test]
    public void TryParseInput_MalformedValue_ReturnsFalse()
    {
        bool parsed = UtcTimes.TryParseInput("2026-13-99", out DateTimeOffset? result);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(result, Is.Null);
        });
    }

    [Test]
    public void TryParseInput_LocalStyleValue_InterpretsAsUtc()
    {
        bool parsed = UtcTimes.TryParseInput("2026-10-07T10:11:12", out DateTimeOffset? result);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(result, Is.EqualTo(DateTimeOffset.Parse("2026-10-07T10:11:12Z")));
        });
    }
}
