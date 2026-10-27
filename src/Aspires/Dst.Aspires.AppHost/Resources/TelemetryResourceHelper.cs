#pragma warning disable ASPIREAZURE003 // Aspire Azure networking APIs are still evaluation-only
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ApplicationInsights;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Network;
using Azure.Provisioning.OperationalInsights;

namespace Dst.Aspires.AppHost.Resources;

internal static class TelemetryResourceHelper
{
    private static void ConfigurePrivateEndpoint(AzureResourceInfrastructure infra, BicepValue<string> name)
    {
        var endpoint = infra.GetProvisionableResources().OfType<PrivateEndpoint>().Single();
        endpoint.Name = name;
        endpoint.CustomNetworkInterfaceName = BicepFunction.Interpolate($"{name}-nic");
    }

    private static IResourceBuilder<ContainerResource>? _otelCollector;

    private static IResourceBuilder<ProjectResource> WithLocalSetup(
        this IResourceBuilder<ProjectResource> project)
    {
        if(_otelCollector is not null)
        {
            project.WithEnvironment(
                    "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
                    ReferenceExpression.Create($"{_otelCollector.GetEndpoint("otlp-http")}/v1/metrics"))
                .WithEnvironment("OTEL_EXPORTER_OTLP_METRICS_PROTOCOL", "http/protobuf")
                .WaitFor(_otelCollector);

            return project;
        }

        var builder = project.ApplicationBuilder;

        _otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib:0.163.1")
            .WithArgs("--config=/etc/otelcol-contrib/config.yaml")
            .WithContainerFiles("/etc/otelcol-contrib", "observability/otel-collector")
            .WithHttpEndpoint(targetPort: 4318, name: "otlp-http");

        var prometheus = builder.AddContainer("prometheus", "prom/prometheus:v3.15.0")
        .WithArgs("--config.file=/etc/prometheus/prometheus.yml")
        .WithContainerFiles("/etc/prometheus", "observability/prometheus")
        .WithHttpEndpoint(targetPort: 9090, name: "http")
        .WaitFor(_otelCollector);

        var grafana = builder.AddContainer("grafana", "grafana/grafana:13.2.4")
            .WithContainerFiles("/etc/grafana/provisioning", "observability/grafana/provisioning")
            .WithBindMount("observability/grafana/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
            .WithHttpEndpoint(targetPort: 3000, name: "http")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
            .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Viewer")
            .WithEnvironment("GF_AUTH_DISABLE_LOGIN_FORM", "true")
            .WaitFor(prometheus);

        // Grafana's own file polling does not pick up edits on this bind mount, so poke its reload API.
        // Edit observability/grafana/dashboards/*.json and refresh the browser.
        builder.AddContainer("grafana-dashboard-reloader", "curlimages/curl:8.11.1")
            .WithEntrypoint("/bin/sh")
            .WithArgs("-c", "while true; do curl -s -o /dev/null -u admin:admin -X POST http://grafana:3000/api/admin/provisioning/dashboards/reload; sleep 3; done")
            .WaitFor(grafana);

        project.WithEnvironment(
                "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
                ReferenceExpression.Create($"{_otelCollector.GetEndpoint("otlp-http")}/v1/metrics"))
            .WithEnvironment("OTEL_EXPORTER_OTLP_METRICS_PROTOCOL", "http/protobuf")
            .WaitFor(_otelCollector);

        return project;
    }

    private static IResourceBuilder<AzureApplicationInsightsResource>? _applicationInsights;

    private static IResourceBuilder<ProjectResource> WithAzureSetup(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<AzureTableStorageResource> orleansClustering,
        IResourceBuilder<AzureBlobStorageResource> orleansGrainState)
    {
        if(_applicationInsights is not null)
        {
            project.WithReference(_applicationInsights);
            return project;
        }

        var builder = project.ApplicationBuilder;
        IResourceBuilder<ParameterResource> cellIdParameter = ParametersHelper.GetOrCreateParameter("cellId", builder);

        // Keep cell-scoped names aligned with deploy/cell_apps.bicep.
        var logAnalytics = builder.AddAzureLogAnalyticsWorkspace("Dst-logs")
            .ConfigureInfrastructure(infra =>
                infra.GetProvisionableResources().OfType<OperationalInsightsWorkspace>().Single().Name = ParametersHelper.CellResourceName(infra, "log", cellIdParameter));
        // One VNet per cell: ACA is injected into a delegated subnet, storage is reachable only through private endpoints.
        var vnet = builder.AddAzureVirtualNetwork("Dst-vnet", "10.0.0.0/16")
            .ConfigureInfrastructure(infra =>
                infra.GetProvisionableResources().OfType<VirtualNetwork>().Single().Name = ParametersHelper.CellResourceName(infra, "vnet", cellIdParameter));
        var acaSubnet = vnet.AddSubnet("aca-subnet", "10.0.0.0/23");
        var privateEndpointSubnet = vnet.AddSubnet("private-endpoints", "10.0.2.0/24");

        privateEndpointSubnet.AddPrivateEndpoint(orleansClustering)
            .ConfigureInfrastructure(infra =>
                ConfigurePrivateEndpoint(infra, ParametersHelper.CellResourceName(infra, "clustering-pep", cellIdParameter)));
        privateEndpointSubnet.AddPrivateEndpoint(orleansGrainState)
            .ConfigureInfrastructure(infra =>
                ConfigurePrivateEndpoint(infra, ParametersHelper.CellResourceName(infra, "grainstate-pep", cellIdParameter)));

        // Images live in one global registry (created by the global resources); cells only pull from it.
        var registry = builder.AddAzureContainerRegistry("Dst-registry")
            .AsExisting(ParametersHelper.GetOrCreateParameter("registryName", builder), ParametersHelper.GetOrCreateParameter("registryResourceGroup", builder));

        builder.AddAzureContainerAppEnvironment("aca")
            .WithAzureContainerRegistry(registry)
            .WithDelegatedSubnet(acaSubnet)
            .WithAzureLogAnalyticsWorkspace(logAnalytics)
            .ConfigureInfrastructure(infra =>
            {
                var environment = infra.GetProvisionableResources().OfType<ContainerAppManagedEnvironment>().Single();
                environment.Name = ParametersHelper.CellResourceName(infra, "cae", cellIdParameter);
                // Container Apps creates this group itself (platform load balancer + public IP); naming it keeps it recognisable.
                environment.InfrastructureResourceGroup = BicepFunction.Interpolate($"{BicepFunction.GetResourceGroup().Name}-cae-infra-rg");
            });

        _applicationInsights = builder.AddAzureApplicationInsights("Dst-insights", logAnalytics)
            .ConfigureInfrastructure(infra =>
                infra.GetProvisionableResources().OfType<ApplicationInsightsComponent>().Single().Name = ParametersHelper.CellResourceName(infra, "appi", cellIdParameter));

        project.WithReference(_applicationInsights);

        return project;
    }

    public static IResourceBuilder<ProjectResource> WithTelemetry(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<AzureTableStorageResource> orleansClustering,
        IResourceBuilder<AzureBlobStorageResource> orleansGrainState)
    {
        var builder = project.ApplicationBuilder;
        var configuration = builder.Configuration;

        var isPublish = builder.ExecutionContext.IsPublishMode;
        IResourceBuilder<ParameterResource>? cellIdParameter = isPublish
            ? ParametersHelper.GetOrCreateParameter("cellId", builder)
            : null;

        IResourceBuilder<ParameterResource>? buildNumberParameter = isPublish
            ? ParametersHelper.GetOrCreateParameter("buildNumber", builder)
            : null;

        if (cellIdParameter is not null && buildNumberParameter is not null)
        {
            project
                .WithEnvironment("HealthChecks__Enabled", "true")
                .WithEnvironment("APP_CELL_ID", cellIdParameter)
                .WithEnvironment("BUILD_NUMBER", buildNumberParameter);
        }
        else
        {
            var cellId = builder.Configuration["APP_CELL_ID"] ?? "local";
            var buildNumber = builder.Configuration["BUILD_NUMBER"] ?? "local";

            project
                .WithEnvironment("APP_CELL_ID", cellId)
                .WithEnvironment("BUILD_NUMBER", buildNumber);
        }

        if (isPublish is false)
        {
            project.WithLocalSetup();
        }
        else
        {
            project.WithAzureSetup(
                orleansClustering: orleansClustering,
                orleansGrainState: orleansGrainState);
        }

        return project;
    }
}
