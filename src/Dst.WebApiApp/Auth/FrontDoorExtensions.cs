using Microsoft.AspNetCore.HttpOverrides;
using Dst.WebApiApp.TrafficGeneration;

namespace Dst.WebApiApp.Auth;

internal static class FrontDoorExtensions
{
    private const string FrontDoorIdHeader = "X-Azure-FDID";

    // Behind Azure Front Door the client IP arrives in X-Forwarded-For; trust it so per-IP rate limiting sees real clients.
    public static void AddFrontDoorForwardedHeaders(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(builder.Configuration["FrontDoor:Id"]))
        {
            return;
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            // ACA ingress (envoy) appends the Front Door edge address, so skip two hops to reach the client.
            options.ForwardLimit = 2;
        });
    }

    // When FrontDoor:Id is set, only requests that carry this Front Door's id are served,
    // which prevents bypassing Front Door (and its WAF) by calling the container app directly.
    public static void UseFrontDoorOnly(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var frontDoorId = app.Configuration["FrontDoor:Id"];
        if (string.IsNullOrWhiteSpace(frontDoorId))
        {
            return;
        }

        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            if (!SelfTrafficCredential.IsAuthenticated(context)
                && context.Request.Headers[FrontDoorIdHeader] != frontDoorId)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context).ConfigureAwait(false);
        });
    }
}
