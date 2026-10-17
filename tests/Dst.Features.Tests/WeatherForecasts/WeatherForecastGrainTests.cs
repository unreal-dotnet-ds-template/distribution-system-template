using Dst.Core;
using Dst.Core.Features.WeatherForecasts;
using Dst.Features.WeatherForecasts;
using NSubstitute;

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
        // Arrange
        var grain = _fixture.Cluster.Client.GetGrain<IWeatherForecastGrain>(101);
        var grainId = grain.GetGrainId();

        // Act
        var firstForecast = await grain.GetAsync();
        var secondForecast = await grain.GetAsync();

        // Assert
        Assert.Equal(firstForecast, secondForecast);

        // Storage was read on activation (setup + OnActivateAsync), and not re-read on the second GetAsync call
        await _fixture.GrainStorage.Received(2).ReadStateAsync(
            DstConsts.WeatherForecastsStateKey,
            grainId,
            Arg.Any<IGrainState<WeatherForecastGrainState>>());

        // Saving does not happen during GetAsync while the grain remains active
        await _fixture.GrainStorage.DidNotReceive().WriteStateAsync(
            DstConsts.WeatherForecastsStateKey,
            grainId,
            Arg.Any<IGrainState<WeatherForecastGrainState>>());
    }

    [Fact]
    public async Task DeactivateGrain_AfterGetAsync_WritesStateToStorage()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var grain = _fixture.Cluster.Client.GetGrain<IWeatherForecastGrain>(102);
        var grainId = grain.GetGrainId();

        var forecast = await grain.GetAsync();

        // Act
        await _fixture.DeactivateAllGrainsAsync(cancellationToken);

        // Assert
        await _fixture.GrainStorage.Received(1).WriteStateAsync(
            DstConsts.WeatherForecastsStateKey,
            grainId,
            Arg.Is<IGrainState<WeatherForecastGrainState>>(s =>
                s.State != null &&
                s.State.IsInitialized &&
                s.State.Date == forecast.Date &&
                s.State.TemperatureC == forecast.TemperatureC &&
                s.State.Summary == forecast.Summary));
    }
}
