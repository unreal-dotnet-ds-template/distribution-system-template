using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// orleans storages for clustering and grain storage
var redisOrleansClustering = builder.AddRedis("Dst-redis-orleans-clustering");
var redisDefaultStorage = builder.AddRedis("Dst-redis-orleans-default-storage");

var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib:0.163.1")
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

var cellId = builder.Configuration["APP_CELL_ID"] ?? "local";
var buildNumber = builder.Configuration["BUILD_NUMBER"] ?? "local";

// orleans cluster
var orleans = builder.AddOrleans("Dst-orleansCluster")
    .WithClustering(redisOrleansClustering)
    .WithGrainStorage("DefaultStore", redisDefaultStorage);

// orleans silo -> backend. you run logic here.
var webOrleansSilo = builder.AddProject<Projects.Dst_OrleansSilo_WebApp>("Dst-web-orleans-silo")
    .WithReference(orleans)
    .WithEnvironment("APP_CELL_ID", cellId)
    .WithEnvironment("BUILD_NUMBER", buildNumber)
    .WithEnvironment(
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
        ReferenceExpression.Create($"{otelCollector.GetEndpoint("otlp-http")}/v1/metrics"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_METRICS_PROTOCOL", "http/protobuf")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(redisOrleansClustering)
    .WaitFor(otelCollector)
    .WithReplicas(1);

// orleans client -> frontend. you call grains here.
var webApi = builder.AddProject<Projects.Dst_WebApiApp>("Dst-web-api")
    .WithReference(orleans.AsClient()) // client-only reference
    .WithEnvironment("APP_CELL_ID", cellId)
    .WithEnvironment("BUILD_NUMBER", buildNumber)
    .WithEnvironment(
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
        ReferenceExpression.Create($"{otelCollector.GetEndpoint("otlp-http")}/v1/metrics"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_METRICS_PROTOCOL", "http/protobuf")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(webOrleansSilo)
    .WaitFor(otelCollector)
    .WithReplicas(1);

builder.Build().Run();
