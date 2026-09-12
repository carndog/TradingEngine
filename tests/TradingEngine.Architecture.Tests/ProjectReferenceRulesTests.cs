using System.Xml.Linq;

namespace TradingEngine.Architecture.Tests;

[TestFixture]
public sealed class ProjectReferenceRulesTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedReferences =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["TradingEngine.Domain"] = new HashSet<string>(StringComparer.Ordinal),
            ["TradingEngine.Application"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "TradingEngine.Domain"
            },
            ["TradingEngine.Infrastructure"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "TradingEngine.Application",
                "TradingEngine.Domain"
            },
            ["TradingEngine.Contracts"] = new HashSet<string>(StringComparer.Ordinal),
            ["TradingEngine.Api"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "TradingEngine.Application",
                "TradingEngine.Contracts",
                "TradingEngine.Infrastructure"
            }
        };

    [TestCase("TradingEngine.Domain")]
    [TestCase("TradingEngine.Application")]
    [TestCase("TradingEngine.Infrastructure")]
    [TestCase("TradingEngine.Contracts")]
    [TestCase("TradingEngine.Api")]
    public void ProductionProject_WithApprovedReferences_MatchesAllowList(string projectName)
    {
        string solutionRoot = SolutionRoot.Find();
        string projectPath = Path.Combine(solutionRoot, "src", projectName, $"{projectName}.csproj");
        string[] actualReferences = ReadIncludes(projectPath, "ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference)!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedReferences = AllowedReferences[projectName]
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(actualReferences, Is.EqualTo(expectedReferences));
    }

    [Test]
    public void DomainProject_WithCurrentPackages_ReferencesOnlyApprovedPackages()
    {
        string solutionRoot = SolutionRoot.Find();
        string projectPath = Path.Combine(
            solutionRoot,
            "src",
            "TradingEngine.Domain",
            "TradingEngine.Domain.csproj");
        string[] actualPackages = ReadIncludes(projectPath, "PackageReference")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(actualPackages, Is.EqualTo(["NodaTime"]));
    }

    private static IEnumerable<string> ReadIncludes(string projectPath, string itemName)
    {
        XDocument project = XDocument.Load(projectPath);

        return project
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => string.IsNullOrWhiteSpace(value) is false)
            .Select(value => value!);
    }
}
