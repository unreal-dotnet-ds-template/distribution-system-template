using System.Net.Http.Json;
using System.Text.Json;
using Dst.WebApiApp.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dst.WebApiApp.Tests;

public sealed class OpenApiTests
{
    [Theory]
    [InlineData("None")]
    [InlineData("JwtBearer")]
    public async Task DocumentHasNoAbsoluteServerSoScalarCallsTheOriginItWasLoadedFrom(string authMode)
    {
        var token = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Mode"] = authMode,
            ["Auth:Authority"] = "https://login.microsoftonline.com/common/v2.0",
            ["Auth:Audience"] = "api://test",
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.AddDstAuthentication();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapOpenApi();
        await app.StartAsync(token);

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = TrafficGeneration.RandomUpdateWeatherBackgroundService.GetLoopbackAddress(
                    app.Services.GetRequiredService<IServer>()),
            };
            // Behind Front Door the app sees the container app host, not the public one.
            client.DefaultRequestHeaders.Host = "dst-1-web-api-ca.example.azurecontainerapps.io";

            using var document = await client.GetFromJsonAsync<JsonDocument>(new Uri("/openapi/v1.json", UriKind.Relative), token);

            Assert.True(
                !document!.RootElement.TryGetProperty("servers", out var servers) || servers.GetArrayLength() == 0,
                $"Unexpected servers: {servers}");
        }
        finally
        {
            await app.StopAsync(token);
        }
    }
}
