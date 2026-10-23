using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Dst.WebApiApp.Tests;

public sealed class BuildMetricsTests
{
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("2.0.0-preview.4")]
    public async Task BuildMetricCarriesTheDeploymentVersionAndOrleansSharesItsResource(string version)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["APP_CELL_ID"] = "test-cell",
            ["BUILD_NUMBER"] = version,
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "",
        });
        builder.AddServiceDefaults();
        using var exporter = new BuildExporter();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics =>
            metrics.AddReader(new PeriodicExportingMetricReader(exporter, exportIntervalMilliseconds: int.MaxValue)));
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var orleans = new Meter("Microsoft.Orleans");
            orleans.CreateCounter<long>("orleans-test-build").Add(1);
            var provider = app.Services.GetRequiredService<MeterProvider>();
            Assert.True(provider.ForceFlush());
            Assert.Equal(version, exporter.Build);
            Assert.Equal("test-cell", exporter.Cell);
            Assert.Equal(1, exporter.Value);
            Assert.True(exporter.SawOrleans);
            var resource = provider.GetResource().Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.Equal(version, resource["app.build.number"]);
            Assert.Equal(version, resource["service.version"]);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class BuildExporter : BaseExporter<Metric>
    {
        public string? Build { get; private set; }
        public string? Cell { get; private set; }
        public long Value { get; private set; }
        public bool SawOrleans { get; private set; }

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                if (metric.Name == "orleans-test-build")
                {
                    SawOrleans = true;
                }

                if (metric.Name != "dst.build.info")
                {
                    continue;
                }

                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    string? build = null;
                    string? cell = null;
                    foreach (var tag in point.Tags)
                    {
                        if (tag.Key == "app.build.number")
                        {
                            build = tag.Value?.ToString();
                        }
                        else if (tag.Key == "app.cell.id")
                        {
                            cell = tag.Value?.ToString();
                        }
                    }

                    // Other test hosts can publish meters in the same process.
                    if (cell == "test-cell")
                    {
                        Build = build;
                        Cell = cell;
                        Value = point.GetGaugeLastValueLong();
                    }
                }
            }

            return ExportResult.Success;
        }
    }
}
