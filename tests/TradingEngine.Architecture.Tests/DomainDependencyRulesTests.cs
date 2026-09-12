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
    public void DomainTypes_WithCurrentDependencies_HaveNoForbiddenDependencies()
    {
        Assembly domainAssembly = typeof(WatchedInstrument).Assembly;

        TestResult result = Types
            .InAssembly(domainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenNamespaces)
            .GetResult();

        string[] failingTypes = result.FailingTypeNames?
            .Order(StringComparer.Ordinal)
            .ToArray()
            ?? [];

        Assert.Multiple(() =>
        {
            Assert.That(failingTypes, Is.Empty);
            Assert.That(result.IsSuccessful, Is.True);
        });
    }

    [Test]
    public void DomainAssembly_WithCurrentReferences_HasNoForbiddenAssemblies()
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
    public void DomainTypes_WithCurrentDesign_HaveNoClockDependency()
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
