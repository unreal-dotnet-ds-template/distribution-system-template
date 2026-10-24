using System.Threading.RateLimiting;
using Dst.WebApiApp.Modules.WeatherForecasts;

namespace Dst.WebApiApp.Modules.RateLimiting;

internal static class DstRateLimitingExtensions
{
    public static void AddDstRateLimiter(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var permitsPerMinute = builder.Configuration.GetValue("RateLimit:PermitsPerMinute", 600);
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                // Verified self-calls all share 127.0.0.1; their rate is capped by SyntheticGeneration:MaxRequestsPerSecond instead.
                SelfSyntheticTrafficCredential.IsAuthenticated(context)
                    ? RateLimitPartition.GetNoLimiter(SelfSyntheticTrafficCredential.Scheme)
                    : RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = permitsPerMinute,
                            Window = TimeSpan.FromMinutes(1),
                        }));
        });
    }
}
