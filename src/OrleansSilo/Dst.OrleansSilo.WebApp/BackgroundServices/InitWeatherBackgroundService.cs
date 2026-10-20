using Dst.Core.Features.WeatherForecasts;

namespace Dst.OrleansSilo.WebApp.BackgroundServices;

public class InitWeatherBackgroundService : BackgroundService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<InitWeatherBackgroundService> _logger;
    
    public InitWeatherBackgroundService(IGrainFactory grainFactory, ILogger<InitWeatherBackgroundService> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var combinations = WeatherHelper.GetCombinations();

        try
        {
            var tasks = combinations.Select(async x =>
            {
                var weatherGrain = _grainFactory.GetGrain<ICityWeatherGrain>(x.Day.DayOfYear, x.Location);
                await weatherGrain.UpdateAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(10), stoppingToken).ConfigureAwait(false);
            });

            var chunks = tasks.Chunk(100);

            foreach (var chunk in chunks)
            {
                await Task.WhenAll(chunk).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("WeatherBackgroundService is stopping due to cancellation.");
        }
        finally
        {
            _logger.LogInformation("WeatherBackgroundService has stopped.");
        }
    }
}
