using Dst.Aspires.AppHost.Resources;

var builder = DistributedApplication.CreateBuilder(args);

// orleans storages for clustering (tables) and grain state (blobs); Azurite locally, Azure Storage when published
var storage = builder.AddAzureStorage("Dst-storage")
    .WithProvisioning()
    .RunAsEmulator();

var orleansClustering = storage.AddTables("Dst-orleans-clustering");
var orleansGrainState = storage.AddBlobs("Dst-orleans-grainstate");

// orleans cluster
var orleans = builder.AddOrleans("Dst-orleansCluster")
    .WithClustering(orleansClustering)
    .WithGrainStorage("DefaultStore", orleansGrainState);

// orleans silo -> backend. you run logic here.
var webOrleansSilo = builder.AddProject<Projects.Dst_OrleansSilo_WebApp>("Dst-web-orleans-silo")
    .WithReference(orleans)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithTelemetry(
        orleansClustering,
        orleansGrainState)
    .WithSiloAuth()
    .WaitFor(orleansClustering)
    .WaitFor(orleansGrainState)
    .WithReplicas(1);

// orleans client -> frontend. you call grains here.
var webApi = builder.AddProject<Projects.Dst_WebApiApp>("Dst-web-api")
    .WithReference(orleans.AsClient()) // client-only reference
    .WithExternalHttpEndpoints()
    .WithTelemetry(
        orleansClustering,
        orleansGrainState)
    .WithWebApiAuth()
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(webOrleansSilo)
    .WithReplicas(1);

builder.Build().Run();
