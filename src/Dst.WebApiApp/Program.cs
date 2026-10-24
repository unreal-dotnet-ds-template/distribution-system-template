using Dst.WebApiApp.Modules.Auth;
using Dst.WebApiApp.Modules.RateLimiting;
using Dst.WebApiApp.Modules.WeatherForecasts;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.AddFrontDoorForwardedHeaders();
var authOptions = builder.AddDstAuthentication();
builder.AddSyntheticGeneration();
builder.AddDstRateLimiter();
builder.AddKeyedAzureTableServiceClient("Dst-orleans-clustering");
builder.UseOrleansClient();

var app = builder.Build();

app.UseSelfTrafficValidation();
app.UseFrontDoorOnly();
app.UseRateLimiter();
app.UseDstAuthentication(authOptions);

// Docs are served in every environment; set Docs__Enabled=false to turn them off.
if (app.Configuration.GetValue("Docs:Enabled", true))
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(scalar => scalar.ConfigureDstScalar(authOptions)).AllowAnonymous();
}

app.MapWeatherForecastsEndpoints(authOptions);

app.MapSyntheticGeneration(authOptions);

app.MapDefaultEndpoints();

app.Run();
