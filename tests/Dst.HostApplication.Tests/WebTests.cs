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

        // Act
        using var siloHttpClient = app.CreateHttpClient("Dst-web-orleans-silo");
        var siloResponse = await siloHttpClient.GetAsync(new Uri("/orleans-dashboard", UriKind.Relative), cancellationToken);
        siloResponse.EnsureSuccessStatusCode();

        // Assert
        Assert.Equal(HttpStatusCode.OK, siloResponse.StatusCode);
    }
}
