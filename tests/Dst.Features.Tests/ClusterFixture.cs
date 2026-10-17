using Dst.Core;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Orleans.Storage;
using Orleans.TestingHost;

namespace Dst.Features.Tests;

public sealed class ClusterFixture : IAsyncLifetime
{
    private static readonly IGrainStorage SharedStorageSpy = Substitute.For<IGrainStorage>();

    public TestCluster Cluster { get; private set; } = null!;

    public IGrainStorage GrainStorage { get; } = SharedStorageSpy;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Cluster.StopAllSilosAsync();
        await Cluster.DisposeAsync();
    }

    public async Task DeactivateAllGrainsAsync(CancellationToken cancellationToken)
    {
        var managementGrain = Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        await managementGrain.ForceActivationCollection(TimeSpan.Zero, cancellationToken);
    }

    private sealed class TestSiloConfigurations : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddMemoryGrainStorage(DstConsts.OrleansDefaultStoreKey);

            siloBuilder.ConfigureServices(services =>
            {
                var descriptor = services.Single(d =>
                    d.ServiceType == typeof(IGrainStorage) &&
                    Equals(d.ServiceKey, DstConsts.OrleansDefaultStoreKey));

                services.Remove(descriptor);
                services.AddKeyedSingleton<IGrainStorage>(
                    DstConsts.OrleansDefaultStoreKey,
                    (sp, key) =>
                    {
                        var inner = (IGrainStorage)descriptor.KeyedImplementationFactory!(sp, key);
                        return new ObservableGrainStorage(inner, SharedStorageSpy);
                    });
            });
        }
    }

    private sealed class ObservableGrainStorage(
        IGrainStorage innerStorage,
        IGrainStorage spyStorage) : IGrainStorage, ILifecycleParticipant<ISiloLifecycle>
    {
        public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            await innerStorage.ReadStateAsync(stateName, grainId, grainState);
            await spyStorage.ReadStateAsync(stateName, grainId, grainState);
        }

        public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            await innerStorage.WriteStateAsync(stateName, grainId, grainState);
            await spyStorage.WriteStateAsync(stateName, grainId, grainState);
        }

        public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        {
            await innerStorage.ClearStateAsync(stateName, grainId, grainState);
            await spyStorage.ClearStateAsync(stateName, grainId, grainState);
        }

        public void Participate(ISiloLifecycle observer)
        {
            if (innerStorage is ILifecycleParticipant<ISiloLifecycle> participant)
            {
                participant.Participate(observer);
            }
        }
    }
}

[CollectionDefinition(Name)]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public sealed class ClusterCollection : ICollectionFixture<ClusterFixture>
#pragma warning restore CA1711
{
    public const string Name = nameof(ClusterCollection);
}
