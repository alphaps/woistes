using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Woistes.Infrastructure;

namespace Woistes.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public TestWebApplicationFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<WoistesDbContext>)
                    || d.ServiceType == typeof(WoistesDbContext)
                    || d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true)
                .ToList();
            foreach (var d in descriptorsToRemove)
                services.Remove(d);

            services.AddDbContext<WoistesDbContext>(options =>
                options.UseSqlite(_connection));

            services.Configure<AllowedEmailsOptions>(opts =>
                opts.Emails = new List<string> { "test@gmail.com" });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            });

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WoistesDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }

    public HttpClient CreateAnonymousClient()
    {
        return CreateDefaultClient(new AnonymousHeaderHandler());
    }

    public HttpClient CreateAuthenticatedClient(string email)
    {
        return CreateDefaultClient(new AuthEmailHeaderHandler(email));
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // X-Test-Anonymous header = unauthenticated request
        if (Request.Headers.ContainsKey("X-Test-Anonymous"))
            return Task.FromResult(AuthenticateResult.NoResult());

        // X-Test-Email header = authenticate as that email
        var email = Request.Headers.TryGetValue("X-Test-Email", out var emailHeader)
            ? emailHeader.ToString()
            : "test@gmail.com";

        var claims = new[]
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, email)
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 302;
        Response.Headers.Location = "/login";
        return Task.CompletedTask;
    }
}

public class AnonymousHeaderHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Test-Anonymous", "true");
        return base.SendAsync(request, cancellationToken);
    }
}

public class AuthEmailHeaderHandler : DelegatingHandler
{
    private readonly string _email;

    public AuthEmailHeaderHandler(string email)
    {
        _email = email;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Test-Email", _email);
        return base.SendAsync(request, cancellationToken);
    }
}
