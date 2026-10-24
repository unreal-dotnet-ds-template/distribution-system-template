using Dst.WebApiApp.Modules.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.FeatureManagement;
using System.Text.Json.Serialization;

namespace Dst.WebApiApp.Modules.WeatherForecasts;

internal static class SyntheticGenerationExtensions
{
    public static void AddSyntheticGeneration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<SelfSyntheticTrafficCredential>();
        builder.Services.AddSingleton<SyntheticTrafficFeatureFilter>();
        builder.Services.AddSingleton<IFeatureFilterMetadata>(services => services.GetRequiredService<SyntheticTrafficFeatureFilter>());
        builder.Services.AddFeatureManagement();
        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, SelfSyntheticTrafficAuthenticationHandler>(SelfSyntheticTrafficCredential.Scheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddOptions<SyntheticTrafficOptions>()
            .BindConfiguration("SyntheticGeneration")
            .Validate(options => options.StartupDelay >= TimeSpan.Zero, "StartupDelay must not be negative.")
            .Validate(
                options => options.MaxRequestsPerSecond is > 0 and <= SyntheticTrafficOptions.RequestsPerSecondCeiling,
                $"MaxRequestsPerSecond must be between 1 and {SyntheticTrafficOptions.RequestsPerSecondCeiling}.")
            .Validate(
                options => options.RequestsPerSecond > 0 && options.RequestsPerSecond <= options.MaxRequestsPerSecond,
                "RequestsPerSecond must be between 1 and MaxRequestsPerSecond.")
            .Validate(options => options.MaxConcurrency is > 0 and <= 1000, "MaxConcurrency must be between 1 and 1000.")
            .ValidateOnStart();
        builder.Services.AddSingleton<TrafficRate>();
        // Synthetic POSTs must not be replayed by the service-default resilience pipeline.
#pragma warning disable EXTEXP0001
        builder.Services.AddHttpClient(SelfSyntheticTrafficCredential.Scheme, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
            })
            .RedactLoggedHeaders([SelfSyntheticTrafficCredential.HeaderName])
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        builder.Services.AddHostedService<SyntheticBackgroundService>();
    }

    public static void UseSelfTrafficValidation(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var credential = app.Services.GetRequiredService<SelfSyntheticTrafficCredential>();
        app.Use(async (context, next) =>
        {
            credential.ValidateRequest(context);
            await next(context).ConfigureAwait(false);
        });
    }

    public static void MapSyntheticGeneration(this WebApplication app, DstAuthOptions authOptions)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup("/api/features")
            .WithTags("Feature Flags")
            .RequireDstAuthorization(authOptions);

        group.MapGet("/synthetic-traffic", (IFeatureManager features, TrafficRate rate, CancellationToken cancellationToken) =>
                GetStateAsync(features, rate, cancellationToken))
            .WithName("GetSyntheticTraffic")
            .WithSummary("Get this replica's Synthetic HTTP generation state.");

        group.MapPut("/synthetic-traffic", async Task<Results<Ok<SyntheticGenerationState>, ValidationProblem>> (
            SyntheticGenerationUpdate update,
            SyntheticTrafficFeatureFilter filter,
            TrafficRate rate,
            IFeatureManager features,
            CancellationToken cancellationToken) =>
        {
            if (update.RequestsPerSecond is { } requestsPerSecond && !rate.TrySet(requestsPerSecond))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(SyntheticGenerationUpdate.RequestsPerSecond)] =
                        [$"Must be between 1 and {rate.MaxRequestsPerSecond}."],
                });
            }

            filter.SetEnabled(update.Enabled);
            return await GetStateAsync(features, rate, cancellationToken).ConfigureAwait(false);
        })
            .WithName("SetSyntheticTraffic")
            .WithSummary("Enable or disable Synthetic HTTP traffic and optionally set its requests per second on this replica until restart.");
    }

    private static async Task<Ok<SyntheticGenerationState>> GetStateAsync(
        IFeatureManager features, TrafficRate rate, CancellationToken cancellationToken)
    {
        var enabled = await features.IsEnabledAsync(SyntheticTrafficFeatureFilter.FeatureName)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new SyntheticGenerationState(enabled, rate.RequestsPerSecond, rate.MaxRequestsPerSecond));
    }
}

/// <summary>Synthetic weather HTTP traffic state on the current Web API replica.</summary>
internal sealed record SyntheticGenerationState(bool Enabled, int RequestsPerSecond, int MaxRequestsPerSecond);

/// <summary>Turns synthetic traffic on or off; <c>requestsPerSecond</c> optionally changes the target rate.</summary>
internal sealed record SyntheticGenerationUpdate([property: JsonRequired] bool Enabled, int? RequestsPerSecond = null);
