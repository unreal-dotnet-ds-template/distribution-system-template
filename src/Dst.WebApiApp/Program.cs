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

app.MapGet("/weatherforecast", async ([FromServices] IClusterClient clusterClient, [FromQuery] int weekNumber = 0) =>
{
    var days = Enumerable.Range(0, 7)
        .Select(offset => DateOnly.FromDateTime(DateTime.Now.AddDays(offset + (weekNumber * 7))))
        .ToArray();

    var forecasts = days.Select(async day =>
    {
        var grain = clusterClient.GetGrain<IWeatherForecastGrain>(day.DayNumber);
        var forecast = await grain.GetAsync().ConfigureAwait(false);
        return new { Day = day, Forecast = forecast };
    });

    var result = await Task.WhenAll(forecasts).ConfigureAwait(false);

    return Results.Ok(result.ToDictionary(x => x.Day, x => x.Forecast));
}).WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.Run();
