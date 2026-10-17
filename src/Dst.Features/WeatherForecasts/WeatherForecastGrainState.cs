namespace Dst.Features.WeatherForecasts;

[GenerateSerializer]
public class WeatherForecastGrainState
{
    [Id(0)]
    public bool IsInitialized { get; set; }
    [Id(1)]
    public DateOnly Date { get; set; }
    [Id(2)]
    public int TemperatureC { get; set; }
    [Id(3)]
    public string? Summary { get; set; }
}
