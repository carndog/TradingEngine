using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Infrastructure.MonitoringRules.Xml;

public sealed class ChartAnalysisDefinitionXmlSerializer
{
    private const string SchemaResource =
        "TradingEngine.Infrastructure.MonitoringRules.Xml.chart-analysis-definition.xsd";

    private static readonly XmlReaderSettings SafeReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

    private static readonly string[] PriceAttributeNames = ["lower", "level", "upper"];

    private static readonly Lazy<XmlSchemaSet> Schemas = new(LoadSchemas);

    public string Serialize(ChartAnalysisDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        XElement root = new(
            "ChartAnalysisDefinition",
            new XAttribute("priceScale", definition.PriceScale.ToString(CultureInfo.InvariantCulture)));

        if (definition.SupportZones.Count > 0)
        {
            root.Add(new XElement(
                "SupportZones",
                OrderZones(definition.SupportZones)
                    .Select(zone => SerializeZone("SupportZone", zone, definition.PriceScale))));
        }

        if (definition.ResistanceZones.Count > 0)
        {
            root.Add(new XElement(
                "ResistanceZones",
                OrderZones(definition.ResistanceZones)
                    .Select(zone => SerializeZone("ResistanceZone", zone, definition.PriceScale))));
        }

        XDocument document = new(root);
        ValidateAgainstSchema(document);

        return root.ToString(SaveOptions.DisableFormatting);
    }

    public ChartAnalysisDefinition Deserialize(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        XDocument document = ParseSafely(xml);
        XElement root = document.Root
            ?? throw new InvalidDataException("The chart-analysis document has no root element.");

        if (root.Name != XName.Get("ChartAnalysisDefinition"))
        {
            throw new InvalidDataException(
                $"Unexpected root element '{root.Name}'. Expected 'ChartAnalysisDefinition' with no namespace.");
        }

        EnsurePricesWithinDecimalRange(root);
        ValidateAgainstSchema(document);

        return Map(root);
    }

    private static XDocument ParseSafely(string xml)
    {
        using StringReader text = new(xml);
        using XmlReader reader = XmlReader.Create(text, SafeReaderSettings);

        return XDocument.Load(reader, LoadOptions.None);
    }

    private static void ValidateAgainstSchema(XDocument document)
    {
        List<string> failures = [];

        document.Validate(
            Schemas.Value,
            (_, args) => failures.Add(args.Message),
            true);

        if (failures.Count > 0)
        {
            throw new XmlSchemaValidationException(
                $"The chart-analysis document failed schema validation: {string.Join(" ", failures)}");
        }
    }

    private static void EnsurePricesWithinDecimalRange(XElement root)
    {
        IEnumerable<XElement> zones = root
            .Descendants("SupportZone")
            .Concat(root.Descendants("ResistanceZone"));

        foreach (XElement zone in zones)
        {
            foreach (string attributeName in PriceAttributeNames)
            {
                XAttribute? attribute = zone.Attribute(attributeName);

                if (attribute is null)
                {
                    continue;
                }

                try
                {
                    XmlConvert.ToDecimal(attribute.Value);
                }
                catch (OverflowException exception)
                {
                    throw new InvalidDataException(
                        $"The '{attributeName}' price is outside the supported decimal range.",
                        exception);
                }
                catch (FormatException)
                {
                }
            }
        }
    }

    private static ChartAnalysisDefinition Map(XElement root)
    {
        int priceScale = (int)root.Attribute("priceScale")!;

        ChartZone[] supportZones = ReadZones(
            root.Element("SupportZones"),
            "SupportZone");
        ChartZone[] resistanceZones = ReadZones(
            root.Element("ResistanceZones"),
            "ResistanceZone");

        return Unwrap(ChartAnalysisDefinition.Create(priceScale, supportZones, resistanceZones));
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
        ChartAnalysisIdentifier id = Unwrap(ChartAnalysisIdentifier.From((string)zone.Attribute("id")!));
        decimal lower = ReadPrice(zone, "lower");
        decimal level = ReadPrice(zone, "level");
        decimal upper = ReadPrice(zone, "upper");

        ChartCondition[] conditions = zone
            .Elements("Condition")
            .Select(ReadCondition)
            .ToArray();

        return Unwrap(ChartZone.Create(id, lower, level, upper, conditions));
    }

    private static decimal ReadPrice(XElement zone, string attributeName)
    {
        try
        {
            return (decimal)zone.Attribute(attributeName)!;
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"The '{attributeName}' price is outside the supported decimal range.",
                exception);
        }
    }

    private static ChartCondition ReadCondition(XElement condition)
    {
        ChartConditionType type = ParseConditionType((string)condition.Attribute("type")!);
        ChartAnalysisIdentifier actionId = Unwrap(ChartAnalysisIdentifier.From(
            (string)condition.Attribute("actionId")!));

        return Unwrap(ChartCondition.Create(type, actionId));
    }

    private static T Unwrap<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            throw new InvalidDataException(
                $"The chart-analysis document is invalid ({result.Error.Code}): {result.Error.Description}");
        }

        return result.Value;
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
        return new XElement(
            elementName,
            new XAttribute("id", zone.Id.Value),
            new XAttribute("lower", FormatPrice(zone.Lower, priceScale)),
            new XAttribute("level", FormatPrice(zone.Level, priceScale)),
            new XAttribute("upper", FormatPrice(zone.Upper, priceScale)),
            zone.Conditions.Select(SerializeCondition));
    }

    private static XElement SerializeCondition(ChartCondition condition)
    {
        return new XElement(
            "Condition",
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

    private static XmlSchemaSet LoadSchemas()
    {
        using Stream stream = typeof(ChartAnalysisDefinitionXmlSerializer).Assembly
            .GetManifestResourceStream(SchemaResource)
            ?? throw new InvalidOperationException(
                $"The embedded schema resource '{SchemaResource}' was not found.");
        using XmlReader reader = XmlReader.Create(stream, SafeReaderSettings);

        XmlSchemaSet schemas = new();
        schemas.Add(null, reader);

        return schemas;
    }
}
