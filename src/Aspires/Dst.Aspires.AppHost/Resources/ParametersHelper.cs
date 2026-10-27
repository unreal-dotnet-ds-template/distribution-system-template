using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Expressions;
using Microsoft.AspNetCore.Builder;

namespace Dst.Aspires.AppHost.Resources;

internal static class ParametersHelper
{
    private static Dictionary<string, IResourceBuilder<ParameterResource>> _parameters = new Dictionary<string, IResourceBuilder<ParameterResource>>();

    public static IResourceBuilder<ParameterResource> GetOrCreateParameter(string name, IDistributedApplicationBuilder builder)
    {
        if (!_parameters.TryGetValue(name, out var parameter))
        {
            _parameters[name] = builder.AddParameter(name);
        }
        return _parameters[name];
    }

    public static BicepValue<string> CellResourceName(
        AzureResourceInfrastructure infra,
        string suffix,
        IResourceBuilder<ParameterResource> cellIdParameter)
        => BicepFunction.Interpolate($"dst-{cellIdParameter.AsProvisioningParameter(infra)}-{suffix}");
}
