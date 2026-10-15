using TradingEngine.Client.Services;
using TradingEngine.Contracts.Session;

namespace TradingEngine.Client.Tests.Services;

[TestFixture]
public sealed class SessionPresentationTests
{
    private const string SignInUrl = "/.auth/login/aad?post_login_redirect_uri=%2Finstruments";

    [Test]
    public void Checking_Always_PresentsPendingState()
    {
        SessionPresentation presentation = SessionPresentation.Checking;

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Status, Is.EqualTo(SessionStatus.Checking));
            Assert.That(presentation.SignInUrl, Is.Null);
            Assert.That(presentation.SignOutUrl, Is.Null);
            Assert.That(presentation.CanRetry, Is.False);
        });
    }

    [Test]
    public void From_WhenDevelopmentMode_PresentsLocalDevelopmentWithoutSignIn()
    {
        ApiResult<SessionResponse> result =
            ApiResult<SessionResponse>.Ok(new SessionResponse("development", null));

        SessionPresentation presentation = SessionPresentation.From(result, SignInUrl);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Status, Is.EqualTo(SessionStatus.Development));
            Assert.That(presentation.Label, Is.EqualTo("Local development"));
            Assert.That(presentation.SignInUrl, Is.Null);
            Assert.That(presentation.SignOutUrl, Is.Null);
            Assert.That(presentation.CanRetry, Is.False);
        });
    }

    [Test]
    public void From_WhenEntraModeWithDisplayName_PresentsSignedInWithSignOut()
    {
        ApiResult<SessionResponse> result =
            ApiResult<SessionResponse>.Ok(new SessionResponse("entra", "Owner"));

        SessionPresentation presentation = SessionPresentation.From(result, SignInUrl);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Status, Is.EqualTo(SessionStatus.SignedIn));
            Assert.That(presentation.Label, Is.EqualTo("Signed in as Owner"));
            Assert.That(presentation.SignOutUrl, Is.EqualTo(AuthLinks.SignOutUrl));
            Assert.That(presentation.SignInUrl, Is.Null);
        });
    }

    [Test]
    public void From_WhenEntraModeWithoutDisplayName_PresentsGenericSignedIn()
    {
        ApiResult<SessionResponse> result =
            ApiResult<SessionResponse>.Ok(new SessionResponse("entra", null));

        SessionPresentation presentation = SessionPresentation.From(result, SignInUrl);

        Assert.That(presentation.Label, Is.EqualTo("Signed in"));
    }

    [Test]
    public void From_WhenUnauthorized_PresentsReauthenticationPath()
    {
        ApiResult<SessionResponse> result = ApiResult<SessionResponse>.Fail(
            ApiError.Create(401, "authentication.principal_required", "missing", null));

        SessionPresentation presentation = SessionPresentation.From(result, SignInUrl);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Status, Is.EqualTo(SessionStatus.Unauthorized));
            Assert.That(presentation.SignInUrl, Is.EqualTo(SignInUrl));
            Assert.That(presentation.SignOutUrl, Is.Null);
        });
    }

    [Test]
    public void From_WhenForbidden_PresentsAccessDeniedWithSignOut()
    {
        ApiResult<SessionResponse> result = ApiResult<SessionResponse>.Fail(
            ApiError.Create(403, null, null, null));

        SessionPresentation presentation = SessionPresentation.From(result, SignInUrl);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Status, Is.EqualTo(SessionStatus.Forbidden));
            Assert.That(presentation.Label, Is.EqualTo("Access denied"));
            Assert.That(presentation.SignOutUrl, Is.EqualTo(AuthLinks.SignOutUrl));
            Assert.That(presentation.SignInUrl, Is.Null);
        });
    }

    [Test]
    public void From_WhenLookupUnavailable_PresentsRetryableUnavailable()
    {
        ApiResult<SessionResponse> result = ApiResult<SessionResponse>.Fail(
            ApiError.Network("unreachable"));

        SessionPresentation presentation = SessionPresentation.From(result, SignInUrl);

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Status, Is.EqualTo(SessionStatus.Unavailable));
            Assert.That(presentation.CanRetry, Is.True);
            Assert.That(presentation.SignInUrl, Is.Null);
            Assert.That(presentation.SignOutUrl, Is.Null);
        });
    }

    [Test]
    public void From_WhenModeUnrecognised_PresentsRetryableUnavailable()
    {
        ApiResult<SessionResponse> result =
            ApiResult<SessionResponse>.Ok(new SessionResponse("other", null));

        SessionPresentation presentation = SessionPresentation.From(result, SignInUrl);

        Assert.That(presentation.Status, Is.EqualTo(SessionStatus.Unavailable));
    }
}
