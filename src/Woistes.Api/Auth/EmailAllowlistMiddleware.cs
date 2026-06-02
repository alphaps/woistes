using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Woistes.Api.Auth;

public class EmailAllowlistMiddleware
{
    private readonly RequestDelegate _next;

    public EmailAllowlistMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<AllowedEmailsOptions> options)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var email = context.User.FindFirstValue(ClaimTypes.Email);
            var allowed = options.Value.GetAllEmails();

            if (email == null || !allowed.Contains(email, StringComparer.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await _next(context);
    }
}
