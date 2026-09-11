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

    [Test]
    public void Production_projects_follow_the_reference_allow_list()
    {
        string solutionRoot = SolutionRoot.Find();

        foreach (KeyValuePair<string, IReadOnlySet<string>> rule in AllowedReferences)
        {
            string projectPath = Path.Combine(solutionRoot, "src", rule.Key, $"{rule.Key}.csproj");
            string[] actualReferences = ReadIncludes(projectPath, "ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(reference)!)
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] expectedReferences = rule.Value
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                actualReferences,
                Is.EqualTo(expectedReferences),
                $"Unexpected project-reference direction in {rule.Key}.");
        }
    }

    [Test]
    public void Domain_project_references_only_approved_packages()
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

        Assert.That(actualPackages, Is.EqualTo(new[] { "NodaTime" }));
    }

    private static IEnumerable<string> ReadIncludes(string projectPath, string itemName)
    {
        XDocument project = XDocument.Load(projectPath);

        return project
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!);
    }

}
