using System.Reflection;
using Microsoft.Data.SqlClient;

namespace TradingEngine.Infrastructure.Tests.Authentication;

[TestFixture]
public sealed class SqlAuthenticationProviderRegistrationTests
{
    [Test]
    public void GetProvider_AfterAzureExtensionLoads_ReturnsAzureAuthenticationProviders()
    {
        Assembly.Load("Microsoft.Data.SqlClient.Extensions.Azure");

        Assert.Multiple(() =>
        {
            Assert.That(
                SqlAuthenticationProvider.GetProvider(
                    SqlAuthenticationMethod.ActiveDirectoryDefault),
                Is.Not.Null);
            Assert.That(
                SqlAuthenticationProvider.GetProvider(
                    SqlAuthenticationMethod.ActiveDirectoryManagedIdentity),
                Is.Not.Null);
        });
    }
}
