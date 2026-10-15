using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Models;

public sealed class ZoneEditModel
{
    private ZoneEditModel(string kind, IReadOnlyList<ConditionEditModel> conditions)
    {
        Kind = kind;
        Conditions = conditions;
    }

    public string Kind { get; }

    public string Id { get; set; } = string.Empty;

    public decimal? Lower { get; set; }

    public decimal? Level { get; set; }

    public decimal? Upper { get; set; }

    public IReadOnlyList<ConditionEditModel> Conditions { get; }

    public ChartZoneDto ToDto()
    {
        return new ChartZoneDto(
            Id.Trim(),
            Lower ?? 0m,
            Level ?? 0m,
            Upper ?? 0m,
            Conditions.Select(condition => condition.ToDto()).ToList());
    }

    public ZoneEditModel Clone()
    {
        return new ZoneEditModel(
            Kind,
            Conditions.Select(condition => condition.Clone()).ToList())
        {
            Id = Id,
            Lower = Lower,
            Level = Level,
            Upper = Upper
        };
    }

    public bool SameContent(ZoneEditModel other)
    {
        return Kind == other.Kind
            && Id == other.Id
            && Lower == other.Lower
            && Level == other.Level
            && Upper == other.Upper
            && Conditions.Count == other.Conditions.Count
            && Conditions.Zip(other.Conditions, (left, right) => left.SameContent(right)).All(equal => equal);
    }

    public static ZoneEditModel CreateSupport()
    {
        return new ZoneEditModel(
            "support",
            [
                new ConditionEditModel("buy-zone", string.Empty),
                new ConditionEditModel("support-loss", string.Empty)
            ]);
    }

    public static ZoneEditModel CreateResistance()
    {
        return new ZoneEditModel(
            "resistance",
            [new ConditionEditModel("breakout", string.Empty)]);
    }

    public static ZoneEditModel FromDto(ChartZoneDto dto, string kind)
    {
        ZoneEditModel zone = kind == "support" ? CreateSupport() : CreateResistance();
        zone.Id = dto.Id ?? string.Empty;
        zone.Lower = dto.Lower;
        zone.Level = dto.Level;
        zone.Upper = dto.Upper;

        if (dto.Conditions is not null)
        {
            List<ConditionEditModel> conditions = zone.Conditions.ToList();
            foreach (ChartConditionDto condition in dto.Conditions)
            {
                ConditionEditModel? match = conditions.FirstOrDefault(existing => existing.Type == condition.Type);
                if (match is not null)
                {
                    match.ActionId = condition.ActionId ?? string.Empty;
                }
            }
        }

        return zone;
    }
}
