var builder = DistributedApplication.CreateBuilder(args);

// orleans storages for clustering and grain storage
var redisOrleansClustering = builder.AddRedis("Dst-redis-orleans-clustering");

// orleans cluster
var orleans = builder.AddOrleans("Dst-orleansCluster")
    .WithClustering(redisOrleansClustering)
    .WithGrainStorage("DefaultStore", redisOrleansClustering)
    .WithGrainStorage("PubSubStore", redisOrleansClustering);

// orleans silo -> backend. you run logic here.
var webOrleansSilo = builder.AddProject<Projects.Dst_OrleansSilo_WebApp>("Dst-web-orleans-silo")
    .WithReference(orleans)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(redisOrleansClustering)
    .WithReplicas(1);

// orleans client -> frontend. you call grains here.
var webApi = builder.AddProject<Projects.Dst_WebApiApp>("Dst-web-api")
    .WithReference(orleans.AsClient()) // client-only reference
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WaitFor(webOrleansSilo)
    .WithReplicas(1);

builder.Build().Run();
