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
