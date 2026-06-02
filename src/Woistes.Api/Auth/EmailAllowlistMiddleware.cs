using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Woistes.Api.Auth;

public class EmailAllowlistMiddleware
{
    private readonly RequestDelegate _next;

    // Paths a blocked-but-authenticated user must still reach, so they can read
    // the denial notice and sign out / switch accounts instead of being trapped.
    private static readonly string[] ExemptPaths =
        ["/denied", "/logout", "/loggedout", "/login", "/health"];

    public EmailAllowlistMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<AllowedEmailsOptions> options)
    {
        var path = context.Request.Path;
        var exempt = ExemptPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

        if (!exempt && context.User.Identity?.IsAuthenticated == true)
        {
            var email = context.User.FindFirstValue(ClaimTypes.Email);
            var allowed = options.Value.GetAllEmails();

            if (email == null || !allowed.Contains(email, StringComparer.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/denied");
                return;
            }
        }

        await _next(context);
    }
}
