using TradingEngine.Api.Diagnostics;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class SensitiveTextRedactorTests
{
    private const string SyntheticValue = "synthetic-credential-7e2a";

    [Test]
    public void ContainsSensitiveValue_WhenCredentialPairUnquoted_ReturnsTrue()
    {
        Assert.That(
            SensitiveTextRedactor.ContainsSensitiveValue($"password={SyntheticValue}"),
            Is.True);
    }

    [Test]
    public void ContainsSensitiveValue_WhenCredentialPairDoubleQuoted_ReturnsTrue()
    {
        Assert.That(
            SensitiveTextRedactor.ContainsSensitiveValue($"password=\"{SyntheticValue}\""),
            Is.True);
    }

    [Test]
    public void ContainsSensitiveValue_WhenCredentialPairSingleQuoted_ReturnsTrue()
    {
        Assert.That(
            SensitiveTextRedactor.ContainsSensitiveValue($"password='{SyntheticValue}'"),
            Is.True);
    }

    [Test]
    public void Redact_WhenCredentialPairDoubleQuoted_RemovesValueAndKeepsKey()
    {
        string redacted = SensitiveTextRedactor.Redact(
            $"connection failed: password=\"{SyntheticValue}\"; host=db.example.test");

        Assert.Multiple(() =>
        {
            Assert.That(redacted, Does.Not.Contain(SyntheticValue));
            Assert.That(redacted, Does.Contain("password"));
            Assert.That(redacted, Does.Contain("host=db.example.test"));
        });
    }

    [Test]
    public void Redact_WhenCredentialPairSingleQuoted_RemovesValue()
    {
        string redacted = SensitiveTextRedactor.Redact($"api_key='{SyntheticValue}'");

        Assert.That(redacted, Does.Not.Contain(SyntheticValue));
    }

    [Test]
    public void Redact_WhenAuthorizationBearerHeader_RemovesToken()
    {
        string redacted = SensitiveTextRedactor.Redact(
            $"Authorization: Bearer {SyntheticValue}");

        Assert.Multiple(() =>
        {
            Assert.That(redacted, Does.Not.Contain(SyntheticValue));
            Assert.That(redacted, Does.Contain("Bearer"));
        });
    }

    [Test]
    public void Redact_WhenNoSensitiveContent_LeavesTextUnchanged()
    {
        string safe = "Persisted chart-analysis definition rejected.";

        Assert.That(SensitiveTextRedactor.Redact(safe), Is.EqualTo(safe));
    }
}
