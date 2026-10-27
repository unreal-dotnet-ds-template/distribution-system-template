using Aspire.Hosting.Azure;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Storage;

namespace Dst.Aspires.AppHost.Resources;

internal static class StorageAccountHelper
{
    public static IResourceBuilder<AzureStorageResource> WithProvisioning(this IResourceBuilder<AzureStorageResource> resource)
    {
        return resource.ConfigureInfrastructure(infra =>
        {
            // Storage names are global, lowercase alphanumeric only (3-24): a hash keeps them unique per cell.
            var account = infra.GetProvisionableResources().OfType<StorageAccount>().Single();
            account.Name = BicepFunction.Interpolate($"dst{BicepFunction.GetUniqueString(BicepFunction.GetResourceGroup().Id)}st");
        });
    }
}
