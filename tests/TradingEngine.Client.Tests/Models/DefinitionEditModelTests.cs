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

        Assert.That(summary, Does.Contain("support zone 'support-a' changed"));
    }

    [Test]
    public void DescribeChangesAgainst_ZoneAdded_ReportsZoneCountChange()
    {
        DefinitionEditModel model = DefinitionEditModel.Empty();
        model.AddSupportZone();
        DefinitionEditModel latest = model.Clone();
        latest.AddResistanceZone();

        string summary = latest.DescribeChangesAgainst(model);

        Assert.That(summary, Does.Contain("resistance zones 0 → 1"));
    }
}
