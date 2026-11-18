using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Signals;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.Signals;

[TestFixture]
public sealed class SignalTrackingEndpointTests : ISignalTrackingStore
{
    private int _writes;

    [TestCase(null, HttpStatusCode.PreconditionRequired)]
    [TestCase("not-base64", HttpStatusCode.BadRequest)]
    [TestCase("\"AQ==\"", HttpStatusCode.BadRequest)]
    public async Task Put_InvalidToken_RejectsBeforeWriting(string? token, HttpStatusCode expected)
    {
        _writes = 0;
        using WebApplicationFactory<Program> factory = Factory();
        HttpRequestMessage request = new(HttpMethod.Put, $"/api/signals/{Guid.NewGuid()}/tracking")
        {
            Content = JsonContent.Create(new { archived = true })
        };
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", token);
        }

        HttpResponseMessage response = await factory.CreateClient().SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(expected));
        Assert.That(_writes, Is.Zero);
    }

    [Test]
    public async Task Put_ReadEtag_ChangesStateAndReturnsNewEtag()
    {
        using WebApplicationFactory<Program> factory = Factory();
        HttpClient client = factory.CreateClient();
        string path = $"/api/signals/{Guid.NewGuid()}/tracking";
        HttpResponseMessage current = await client.GetAsync(path);
        HttpRequestMessage request = new(HttpMethod.Put, path) { Content = JsonContent.Create(new { archived = true }) };
        request.Headers.IfMatch.Add(current.Headers.ETag!);

        HttpResponseMessage result = await client.SendAsync(request);

        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result.Headers.ETag, Is.Not.EqualTo(current.Headers.ETag));
    }

    public Task<Result<SignalTrackingState>> GetAsync(Guid signalId, CancellationToken cancellationToken)
    {
        return Task.FromResult<Result<SignalTrackingState>>(new SignalTrackingState(signalId, false, 0, null));
    }

    public Task<Result<SignalTrackingState>> SetAsync(Guid signalId, bool archived, long expectedVersion, string actor, Instant at, CancellationToken cancellationToken)
    {
        _writes++;
        return Task.FromResult<Result<SignalTrackingState>>(new SignalTrackingState(signalId, archived, 1, at.ToDateTimeOffset()));
    }

    private WebApplicationFactory<Program> Factory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSyntheticConnectionString();
            builder.ConfigureServices(services => services.AddSingleton<ISignalTrackingStore>(this));
        });
    }
}
