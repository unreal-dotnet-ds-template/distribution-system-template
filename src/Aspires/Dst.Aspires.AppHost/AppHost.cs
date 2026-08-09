var builder = DistributedApplication.CreateBuilder(args);

#region Orleans

// orleans storages for clustering and grain storage
var orleansClusteringRedis = builder.AddRedis("orleans-clustering-redis");

// orleans cluster
var orleans = builder.AddOrleans("orleansCluster")
    .WithClustering(orleansClusteringRedis)
    .WithGrainStorage("DefaultStore", orleansClusteringRedis)
    .WithGrainStorage("PubSubStore", orleansClusteringRedis);

// orleans silo -> backend. you run logic here.
var orleansSilo = builder.AddProject<Projects.Dst_Apps_OrleansSiloWebApp>("orleanssilo")
    .WithReference(orleans)
    .WaitFor(orleansClusteringRedis)
    .WithReplicas(1);

// orleans client -> frontend. you call grains here.
var orleansClient = builder.AddProject<Projects.Dst_Apps_OrleansClientWebApp>("orleansClient")
    .WithReference(orleans.AsClient()) // client-only reference
    .WithHttpHealthCheck("/health")
    .WaitFor(orleansSilo)
    .WithReplicas(1);

#endregion

builder.AddProject<Projects.Dst_Apps_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(orleansClient)
    .WaitFor(orleansClient);

builder.Build().Run();
