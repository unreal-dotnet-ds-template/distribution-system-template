using Dst.Core;
using Dst.OrleansSilo.WebApp.BackgroundServices;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.AddServiceDefaults();
builder.AddKeyedRedisClient("Dst-redis-orleans-clustering", (options) =>
{
    options.DisableHealthChecks = true;
});
builder.AddKeyedRedisClient("Dst-redis-orleans-default-storage", (options) =>
{
    options.DisableHealthChecks = true;
});

builder.UseOrleans(siloBuilder =>
{
    siloBuilder.AddDashboard();
    siloBuilder.AddRedisGrainStorage(DstConsts.OrleansDefaultStoreKey);
});

//builder.Services.AddHostedService<InitWeatherBackgroundService>();
//builder.Services.AddHostedService<RandomUpdateWeatherBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();
app.MapOrleansDashboard("/orleans-dashboard");

app.Run();
