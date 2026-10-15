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
        bool[] usedCurrent = new bool[current.Count];
        bool[] usedLatest = new bool[latest.Count];
        for (int latestIndex = 0; latestIndex < latest.Count; latestIndex++)
        {
            if (string.IsNullOrWhiteSpace(latest[latestIndex].Id))
            {
                continue;
            }

            int currentIndex = FindZoneIndexById(current, latest[latestIndex].Id);
            if (currentIndex < 0)
            {
                continue;
            }

            usedCurrent[currentIndex] = true;
            usedLatest[latestIndex] = true;
            foreach (string diff in DescribeZoneChange(kind, current[currentIndex], latest[latestIndex], latestIndex))
            {
                yield return diff;
            }
        }

        int cursor = 0;
        for (int latestIndex = 0; latestIndex < latest.Count; latestIndex++)
        {
            if (usedLatest[latestIndex])
            {
                continue;
            }

            while (cursor < current.Count && usedCurrent[cursor])
            {
                cursor++;
            }

            if (cursor >= current.Count)
            {
                yield return $"{kind} zone {ZoneLabel(latest[latestIndex], latestIndex)} added ({DescribeBounds(latest[latestIndex])})";
                continue;
            }

            usedCurrent[cursor] = true;
            usedLatest[latestIndex] = true;
            foreach (string diff in DescribeZoneChange(kind, current[cursor], latest[latestIndex], latestIndex))
            {
                yield return diff;
            }

            cursor++;
        }

        for (int currentIndex = 0; currentIndex < current.Count; currentIndex++)
        {
            if (usedCurrent[currentIndex] is false)
            {
                yield return $"{kind} zone {ZoneLabel(current[currentIndex], currentIndex)} removed (was {DescribeBounds(current[currentIndex])})";
            }
        }
    }

    private static int FindZoneIndexById(IReadOnlyList<ZoneEditModel> zones, string id)
    {
        for (int index = 0; index < zones.Count; index++)
        {
            if (zones[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static IEnumerable<string> DescribeZoneChange(
        string kind,
        ZoneEditModel before,
        ZoneEditModel after,
        int index)
    {
        string label = ZoneLabel(before, after, index);
        if (before.Id != after.Id)
        {
            yield return $"{kind} zone {label} id '{EmptyAsDash(before.Id)}' → '{EmptyAsDash(after.Id)}'";
        }

        if (before.Lower != after.Lower || before.Level != after.Level || before.Upper != after.Upper)
        {
            yield return $"{kind} zone {label} bounds {DescribeBounds(before)} → {DescribeBounds(after)}";
        }

        foreach (string diff in ConditionDiffs(kind, label, before.Conditions, after.Conditions))
        {
            yield return diff;
        }
    }

    private static IEnumerable<string> ConditionDiffs(
        string kind,
        string zoneLabel,
        IReadOnlyList<ConditionEditModel> current,
        IReadOnlyList<ConditionEditModel> latest)
    {
        bool[] usedCurrent = new bool[current.Count];
        for (int latestIndex = 0; latestIndex < latest.Count; latestIndex++)
        {
            int currentIndex = FindConditionIndexByType(current, usedCurrent, latest[latestIndex].Type);
            if (currentIndex < 0)
            {
                yield return $"{kind} zone {zoneLabel} condition '{latest[latestIndex].Type}' added (action '{EmptyAsDash(latest[latestIndex].ActionId)}')";
                continue;
            }

            usedCurrent[currentIndex] = true;
            if (current[currentIndex].ActionId != latest[latestIndex].ActionId)
            {
                yield return $"{kind} zone {zoneLabel} condition '{latest[latestIndex].Type}' action '{EmptyAsDash(current[currentIndex].ActionId)}' → '{EmptyAsDash(latest[latestIndex].ActionId)}'";
            }
        }

        for (int currentIndex = 0; currentIndex < current.Count; currentIndex++)
        {
            if (usedCurrent[currentIndex] is false)
            {
                yield return $"{kind} zone {zoneLabel} condition '{current[currentIndex].Type}' removed (was action '{EmptyAsDash(current[currentIndex].ActionId)}')";
            }
        }
    }

    private static int FindConditionIndexByType(
        IReadOnlyList<ConditionEditModel> conditions,
        bool[] used,
        string type)
    {
        for (int index = 0; index < conditions.Count; index++)
        {
            if (used[index] is false && conditions[index].Type == type)
            {
                return index;
            }
        }

        return -1;
    }

    private static string ZoneLabel(ZoneEditModel before, ZoneEditModel after, int index)
    {
        return ZoneLabel(string.IsNullOrWhiteSpace(after.Id) ? before : after, index);
    }

    private static string ZoneLabel(ZoneEditModel zone, int index)
    {
        return string.IsNullOrWhiteSpace(zone.Id) ? $"#{index + 1}" : $"'{zone.Id}'";
    }

    private static string DescribeBounds(ZoneEditModel zone)
    {
        return $"{zone.Lower}–{zone.Upper} around {zone.Level}";
    }

    private static string EmptyAsDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
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
