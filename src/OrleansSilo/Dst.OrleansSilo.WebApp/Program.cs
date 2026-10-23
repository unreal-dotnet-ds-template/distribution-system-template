using Dst.Core;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("Dst-orleans-clustering");
builder.AddKeyedAzureBlobServiceClient("Dst-orleans-grainstate");

builder.UseOrleans(siloBuilder =>
{
    siloBuilder.AddDashboard();
    siloBuilder.AddAzureBlobGrainStorage(DstConsts.OrleansDefaultStoreKey);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();
app.MapOrleansDashboard("/orleans-dashboard");

app.Run();
