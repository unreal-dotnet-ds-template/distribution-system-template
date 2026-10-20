namespace Dst.Features.WeatherForecasts;

[GenerateSerializer]
public class WeatherForecastViewGrainState
{
    [Id(0)]
    public bool IsInitialized { get; set; }
    [Id(1)]
    public Dictionary<string, int[]> Locations { get; set; } = new();
}
