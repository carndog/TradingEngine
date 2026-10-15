using AngleSharp.Dom;
using Bunit;
using TradingEngine.Client.Components;
using TradingEngine.Client.Models;

namespace TradingEngine.Client.Tests.Components;

[TestFixture]
public sealed class DefinitionEditorTests
{
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
