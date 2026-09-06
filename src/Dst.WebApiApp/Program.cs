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
    app.MapGet("/", () => Results.Redirect("/scalar/v1"));
}

app.MapGet("/weatherforecast/{weekNumber}", async ([FromServices] IClusterClient clusterClient, int weekNumber = 0) =>
{
    var grain = clusterClient.GetGrain<IWeatherForecastGrain>(weekNumber);
    var result = await grain.GetWeatherForecastsAsync().ConfigureAwait(false);
    return Results.Ok(result);
}).WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.Run();
