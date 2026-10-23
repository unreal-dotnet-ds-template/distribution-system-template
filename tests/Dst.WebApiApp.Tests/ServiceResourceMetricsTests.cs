using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Dst.WebApiApp.Tests;

[Collection("Service resource metrics")]
public sealed class ServiceResourceMetricsTests
{
    [Theory]
    [InlineData(MetricReaderTemporalityPreference.Cumulative)]
    [InlineData(MetricReaderTemporalityPreference.Delta)]
    public async Task ThreadPoolCountsRemainGaugesAcrossExports(MetricReaderTemporalityPreference temporality)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "",
        });
        builder.AddServiceDefaults();
        using var exporter = new ResourceExporter();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics =>
            metrics.AddReader(new PeriodicExportingMetricReader(exporter, exportIntervalMilliseconds: int.MaxValue)
            {
                TemporalityPreference = temporality,
            }));
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await Task.Run(() => Assert.True(ThreadPool.ThreadCount > 0), TestContext.Current.CancellationToken);
            var provider = app.Services.GetRequiredService<MeterProvider>();
            for (var i = 0; i < 2; i++)
            {
                exporter.ThreadCount = null;
                exporter.QueueLength = null;
                Assert.True(provider.ForceFlush());
                Assert.True(exporter.ThreadsAreGauge);
                Assert.True(exporter.QueueIsGauge);
                Assert.NotNull(exporter.ThreadCount);
                Assert.NotNull(exporter.QueueLength);
                Assert.True(exporter.ThreadCount > 0);
                Assert.True(exporter.QueueLength >= 0);
            }
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class ResourceExporter : BaseExporter<Metric>
    {
        public long? ThreadCount { get; set; }
        public long? QueueLength { get; set; }
        public bool ThreadsAreGauge { get; private set; }
        public bool QueueIsGauge { get; private set; }

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    if (metric.Name == "dst.process.thread_pool.thread.count")
                    {
                        ThreadsAreGauge = metric.MetricType == MetricType.LongGauge;
                        if (ThreadsAreGauge)
                        {
                            ThreadCount = point.GetGaugeLastValueLong();
                        }
                    }
                    else if (metric.Name == "dst.process.thread_pool.queue.length")
                    {
                        QueueIsGauge = metric.MetricType == MetricType.LongGauge;
                        if (QueueIsGauge)
                        {
                            QueueLength = point.GetGaugeLastValueLong();
                        }
                    }
                }
            }

            return ExportResult.Success;
        }
    }
}

// Observable instruments are process-wide; another host disposing its meter can interrupt collection.
[CollectionDefinition("Service resource metrics", DisableParallelization = true)]
public sealed class ServiceResourceMetricsTestGroup;
