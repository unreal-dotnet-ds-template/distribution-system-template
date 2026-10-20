namespace Dst.Features.Tests.WeatherForecasts;

[Collection(ClusterCollection.Name)]
public class WeatherForecastGrainTests
{
    private readonly ClusterFixture _fixture;

    public WeatherForecastGrainTests(ClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetAsync_CalledMultipleTimes_ReturnsSameForecastAndReadsStorageWithoutWriting()
    {

    }
}
