using Dst.Core.Features.WeatherForecasts;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.AddKeyedRedisClient("Dst-redis-orleans-clustering", (options) =>
{
    options.DisableHealthChecks = true;
});
builder.UseOrleansClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

var queriesGroup = app.MapGroup("/api/queries/weather")
    .WithTags("Weather Forecasts Queries");

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
    .WithTags("Weather Forecasts Commands");

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
