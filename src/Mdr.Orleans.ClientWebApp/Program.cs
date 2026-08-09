using Mdr.Orleans.Core.Features.WeatherForecasts;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();

builder.AddKeyedRedisClient("orleans-clustering-redis");

builder.UseOrleansClient();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/hello-world", ([FromServices] IClusterClient clusterClient) =>
{
    return Results.Ok("Hello, World!");
})
.WithName("GetHelloWorld");

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
