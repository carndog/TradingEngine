using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Client.Components;
using TradingEngine.Client.Services;
using TradingEngine.Contracts.MonitoringRules;

namespace TradingEngine.Client.Tests.Components;

[TestFixture]
public sealed class TimelinePanelTests
{
    private static BunitContext CreateContext(DateTimeOffset now)
    {
        BunitContext ctx = new();
        ctx.Services.AddSingleton<IUiClock>(new ManualUiClock(now));
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    [Test]
    public void Revisions_ShowActorLabelFocusAndReadOnlyDefinition()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-05T09:00:00Z");
        MonitoringRuleRevisionDto committed = ClientTestData.Revision(
            Guid.NewGuid(),
            kind: "committed",
            revisionNumber: 4,
            effectiveFrom: now - TimeSpan.FromDays(2),
            createdByCaller: true);

        using BunitContext ctx = CreateContext(now);
        IRenderedComponent<TimelinePanel> cut = ctx.Render<TimelinePanel>(
            parameters => parameters
                .Add(component => component.Timeline,
                    ClientTestData.Timeline("tok-1", committed))
                .Add(component => component.InstrumentId, Guid.NewGuid())
                .Add(component => component.FocusId, committed.Id));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Revision 4"));
            Assert.That(cut.Markup, Does.Contain("You"));
            Assert.That(cut.Markup, Does.Contain("Just changed"));
            Assert.That(cut.Markup, Does.Contain("View definition"));
            Assert.That(cut.Markup, Does.Contain("Price scale 2"));
        });

        Assert.That(
            cut.Find($"#revision-{committed.Id}").ClassList,
            Does.Contain("te-focus"));
    }
}
