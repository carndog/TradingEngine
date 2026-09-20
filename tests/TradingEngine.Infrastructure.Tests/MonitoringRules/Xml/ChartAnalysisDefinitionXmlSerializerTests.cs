using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
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
            [CreateResistanceZone("resistance-a", 120m, 125m, 130m)]).Value;

        string xml = _serializer.Serialize(definition);

        Assert.Multiple(() =>
        {
            Assert.That(xml, Does.StartWith("<ChartAnalysisDefinition priceScale=\"4\">"));
            Assert.That(xml.IndexOf("support-b", StringComparison.Ordinal), Is.LessThan(xml.IndexOf("support-a", StringComparison.Ordinal)));
            Assert.That(xml.IndexOf("SupportZones", StringComparison.Ordinal), Is.LessThan(xml.IndexOf("ResistanceZones", StringComparison.Ordinal)));
            Assert.That(xml, Does.Contain("lower=\"50.0000\""));
            Assert.That(xml, Does.Not.Contain("<?xml"));
        });
    }

    [Test]
    public void Serialize_WithValidDefinition_GeneratesXmlValidatingAgainstEmbeddedSchema()
    {
        ChartAnalysisDefinition definition = ChartAnalysisDefinition.Create(
            4,
            [CreateSupportZone("support-a", 95m, 100m, 105m)],
            [CreateResistanceZone("resistance-a", 120m, 125m, 130m)]).Value;

        string xml = _serializer.Serialize(definition);
        XDocument document = XDocument.Parse(xml);
        List<string> failures = [];
        document.Validate(LoadEmbeddedSchemas(), (_, args) => failures.Add(args.Message), true);

        Assert.That(failures, Is.Empty);
    }

    [Test]
    public void Serialize_WithMaximumDecimalPrices_RoundTripsThroughXml()
    {
        ChartAnalysisDefinition definition = ChartAnalysisDefinition.Create(
            0,
            [CreateSupportZone("support-a", 1m, 2m, decimal.MaxValue)],
            []).Value;

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
        string xml = "<ChartAnalysisDefinition priceScale=\"4\"><SupportZones><SupportZone id=\"support-a\" lower=\"95.0000\" level=\"100.0000\" upper=\"79228162514264337593543950336\"><Condition type=\"buy-zone\" actionId=\"publish-signal\" /><Condition type=\"support-loss\" actionId=\"publish-signal\" /></SupportZone></SupportZones></ChartAnalysisDefinition>";

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => _serializer.Deserialize(xml));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("outside the supported decimal range"));
            Assert.That(exception.InnerException, Is.TypeOf<OverflowException>());
        });
    }

    [Test]
    public void Deserialize_WithNonNumericPrice_ThrowsXmlSchemaValidationException()
    {
        string xml = "<ChartAnalysisDefinition priceScale=\"4\"><SupportZones><SupportZone id=\"support-a\" lower=\"95.0000\" level=\"abc\" upper=\"105.0000\"><Condition type=\"buy-zone\" actionId=\"publish-signal\" /><Condition type=\"support-loss\" actionId=\"publish-signal\" /></SupportZone></SupportZones></ChartAnalysisDefinition>";

        XmlSchemaValidationException? exception = Assert.Throws<XmlSchemaValidationException>(
            () => _serializer.Deserialize(xml));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("not a valid decimal"));
            Assert.That(exception.InnerException, Is.TypeOf<FormatException>());
        });
    }

    [Test]
    public void Deserialize_WithMalformedXml_ThrowsXmlException()
    {
        Assert.Throws<XmlException>(() => _serializer.Deserialize("<ChartAnalysisDefinition"));
    }

    [Test]
    public void Deserialize_WithNamespaceQualifiedDocument_ThrowsInvalidDataException()
    {
        string xml = "<ChartAnalysisDefinition xmlns=\"urn:carndog:trading-engine:chart-analysis:v1\" priceScale=\"4\" />";

        Assert.Throws<InvalidDataException>(() => _serializer.Deserialize(xml));
    }

    [Test]
    public void Deserialize_WithSchemaVersionAttribute_ThrowsXmlSchemaValidationException()
    {
        string xml = "<ChartAnalysisDefinition schemaVersion=\"1\" priceScale=\"4\"><ResistanceZones><ResistanceZone id=\"resistance-a\" lower=\"120.0000\" level=\"125.0000\" upper=\"130.0000\"><Condition type=\"breakout\" actionId=\"publish-signal\" /></ResistanceZone></ResistanceZones></ChartAnalysisDefinition>";

        Assert.Throws<XmlSchemaValidationException>(() => _serializer.Deserialize(xml));
    }

    [Test]
    public void Deserialize_WithDocumentTypeDefinition_ThrowsXmlException()
    {
        string xml = "<!DOCTYPE ChartAnalysisDefinition [<!ENTITY external SYSTEM \"file:///c:/windows/win.ini\">]><ChartAnalysisDefinition priceScale=\"4\" />";

        Assert.Throws<XmlException>(() => _serializer.Deserialize(xml));
    }

    [Test]
    public void Deserialize_WithoutRootElement_ThrowsXmlException()
    {
        Assert.Throws<XmlException>(() => _serializer.Deserialize("<?xml version=\"1.0\"?>"));
    }

    [Test]
    public void Deserialize_WithInvertedZoneBoundaries_ThrowsInvalidDataException()
    {
        string xml = ReadExample("invalid-boundaries.xml");

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => _serializer.Deserialize(xml));

        Assert.That(exception!.Message, Does.Contain("chart_analysis.zone_invalid_boundary_order"));
    }

    [Test]
    public void Deserialize_WithSemanticallyInvalidDocument_ThrowsInvalidDataExceptionWithErrorCode()
    {
        string xml = "<ChartAnalysisDefinition priceScale=\"4\"><SupportZones><SupportZone id=\"support-a\" lower=\"95.0000\" level=\"100.0000\" upper=\"105.0000\"><Condition type=\"buy-zone\" actionId=\"publish-signal\" /><Condition type=\"support-loss\" actionId=\"publish-signal\" /></SupportZone><SupportZone id=\"support-a\" lower=\"110.0000\" level=\"115.0000\" upper=\"120.0000\"><Condition type=\"buy-zone\" actionId=\"publish-signal\" /><Condition type=\"support-loss\" actionId=\"publish-signal\" /></SupportZone></SupportZones></ChartAnalysisDefinition>";

        InvalidDataException? exception = Assert.Throws<InvalidDataException>(
            () => _serializer.Deserialize(xml));

        Assert.That(exception!.Message, Does.Contain("chart_analysis.duplicate_zone_id"));
    }

    [Test]
    public void Deserialize_WithSchemaViolation_ThrowsXmlSchemaValidationException()
    {
        string xml = "<ChartAnalysisDefinition priceScale=\"4\"><SupportZones><SupportZone id=\"support-a\" lower=\"95.0000\" level=\"100.0000\" upper=\"105.0000\"><Condition type=\"unknown\" actionId=\"publish-signal\" /></SupportZone></SupportZones></ChartAnalysisDefinition>";

        Assert.Throws<XmlSchemaValidationException>(() => _serializer.Deserialize(xml));
    }

    [Test]
    public void Deserialize_WithUnexpectedRootElement_ThrowsInvalidDataException()
    {
        string xml = "<Other />";

        Assert.Throws<InvalidDataException>(() => _serializer.Deserialize(xml));
    }

    private static ChartZone CreateSupportZone(string id, decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From(id).Value,
            lower,
            level,
            upper,
            [
                ChartCondition.Create(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                ChartCondition.Create(ChartConditionType.SupportLoss, ChartAnalysisIdentifier.From("publish-signal").Value).Value
            ]).Value;
    }

    private static ChartZone CreateResistanceZone(string id, decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From(id).Value,
            lower,
            level,
            upper,
            [ChartCondition.Create(ChartConditionType.Breakout, ChartAnalysisIdentifier.From("publish-signal").Value).Value]).Value;
    }

    private static string ReadExample(string fileName)
    {
        string path = Path.Combine(
            SolutionRoot.Find(),
            "docs",
            "examples",
            "chart-analysis",
            fileName);

        return File.ReadAllText(path);
    }

    private static XmlSchemaSet LoadEmbeddedSchemas()
    {
        using Stream stream = typeof(ChartAnalysisDefinitionXmlSerializer).Assembly
            .GetManifestResourceStream("TradingEngine.Infrastructure.MonitoringRules.Xml.chart-analysis-definition.xsd")
            ?? throw new InvalidOperationException("The embedded schema resource was not found.");
        using XmlReader reader = XmlReader.Create(stream);

        XmlSchemaSet schemas = new();
        schemas.Add(null, reader);

        return schemas;
    }
}
