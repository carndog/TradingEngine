using System.Xml;
using System.Xml.Schema;
using TradingEngine.Domain;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Infrastructure.MonitoringRules.Xml;

namespace TradingEngine.Infrastructure.Tests.MonitoringRules.Xml;

[TestFixture]
public sealed class ChartAnalysisDefinitionXmlSerializerTests
{
    private ChartAnalysisDefinitionXmlSerializer _serializer = null!;

    [SetUp]
    public void SetUp()
    {
        _serializer = new ChartAnalysisDefinitionXmlSerializer();
    }

    [Test]
    public void Deserialize_WithValidDocument_RoundTripsToCanonicalXml()
    {
        string xml = ReadExample("valid.xml");

        ChartAnalysisDefinition definition = _serializer.Deserialize(xml);
        string reserialized = _serializer.Serialize(definition);
        ChartAnalysisDefinition reparsed = _serializer.Deserialize(reserialized);

        Assert.Multiple(() =>
        {
            Assert.That(_serializer.Serialize(reparsed), Is.EqualTo(reserialized));
            Assert.That(definition.PriceScale, Is.EqualTo(4));
            Assert.That(definition.SupportZones, Has.Count.EqualTo(1));
            Assert.That(definition.ResistanceZones, Has.Count.EqualTo(1));
            Assert.That(definition.SupportZones[0].Id.Value, Is.EqualTo("support-a"));
            Assert.That(definition.SupportZones[0].Level, Is.EqualTo(100.0000m));
            Assert.That(
                definition.SupportZones[0].Conditions.Select(condition => condition.Type),
                Is.EqualTo([ChartConditionType.BuyZone, ChartConditionType.SupportLoss]));
            Assert.That(
                definition.ResistanceZones[0].Conditions.Select(condition => condition.Type),
                Is.EqualTo([ChartConditionType.Breakout]));
        });
    }

    [Test]
    public void Serialize_WithUnorderedZones_WritesDeterministicCanonicalXml()
    {
        ChartAnalysisDefinition definition = ChartAnalysisDefinition.Create(
            4,
            [CreateSupportZone("support-b", 50m, 55m, 60m), CreateSupportZone("support-a", 90m, 95m, 99m)],
            [CreateResistanceZone("resistance-a", 120m, 125m, 130m)]);

        string xml = _serializer.Serialize(definition);

        Assert.Multiple(() =>
        {
            Assert.That(xml, Does.StartWith("<ChartAnalysisDefinition xmlns=\"urn:carndog:trading-engine:chart-analysis:v1\" schemaVersion=\"1\" priceScale=\"4\">"));
            Assert.That(xml.IndexOf("support-b", StringComparison.Ordinal), Is.LessThan(xml.IndexOf("support-a", StringComparison.Ordinal)));
            Assert.That(xml.IndexOf("SupportZones", StringComparison.Ordinal), Is.LessThan(xml.IndexOf("ResistanceZones", StringComparison.Ordinal)));
            Assert.That(xml, Does.Contain("lower=\"50.0000\""));
            Assert.That(xml, Does.Not.Contain("<?xml"));
        });
    }

    [Test]
    public void Serialize_WithMaximumDecimalPrices_RoundTripsThroughXml()
    {
        ChartAnalysisDefinition definition = ChartAnalysisDefinition.Create(
            0,
            [CreateSupportZone("support-a", 1m, 2m, decimal.MaxValue)],
            []);

        string xml = _serializer.Serialize(definition);
        ChartAnalysisDefinition reparsed = _serializer.Deserialize(xml);

        Assert.Multiple(() =>
        {
            Assert.That(xml, Does.Contain("upper=\"79228162514264337593543950335\""));
            Assert.That(reparsed.SupportZones[0].Upper, Is.EqualTo(decimal.MaxValue));
            Assert.That(_serializer.Serialize(reparsed), Is.EqualTo(xml));
        });
    }

    [Test]
    public void Deserialize_WithPriceOutsideDecimalRange_ThrowsInvalidDataException()
    {
        string xml = "<ChartAnalysisDefinition xmlns=\"urn:carndog:trading-engine:chart-analysis:v1\" schemaVersion=\"1\" priceScale=\"4\"><SupportZones><SupportZone id=\"support-a\" lower=\"95.0000\" level=\"100.0000\" upper=\"79228162514264337593543950336\"><Condition type=\"buy-zone\" actionId=\"publish-signal\" /><Condition type=\"support-loss\" actionId=\"publish-signal\" /></SupportZone></SupportZones></ChartAnalysisDefinition>";

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => _serializer.Deserialize(xml));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("outside the supported decimal range"));
            Assert.That(exception.InnerException, Is.TypeOf<OverflowException>());
        });
    }

    [Test]
    public void Deserialize_WithMalformedXml_ThrowsXmlException()
    {
        Assert.Throws<XmlException>(() => _serializer.Deserialize("<ChartAnalysisDefinition"));
    }

    [Test]
    public void Deserialize_WithUnsupportedSchemaVersion_ThrowsUnsupportedChartAnalysisSchemaVersionException()
    {
        string xml = ReadExample("invalid-unsupported-version.xml");

        Assert.Throws<UnsupportedChartAnalysisSchemaVersionException>(() => _serializer.Deserialize(xml));
    }

    [Test]
    public void Deserialize_WithUnsupportedNamespace_ThrowsUnsupportedChartAnalysisSchemaVersionException()
    {
        string xml = "<ChartAnalysisDefinition xmlns=\"urn:carndog:trading-engine:chart-analysis:v9\" schemaVersion=\"9\" priceScale=\"4\" />";

        Assert.Throws<UnsupportedChartAnalysisSchemaVersionException>(() => _serializer.Deserialize(xml));
    }

    [Test]
    public void Deserialize_WithInvertedZoneBoundaries_ThrowsDomainRuleViolationException()
    {
        string xml = ReadExample("invalid-boundaries.xml");

        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => _serializer.Deserialize(xml));

        Assert.That(exception!.Rule, Is.EqualTo(ChartZoneRule.InvalidBoundaryOrder));
    }

    [Test]
    public void Deserialize_WithSchemaViolation_ThrowsXmlSchemaValidationException()
    {
        string xml = "<ChartAnalysisDefinition xmlns=\"urn:carndog:trading-engine:chart-analysis:v1\" schemaVersion=\"1\" priceScale=\"4\"><SupportZones><SupportZone id=\"support-a\" lower=\"95.0000\" level=\"100.0000\" upper=\"105.0000\"><Condition type=\"unknown\" actionId=\"publish-signal\" /></SupportZone></SupportZones></ChartAnalysisDefinition>";

        Assert.Throws<XmlSchemaValidationException>(() => _serializer.Deserialize(xml));
    }

    [Test]
    public void Deserialize_WithUnexpectedRootElement_ThrowsInvalidDataException()
    {
        string xml = "<Other xmlns=\"urn:carndog:trading-engine:chart-analysis:v1\" />";

        Assert.Throws<InvalidDataException>(() => _serializer.Deserialize(xml));
    }

    private static ChartZone CreateSupportZone(string id, decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From(id),
            lower,
            level,
            upper,
            [
                ChartCondition.Create(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal")),
                ChartCondition.Create(ChartConditionType.SupportLoss, ChartAnalysisIdentifier.From("publish-signal"))
            ]);
    }

    private static ChartZone CreateResistanceZone(string id, decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From(id),
            lower,
            level,
            upper,
            [ChartCondition.Create(ChartConditionType.Breakout, ChartAnalysisIdentifier.From("publish-signal"))]);
    }

    private static string ReadExample(string fileName)
    {
        string path = Path.Combine(
            SolutionRoot.Find(),
            "docs",
            "examples",
            "chart-analysis",
            "v1",
            fileName);

        return File.ReadAllText(path);
    }
}
