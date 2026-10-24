using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Dst.WebApiApp.Modules.WeatherForecasts;

namespace Dst.WebApiApp.Modules.Auth;

internal enum DstAuthMode
{
    None,
    JwtBearer,
}

internal sealed class DstAuthOptions
{
    public const string SectionName = "Auth";

    public DstAuthMode Mode { get; set; } = DstAuthMode.None;

    public string? Authority { get; set; }

    public string? Audience { get; set; }

    public DstScalarAuthOptions Scalar { get; set; } = new();

    public bool IsScalarOAuthEnabled => !string.IsNullOrWhiteSpace(Scalar.ClientId);
}

/// <summary>Optional OAuth2 (authorization code + PKCE) login button in Scalar. Enabled when ClientId is set.</summary>
internal sealed class DstScalarAuthOptions
{
    public string? ClientId { get; set; }

    // Defaults are derived from an Entra ID authority (".../v2.0"); set explicitly for other providers.
    public string? AuthorizationUrl { get; set; }

    public string? TokenUrl { get; set; }

    public string[] Scopes { get; set; } = [];
}

/// <summary>
/// Pluggable authentication for the public Web API. Configured through the "Auth" section
/// (e.g. env var Auth__Mode=JwtBearer). Replace this file to plug in your own scheme.
/// </summary>
internal static class DstAuthExtensions
{
    private const string BearerScheme = "Bearer";
    private const string OAuthScheme = "OAuth2";

    public static DstAuthOptions AddDstAuthentication(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(DstAuthOptions.SectionName).Get<DstAuthOptions>() ?? new DstAuthOptions();

        if (options.Mode == DstAuthMode.JwtBearer)
        {
            if (string.IsNullOrWhiteSpace(options.Authority) || string.IsNullOrWhiteSpace(options.Audience))
            {
                throw new InvalidOperationException("Auth:Authority and Auth:Audience are required when Auth:Mode is JwtBearer.");
            }

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(jwt =>
                {
                    jwt.Authority = options.Authority;
                    jwt.Audience = options.Audience;
                });
            builder.Services.AddAuthorization();
        }

        builder.Services.AddOpenApi(openApi =>
        {
            // The generated server URL is the container app host, which rejects calls that bypass Front Door.
            // Without servers, Scalar targets the origin the docs were loaded from (Front Door or localhost).
            openApi.AddDocumentTransformer((document, _, _) =>
            {
                document.Servers?.Clear();
                return Task.CompletedTask;
            });

            if (options.Mode != DstAuthMode.JwtBearer)
            {
                return;
            }

            openApi.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                };
                document.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [],
                    },
                ];

                if (options.IsScalarOAuthEnabled && ResolveOAuthUrls(options) is var (authorizationUrl, tokenUrl))
                {
                    var scopes = options.Scalar.Scopes.ToDictionary(scope => scope, scope => scope);
                    document.Components.SecuritySchemes[OAuthScheme] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.OAuth2,
                        Flows = new OpenApiOAuthFlows
                        {
                            AuthorizationCode = new OpenApiOAuthFlow
                            {
                                AuthorizationUrl = new Uri(authorizationUrl),
                                TokenUrl = new Uri(tokenUrl),
                                Scopes = scopes,
                            },
                        },
                    };
                    document.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(OAuthScheme, document)] = [.. options.Scalar.Scopes],
                    });
                }

                return Task.CompletedTask;
            });
        });

        return options;
    }

    public static void ConfigureDstScalar(this ScalarOptions scalar, DstAuthOptions options)
    {
        if (options.Mode != DstAuthMode.JwtBearer)
        {
            return;
        }

        if (options.IsScalarOAuthEnabled && ResolveOAuthUrls(options) is var (authorizationUrl, tokenUrl))
        {
            scalar.AddPreferredSecuritySchemes(OAuthScheme)
                .AddAuthorizationCodeFlow(OAuthScheme, flow =>
                {
                    flow.ClientId = options.Scalar.ClientId;
                    flow.AuthorizationUrl = authorizationUrl;
                    flow.TokenUrl = tokenUrl;
                    flow.Pkce = Pkce.Sha256;
                    flow.SelectedScopes = options.Scalar.Scopes;
                });
        }
        else
        {
            scalar.AddPreferredSecuritySchemes(BearerScheme);
        }
    }

    private static (string AuthorizationUrl, string TokenUrl)? ResolveOAuthUrls(DstAuthOptions options)
    {
        var scalar = options.Scalar;
        if (!string.IsNullOrWhiteSpace(scalar.AuthorizationUrl) && !string.IsNullOrWhiteSpace(scalar.TokenUrl))
        {
            return (scalar.AuthorizationUrl, scalar.TokenUrl);
        }

        const string entraSuffix = "/v2.0";
        var authority = options.Authority?.TrimEnd('/');
        if (authority is not null && authority.EndsWith(entraSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = authority[..^entraSuffix.Length];
            return ($"{baseUrl}/oauth2/v2.0/authorize", $"{baseUrl}/oauth2/v2.0/token");
        }

        throw new InvalidOperationException("Auth:Scalar:AuthorizationUrl and Auth:Scalar:TokenUrl are required when Auth:Authority is not an Entra ID '/v2.0' authority.");
    }

    public static void UseDstAuthentication(this WebApplication app, DstAuthOptions options)
    {
        if (options.Mode == DstAuthMode.None)
        {
            return;
        }

        app.UseAuthentication();
        app.UseAuthorization();
    }

    // No-op when auth is disabled, so endpoints stay open by default.
    public static TBuilder RequireDstAuthorization<TBuilder>(this TBuilder builder, DstAuthOptions options)
        where TBuilder : IEndpointConventionBuilder
        => options.Mode == DstAuthMode.None ? builder : builder.RequireAuthorization();

    public static TBuilder WithWeatherTrafficAuthorization<TBuilder>(this TBuilder builder, DstAuthOptions options)
        where TBuilder : IEndpointConventionBuilder
        => options.Mode == DstAuthMode.None ? builder : builder.RequireAuthorization(policy =>
            policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, SelfSyntheticTrafficCredential.Scheme)
                .RequireAuthenticatedUser());
}
