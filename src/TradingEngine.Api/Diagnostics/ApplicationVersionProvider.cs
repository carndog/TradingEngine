using System.Reflection;
using TradingEngine.Contracts.Diagnostics;

namespace TradingEngine.Api.Diagnostics;

internal sealed class ApplicationVersionProvider
{
    private const string LocalCommit = "local";
    private const string UnknownVersion = "unknown";

    public VersionResponse GetCurrent()
    {
        Assembly assembly = typeof(Program).Assembly;
        AssemblyName assemblyName = assembly.GetName();
        AssemblyInformationalVersionAttribute? informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        AssemblyMetadataAttribute? commitMetadata = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "CommitSha");

        string application = assemblyName.Name ?? "TradingEngine.Api";
        string version = informationalVersion?.InformationalVersion
            ?? assemblyName.Version?.ToString()
            ?? UnknownVersion;
        string commit = string.IsNullOrWhiteSpace(commitMetadata?.Value)
            ? LocalCommit
            : commitMetadata.Value;

        return new VersionResponse(application, version, commit);
    }
}
