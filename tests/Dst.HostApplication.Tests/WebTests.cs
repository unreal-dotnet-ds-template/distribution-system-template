using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.Utilities;

namespace Dst.HostApplication.Tests;

public class WebTests
{
    private static readonly TimeSpan ResourceTimeout = TimeSpan.FromMinutes(2);
    private readonly ITestOutputHelper _output;

    public WebTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task GetWebResourceRootReturnsOkStatusCode()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Dst_Aspires_AppHost>(cancellationToken);

        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
            logging.AddFilter("Orleans.", LogLevel.Debug);

            // Выводим логи Aspire и Orleans напрямую в xUnit output
            logging.AddProvider(new XUnitLoggerProvider(_output));
        });

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        // Пошаговая проверка готовности ресурсов с информативным выводом
        _output.WriteLine("[Test] Waiting for Redis...");
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("Dst-redis-orleans-clustering", cancellationToken)
            .WaitAsync(ResourceTimeout, cancellationToken);

        _output.WriteLine("[Test] Waiting for Orleans Silo...");
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("Dst-web-orleans-silo", cancellationToken)
            .WaitAsync(ResourceTimeout, cancellationToken);

        _output.WriteLine("[Test] Waiting for Web API...");
        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("Dst-web-api", cancellationToken)
            .WaitAsync(ResourceTimeout, cancellationToken);

        // Act
        using var httpClient = app.CreateHttpClient("Dst-web-api");
        var response = await httpClient
            .GetAsync(new Uri("/weatherforecast", UriKind.Relative), cancellationToken)
            .WaitAsync(ResourceTimeout, cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
