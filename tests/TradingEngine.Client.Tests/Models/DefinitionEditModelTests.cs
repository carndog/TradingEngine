using TradingEngine.Client.Models;

namespace TradingEngine.Client.Tests.Models;

[TestFixture]
public sealed class DefinitionEditModelTests
{
    [Test]
    public void SameContent_ClonedModel_ReturnsTrue()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddSupportZone();
        model.AddResistanceZone();

        DefinitionEditModel clone = model.Clone();

        Assert.That(model.SameContent(clone), Is.True);
    }

    [Test]
    public void SameContent_RemovedSupportZone_ReturnsFalse()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddSupportZone();
        DefinitionEditModel baseline = model.Clone();

        model.SupportZones.RemoveAt(0);

        Assert.That(model.SameContent(baseline), Is.False);
    }

    [Test]
    public void SameContent_RemovedLastResistanceZone_ReturnsFalse()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddResistanceZone();
        DefinitionEditModel baseline = model.Clone();

        model.ResistanceZones.RemoveAt(0);

        Assert.That(model.SameContent(baseline), Is.False);
    }

    [Test]
    public void SameContent_ChangedConditionAction_ReturnsFalse()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddSupportZone();
        DefinitionEditModel baseline = model.Clone();

        model.SupportZones[0].Conditions[0].ActionId = "changed";

        Assert.That(model.SameContent(baseline), Is.False);
    }

    [Test]
    public void SameContent_ChangedPriceScale_ReturnsFalse()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        DefinitionEditModel baseline = model.Clone();

        model.PriceScale = 5;

        Assert.That(model.SameContent(baseline), Is.False);
    }

    [Test]
    public void Clone_MutatingClone_LeavesOriginalUnchanged()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddSupportZone();

        DefinitionEditModel clone = model.Clone();
        clone.SupportZones[0].Id = "mutated";
        clone.AddResistanceZone();

        Assert.Multiple(() =>
        {
            Assert.That(model.SupportZones[0].Id, Is.EqualTo(string.Empty));
            Assert.That(model.ResistanceZones, Is.Empty);
        });
    }

    [Test]
    public void DescribeChangesAgainst_IdenticalModel_ReportsIdentical()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddSupportZone();
        DefinitionEditModel latest = model.Clone();

        string summary = latest.DescribeChangesAgainst(model);

        Assert.That(summary, Does.Contain("identical"));
    }

    [Test]
    public void DescribeChangesAgainst_DifferentPriceScale_ReportsScaleChange()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.PriceScale = 4;
        DefinitionEditModel latest = model.Clone();
        latest.PriceScale = 6;

        string summary = latest.DescribeChangesAgainst(model);

        Assert.That(summary, Does.Contain("price scale 4 → 6"));
    }

    [Test]
    public void DescribeChangesAgainst_ZoneBoundsChanged_ReportsZoneDetail()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddSupportZone();
        model.SupportZones[0].Id = "support-a";
        model.SupportZones[0].Lower = 90m;
        model.SupportZones[0].Upper = 100m;
        model.SupportZones[0].Level = 95m;
        DefinitionEditModel latest = model.Clone();
        latest.SupportZones[0].Lower = 80m;
        latest.SupportZones[0].Upper = 110m;

        string summary = latest.DescribeChangesAgainst(model);

        Assert.That(summary, Does.Contain("support zone 'support-a' bounds 90–100 around 95 → 80–110 around 95"));
    }

    [Test]
    public void DescribeChangesAgainst_ZoneAdded_ReportsAddedZone()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddSupportZone();
        DefinitionEditModel latest = model.Clone();
        latest.AddResistanceZone();
        latest.ResistanceZones[0].Id = "res-b";
        latest.ResistanceZones[0].Lower = 105m;
        latest.ResistanceZones[0].Upper = 115m;
        latest.ResistanceZones[0].Level = 110m;

        string summary = latest.DescribeChangesAgainst(model);

        Assert.That(summary, Does.Contain("resistance zone 'res-b' added (105–115 around 110)"));
    }

    [Test]
    public void DescribeChangesAgainst_ZoneRemoved_ReportsRemovedZone()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddSupportZone();
        model.SupportZones[0].Id = "support-a";
        model.SupportZones[0].Lower = 90m;
        model.SupportZones[0].Upper = 100m;
        model.SupportZones[0].Level = 95m;
        DefinitionEditModel latest = model.Clone();
        latest.SupportZones.RemoveAt(0);

        string summary = latest.DescribeChangesAgainst(model);

        Assert.That(summary, Does.Contain("support zone 'support-a' removed (was 90–100 around 95)"));
    }

    [Test]
    public void DescribeChangesAgainst_ActionChanged_ReportsActionNotBounds()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddResistanceZone();
        model.ResistanceZones[0].Id = "res-a";
        model.ResistanceZones[0].Lower = 105m;
        model.ResistanceZones[0].Upper = 115m;
        model.ResistanceZones[0].Level = 110m;
        model.ResistanceZones[0].Conditions[0].ActionId = "old-action";
        DefinitionEditModel latest = model.Clone();
        latest.ResistanceZones[0].Conditions[0].ActionId = "new-action";

        string summary = latest.DescribeChangesAgainst(model);

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("condition 'breakout' action 'old-action' → 'new-action'"));
            Assert.That(summary, Does.Not.Contain("bounds"));
        });
    }

    [Test]
    public void DescribeChangesAgainst_ConditionActionEmptied_ReportsEmptyAction()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddResistanceZone();
        model.ResistanceZones[0].Id = "res-a";
        model.ResistanceZones[0].Conditions[0].ActionId = "old-action";
        DefinitionEditModel latest = model.Clone();
        latest.ResistanceZones[0].Conditions[0].ActionId = string.Empty;

        string summary = latest.DescribeChangesAgainst(model);

        Assert.That(summary, Does.Contain("condition 'breakout' action 'old-action' → '—'"));
    }
}
