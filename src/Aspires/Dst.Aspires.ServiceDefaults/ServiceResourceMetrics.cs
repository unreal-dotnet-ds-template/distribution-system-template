using System.Diagnostics.Metrics;

namespace Microsoft.Extensions.Hosting;

internal sealed class ServiceResourceMetrics : IHostedService, IDisposable
{
    internal const string MeterName = "Dst.Resources";
    private readonly Meter _meter = new(MeterName);

    public ServiceResourceMetrics()
    {
        // Azure exports the runtime's observable up/down counters as deltas, not current counts.
        _meter.CreateObservableGauge("dst.process.thread_pool.thread.count",
            () => ThreadPool.ThreadCount, unit: "{thread}",
            description: "Current number of thread-pool threads.");
        _meter.CreateObservableGauge("dst.process.thread_pool.queue.length",
            () => ThreadPool.PendingWorkItemCount, unit: "{work_item}",
            description: "Current number of queued thread-pool work items.");
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _meter.Dispose();
}
