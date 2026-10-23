using System.Diagnostics.Metrics;

namespace Microsoft.Extensions.Hosting;

internal sealed class BuildInfoMetrics : IHostedService, IDisposable
{
    internal const string MeterName = "Dst.Build";
    private readonly Meter _meter = new(MeterName);

    public BuildInfoMetrics(BuildInfo info)
    {
        _meter.CreateObservableGauge("dst.build.info", () => new Measurement<int>(1,
            new KeyValuePair<string, object?>(BuildInfo.CellIdTag, info.CellId),
            new KeyValuePair<string, object?>(BuildInfo.BuildNumberTag, info.BuildNumber)),
            description: "Build identity of this running service. The value is always 1; the version is a dimension.");
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _meter.Dispose();
}
