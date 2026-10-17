using Dst.Core;
using Dst.Core.Features.WeatherForecasts;

namespace Dst.Features.WeatherForecasts;

public class WeatherForecastGrain : Grain, IWeatherForecastGrain
{
    private readonly IPersistentState<WeatherForecastGrainState> _stateProvider;

    public WeatherForecastGrain(
        [PersistentState(DstConsts.WeatherForecastsStateKey, DstConsts.OrleansDefaultStoreKey)]
        IPersistentState<WeatherForecastGrainState> stateProvider)
    {
        _stateProvider = stateProvider;
    }

    public ValueTask<WeatherForecast> GetAsync()
    {
        if(_stateProvider.State.IsInitialized is false)
        {
            _stateProvider.State = WeatherForecastHelper.Generate();
        }

        var result = new WeatherForecast(
            Date: _stateProvider.State.Date,
            TemperatureC: _stateProvider.State.TemperatureC,
            Summary: _stateProvider.State.Summary);

        return new ValueTask<WeatherForecast>(result);
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await _stateProvider.ReadStateAsync(cancellationToken).ConfigureAwait(true);
        await base.OnActivateAsync(cancellationToken).ConfigureAwait(true);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await _stateProvider.WriteStateAsync(cancellationToken).ConfigureAwait(true);
        await base.OnDeactivateAsync(reason, cancellationToken).ConfigureAwait(true);
    }
}
