using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Infrastructure.MonitoringRules.Xml;

public sealed class ChartAnalysisDefinitionXmlSerializer
{
    private const string V1Namespace = "urn:carndog:trading-engine:chart-analysis:v1";
    private const string V1SchemaResource =
        "TradingEngine.Infrastructure.MonitoringRules.Xml.V1.chart-analysis-definition-v1.xsd";

    private static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

    private static readonly Lazy<XmlSchemaSet> V1Schemas = new(LoadV1Schemas);

    public string Serialize(ChartAnalysisDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        XElement root = new(
            XName.Get("ChartAnalysisDefinition", V1Namespace),
            new XAttribute("xmlns", V1Namespace),
            new XAttribute("schemaVersion", "1"),
            new XAttribute("priceScale", definition.PriceScale.ToString(CultureInfo.InvariantCulture)));

        if (definition.SupportZones.Count > 0)
        {
            root.Add(new XElement(
                XName.Get("SupportZones", V1Namespace),
                OrderZones(definition.SupportZones)
                    .Select(zone => SerializeZone("SupportZone", zone, definition.PriceScale))));
        }

        if (definition.ResistanceZones.Count > 0)
        {
            root.Add(new XElement(
                XName.Get("ResistanceZones", V1Namespace),
                OrderZones(definition.ResistanceZones)
                    .Select(zone => SerializeZone("ResistanceZone", zone, definition.PriceScale))));
        }

        return root.ToString(SaveOptions.DisableFormatting);
    }

    public ChartAnalysisDefinition Deserialize(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        XDocument document = ParseSafely(xml);
        XElement root = document.Root
            ?? throw new InvalidDataException("The chart-analysis document has no root element.");

        if (root.Name.LocalName != "ChartAnalysisDefinition")
        {
            throw new InvalidDataException(
                $"Unexpected root element '{root.Name.LocalName}'. Expected 'ChartAnalysisDefinition'.");
        }

        if (root.Name.NamespaceName != V1Namespace)
        {
            throw new UnsupportedChartAnalysisSchemaVersionException(root.Name.NamespaceName);
        }

        string? schemaVersion = (string?)root.Attribute("schemaVersion");

        if (schemaVersion is not null && schemaVersion != "1")
        {
            throw new UnsupportedChartAnalysisSchemaVersionException(root.Name.NamespaceName);
        }

        ValidateAgainstV1Schema(document);

        return MapV1(root);
    }

    private static XDocument ParseSafely(string xml)
    {
        using StringReader text = new(xml);
        using XmlReader reader = XmlReader.Create(text, SafeReaderSettings);

        return XDocument.Load(reader, LoadOptions.None);
    }

    private static void ValidateAgainstV1Schema(XDocument document)
    {
        List<string> failures = [];

        document.Validate(
            V1Schemas.Value,
            (_, args) => failures.Add(args.Message),
            true);

        if (failures.Count > 0)
        {
            throw new XmlSchemaValidationException(
                $"The chart-analysis document failed v1 schema validation: {string.Join(" ", failures)}");
        }
    }

    private static ChartAnalysisDefinition MapV1(XElement root)
    {
        XNamespace ns = V1Namespace;
        int priceScale = (int)root.Attribute("priceScale")!;

        ChartZone[] supportZones = ReadZones(
            root.Element(ns + "SupportZones"),
            ns + "SupportZone");
        ChartZone[] resistanceZones = ReadZones(
            root.Element(ns + "ResistanceZones"),
            ns + "ResistanceZone");

        return ChartAnalysisDefinition.Create(priceScale, supportZones, resistanceZones);
    }

    private static ChartZone[] ReadZones(XElement? container, XName zoneName)
    {
        if (container is null)
        {
            return [];
        }

        return container
            .Elements(zoneName)
            .Select(ReadZone)
            .ToArray();
    }

    private static ChartZone ReadZone(XElement zone)
    {
        ChartAnalysisIdentifier id = ChartAnalysisIdentifier.From((string)zone.Attribute("id")!);
        decimal lower = (decimal)zone.Attribute("lower")!;
        decimal level = (decimal)zone.Attribute("level")!;
        decimal upper = (decimal)zone.Attribute("upper")!;

        ChartCondition[] conditions = zone
            .Elements(XName.Get("Condition", V1Namespace))
            .Select(ReadCondition)
            .ToArray();

        return ChartZone.Create(id, lower, level, upper, conditions);
    }

    private static ChartCondition ReadCondition(XElement condition)
    {
        ChartConditionType type = ParseConditionType((string)condition.Attribute("type")!);
        ChartAnalysisIdentifier actionId = ChartAnalysisIdentifier.From(
            (string)condition.Attribute("actionId")!);

        return new ChartCondition(type, actionId);
    }

    private static ChartConditionType ParseConditionType(string value)
    {
        return value switch
        {
            "buy-zone" => ChartConditionType.BuyZone,
            "support-loss" => ChartConditionType.SupportLoss,
            "breakout" => ChartConditionType.Breakout,
            _ => throw new InvalidDataException($"Unknown chart condition type '{value}'.")
        };
    }

    private static IEnumerable<ChartZone> OrderZones(IReadOnlyList<ChartZone> zones)
    {
        return zones
            .OrderBy(zone => zone.Lower)
            .ThenBy(zone => zone.Id.Value, StringComparer.Ordinal);
    }

    private static XElement SerializeZone(string elementName, ChartZone zone, int priceScale)
    {
        XNamespace ns = V1Namespace;

        return new XElement(
            ns + elementName,
            new XAttribute("id", zone.Id.Value),
            new XAttribute("lower", FormatPrice(zone.Lower, priceScale)),
            new XAttribute("level", FormatPrice(zone.Level, priceScale)),
            new XAttribute("upper", FormatPrice(zone.Upper, priceScale)),
            zone.Conditions.Select(SerializeCondition));
    }

    private static XElement SerializeCondition(ChartCondition condition)
    {
        return new XElement(
            XName.Get("Condition", V1Namespace),
            new XAttribute("type", FormatConditionType(condition.Type)),
            new XAttribute("actionId", condition.ActionId.Value));
    }

    private static string FormatConditionType(ChartConditionType type)
    {
        return type switch
        {
            ChartConditionType.BuyZone => "buy-zone",
            ChartConditionType.SupportLoss => "support-loss",
            ChartConditionType.Breakout => "breakout",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown chart condition type.")
        };
    }

    private static string FormatPrice(decimal price, int priceScale)
    {
        return price.ToString($"F{priceScale}", CultureInfo.InvariantCulture);
    }

    private static XmlSchemaSet LoadV1Schemas()
    {
        using Stream stream = typeof(ChartAnalysisDefinitionXmlSerializer).Assembly
            .GetManifestResourceStream(V1SchemaResource)
            ?? throw new InvalidOperationException(
                $"The embedded schema resource '{V1SchemaResource}' was not found.");
        using XmlReader reader = XmlReader.Create(stream, SafeReaderSettings);

        XmlSchemaSet schemas = new();
        schemas.Add(null, reader);

        return schemas;
    }
}
