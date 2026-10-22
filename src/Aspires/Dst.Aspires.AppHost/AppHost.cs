#pragma warning disable ASPIREAZURE003 // Aspire Azure networking APIs are still evaluation-only
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ApplicationInsights;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Roles;
using Azure.Provisioning.Network;
using Azure.Provisioning.OperationalInsights;
using Azure.Provisioning.Storage;

var builder = DistributedApplication.CreateBuilder(args);
var isPublish = builder.ExecutionContext.IsPublishMode;

// orleans storages for clustering (tables) and grain state (blobs); Azurite locally, Azure Storage when published
var storage = builder.AddAzureStorage("Dst-storage")
    .ConfigureInfrastructure(infra =>
    {
        // Storage names are global, lowercase alphanumeric only (3-24): a hash keeps them unique per cell.
        var account = infra.GetProvisionableResources().OfType<StorageAccount>().Single();
        account.Name = BicepFunction.Interpolate($"dst{BicepFunction.GetUniqueString(BicepFunction.GetResourceGroup().Id)}st");
    })
    .RunAsEmulator();
var orleansClustering = storage.AddTables("Dst-orleans-clustering");
var orleansGrainState = storage.AddBlobs("Dst-orleans-grainstate");

// Topology differs by mode: local runs the OTel collector + Prometheus + Grafana containers,
// while the published (Azure) topology uses Application Insights and a shared Log Analytics workspace.
IResourceBuilder<ContainerResource>? otelCollector = null;
IResourceBuilder<AzureApplicationInsightsResource>? appInsights = null;

if (isPublish)
{
    // Resource names are <app>-<type> (type suffix follows the Azure CAF abbreviations); they only need to be unique
    // inside the cell's resource group, so they are fixed. deploy/cell_apps.bicep relies on the same names.
    var logAnalytics = builder.AddAzureLogAnalyticsWorkspace("Dst-logs")
        .ConfigureInfrastructure(infra =>
            infra.GetProvisionableResources().OfType<OperationalInsightsWorkspace>().Single().Name = "dst-log");
    // One VNet per cell: ACA is injected into a delegated subnet, storage is reachable only through private endpoints.
    var vnet = builder.AddAzureVirtualNetwork("Dst-vnet", "10.0.0.0/16")
        .ConfigureInfrastructure(infra =>
            infra.GetProvisionableResources().OfType<VirtualNetwork>().Single().Name = "dst-vnet");
    var acaSubnet = vnet.AddSubnet("aca-subnet", "10.0.0.0/23");
    var privateEndpointSubnet = vnet.AddSubnet("private-endpoints", "10.0.2.0/24");
    privateEndpointSubnet.AddPrivateEndpoint(orleansClustering)
        .ConfigureInfrastructure(infra =>
            ConfigurePrivateEndpoint(infra, "dst-clustering-pep"));
    privateEndpointSubnet.AddPrivateEndpoint(orleansGrainState)
        .ConfigureInfrastructure(infra =>
            ConfigurePrivateEndpoint(infra, "dst-grainstate-pep"));

    // Images live in one global registry (created by the global resources); cells only pull from it.
    var registry = builder.AddAzureContainerRegistry("Dst-registry")
        .AsExisting(builder.AddParameter("registryName"), builder.AddParameter("registryResourceGroup"));

    builder.AddAzureContainerAppEnvironment("aca")
        .WithAzureContainerRegistry(registry)
        .WithDelegatedSubnet(acaSubnet)
        .WithAzureLogAnalyticsWorkspace(logAnalytics)
        .ConfigureInfrastructure(infra =>
        {
            var environment = infra.GetProvisionableResources().OfType<ContainerAppManagedEnvironment>().Single();
            environment.Name = "dst-cae";
            // Container Apps creates this group itself (platform load balancer + public IP); naming it keeps it recognisable.
            environment.InfrastructureResourceGroup = BicepFunction.Interpolate($"{BicepFunction.GetResourceGroup().Name}-cae-infra-rg");
        });
    appInsights = builder.AddAzureApplicationInsights("Dst-insights", logAnalytics)
        .ConfigureInfrastructure(infra =>
            infra.GetProvisionableResources().OfType<ApplicationInsightsComponent>().Single().Name = "dst-appi");
}
else
{
    otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib:0.163.1")
        .WithArgs("--config=/etc/otelcol-contrib/config.yaml")
        .WithContainerFiles("/etc/otelcol-contrib", "observability/otel-collector")
        .WithHttpEndpoint(targetPort: 4318, name: "otlp-http");

    var prometheus = builder.AddContainer("prometheus", "prom/prometheus:v3.15.0")
        .WithArgs("--config.file=/etc/prometheus/prometheus.yml")
        .WithContainerFiles("/etc/prometheus", "observability/prometheus")
        .WithHttpEndpoint(targetPort: 9090, name: "http")
        .WaitFor(otelCollector);

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
}

// Published artifacts are cell-agnostic: cell id and build number become deployment parameters instead of baked-in values.
IResourceBuilder<ParameterResource>? cellIdParameter = isPublish ? builder.AddParameter("cellId") : null;
IResourceBuilder<ParameterResource>? buildNumberParameter = isPublish ? builder.AddParameter("buildNumber") : null;
var cellId = builder.Configuration["APP_CELL_ID"] ?? "local";
var buildNumber = builder.Configuration["BUILD_NUMBER"] ?? "local";

IResourceBuilder<ProjectResource> WithTelemetry(IResourceBuilder<ProjectResource> project)
{
    if (cellIdParameter is not null && buildNumberParameter is not null)
    {
        project
            .WithEnvironment("HealthChecks__Enabled", "true")
            .WithEnvironment("APP_CELL_ID", cellIdParameter)
            .WithEnvironment("BUILD_NUMBER", buildNumberParameter);
    }
    else
    {
        project
            .WithEnvironment("APP_CELL_ID", cellId)
            .WithEnvironment("BUILD_NUMBER", buildNumber);
    }

    if (otelCollector is not null)
    {
        project
            .WithEnvironment(
                "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
                ReferenceExpression.Create($"{otelCollector.GetEndpoint("otlp-http")}/v1/metrics"))
            .WithEnvironment("OTEL_EXPORTER_OTLP_METRICS_PROTOCOL", "http/protobuf")
            .WaitFor(otelCollector);
    }

    if (appInsights is not null)
    {
        project.WithReference(appInsights);
    }

    return project;
}

// orleans cluster
var orleans = builder.AddOrleans("Dst-orleansCluster")
    .WithClustering(orleansClustering)
    .WithGrainStorage("DefaultStore", orleansGrainState);

// orleans silo -> backend. you run logic here.
var webOrleansSilo = WithTelemetry(builder.AddProject<Projects.Dst_OrleansSilo_WebApp>("Dst-web-orleans-silo")
    .WithReference(orleans))
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(orleansClustering)
    .WaitFor(orleansGrainState)
    .WithReplicas(1);

// orleans client -> frontend. you call grains here.
var webApi = WithTelemetry(builder.AddProject<Projects.Dst_WebApiApp>("Dst-web-api")
    .WithReference(orleans.AsClient())) // client-only reference
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(webOrleansSilo)
    .WithReplicas(1);

// Published Web API only serves requests carrying this Front Door id (empty = not restricted).
if (isPublish)
{
    foreach (var (project, containerApp, identityName, identity) in new[]
    {
        (webApi, "dst-web-api-ca", "Dst-web-api-identity", "dst-web-api-id"),
        (webOrleansSilo, "dst-web-orleans-silo-ca", "Dst-web-orleans-silo-identity", "dst-web-orleans-silo-id"),
    })
    {
        var appIdentity = builder.AddAzureUserAssignedIdentity(identityName)
            .ConfigureInfrastructure(infra =>
                infra.GetProvisionableResources().OfType<UserAssignedIdentity>().Single().Name = identity);
        project.WithAzureUserAssignedIdentity(appIdentity)
            .PublishAsAzureContainerApp((_, app) => app.Name = containerApp);
    }

    webApi.WithEnvironment("FrontDoor__Id", builder.AddParameter("frontDoorId"));

    // Auth/docs settings are Bicep parameters, so the release pipeline decides them per deployment.
    webApi.WithEnvironment("Auth__Mode", builder.AddParameter("authMode"))
        .WithEnvironment("Auth__Authority", builder.AddParameter("authAuthority"))
        .WithEnvironment("Auth__Audience", builder.AddParameter("authAudience"))
        .WithEnvironment("Auth__Scalar__ClientId", builder.AddParameter("authScalarClientId"))
        .WithEnvironment("Auth__Scalar__Scopes__0", builder.AddParameter("authScalarScope"))
        .WithEnvironment("Docs__Enabled", builder.AddParameter("docsEnabled"));
}
else
{
    // Local: forwarded only when provided in AppHost configuration (e.g. to try JwtBearer locally).
    foreach (var key in new[] { "Auth:Mode", "Auth:Authority", "Auth:Audience", "Auth:Scalar:ClientId", "Auth:Scalar:AuthorizationUrl", "Auth:Scalar:TokenUrl", "Auth:Scalar:Scopes:0", "Auth:Scalar:Scopes:1", "Docs:Enabled" })
    {
        if (builder.Configuration[key] is { Length: > 0 } value)
        {
            webApi.WithEnvironment(key.Replace(":", "__", StringComparison.Ordinal), value);
        }
    }
}

static void ConfigurePrivateEndpoint(AzureResourceInfrastructure infra, string name)
{
    var endpoint = infra.GetProvisionableResources().OfType<PrivateEndpoint>().Single();
    endpoint.Name = name;
    endpoint.CustomNetworkInterfaceName = $"{name}-nic";
}

builder.Build().Run();
