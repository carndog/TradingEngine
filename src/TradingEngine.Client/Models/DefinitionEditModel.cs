using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Models;

public sealed class DefinitionEditModel
{
    private DefinitionEditModel()
    {
    }

    public int PriceScale { get; set; } = 2;

    public List<ZoneEditModel> SupportZones { get; } = [];

    public List<ZoneEditModel> ResistanceZones { get; } = [];

    public MonitoringRuleDefinitionDto ToDto()
    {
        return new MonitoringRuleDefinitionDto(
            PriceScale,
            SupportZones.Select(zone => zone.ToDto()).ToList(),
            ResistanceZones.Select(zone => zone.ToDto()).ToList());
    }

    public void AddSupportZone()
    {
        SupportZones.Add(ZoneEditModel.CreateSupport());
    }

    public void AddResistanceZone()
    {
        ResistanceZones.Add(ZoneEditModel.CreateResistance());
    }

    public DefinitionEditModel Clone()
    {
        DefinitionEditModel clone = new()
        {
            PriceScale = PriceScale
        };
        clone.SupportZones.AddRange(SupportZones.Select(zone => zone.Clone()));
        clone.ResistanceZones.AddRange(ResistanceZones.Select(zone => zone.Clone()));
        return clone;
    }

    public bool SameContent(DefinitionEditModel other)
    {
        return PriceScale == other.PriceScale
            && ZonesEqual(SupportZones, other.SupportZones)
            && ZonesEqual(ResistanceZones, other.ResistanceZones);
    }

    private static bool ZonesEqual(IReadOnlyList<ZoneEditModel> first, IReadOnlyList<ZoneEditModel> second)
    {
        return first.Count == second.Count
            && first.Zip(second, (left, right) => left.SameContent(right)).All(equal => equal);
    }

    public string Describe()
    {
        return $"{SupportZones.Count} support / {ResistanceZones.Count} resistance zones, price scale {PriceScale}";
    }

    public string DescribeChangesAgainst(DefinitionEditModel current)
    {
        if (SameContent(current))
        {
            return "definition identical to your edit";
        }

        List<string> parts = [];
        if (PriceScale != current.PriceScale)
        {
            parts.Add($"price scale {current.PriceScale} → {PriceScale}");
        }

        parts.AddRange(ZoneDiffs("support", current.SupportZones, SupportZones));
        parts.AddRange(ZoneDiffs("resistance", current.ResistanceZones, ResistanceZones));

        return parts.Count == 0 ? "definition differs" : string.Join("; ", parts);
    }

    private static IEnumerable<string> ZoneDiffs(
        string kind,
        IReadOnlyList<ZoneEditModel> current,
        IReadOnlyList<ZoneEditModel> latest)
    {
        if (current.Count != latest.Count)
        {
            yield return $"{kind} zones {current.Count} → {latest.Count}";
        }

        int shared = Math.Min(current.Count, latest.Count);
        for (int index = 0; index < shared; index++)
        {
            ZoneEditModel before = current[index];
            ZoneEditModel after = latest[index];
            if (before.SameContent(after))
            {
                continue;
            }

            string label = string.IsNullOrWhiteSpace(after.Id) ? $"#{index + 1}" : $"'{after.Id}'";
            yield return $"{kind} zone {label} changed to {after.Lower}–{after.Upper} around {after.Level}";
        }
    }

    public static DefinitionEditModel Empty()
    {
        return new DefinitionEditModel();
    }

    public static DefinitionEditModel FromDto(MonitoringRuleDefinitionDto dto)
    {
        DefinitionEditModel model = new()
        {
            PriceScale = dto.PriceScale
        };

        if (dto.SupportZones is not null)
        {
            foreach (ChartZoneDto zone in dto.SupportZones)
            {
                model.SupportZones.Add(ZoneEditModel.FromDto(zone, "support"));
            }
        }

        if (dto.ResistanceZones is not null)
        {
            foreach (ChartZoneDto zone in dto.ResistanceZones)
            {
                model.ResistanceZones.Add(ZoneEditModel.FromDto(zone, "resistance"));
            }
        }

        return model;
    }
}
