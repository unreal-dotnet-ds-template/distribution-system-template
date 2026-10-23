using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Dst.Core.Features.WeatherForecasts;
using Dst.WebApiApp.WeatherForecasts;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

namespace Dst.WebApiApp.TrafficGeneration;

internal sealed partial class RandomUpdateWeatherBackgroundService(
    IHttpClientFactory httpClientFactory,
    IFeatureManager features,
    TrafficRate rate,
    IServer server,
    IHostApplicationLifetime lifetime,
    SelfTrafficCredential credential,
    IOptionsMonitor<TrafficGenerationOptions> options,
    ILogger<RandomUpdateWeatherBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(30);

    private long _sent;
    private long _succeeded;
    private long _failed;
    private long _skipped;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        {
            try
            {
                await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
                WaitingForStartupDelay(logger, options.CurrentValue.StartupDelay);
                await Task.Delay(options.CurrentValue.StartupDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        // Concurrency is fixed for the process lifetime; change MaxConcurrency and restart to resize it.
        var maxConcurrency = options.CurrentValue.MaxConcurrency;
        using var concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        using var client = httpClientFactory.CreateClient(SelfTrafficCredential.Scheme);
        client.BaseAddress = GetLoopbackAddress(server);
        client.DefaultRequestHeaders.Add(SelfTrafficCredential.HeaderName, credential.Token);

        try
        {
            await RunAsync(client, concurrency, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Stopping(logger);
        }
        finally
        {
            // Let in-flight requests observe cancellation before the semaphore and client are disposed.
            for (var index = 0; index < maxConcurrency; index++)
            {
                await concurrency.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task RunAsync(HttpClient client, SemaphoreSlim concurrency, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        var previousTick = Stopwatch.GetTimestamp();
        var lastReport = previousTick;
        var budget = 0d;
        (bool Enabled, int RequestsPerSecond)? previousState = null;

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(previousTick, now);
            previousTick = now;

            bool enabled;
            int requestsPerSecond;
            try
            {
                enabled = await features.IsEnabledAsync(TrafficGenerationFeatureFilter.FeatureName).ConfigureAwait(false);
                requestsPerSecond = rate.RequestsPerSecond;
            }
            catch (OptionsValidationException exception)
            {
                InvalidConfiguration(logger, exception);
                continue;
            }

            if (previousState != (enabled, requestsPerSecond))
            {
                StateChanged(logger, enabled, requestsPerSecond);
                previousState = (enabled, requestsPerSecond);
            }

            if (enabled)
            {
                // Accumulate fractional requests so low rates are exact, but cap catch-up bursts after stalls.
                var burstLimit = Math.Max(1d, requestsPerSecond * TickInterval.TotalSeconds * 2);
                budget = Math.Min(budget + (requestsPerSecond * elapsed.TotalSeconds), burstLimit);
                var due = (int)budget;
                budget -= due;

                for (var index = 0; index < due; index++)
                {
                    if (!concurrency.Wait(0, CancellationToken.None))
                    {
                        Interlocked.Add(ref _skipped, due - index);
                        break;
                    }

                    Interlocked.Increment(ref _sent);
                    _ = SendAsync(client, concurrency, stoppingToken);
                }
            }
            else
            {
                budget = 0;
            }

            var sinceReport = Stopwatch.GetElapsedTime(lastReport, now);
            if (sinceReport >= ReportInterval)
            {
                Report(requestsPerSecond, sinceReport);
                lastReport = now;
            }
        }
    }

    private async Task SendAsync(HttpClient client, SemaphoreSlim concurrency, CancellationToken stoppingToken)
    {
        try
        {
            var date = new DateOnly(DateTime.UtcNow.Year, 1, 1).AddDays(RandomNumberGenerator.GetInt32(365));
            var location = WeatherHelper.Locations[RandomNumberGenerator.GetInt32(WeatherHelper.Locations.Count)];
            using var response = await client.PostAsJsonAsync(
                SelfTrafficCredential.UpdatePath,
                new UpdateWeatherRequest(date, location),
                stoppingToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref _succeeded);
            }
            else if (Interlocked.Increment(ref _failed) == 1)
            {
                RequestFailed(logger, (int)response.StatusCode);
            }
        }
        catch (HttpRequestException exception)
        {
            if (Interlocked.Increment(ref _failed) == 1)
            {
                TransportFailed(logger, exception);
            }
        }
        catch (OperationCanceledException exception) when (!stoppingToken.IsCancellationRequested)
        {
            if (Interlocked.Increment(ref _failed) == 1)
            {
                TransportFailed(logger, exception);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
        finally
        {
            concurrency.Release();
        }
    }

    private void Report(int requestsPerSecond, TimeSpan period)
    {
        var sent = Interlocked.Exchange(ref _sent, 0);
        var succeeded = Interlocked.Exchange(ref _succeeded, 0);
        var failed = Interlocked.Exchange(ref _failed, 0);
        var skipped = Interlocked.Exchange(ref _skipped, 0);
        if (sent + skipped == 0)
        {
            return;
        }

        var actual = sent / period.TotalSeconds;
        if (failed > 0 || skipped > 0)
        {
            SummaryWithErrors(logger, requestsPerSecond, actual, sent, succeeded, failed, skipped, period);
        }
        else
        {
            Summary(logger, requestsPerSecond, actual, sent, succeeded, period);
        }
    }

    internal static Uri GetLoopbackAddress(IServer server) =>
        GetLoopbackAddress(server.Features.Get<IServerAddressesFeature>()?.Addresses ?? []);

    internal static Uri GetLoopbackAddress(IEnumerable<string> addresses)
    {
        var address = addresses
            .Select(address => Uri.TryCreate(address.Replace("://+:", "://0.0.0.0:", StringComparison.Ordinal)
                .Replace("://*:", "://0.0.0.0:", StringComparison.Ordinal), UriKind.Absolute, out var uri) ? uri : null)
            .FirstOrDefault(address => address?.Scheme == Uri.UriSchemeHttp)
            ?? throw new InvalidOperationException("HTTP traffic generation requires an HTTP listener on the Web API.");
        // Wildcard binds ([::] in containers) also accept IPv4, and IPv6 loopback may not exist (Azure Container Apps).
        var loopback = address.HostNameType == UriHostNameType.IPv6
            && IPAddress.TryParse(address.Host.Trim('[', ']'), out var bound) && IPAddress.IsLoopback(bound)
            ? IPAddress.IPv6Loopback
            : IPAddress.Loopback;
        return new UriBuilder(address) { Host = loopback.ToString() }.Uri;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic weather HTTP traffic generator waits {StartupDelay} before its first check.")]
    private static partial void WaitingForStartupDelay(ILogger logger, TimeSpan startupDelay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic weather HTTP traffic enabled: {Enabled} (target {RequestsPerSecond} requests/second).")]
    private static partial void StateChanged(ILogger logger, bool enabled, int requestsPerSecond);

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic weather HTTP traffic: target {RequestsPerSecond} rps, actual {ActualRequestsPerSecond:F1} rps, sent {Sent}, succeeded {Succeeded} in {Period}.")]
    private static partial void Summary(ILogger logger, int requestsPerSecond, double actualRequestsPerSecond, long sent, long succeeded, TimeSpan period);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic weather HTTP traffic: target {RequestsPerSecond} rps, actual {ActualRequestsPerSecond:F1} rps, sent {Sent}, succeeded {Succeeded}, failed {Failed}, skipped at the concurrency limit {Skipped} in {Period}.")]
    private static partial void SummaryWithErrors(ILogger logger, int requestsPerSecond, double actualRequestsPerSecond, long sent, long succeeded, long failed, long skipped, TimeSpan period);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Synthetic weather HTTP request failed with status {StatusCode}; further failures in this period are only counted.")]
    private static partial void RequestFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Synthetic weather HTTP transport failed; further failures in this period are only counted.")]
    private static partial void TransportFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Synthetic weather HTTP traffic configuration is invalid; skipping until it is fixed.")]
    private static partial void InvalidConfiguration(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Synthetic weather HTTP traffic generator is stopping.")]
    private static partial void Stopping(ILogger logger);
}
