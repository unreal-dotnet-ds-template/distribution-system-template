using Dst.WebApiApp.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.FeatureManagement;
using System.Text.Json.Serialization;

namespace Dst.WebApiApp.TrafficGeneration;

internal static class TrafficGenerationExtensions
{
    public static void AddTrafficGeneration(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<SelfTrafficCredential>();
        builder.Services.AddSingleton<TrafficGenerationFeatureFilter>();
        builder.Services.AddSingleton<IFeatureFilterMetadata>(services => services.GetRequiredService<TrafficGenerationFeatureFilter>());
        builder.Services.AddFeatureManagement();
        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, SelfTrafficAuthenticationHandler>(SelfTrafficCredential.Scheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddOptions<TrafficGenerationOptions>()
            .BindConfiguration("TrafficGeneration")
            .Validate(options => options.StartupDelay >= TimeSpan.Zero, "StartupDelay must not be negative.")
            .Validate(
                options => options.MaxRequestsPerSecond is > 0 and <= TrafficGenerationOptions.RequestsPerSecondCeiling,
                $"MaxRequestsPerSecond must be between 1 and {TrafficGenerationOptions.RequestsPerSecondCeiling}.")
            .Validate(
                options => options.RequestsPerSecond > 0 && options.RequestsPerSecond <= options.MaxRequestsPerSecond,
                "RequestsPerSecond must be between 1 and MaxRequestsPerSecond.")
            .Validate(options => options.MaxConcurrency is > 0 and <= 1000, "MaxConcurrency must be between 1 and 1000.")
            .ValidateOnStart();
        builder.Services.AddSingleton<TrafficRate>();
        // Synthetic POSTs must not be replayed by the service-default resilience pipeline.
#pragma warning disable EXTEXP0001
        builder.Services.AddHttpClient(SelfTrafficCredential.Scheme, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
            })
            .RedactLoggedHeaders([SelfTrafficCredential.HeaderName])
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        builder.Services.AddHostedService<RandomUpdateWeatherBackgroundService>();
    }

    public static void UseSelfTrafficValidation(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var credential = app.Services.GetRequiredService<SelfTrafficCredential>();
        app.Use(async (context, next) =>
        {
            credential.ValidateRequest(context);
            await next(context).ConfigureAwait(false);
        });
    }

    public static void MapTrafficGeneration(this WebApplication app, DstAuthOptions authOptions)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup("/api/features")
            .WithTags("Feature Flags")
            .RequireDstAuthorization(authOptions);

        group.MapGet("/random-weather-traffic", (IFeatureManager features, TrafficRate rate, CancellationToken cancellationToken) =>
                GetStateAsync(features, rate, cancellationToken))
            .WithName("GetRandomWeatherTraffic")
            .WithSummary("Get this replica's weather HTTP traffic generation state.");

        group.MapPut("/random-weather-traffic", async Task<Results<Ok<TrafficGenerationState>, ValidationProblem>> (
            TrafficGenerationUpdate update,
            TrafficGenerationFeatureFilter filter,
            TrafficRate rate,
            IFeatureManager features,
            CancellationToken cancellationToken) =>
        {
            if (update.RequestsPerSecond is { } requestsPerSecond && !rate.TrySet(requestsPerSecond))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(TrafficGenerationUpdate.RequestsPerSecond)] =
                        [$"Must be between 1 and {rate.MaxRequestsPerSecond}."],
                });
            }

            filter.SetEnabled(update.Enabled);
            return await GetStateAsync(features, rate, cancellationToken).ConfigureAwait(false);
        })
            .WithName("SetRandomWeatherTraffic")
            .WithSummary("Enable or disable weather HTTP traffic and optionally set its requests per second on this replica until restart.");
    }

    private static async Task<Ok<TrafficGenerationState>> GetStateAsync(
        IFeatureManager features, TrafficRate rate, CancellationToken cancellationToken)
    {
        var enabled = await features.IsEnabledAsync(TrafficGenerationFeatureFilter.FeatureName)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(new TrafficGenerationState(enabled, rate.RequestsPerSecond, rate.MaxRequestsPerSecond));
    }
}

/// <summary>Synthetic weather HTTP traffic state on the current Web API replica.</summary>
internal sealed record TrafficGenerationState(bool Enabled, int RequestsPerSecond, int MaxRequestsPerSecond);

/// <summary>Turns synthetic traffic on or off; <c>requestsPerSecond</c> optionally changes the target rate.</summary>
internal sealed record TrafficGenerationUpdate([property: JsonRequired] bool Enabled, int? RequestsPerSecond = null);
