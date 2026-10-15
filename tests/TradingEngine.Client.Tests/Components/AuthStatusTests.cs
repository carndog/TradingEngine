using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Client.Components;
using TradingEngine.Client.Services;

namespace TradingEngine.Client.Tests.Components;

[TestFixture]
public sealed class AuthStatusTests
{
    [Test]
    public async Task SessionInvalidated_WhileSignedIn_ReloadsAndShowsSignInRequired()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{"mode":"entra","displayName":"Test User"}"""));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized,
            """{"detail":"unauthorized","code":"auth.unauthorized"}"""));
        using BunitContext ctx = new();
        SessionState sessionState = new();
        HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost/") };
        ctx.Services.AddSingleton(sessionState);
        ctx.Services.AddSingleton(new TradingEngineApiClient(http, TimeSpan.FromSeconds(30), [], null, sessionState));
        IRenderedComponent<AuthStatus> cut = ctx.Render<AuthStatus>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Signed in as Test User")),
            TimeSpan.FromSeconds(5));

        sessionState.Invalidate();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Sign in required")),
            TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(2));
            Assert.That(cut.Markup, Does.Not.Contain("Signed in as Test User"));
        });
    }
}
