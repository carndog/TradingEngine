using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Client.Components;
using TradingEngine.Client.Services;
using TradingEngine.Contracts.MonitoringRules;

namespace TradingEngine.Client.Tests.Components;

[TestFixture]
public sealed class DraftListPanelTests
{
    private static BunitContext CreateContext(DateTimeOffset now)
    {
        BunitContext ctx = new();
        ctx.Services.AddSingleton<IUiClock>(new ManualUiClock(now));
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    [Test]
    public void Drafts_ShowDistinguishingLabelsActorAndFocusMarker()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-05T09:00:00Z");
        MonitoringRuleRevisionDto mine = ClientTestData.Revision(
            Guid.NewGuid(),
            changeReason: "widen bands",
            createdByCaller: true);
        MonitoringRuleRevisionDto other = ClientTestData.Revision(
            Guid.NewGuid(),
            createdBy: "another-actor",
            proposedFrom: now - TimeSpan.FromDays(1));

        using BunitContext ctx = CreateContext(now);
        IRenderedComponent<DraftListPanel> cut = ctx.Render<DraftListPanel>(
            parameters => parameters
                .Add(component => component.Drafts,
                    ClientTestData.Drafts("tok-1", mine, other))
                .Add(component => component.InstrumentId, Guid.NewGuid())
                .Add(component => component.FocusId, mine.Id));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("widen bands"));
            Assert.That(cut.Markup, Does.Contain("You"));
            Assert.That(cut.Markup, Does.Contain("Another user"));
            Assert.That(cut.Markup, Does.Contain("Proposed start has passed"));
            Assert.That(cut.Markup, Does.Contain("Just changed"));
            Assert.That(cut.Markup, Does.Contain("support zone"));
        });

        Assert.That(
            cut.Find($"#revision-{mine.Id}").ClassList,
            Does.Contain("te-focus"));
        Assert.That(
            cut.Find($"#revision-{other.Id}").ClassList,
            Does.Not.Contain("te-focus"));
    }
}
