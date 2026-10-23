using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Dst.WebApiApp.Auth;
using Dst.WebApiApp.RateLimiting;
using Dst.WebApiApp.TrafficGeneration;
using Dst.WebApiApp.WeatherForecasts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace Dst.WebApiApp.Tests;

public sealed class TrafficGenerationTests
{
    private const string FlagPath = "/api/features/random-weather-traffic";
    private const string FlagSetting = "FeatureManagement:RandomWeatherTraffic:EnabledFor:0:Parameters:Enabled";

    [Theory]
    // ASPNETCORE_HTTP_PORTS in containers: Kestrel reports the IPv6 wildcard, but ACA has no IPv6 loopback.
    [InlineData("http://[::]:8080", "http://127.0.0.1:8080/")]
    [InlineData("http://0.0.0.0:8080", "http://127.0.0.1:8080/")]
    [InlineData("http://+:8080", "http://127.0.0.1:8080/")]
    [InlineData("http://*:8080", "http://127.0.0.1:8080/")]
    [InlineData("http://localhost:5123", "http://127.0.0.1:5123/")]
    [InlineData("http://127.0.0.1:5123", "http://127.0.0.1:5123/")]
    [InlineData("http://[::1]:5123", "http://[::1]:5123/")]
    public void SelfCallsUseALoopbackTheServerAccepts(string listening, string expected)
    {
        Assert.Equal(new Uri(expected), RandomUpdateWeatherBackgroundService.GetLoopbackAddress(["https://[::]:8443", listening]));
    }

    [Fact]
    public async Task FlagStartsFromConfigurationAndCanBeEnabledAndDisabled()
    {
        await using var host = await TestHost.StartAsync();
        var token = TestContext.Current.CancellationToken;

        var initial = await host.Client.GetFromJsonAsync<TrafficGenerationState>(FlagPath, token);
        Assert.False(initial!.Enabled);
        await Task.Delay(150, token);
        Assert.Empty(host.Requests);

        using var enabled = await host.Client.PutAsJsonAsync(FlagPath, new TrafficGenerationUpdate(true), token);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        await host.FirstRequest.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        var request = Assert.Single(host.Requests.Take(1));
        Assert.NotEqual(default, request.Date);
        Assert.False(string.IsNullOrWhiteSpace(request.Location));

        using var disabled = await host.Client.PutAsJsonAsync(FlagPath, new TrafficGenerationUpdate(false), token);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var state = await host.Client.GetFromJsonAsync<TrafficGenerationState>(FlagPath, token);
        Assert.False(state!.Enabled);
        await Task.Delay(150, token);
        var count = host.Requests.Count;
        await Task.Delay(200, token);
        Assert.Equal(count, host.Requests.Count);
    }

    [Fact]
    public async Task AppsettingsReloadIsObservedUntilLocallyOverridden()
    {
        await using var host = await TestHost.StartAsync(startupDelay: "01:00:00");
        var features = host.App.Services.GetRequiredService<IFeatureManager>();
        Assert.False(await features.IsEnabledAsync(TrafficGenerationFeatureFilter.FeatureName));
        host.App.Configuration[FlagSetting] = "true";
        ((IConfigurationRoot)host.App.Configuration).Reload();
        Assert.True(await features.IsEnabledAsync(TrafficGenerationFeatureFilter.FeatureName));
        host.App.Services.GetRequiredService<TrafficGenerationFeatureFilter>().SetEnabled(false);
        Assert.False(await features.IsEnabledAsync(TrafficGenerationFeatureFilter.FeatureName));
    }

    [Fact]
    public async Task SelfHttpTrafficWorksWithJwtAndFrontDoorWithoutOpeningManagementEndpoints()
    {
        // Bind like the container image (ASPNETCORE_HTTP_PORTS -> [::]).
        await using var host = await TestHost.StartAsync(jwt: true, frontDoor: true, enabled: true, listenUrl: "http://[::]:0");
        var token = TestContext.Current.CancellationToken;
        await host.FirstRequest.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

        using var anonymousUpdate = await host.Client.PostAsJsonAsync(
            SelfTrafficCredential.UpdatePath, new UpdateWeatherRequest(new DateOnly(2026, 1, 1), "London"), token);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousUpdate.StatusCode);

        host.Client.DefaultRequestHeaders.Add(
            SelfTrafficCredential.HeaderName, host.App.Services.GetRequiredService<SelfTrafficCredential>().Token);
        using var flagResponse = await host.Client.GetAsync(new Uri(FlagPath, UriKind.Relative), token);
        Assert.Equal(HttpStatusCode.Unauthorized, flagResponse.StatusCode);
        using var toggleResponse = await host.Client.PutAsJsonAsync(FlagPath, new TrafficGenerationUpdate(false), token);
        Assert.Equal(HttpStatusCode.Unauthorized, toggleResponse.StatusCode);

        using var updateResponse = await host.Client.PostAsJsonAsync(
            SelfTrafficCredential.UpdatePath, new UpdateWeatherRequest(new DateOnly(2026, 1, 1), "London"), token);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
    }

    [Fact]
    public async Task MissingOrWrongSelfCredentialDoesNotBypassFrontDoor()
    {
        await using var host = await TestHost.StartAsync(frontDoor: true);
        var token = TestContext.Current.CancellationToken;
        host.Client.DefaultRequestHeaders.Remove("X-Azure-FDID");
        using var missing = await host.Client.PostAsJsonAsync(
            SelfTrafficCredential.UpdatePath, new UpdateWeatherRequest(new DateOnly(2026, 1, 1), "London"), token);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);

        host.Client.DefaultRequestHeaders.Add(SelfTrafficCredential.HeaderName, new string('0', 64));
        using var wrong = await host.Client.PostAsJsonAsync(
            SelfTrafficCredential.UpdatePath, new UpdateWeatherRequest(new DateOnly(2026, 1, 1), "London"), token);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Empty(host.Requests);
    }

    [Theory]
    [InlineData("192.0.2.1", "POST", SelfTrafficCredential.UpdatePath)]
    [InlineData("127.0.0.1", "GET", SelfTrafficCredential.UpdatePath)]
    [InlineData("127.0.0.1", "PUT", FlagPath)]
    public void SelfCredentialIsRestrictedToTheSocketPeerMethodAndRoute(string peer, string method, string path)
    {
        var credential = new SelfTrafficCredential();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers[SelfTrafficCredential.HeaderName] = credential.Token;
        context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";

        credential.ValidateRequest(context);

        Assert.False(SelfTrafficCredential.IsAuthenticated(context));
    }

    [Fact]
    public void CredentialFromAnotherProcessIsNotAccepted()
    {
        var credential = new SelfTrafficCredential();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Method = "POST";
        context.Request.Path = SelfTrafficCredential.UpdatePath;
        context.Request.Headers[SelfTrafficCredential.HeaderName] = credential.Token;
        credential.ValidateRequest(context);
        Assert.True(SelfTrafficCredential.IsAuthenticated(context));

        var otherContext = new DefaultHttpContext();
        otherContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        otherContext.Request.Method = "POST";
        otherContext.Request.Path = SelfTrafficCredential.UpdatePath;
        otherContext.Request.Headers[SelfTrafficCredential.HeaderName] = credential.Token;
        new SelfTrafficCredential().ValidateRequest(otherContext);
        Assert.False(SelfTrafficCredential.IsAuthenticated(otherContext));
    }

    [Fact]
    public async Task MissingEnabledPropertyReturnsBadRequest()
    {
        await using var host = await TestHost.StartAsync();
        using var response = await host.Client.PutAsJsonAsync(FlagPath, new { }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RequestsPerSecondCanBeChangedWithinTheConfiguredMaximum()
    {
        await using var host = await TestHost.StartAsync();
        var token = TestContext.Current.CancellationToken;

        var initial = await host.Client.GetFromJsonAsync<TrafficGenerationState>(FlagPath, token);
        Assert.Equal(new TrafficGenerationState(false, 20, 100), initial);

        using var changed = await host.Client.PutAsJsonAsync(FlagPath, new TrafficGenerationUpdate(false, 50), token);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(new TrafficGenerationState(false, 50, 100), await changed.Content.ReadFromJsonAsync<TrafficGenerationState>(token));

        using var tooHigh = await host.Client.PutAsJsonAsync(FlagPath, new TrafficGenerationUpdate(true, 101), token);
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);
        using var zero = await host.Client.PutAsJsonAsync(FlagPath, new TrafficGenerationUpdate(true, 0), token);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        // Rejected updates change neither the rate nor the flag.
        var state = await host.Client.GetFromJsonAsync<TrafficGenerationState>(FlagPath, token);
        Assert.Equal(new TrafficGenerationState(false, 50, 100), state);
    }

    [Fact]
    public async Task GeneratorFollowsTargetRateAndBypassesThePerIpLimiter()
    {
        await using var host = await TestHost.StartAsync(enabled: true, requestsPerSecond: 50, permitsPerMinute: 5);
        var token = TestContext.Current.CancellationToken;
        await host.FirstRequest.Task.WaitAsync(TimeSpan.FromSeconds(10), token);

        var start = host.Requests.Count;
        await Task.Delay(TimeSpan.FromSeconds(2), token);
        var sent = host.Requests.Count - start;

        // About 100 expected; loose bounds keep the test stable on slow agents while proving the rate is applied.
        Assert.InRange(sent, 30, 150);

        // Ordinary callers from the same loopback address are still limited.
        HttpStatusCode status = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 10 && status != HttpStatusCode.TooManyRequests; attempt++)
        {
            using var response = await host.Client.GetAsync(new Uri(FlagPath, UriKind.Relative), token);
            status = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, status);
    }

    private sealed class TestHost(WebApplication app) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;
        public HttpClient Client { get; } = new();
        public ConcurrentQueue<UpdateWeatherRequest> Requests { get; } = new();
        public TaskCompletionSource FirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<TestHost> StartAsync(
            bool jwt = false,
            bool frontDoor = false,
            bool enabled = false,
            string startupDelay = "00:00:00",
            int requestsPerSecond = 20,
            int permitsPerMinute = 600,
            string listenUrl = "http://127.0.0.1:0")
        {
            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Mode"] = jwt ? "JwtBearer" : "None",
                ["Auth:Authority"] = "https://login.microsoftonline.com/common/v2.0",
                ["Auth:Audience"] = "api://test",
                ["FrontDoor:Id"] = frontDoor ? "test-front-door" : "",
                ["FeatureManagement:RandomWeatherTraffic:EnabledFor:0:Name"] = "TrafficGeneration",
                [FlagSetting] = enabled ? "true" : "false",
                ["TrafficGeneration:StartupDelay"] = startupDelay,
                ["TrafficGeneration:RequestsPerSecond"] = requestsPerSecond.ToString(CultureInfo.InvariantCulture),
                ["TrafficGeneration:MaxRequestsPerSecond"] = "100",
                ["RateLimit:PermitsPerMinute"] = permitsPerMinute.ToString(CultureInfo.InvariantCulture),
            });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.AddServiceDefaults();
            builder.AddFrontDoorForwardedHeaders();
            var authOptions = builder.AddDstAuthentication();
            builder.AddTrafficGeneration();
            builder.AddDstRateLimiter();
            var app = builder.Build();
            app.Urls.Add(listenUrl);
            app.UseSelfTrafficValidation();
            app.UseFrontDoorOnly();
            app.UseRateLimiter();
            app.UseDstAuthentication(authOptions);
            app.MapTrafficGeneration(authOptions);
            var host = new TestHost(app);
            app.MapGroup("/api/commands/weather")
                .RequireDstAuthorization(authOptions)
                .MapPost("/update", (UpdateWeatherRequest request) =>
                {
                    host.Requests.Enqueue(request);
                    host.FirstRequest.TrySetResult();
                    return TypedResults.Ok();
                })
                .WithWeatherTrafficAuthorization(authOptions);
            await app.StartAsync(TestContext.Current.CancellationToken);
            host.Client.BaseAddress = RandomUpdateWeatherBackgroundService.GetLoopbackAddress(app.Services.GetRequiredService<IServer>());
            if (frontDoor)
            {
                host.Client.DefaultRequestHeaders.Add("X-Azure-FDID", "test-front-door");
            }

            return host;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync(TestContext.Current.CancellationToken);
            await App.DisposeAsync();
        }
    }
}
