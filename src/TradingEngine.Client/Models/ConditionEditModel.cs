using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Models;

public sealed class ConditionEditModel
{
    public ConditionEditModel(string type, string actionId)
    {
        Type = type;
        ActionId = actionId;
    }

    public string Type { get; }

    public string ActionId { get; set; }

    public ChartConditionDto ToDto()
    {
        return new ChartConditionDto(Type, ActionId.Trim());
    }

    public static ConditionEditModel FromDto(ChartConditionDto dto)
    {
        return new ConditionEditModel(dto.Type ?? string.Empty, dto.ActionId ?? string.Empty);
    }
}
