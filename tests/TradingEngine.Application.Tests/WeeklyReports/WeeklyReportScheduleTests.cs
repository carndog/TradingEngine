using NodaTime;
using TradingEngine.Application.WeeklyReports;

namespace TradingEngine.Application.Tests.WeeklyReports;

[TestFixture]
public sealed class WeeklyReportScheduleTests
{
    [TestCase(3, 22, 18, 3, 29, 17, 167)]
    [TestCase(10, 18, 17, 10, 25, 18, 169)]
    public void NextAfter_ClockChange_KeepsLocalSundayEvening(int month, int day, int hour, int nextMonth, int nextDay, int nextHour, int hours)
    {
        WeeklyReportSchedule schedule = new(new WeeklyReportOptions());
        Instant now = Instant.FromUtc(2026, month, day, hour, 0);
        Instant next = schedule.NextAfter(now);

        Assert.That(next, Is.EqualTo(Instant.FromUtc(2026, nextMonth, nextDay, nextHour, 0)));
        Assert.That((next - now).TotalHours, Is.EqualTo(hours));
        Assert.That(schedule.LatestOnOrBefore(next), Is.EqualTo(next));
        Assert.That(schedule.PeriodEndingAt(next).StartsAt, Is.EqualTo(now));
    }

    [TestCase(3, 29, 1, 30)]
    [TestCase(10, 25, 0, 30)]
    public void NextAfter_GapOrOverlap_UsesLenientLocalMapping(int month, int day, int utcHour, int utcMinute)
    {
        WeeklyReportSchedule schedule = new(new WeeklyReportOptions { LocalTime = "01:30" });
        Instant next = schedule.NextAfter(Instant.FromUtc(2026, month, day - 1, 0, 0));
        Assert.That(next, Is.EqualTo(Instant.FromUtc(2026, month, day, utcHour, utcMinute)));
        Assert.That(schedule.NextAfter(next), Is.GreaterThan(next));
    }

    [Test]
    public void SkippedPeriods_MissedWeeks_SelectsOnlyLatestCutoff()
    {
        WeeklyReportSchedule schedule = new(new WeeklyReportOptions());
        Instant latest = schedule.LatestOnOrBefore(Instant.FromUtc(2026, 4, 14, 9, 0));
        Assert.That(latest, Is.EqualTo(Instant.FromUtc(2026, 4, 12, 17, 0)));
        Assert.That(schedule.SkippedPeriods(Instant.FromUtc(2026, 3, 22, 18, 0), latest), Is.EqualTo(3));
        Assert.That(schedule.NextAfter(latest), Is.EqualTo(Instant.FromUtc(2026, 4, 19, 17, 0)));
    }

    [Test]
    public void Validate_EnabledWithoutPrivateConfiguration_RejectsStartup()
    {
        WeeklyReportOptions options = new() { Enabled = true };
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [TestCase("http://example.test")]
    [TestCase("https://example.test/redirect?target=elsewhere")]
    [TestCase("https://user@example.test")]
    public void Validate_UnsafeContinuationOrigin_RejectsConfiguration(string origin)
    {
        WeeklyReportOptions options = new() { BaseUrl = origin };
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Test]
    public void Validate_ValidPrivateConfiguration_AcceptsBoundedSettings()
    {
        WeeklyReportOptions options = new()
        {
            Enabled = true, DeploymentId = "synthetic", BaseUrl = "https://example.test",
            RecipientAddresses = ["recipient@example.test"]
        };
        Assert.DoesNotThrow(options.Validate);
        options.RecipientAddresses = ["recipient@example.test", "RECIPIENT@example.test"];
        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
