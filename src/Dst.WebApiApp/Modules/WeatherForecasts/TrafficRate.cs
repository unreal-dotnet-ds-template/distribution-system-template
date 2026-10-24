using Microsoft.Extensions.Options;

namespace Dst.WebApiApp.Modules.WeatherForecasts;

internal sealed class SyntheticTrafficOptions
{
    /// <summary>Absolute per-replica ceiling; <see cref="MaxRequestsPerSecond"/> cannot be configured above it.</summary>
    public const int RequestsPerSecondCeiling = 1000;

    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromMinutes(5);

    public int RequestsPerSecond { get; set; } = 20;

    public int MaxRequestsPerSecond { get; set; } = 100;

    public int MaxConcurrency { get; set; } = 100;
}

/// <summary>Target request rate: appsettings value unless overridden at runtime through the feature endpoint.</summary>
internal sealed class TrafficRate(IOptionsMonitor<SyntheticTrafficOptions> options)
{
    private int _override;

    public int MaxRequestsPerSecond => options.CurrentValue.MaxRequestsPerSecond;

    public int RequestsPerSecond
    {
        get
        {
            var current = options.CurrentValue;
            var requested = Volatile.Read(ref _override);
            return Math.Min(requested > 0 ? requested : current.RequestsPerSecond, current.MaxRequestsPerSecond);
        }
    }

    public bool TrySet(int requestsPerSecond)
    {
        if (requestsPerSecond < 1 || requestsPerSecond > MaxRequestsPerSecond)
        {
            return false;
        }

        Volatile.Write(ref _override, requestsPerSecond);
        return true;
    }
}
