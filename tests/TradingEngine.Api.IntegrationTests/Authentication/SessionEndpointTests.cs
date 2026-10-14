using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TradingEngine.Api.IntegrationTests.MonitoringRules;

namespace TradingEngine.Api.IntegrationTests.Authentication;

[TestFixture]
public sealed class SessionEndpointTests
{
    private const string PrincipalHeader = "X-MS-CLIENT-PRINCIPAL";

    private static readonly Dictionary<string, string?> TrustEnabled = new()
    {
        ["Authentication:EasyAuth:TrustPlatformHeaders"] = "true"
    };

    [Test]
    public async Task GetSession_WithTrustDisabled_ReportsDevelopmentMode()
    {
        using MonitoringRuleApiHost host = new();

        HttpResponseMessage response = await host.Client.GetAsync("/api/session");
        JsonElement body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("mode").GetString(), Is.EqualTo("development"));
            Assert.That(body.GetProperty("displayName").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task GetSession_WithTrustedPrincipal_ReportsEntraModeAndDisplayName()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);
        host.Client.DefaultRequestHeaders.Add(PrincipalHeader, EncodePrincipal(
            ("name", "Synthetic Owner"),
            ("preferred_username", "synthetic.owner@example.test"),
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", "11111111-2222-3333-4444-555555555555")));

        HttpResponseMessage response = await host.Client.GetAsync("/api/session");
        JsonElement body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("mode").GetString(), Is.EqualTo("entra"));
            Assert.That(body.GetProperty("displayName").GetString(), Is.EqualTo("Synthetic Owner"));
        });
    }

    [Test]
    public async Task GetSession_WithTrustedPrincipalWithoutNameClaims_OmitsDisplayName()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);
        host.Client.DefaultRequestHeaders.Add(PrincipalHeader, EncodePrincipal(
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", "11111111-2222-3333-4444-555555555555")));

        HttpResponseMessage response = await host.Client.GetAsync("/api/session");
        JsonElement body = await ReadBodyAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("displayName").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task GetSession_WithTrustEnabledAndNoPrincipal_ReturnsUnauthorizedProblem()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);

        HttpResponseMessage response = await host.Client.GetAsync("/api/session");
        string? code = await MonitoringRuleApiHost.ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(code, Is.EqualTo("authentication.principal_required"));
        });
    }

    [Test]
    public async Task GetSession_Response_ContainsNoClaimsOrTokens()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);
        host.Client.DefaultRequestHeaders.Add(PrincipalHeader, EncodePrincipal(
            ("name", "Synthetic Owner"),
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", "11111111-2222-3333-4444-555555555555")));

        HttpResponseMessage response = await host.Client.GetAsync("/api/session");
        string text = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("claims"));
            Assert.That(text, Does.Not.Contain("token"));
            Assert.That(text, Does.Not.Contain("11111111-2222-3333-4444-555555555555"));
        });
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response)
    {
        JsonDocument? document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.That(document, Is.Not.Null);

        return document!.RootElement.Clone();
    }

    private static string EncodePrincipal(params (string Type, string Value)[] claims)
    {
        string claimsJson = string.Join(
            ",",
            claims.Select(claim => $$"""{"typ":"{{claim.Type}}","val":"{{claim.Value}}"}"""));
        string json = $$"""
            {"auth_typ":"aad","claims":[{{claimsJson}}],"name_typ":"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name","role_typ":"http://schemas.microsoft.com/ws/2008/06/identity/claims/role"}
            """;

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
