using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Woistes.Api;
using Woistes.Api.Auth;
using Woistes.Api.Components;
using Woistes.Api.Endpoints;
using Woistes.CtfParser;
using Woistes.Domain;
using Woistes.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Woistes");

if (string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<WoistesDbContext>(options =>
        options.UseInMemoryDatabase("Woistes"));
    builder.Services.AddScoped<ICatalogueRepository, CatalogueRepository>();
}
else
{
    builder.Services.AddWoistesInfrastructure(connectionString);
}

builder.Services.AddSingleton<ICtfParser, CtfFileParser>();

builder.Services.Configure<AllowedEmailsOptions>(
    builder.Configuration.GetSection("Authentication:AllowedEmails"));

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
}).AddCookie();

if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        // The deployment is currently served over plain HTTP (no TLS cert yet).
        // The default correlation cookie is SameSite=None+Secure, which browsers
        // refuse to store over HTTP, causing "Correlation failed" on callback.
        // Lax is sent on the top-level GET redirect back from Google.
        // TODO: revert to SameSite=None+Always once TLS is configured.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
}

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!string.IsNullOrEmpty(connectionString))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WoistesDbContext>();
    db.Database.Migrate();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<EmailAllowlistMiddleware>();

app.MapStaticAssets();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok("healthy")).AllowAnonymous();

app.MapGet("/login", () => Results.Challenge(new AuthenticationProperties
{
    RedirectUri = "/"
}, [GoogleDefaults.AuthenticationScheme])).AllowAnonymous();

app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).AllowAnonymous();

app.MapCatalogueEndpoints();
app.MapBrowseEndpoints();
app.MapSearchEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }
