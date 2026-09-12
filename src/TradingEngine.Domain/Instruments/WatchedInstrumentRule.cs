namespace TradingEngine.Domain.Instruments;

public enum WatchedInstrumentRule
{
    AlreadyMonitored,
    NotMonitored,
    SamplingPolicyUnchanged,
    ChangePrecedesLatestChange
}
