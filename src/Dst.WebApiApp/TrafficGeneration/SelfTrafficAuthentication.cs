using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Dst.WebApiApp.TrafficGeneration;

internal sealed class SelfTrafficCredential
{
    public const string Scheme = "SelfTraffic";
    public const string HeaderName = "X-Dst-Self-Traffic";
    public const string UpdatePath = "/api/commands/weather/update";
    private const string ForwardedForHeader = "X-Forwarded-For";
    private const string OriginalForHeader = "X-Original-For";
    private static readonly object AuthenticatedRequestKey = new();
    private readonly byte[] _credential = RandomNumberGenerator.GetBytes(32);

    public string Token => Convert.ToHexString(_credential);

    public void ValidateRequest(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Self-calls never carry forwarding headers. In ACA, ASPNETCORE_FORWARDEDHEADERS_ENABLED rewrites RemoteIpAddress
        // before this middleware runs and records the socket peer in X-Original-For, so either header means the
        // address below may not be the real peer.
        if (context.Request.Headers.ContainsKey(ForwardedForHeader) || context.Request.Headers.ContainsKey(OriginalForHeader)
            || context.Connection.RemoteIpAddress is not { } peer || !IPAddress.IsLoopback(peer)
            || !HttpMethods.IsPost(context.Request.Method)
            || !context.Request.Path.Equals(UpdatePath, StringComparison.OrdinalIgnoreCase)
            || context.Request.Headers[HeaderName] is not { Count: 1 } values
            || values[0] is not { Length: 64 } token)
        {
            return;
        }

        var expected = Encoding.ASCII.GetBytes(Token);
        if (CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(token)))
        {
            context.Items[AuthenticatedRequestKey] = true;
        }
    }

    public static bool IsAuthenticated(HttpContext context) => context.Items.ContainsKey(AuthenticatedRequestKey);
}

internal sealed class SelfTrafficAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!SelfTrafficCredential.IsAuthenticated(Context))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "weather-traffic-generator")],
            SelfTrafficCredential.Scheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SelfTrafficCredential.Scheme)));
    }
}
