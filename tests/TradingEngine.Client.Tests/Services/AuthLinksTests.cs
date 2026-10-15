using TradingEngine.Client.Services;

namespace TradingEngine.Client.Tests.Services;

[TestFixture]
public sealed class AuthLinksTests
{
    [Test]
    public void SignInUrl_WithSameOriginPath_EncodesReturnUrl()
    {
        string url = AuthLinks.SignInUrl("/instruments?q=demo&page=2");

        Assert.That(
            url,
            Is.EqualTo("/.auth/login/aad?post_login_redirect_uri=%2Finstruments%3Fq%3Ddemo%26page%3D2"));
    }

    [Test]
    public void SignInUrl_WithNullReturnTo_PointsAtRoot()
    {
        string url = AuthLinks.SignInUrl((string?)null);

        Assert.That(
            url,
            Is.EqualTo("/.auth/login/aad?post_login_redirect_uri=%2F"));
    }

    [TestCase("https://evil.example/path")]
    [TestCase("//evil.example/path")]
    [TestCase("\\evil.example\\path")]
    [TestCase("javascript:alert(1)")]
    [TestCase("instruments")]
    [TestCase("")]
    public void SignInUrl_WithUnsafeReturnTo_FallsBackToRoot(string returnTo)
    {
        string url = AuthLinks.SignInUrl(returnTo);

        Assert.That(
            url,
            Is.EqualTo("/.auth/login/aad?post_login_redirect_uri=%2F"));
    }

    [Test]
    public void IsSafeReturnPath_WithRootPath_IsAccepted()
    {
        Assert.That(AuthLinks.IsSafeReturnPath("/"), Is.True);
    }
}
