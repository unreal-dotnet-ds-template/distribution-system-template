using System.Net.Http.Json;
using Aspire.Hosting;

namespace Dst.HostApplication.Tests;

public class WebTests
{
    [Fact]
    public async Task GetWebResourcesRootReturnsOkStatusCode()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Dst_Aspires_AppHost>(cancellationToken);

        var webApi = appHost.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "Dst-web-api");
        appHost.CreateResourceBuilder(webApi)
            .WithEnvironment("TrafficGeneration__StartupDelay", "00:00:00")
            .WithEnvironment("TrafficGeneration__RequestsPerSecond", "4");

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("Dst-web-api", cancellationToken);
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("Dst-web-orleans-silo", cancellationToken);

        // Act
        using var webApiHttpClient = app.CreateHttpClient("Dst-web-api");
        var apiResponse = await webApiHttpClient.GetAsync(new Uri("/scalar/v1", UriKind.Relative), cancellationToken);
        apiResponse.EnsureSuccessStatusCode();

        // Assert
        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);

        using var initialFlag = await webApiHttpClient.GetAsync(
            new Uri("/api/features/random-weather-traffic", UriKind.Relative), cancellationToken);
        initialFlag.EnsureSuccessStatusCode();

        using var enabled = await webApiHttpClient.PutAsJsonAsync(
            "/api/features/random-weather-traffic", new { enabled = true }, cancellationToken);
        enabled.EnsureSuccessStatusCode();

        // Only the background HTTP worker writes weather data in this test.
        string[] locations = [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (locations.Length == 0)
        {
            using var projection = await webApiHttpClient.GetAsync(
                new Uri($"/api/queries/weather/{DateTime.UtcNow.Year}/locations", UriKind.Relative), timeout.Token);
            if (projection.IsSuccessStatusCode)
            {
                locations = await projection.Content.ReadFromJsonAsync<string[]>(timeout.Token) ?? [];
            }
            else
            {
                Assert.Equal(HttpStatusCode.InternalServerError, projection.StatusCode);
            }

            if (locations.Length == 0)
            {
                await Task.Delay(100, timeout.Token);
            }
        }

        Assert.All(locations, location => Assert.False(string.IsNullOrWhiteSpace(location)));
        using var disabled = await webApiHttpClient.PutAsJsonAsync(
            "/api/features/random-weather-traffic", new { enabled = false }, cancellationToken);
        disabled.EnsureSuccessStatusCode();

        // Act
        using var siloHttpClient = app.CreateHttpClient("Dst-web-orleans-silo");
        var siloResponse = await siloHttpClient.GetAsync(new Uri("/orleans-dashboard", UriKind.Relative), cancellationToken);
        siloResponse.EnsureSuccessStatusCode();

        // Assert
        Assert.Equal(HttpStatusCode.OK, siloResponse.StatusCode);
    }

    [Fact]
    public async Task JwtBearerModeRejectsAnonymousApiCallsButServesDocs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Dst_Aspires_AppHost>(
            [
                "Auth:Mode=JwtBearer",
                "Auth:Authority=https://login.microsoftonline.com/common/v2.0",
                "Auth:Audience=api://dst-test",
                "Auth:Scalar:ClientId=test-client",
                "Auth:Scalar:Scopes:0=api://dst-test/access",
            ],
            cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("Dst-web-api", cancellationToken);

        using var client = app.CreateHttpClient("Dst-web-api");

        var apiResponse = await client.GetAsync(new Uri("/api/queries/weather/2025/forecast", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, apiResponse.StatusCode);

        var docsResponse = await client.GetAsync(new Uri("/scalar/v1", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, docsResponse.StatusCode);

        var openApi = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), cancellationToken);
        Assert.Contains("\"bearer\"", openApi, StringComparison.Ordinal);
        Assert.Contains("oauth2/v2.0/authorize", openApi, StringComparison.Ordinal);
    }
}
