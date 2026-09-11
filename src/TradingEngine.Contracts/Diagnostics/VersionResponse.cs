namespace TradingEngine.Contracts.Diagnostics;

public sealed record VersionResponse(string Application, string Version, string Commit);
