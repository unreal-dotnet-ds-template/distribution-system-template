using Dst.Core;
using Dst.Core.Features.WeatherForecasts;
using Dst.Core.Helpers;

namespace Dst.Features.WeatherForecasts;

public class WeatherForecastViewGrain : Grain, IWeatherForecastViewGrain
{
    private readonly IPersistentState<WeatherForecastViewGrainState> _stateProvider;

    private IDisposable? _updateTimer;
    private bool _isDirty;

    public WeatherForecastViewGrain(
        [PersistentState(DstConsts.WeatherForecastViewStateKey, DstConsts.OrleansDefaultStoreKey)]
        IPersistentState<WeatherForecastViewGrainState> stateProvider)
    {
        _stateProvider = stateProvider;
    }

    public ValueTask<GrainResult<GroupedWeatherForecastResult>> GetAsync()
    {
        if (_stateProvider.State.IsInitialized is false)
        {
            return ValueTask.FromResult(GrainResult<GroupedWeatherForecastResult>.Failure("No data available"));
        }

        var year = (int)this.GetPrimaryKeyLong();

        var result = new GroupedWeatherForecastResult(
            Locations: _stateProvider.State.Locations.ToDictionary(
                key => key.Key,
                key => key.Value
                    .Select((val, index) => new WeatherForecastResult(
                        Date: new DateOnly(year, 1, 1).AddDays(index),
                        TemperatureC: val)
                    ).ToArray()));

        return ValueTask.FromResult(GrainResult<GroupedWeatherForecastResult>.Success(result));
    }

    public ValueTask<GrainResult<GroupedWeatherForecastResult>> GetForMonthAsync(int month)
    {
        if (_stateProvider.State.IsInitialized is false)
        {
            return ValueTask.FromResult(GrainResult<GroupedWeatherForecastResult>.Failure("No data available"));
        }

        var year = (int)this.GetPrimaryKeyLong();
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var firstDayOfMonth = new DateOnly(year, month, 1);
        var dayOfYear = firstDayOfMonth.DayOfYear;

        var result = new GroupedWeatherForecastResult(
            Locations: _stateProvider.State.Locations.ToDictionary(
                key => key.Key,
                key => key.Value
                    .Skip(dayOfYear - 1)
                    .Take(daysInMonth)
                    .Select((val, index) => new WeatherForecastResult(
                        Date: firstDayOfMonth.AddDays(index),
                        TemperatureC: val)
                    ).ToArray()));

        return ValueTask.FromResult(GrainResult<GroupedWeatherForecastResult>.Success(result));
    }

    public ValueTask<GrainResult<GroupedWeatherForecastResult>> GetForWeekAsync(DateOnly dateFrom)
    {
        if (_stateProvider.State.IsInitialized is false)
        {
            return ValueTask.FromResult(GrainResult<GroupedWeatherForecastResult>.Failure("No data available"));
        }

        var dayOfYear = dateFrom.DayOfYear;

        var result = new GroupedWeatherForecastResult(
            Locations: _stateProvider.State.Locations.ToDictionary(
                key => key.Key,
                key => key.Value
                    .Skip(dayOfYear - 1)
                    .Take(7)
                    .Select((val, index) => new WeatherForecastResult(
                        Date: dateFrom.AddDays(index),
                        TemperatureC: val)
                    ).ToArray()));

        return ValueTask.FromResult(GrainResult<GroupedWeatherForecastResult>.Success(result));
    }

    public ValueTask<GrainResult<bool>> SetValue(string location, uint day, int temperatureC)
    {
        if (_stateProvider.State.IsInitialized is false)
        {
            _stateProvider.State.IsInitialized = true;
            _stateProvider.State.Locations = new Dictionary<string, int[]>();
        }

        var isNewLocation = !_stateProvider.State.Locations.ContainsKey(location);
        if (isNewLocation)
        {
            _stateProvider.State.Locations[location] = new int[365];
            _isDirty = true;
        }

        if (!isNewLocation && _stateProvider.State.Locations[location][day - 1] == temperatureC)
        {
            return ValueTask.FromResult(GrainResult<bool>.Success(true));
        }

        _stateProvider.State.Locations[location][day - 1] = temperatureC;
        _isDirty = true;

        return ValueTask.FromResult(GrainResult<bool>.Success(true));
    }

    public ValueTask<GrainResult<string[]>> GetLocations()
    {
        if (_stateProvider.State.IsInitialized is false)
        {
            return ValueTask.FromResult(GrainResult<string[]>.Failure("No data available"));
        }

        var locations = _stateProvider.State.Locations.Keys.ToArray();
        return ValueTask.FromResult(GrainResult<string[]>.Success(locations));
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
        _isDirty = false;
    }
}
