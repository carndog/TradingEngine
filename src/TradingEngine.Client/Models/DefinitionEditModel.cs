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

    public bool IsModified => SupportZones.Count > 0 || ResistanceZones.Count > 0;

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
