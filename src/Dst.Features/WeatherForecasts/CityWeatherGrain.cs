using Dst.Core;
using Dst.Core.Features.WeatherForecasts;
using Dst.Core.Helpers;

namespace Dst.Features.WeatherForecasts;

public class CityWeatherGrain : Grain, ICityWeatherGrain
{
    private readonly IPersistentState<CityWeatherGrainState> _stateProvider;
    private readonly IGrainFactory _grainFactory;

    private IDisposable? _updateTimer;
    private bool _isDirty;

    public CityWeatherGrain(
        [PersistentState(DstConsts.WeatherForecastsStateKey, DstConsts.OrleansDefaultStoreKey)]
        IPersistentState<CityWeatherGrainState> stateProvider,
        IGrainFactory grainFactory)
    {
        _stateProvider = stateProvider;
        _grainFactory = grainFactory;
    }

    public async ValueTask<GrainResult<CityWeatherResult>> GetAsync()
    {
        if (_stateProvider.State.IsInitialized is false)
        {
            var updateResult = await UpdateAsync().ConfigureAwait(true);
            if (!updateResult.IsSuccess)
            {
                return GrainResult<CityWeatherResult>.Failure(updateResult.ErrorMessage ?? "Failed to initialize weather forecast.");
            }
        }

        var result = new CityWeatherResult(
            Date: _stateProvider.State.Date,
            Location: _stateProvider.State.Location,
            TemperatureC: _stateProvider.State.TemperatureC,
            Summary: _stateProvider.State.Summary);

        return GrainResult<CityWeatherResult>.Success(result);
    }

    public ValueTask<GrainResult<bool>> UpdateAsync()
    {
        var day = this.GetPrimaryKeyLong(out string? location);
        var date = new DateOnly(DateTime.UtcNow.Year, 1, 1).AddDays((int)day - 1);

        var newData = WeatherForecastHelper.Generate(date, location!);

        if (_stateProvider.State.TemperatureC != newData.TemperatureC)
        {
            _stateProvider.State = newData;
            _isDirty = true;
        }

        return ValueTask.FromResult(GrainResult<bool>.Success(true));
    }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await _stateProvider.ReadStateAsync(cancellationToken).ConfigureAwait(true);
        EnsureUpdateTimerSubscribed();
        await base.OnActivateAsync(cancellationToken).ConfigureAwait(true);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _updateTimer?.Dispose();
        _updateTimer = null;

        await SendDataAsync(cancellationToken).ConfigureAwait(true);
        await base.OnDeactivateAsync(reason, cancellationToken).ConfigureAwait(true);
    }

    private void EnsureUpdateTimerSubscribed()
    {
        if (_updateTimer is not null) return;

        _updateTimer = this.RegisterGrainTimer(
            callback: SendDataAsync,
            options: new(
                dueTime: TimeSpan.FromMilliseconds(200),
                period: TimeSpan.FromMinutes(2)));
    }

    private async Task SendDataAsync(CancellationToken cancellationToken)
    {
        if (_isDirty is false) return;

        await _stateProvider.WriteStateAsync(cancellationToken).ConfigureAwait(true);

        var viewGrain = _grainFactory.GetGrain<IWeatherForecastViewGrain>(_stateProvider.State.Date.Year);

        await viewGrain.SetValue(
                location: _stateProvider.State.Location,
                day: (uint)_stateProvider.State.Date.DayOfYear,
                temperatureC: _stateProvider.State.TemperatureC)
            .ConfigureAwait(true);

        _isDirty = false;
    }
}
