using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Woistes.Api.Tests;

public class AuthenticationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuthenticationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UnauthenticatedRequest_ToCatalogues_RedirectsToLogin()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/catalogues");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task UnauthenticatedRequest_ToHealth_ReturnsOk()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequest_WithAllowedEmail_ReturnsOk()
    {
        var client = _factory.CreateAuthenticatedClient("test@gmail.com");

        var response = await client.GetAsync("/api/catalogues");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequest_WithDisallowedEmail_RedirectsToDenied()
    {
        var client = _factory.CreateAuthenticatedClient("hacker@gmail.com");

        var response = await client.GetAsync("/api/catalogues");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/denied", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DisallowedEmail_CanStillReachLogout()
    {
        // A blocked user must be able to escape (sign out / switch account),
        // so the allowlist check must not trap them on /logout.
        var client = _factory.CreateAuthenticatedClient("hacker@gmail.com");

        var response = await client.PostAsync("/logout", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/loggedout", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task DeniedPage_IsAccessibleAnonymously()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/denied");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not authorized", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeniedPage_NotBlockedForDisallowedUser()
    {
        // The denied page itself must render for a blocked user, not redirect-loop.
        var client = _factory.CreateAuthenticatedClient("hacker@gmail.com");

        var response = await client.GetAsync("/denied");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequest_WithMultipleAllowedEmails_AllWork()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<AllowedEmailsOptions>(opts =>
                    opts.Emails = new List<string> { "user1@gmail.com", "user2@gmail.com" });
            });
        });

        var client1 = factory.CreateDefaultClient(new AuthEmailHeaderHandler("user1@gmail.com"));
        var client2 = factory.CreateDefaultClient(new AuthEmailHeaderHandler("user2@gmail.com"));

        Assert.Equal(HttpStatusCode.OK, (await client1.GetAsync("/api/catalogues")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client2.GetAsync("/api/catalogues")).StatusCode);
    }

    [Fact]
    public async Task BlazorRoot_RequiresAuthentication()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task LoginEndpoint_IsAccessibleAnonymously()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/login");

        // In test mode, Google challenge won't redirect properly but it shouldn't 401/403
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RedirectsToLoggedOutPage()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.PostAsync("/logout", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/loggedout", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task LoggedOutPage_IsAccessibleAnonymously()
    {
        var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/loggedout");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("signed out", body, StringComparison.OrdinalIgnoreCase);
    }
}
