using AngleSharp.Dom;
using Bunit;
using TradingEngine.Client.Components;
using TradingEngine.Client.Models;

namespace TradingEngine.Client.Tests.Components;

[TestFixture]
public sealed class DefinitionEditorTests
{
    [Test]
    public void ChangeResistanceEntry_DefaultOff_OptsInAndOutWithoutChangingBreakout()
    {
        using BunitContext ctx = new();
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddResistanceZone();
        model.ResistanceZones[0].Conditions[0].ActionId = "existing-action";
        int changes = 0;
        IRenderedComponent<DefinitionEditor> cut = ctx.Render<DefinitionEditor>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.OnChanged, () => changes++));
        Assert.That(cut.Find("input[type=checkbox]").HasAttribute("checked"), Is.False);

        cut.Find("input[type=checkbox]").Change(true);

        Assert.That(model.ResistanceZones[0].ToDto().Conditions!.Select(condition => condition.Type),
            Is.EqualTo(new[] { "breakout", "resistance-entry" }));
        Assert.That(cut.Find("input[aria-label='resistance-entry action identifier']").GetAttribute("value"),
            Is.EqualTo("publish-signal"));
        Assert.That(cut.Markup, Does.Contain("sampled jump above the whole band"));
        cut.Find("input[type=checkbox]").Change(false);

        Assert.That(changes, Is.EqualTo(2));
        Assert.That(model.ResistanceZones[0].Conditions.Single().ActionId, Is.EqualTo("existing-action"));
        Assert.That(cut.FindAll("input[aria-label='resistance-entry action identifier']"), Is.Empty);
    }

    [Test]
    public void RemoveZone_OnlySupportZone_RemovesItAndInvokesOnChanged()
    {
        using BunitContext ctx = new();
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddSupportZone();
        int changes = 0;
        IRenderedComponent<DefinitionEditor> cut = ctx.Render<DefinitionEditor>(
            parameters => parameters
                .Add(component => component.Model, model)
                .Add(component => component.OnChanged, () => changes++));

        IElement remove = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Remove zone"));
        remove.Click();

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(model.SupportZones, Is.Empty);
        });
    }

    [Test]
    public void RemoveZone_OnlyResistanceZone_RemovesItAndInvokesOnChanged()
    {
        using BunitContext ctx = new();
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddResistanceZone();
        int changes = 0;
        IRenderedComponent<DefinitionEditor> cut = ctx.Render<DefinitionEditor>(
            parameters => parameters
                .Add(component => component.Model, model)
                .Add(component => component.OnChanged, () => changes++));

        IElement remove = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Remove zone"));
        remove.Click();

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(model.ResistanceZones, Is.Empty);
        });
    }

    [Test]
    public void AddSupportZone_EmptyModel_AddsZoneAndInvokesOnChanged()
    {
        using BunitContext ctx = new();
        DefinitionEditModel model = DefinitionEditModel.Empty();
        int changes = 0;
        IRenderedComponent<DefinitionEditor> cut = ctx.Render<DefinitionEditor>(
            parameters => parameters
                .Add(component => component.Model, model)
                .Add(component => component.OnChanged, () => changes++));

        IElement add = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Add support"));
        add.Click();

        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(model.SupportZones, Has.Count.EqualTo(1));
        });
    }
}
