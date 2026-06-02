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
var googleConfigured = !string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret);

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    // Only challenge via Google when it's actually configured; otherwise fall
    // back to the cookie scheme so a missing Google config (e.g. local dev with
    // no credentials) doesn't throw "No DefaultChallengeScheme found".
    options.DefaultChallengeScheme = googleConfigured
        ? GoogleDefaults.AuthenticationScheme
        : CookieAuthenticationDefaults.AuthenticationScheme;
}).AddCookie(options =>
{
    // Where the cookie scheme sends unauthenticated users. Point at /login so
    // protected Blazor pages land on a useful page (Google challenge or the
    // "not configured" notice) instead of the framework default /Account/Login.
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
});

if (googleConfigured)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
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

builder.Services.AddCascadingAuthenticationState();

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

app.MapGet("/login", () =>
{
    if (!googleConfigured)
    {
        return Results.Content(
            """
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><title>Login unavailable - Woistes</title>
            <style>body{font-family:system-ui,sans-serif;max-width:40rem;margin:4rem auto;padding:0 1rem;line-height:1.5}
            code{background:#f0f0f0;padding:.1rem .3rem;border-radius:3px}</style></head>
            <body><h1>Google login is not configured</h1>
            <p>Set the Google OAuth credentials, then restart the app:</p>
            <pre><code>dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_ID"
            dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_SECRET"
            dotnet user-secrets set "Authentication:AllowedEmails:Emails:0" "you@gmail.com"</code></pre>
            </body></html>
            """, "text/html");
    }
    var props = new AuthenticationProperties { RedirectUri = "/" };
    // Force Google's account chooser instead of silently reusing the last
    // account, so users (especially blocked ones) can pick a different account.
    props.SetParameter("prompt", "select_account");
    return Results.Challenge(props, [GoogleDefaults.AuthenticationScheme]);
}).AllowAnonymous();

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/loggedout");
}).AllowAnonymous();

app.MapGet("/loggedout", () => Results.Content(
    """
    <!DOCTYPE html>
    <html lang="en">
    <head><meta charset="utf-8"><title>Signed out - Woistes</title>
    <style>body{font-family:system-ui,sans-serif;display:flex;flex-direction:column;
    align-items:center;justify-content:center;height:100vh;margin:0;background:#f5f5f5}
    a{margin-top:1rem;padding:.6rem 1.2rem;background:#1a73e8;color:#fff;
    text-decoration:none;border-radius:4px}</style></head>
    <body><h1>You've been signed out</h1><a href="/login">Sign in again</a></body>
    </html>
    """, "text/html")).AllowAnonymous();

app.MapGet("/denied", (HttpContext ctx) =>
{
    var email = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
    var who = string.IsNullOrEmpty(email) ? "This account" : System.Net.WebUtility.HtmlEncode(email);
    return Results.Content(
        $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Access denied - Woistes</title>
        <style>body{font-family:system-ui,sans-serif;display:flex;flex-direction:column;
        align-items:center;justify-content:center;height:100vh;margin:0;background:#f5f5f5;text-align:center}
        p{color:#555;max-width:28rem}
        form{margin-top:1rem}
        button{padding:.6rem 1.2rem;background:#1a73e8;color:#fff;border:none;
        border-radius:4px;cursor:pointer;font-size:1rem}</style></head>
        <body>
        <h1>You're not authorized</h1>
        <p><strong>{{who}}</strong> is not on the allowed list for this application.
        Sign out and try a different Google account, or contact the administrator.</p>
        <form action="/logout" method="post"><button type="submit">Sign out &amp; switch account</button></form>
        </body></html>
        """, "text/html");
}).AllowAnonymous();

app.MapCatalogueEndpoints();
app.MapBrowseEndpoints();
app.MapSearchEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }
