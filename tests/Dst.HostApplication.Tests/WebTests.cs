using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.Utilities;

namespace Dst.HostApplication.Tests;

public class WebTests
{
    [Fact]
    public async Task GetWebResourceRootReturnsOkStatusCode()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Dst_Aspires_AppHost>(cancellationToken);

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

        // Act
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("Dst-web-api", cancellationToken);

        using var httpClient = app.CreateHttpClient("Dst-web-api");
        var response = await httpClient.GetAsync(new Uri("/weatherforecast", UriKind.Relative), cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
