using System.Diagnostics;
using TradingEngine.Api.Diagnostics;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class SensitiveDataTelemetryProcessorTests
{
    private SensitiveDataTelemetryProcessor _processor = null!;

    [SetUp]
    public void SetUp()
    {
        _processor = new SensitiveDataTelemetryProcessor();
    }

    [Test]
    public void OnEnd_WhenUrlFullContainsQueryString_RemovesQueryValues()
    {
        Activity activity = new("GET /api/watched-instruments");
        activity.SetTag(
            "url.full",
            "https://example.test/api/watched-instruments?probe=synthetic-secret&page=2");

        _processor.OnEnd(activity);

        Assert.That(
            activity.GetTagItem("url.full"),
            Is.EqualTo("https://example.test/api/watched-instruments"));
    }

    [Test]
    public void OnEnd_WhenHttpUrlContainsFragment_RemovesFragment()
    {
        Activity activity = new("GET /health");
        activity.SetTag("http.url", "https://example.test/health#synthetic-fragment");

        _processor.OnEnd(activity);

        Assert.That(
            activity.GetTagItem("http.url"),
            Is.EqualTo("https://example.test/health"));
    }

    [Test]
    public void OnEnd_WhenSqlStatementTagsPresent_RemovesStatementText()
    {
        Activity activity = new("SQL SELECT");
        activity.SetTag(
            "db.statement",
            "SELECT Id, Symbol FROM WatchedInstruments WHERE Symbol = @symbol");
        activity.SetTag("db.query.text", "SELECT Id FROM WatchedInstruments");

        _processor.OnEnd(activity);

        Assert.Multiple(() =>
        {
            Assert.That(activity.GetTagItem("db.statement"), Is.Null);
            Assert.That(activity.GetTagItem("db.query.text"), Is.Null);
        });
    }

    [Test]
    public void OnEnd_WhenRequestHeaderTagsPresent_RemovesHeaderValues()
    {
        Activity activity = new("GET /health/database");
        activity.SetTag("http.request.header.authorization", "Bearer synthetic-token");
        activity.SetTag("http.request.header.x-database-probe-key", "synthetic-probe-key");
        activity.SetTag("http.request.header.cookie", "synthetic-cookie=value");

        _processor.OnEnd(activity);

        Assert.Multiple(() =>
        {
            Assert.That(activity.GetTagItem("http.request.header.authorization"), Is.Null);
            Assert.That(activity.GetTagItem("http.request.header.x-database-probe-key"), Is.Null);
            Assert.That(activity.GetTagItem("http.request.header.cookie"), Is.Null);
        });
    }

    [Test]
    public void OnEnd_WhenTagsContainNoSensitiveData_LeavesTelemetryIntact()
    {
        Activity activity = new("GET /health");
        activity.SetTag("url.full", "https://example.test/health");
        activity.SetTag("url.query", string.Empty);
        activity.SetTag("http.request.method", "GET");
        activity.SetTag("url.path", "/health");
        activity.SetTag("db.system", "mssql");

        _processor.OnEnd(activity);

        Assert.Multiple(() =>
        {
            Assert.That(
                activity.GetTagItem("url.full"),
                Is.EqualTo("https://example.test/health"));
            Assert.That(activity.GetTagItem("http.request.method"), Is.EqualTo("GET"));
            Assert.That(activity.GetTagItem("url.path"), Is.EqualTo("/health"));
            Assert.That(activity.GetTagItem("db.system"), Is.EqualTo("mssql"));
        });
    }
}
