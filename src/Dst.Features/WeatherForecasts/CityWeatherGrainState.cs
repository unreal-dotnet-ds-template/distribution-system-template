namespace Dst.Features.WeatherForecasts;

[GenerateSerializer]
public class CityWeatherGrainState
{
    [Id(0)]
    public bool IsInitialized { get; set; }
    [Id(1)]
    public DateOnly Date { get; set; }
    [Id(2)]
    public string Location { get; set; } = string.Empty;
    [Id(3)]
    public int TemperatureC { get; set; }
    [Id(4)]
    public string? Summary { get; set; }
}
