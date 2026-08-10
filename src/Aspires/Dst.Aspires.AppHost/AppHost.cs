var builder = DistributedApplication.CreateBuilder(args);

// orleans storages for clustering and grain storage
var redisOrleansClustering = builder.AddRedis("redis-orleans-clustering");

// orleans cluster
var orleans = builder.AddOrleans("orleansCluster")
    .WithClustering(redisOrleansClustering)
    .WithGrainStorage("DefaultStore", redisOrleansClustering)
    .WithGrainStorage("PubSubStore", redisOrleansClustering);

// orleans silo -> backend. you run logic here.
var webOrleansSilo = builder.AddProject<Projects.Dst_OrleansSilo_WebApp>("web-orleans-silo")
    .WithReference(orleans)
    .WaitFor(redisOrleansClustering)
    .WithReplicas(1);

// orleans client -> frontend. you call grains here.
var webApi = builder.AddProject<Projects.Dst_WebApiApp>("web-api")
    .WithReference(orleans.AsClient()) // client-only reference
    .WithHttpHealthCheck("/health")
    .WaitFor(webOrleansSilo)
    .WithReplicas(1);

builder.Build().Run();
