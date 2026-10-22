using Dst.Core.Features.WeatherForecasts;
using Dst.WebApiApp.Auth;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.AddFrontDoorForwardedHeaders();
var authOptions = builder.AddDstAuthentication();
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimit:PermitsPerMinute", 600),
                Window = TimeSpan.FromMinutes(1),
            }));
});
builder.AddKeyedAzureTableServiceClient("Dst-orleans-clustering");
builder.UseOrleansClient();

var app = builder.Build();

app.UseFrontDoorOnly();
app.UseRateLimiter();
app.UseDstAuthentication(authOptions);

// Docs are served in every environment; set Docs__Enabled=false to turn them off.
if (app.Configuration.GetValue("Docs:Enabled", true))
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(scalar => scalar.ConfigureDstScalar(authOptions)).AllowAnonymous();
}

var queriesGroup = app.MapGroup("/api/queries/weather")
    .WithTags("Weather Forecasts Queries")
    .RequireDstAuthorization(authOptions);

queriesGroup.MapGet("{year:int}/forecast", async ([FromServices] IClusterClient clusterClient, [FromRoute] int year) =>
{
    var viewGrain = clusterClient.GetGrain<IWeatherForecastViewGrain>(year);
    var result = await viewGrain.GetAsync().ConfigureAwait(true);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Problem(result.ErrorMessage);
});

queriesGroup.MapGet("{date:datetime}/week", async ([FromServices] IClusterClient clusterClient, [FromRoute] DateOnly date) =>
{
    var viewGrain = clusterClient.GetGrain<IWeatherForecastViewGrain>(date.Year);
    var result = await viewGrain.GetForWeekAsync(date).ConfigureAwait(true);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Problem(result.ErrorMessage);
});

queriesGroup.MapGet("{year:int}/months/{month:int}", async ([FromServices] IClusterClient clusterClient, [FromRoute] int year, [FromRoute] int month) =>
{
    var viewGrain = clusterClient.GetGrain<IWeatherForecastViewGrain>(year);
    var result = await viewGrain.GetForMonthAsync(month).ConfigureAwait(true);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Problem(result.ErrorMessage);
});

queriesGroup.MapGet("{year:int}/locations", async ([FromServices] IClusterClient clusterClient, [FromRoute] int year) =>
{
    var grain = clusterClient.GetGrain<IWeatherForecastViewGrain>(year);
    var result = await grain.GetLocations().ConfigureAwait(true);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Problem(result.ErrorMessage);
});

queriesGroup.MapGet("{date:datetime}/locations/{location:alpha}", async ([FromServices] IClusterClient clusterClient, [FromRoute] string location, [FromRoute] DateOnly date) =>
{
    var grain = clusterClient.GetGrain<ICityWeatherGrain>(date.DayNumber, location);
    var result = await grain.GetAsync().ConfigureAwait(true);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Problem(result.ErrorMessage);
});

var commandsGroup = app.MapGroup("/api/commands/weather")
    .WithTags("Weather Forecasts Commands")
    .RequireDstAuthorization(authOptions);

commandsGroup.MapPost("update", async ([FromServices] IClusterClient clusterClient, [FromBody] (DateOnly Date, string Location) data) =>
{
    var grain = clusterClient.GetGrain<ICityWeatherGrain>(data.Date.DayOfYear, data.Location);
    var result = await grain.UpdateAsync().ConfigureAwait(true);

    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Problem(result.ErrorMessage);
});

app.MapDefaultEndpoints();

app.Run();
