using System.Reflection;
using NetArchTest.Rules;
using NodaTime;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Architecture.Tests;

[TestFixture]
public sealed class DomainDependencyRulesTests
{
    private static readonly string[] ForbiddenNamespaces =
    [
        "TradingEngine.Application",
        "TradingEngine.Infrastructure",
        "TradingEngine.Api",
        "TradingEngine.Contracts",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Azure",
        "EToro",
        "eToro"
    ];

    [Test]
    public void Domain_types_do_not_depend_on_outer_layers_or_forbidden_technologies()
    {
        Assembly domainAssembly = typeof(WatchedInstrument).Assembly;

        TestResult result = Types
            .InAssembly(domainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenNamespaces)
            .GetResult();

        string failingTypes = string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>());
        Assert.That(result.IsSuccessful, Is.True, $"Domain dependency violations: {failingTypes}");
    }

    [Test]
    public void Domain_assembly_does_not_reference_forbidden_assemblies()
    {
        Assembly domainAssembly = typeof(WatchedInstrument).Assembly;
        string[] forbiddenReferences = domainAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(IsForbiddenAssembly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(forbiddenReferences, Is.Empty);
    }

    [Test]
    public void Domain_types_do_not_receive_or_store_a_clock()
    {
        Assembly domainAssembly = typeof(WatchedInstrument).Assembly;
        Type clockType = typeof(IClock);
        string[] violations = domainAssembly
            .GetTypes()
            .Where(type => DependsDirectlyOn(type, clockType))
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(violations, Is.Empty);
    }

    private static bool IsForbiddenAssembly(string assemblyName)
    {
        return assemblyName.StartsWith("TradingEngine.Application", StringComparison.Ordinal)
            || assemblyName.StartsWith("TradingEngine.Infrastructure", StringComparison.Ordinal)
            || assemblyName.StartsWith("TradingEngine.Api", StringComparison.Ordinal)
            || assemblyName.StartsWith("TradingEngine.Contracts", StringComparison.Ordinal)
            || assemblyName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || assemblyName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || assemblyName.StartsWith("Azure.", StringComparison.Ordinal)
            || assemblyName.Contains("Etoro", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DependsDirectlyOn(Type candidate, Type dependency)
    {
        BindingFlags flags = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static;

        bool fieldDependency = candidate
            .GetFields(flags)
            .Any(field => field.FieldType == dependency);
        bool propertyDependency = candidate
            .GetProperties(flags)
            .Any(property => property.PropertyType == dependency);
        bool constructorDependency = candidate
            .GetConstructors(flags)
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType == dependency);
        bool methodDependency = candidate
            .GetMethods(flags)
            .Any(method => method.ReturnType == dependency
                || method.GetParameters().Any(parameter => parameter.ParameterType == dependency));

        return fieldDependency || propertyDependency || constructorDependency || methodDependency;
    }
}
