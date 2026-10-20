using Dst.Core.Features.WeatherForecasts;

namespace Dst.OrleansSilo.WebApp.BackgroundServices;

public class RandomUpdateWeatherBackgroundService : BackgroundService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<RandomUpdateWeatherBackgroundService> _logger;

    public RandomUpdateWeatherBackgroundService(IGrainFactory grainFactory, ILogger<RandomUpdateWeatherBackgroundService> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var combinations = WeatherHelper.GetCombinations();

        var amountToTake = combinations.Count / 10;

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                var shuffled = combinations.Shuffle().Take(amountToTake);

                var tasks = shuffled.Select(async x =>
                {
                    var weatherGrain = _grainFactory.GetGrain<ICityWeatherGrain>(x.Day.DayOfYear, x.Location);
                    await weatherGrain.UpdateAsync().ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(10), stoppingToken).ConfigureAwait(false);
                });

                var chunks = tasks.Chunk(100);

                foreach (var chunk in chunks)
                {
                    await Task.WhenAll(chunk).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(50), stoppingToken).ConfigureAwait(false);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);

                _logger.LogInformation("RandomUpdateWeatherBackgroundService has updated {Count} weather forecasts.", amountToTake);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("RandomUpdateWeatherBackgroundService is stopping due to cancellation.");
        }
        finally
        {
            _logger.LogInformation("RandomUpdateWeatherBackgroundService has stopped.");
        }
    }
}
