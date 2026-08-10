using Dst.Core.Features.WeatherForecasts;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.AddKeyedRedisClient("orleans-clustering-redis");
builder.UseOrleansClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapGet("/weatherforecast", async ([FromServices] IClusterClient clusterClient) =>
{
    var currentMinute = DateTime.Now.Minute;
    var isEven = currentMinute % 2 == 0;
    var id = isEven ? 1 : 2;

    var grain = clusterClient.GetGrain<IWeatherForecastGrain>(id);
    var result = await grain.GetWeatherForecastsAsync().ConfigureAwait(false);
    return Results.Ok(result);
}).WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.Run();
