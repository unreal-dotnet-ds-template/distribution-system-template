using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Roles;

namespace Dst.Aspires.AppHost.Resources;

internal static class AuthResourceHelper
{
    private static IResourceBuilder<ProjectResource> WithAuth(
        this IResourceBuilder<ProjectResource> project,
        string containerApp,
        string identityName,
        string identity,
        bool webApiAuth,
        string[] localConfigKeys)
    {
        var builder = project.ApplicationBuilder;
        var configuration = builder.Configuration;

        var isPublish = builder.ExecutionContext.IsPublishMode;

        if (isPublish is false)
        {
            foreach (var key in localConfigKeys)
            {
                if (builder.Configuration[key] is { Length: > 0 } value)
                {
                    project.WithEnvironment(key.Replace(":", "__", StringComparison.Ordinal), value);
                }
            }
        }
        else
        {
            IResourceBuilder<ParameterResource> cellIdParameter = ParametersHelper.GetOrCreateParameter("cellId", builder);

            var appIdentity = builder.AddAzureUserAssignedIdentity(identityName)
                .ConfigureInfrastructure(infra => infra
                    .GetProvisionableResources()
                    .OfType<UserAssignedIdentity>()
                    .Single().Name = ParametersHelper.CellResourceName(
                        infra,
                        identity,
                        cellIdParameter));

            project.WithAzureUserAssignedIdentity(appIdentity)
                .PublishAsAzureContainerApp((infra, app) =>
                    app.Name = ParametersHelper.CellResourceName(infra, containerApp, cellIdParameter));

            if (webApiAuth)
            {
                project.WithEnvironment("FrontDoor__Id", ParametersHelper.GetOrCreateParameter("frontDoorId", builder))
                    // Auth/docs settings are Bicep parameters, so the release pipeline decides them per deployment.
                    .WithEnvironment("Auth__Mode", ParametersHelper.GetOrCreateParameter("authMode", builder))
                    .WithEnvironment("Auth__Authority", ParametersHelper.GetOrCreateParameter("authAuthority", builder))
                    .WithEnvironment("Auth__Audience", ParametersHelper.GetOrCreateParameter("authAudience", builder))
                    .WithEnvironment("Auth__Scalar__ClientId", ParametersHelper.GetOrCreateParameter("authScalarClientId", builder))
                    .WithEnvironment("Auth__Scalar__Scopes__0", ParametersHelper.GetOrCreateParameter("authScalarScope", builder))
                    .WithEnvironment("Docs__Enabled", ParametersHelper.GetOrCreateParameter("docsEnabled", builder));
            }
        }

        return project;
    }

    public static IResourceBuilder<ProjectResource> WithSiloAuth(
        this IResourceBuilder<ProjectResource> project)
    {
        return project.WithAuth(
            containerApp: "web-orleans-silo-ca",
            identityName: "Dst-web-orleans-silo-identity",
            identity: "web-orleans-silo-id",
            webApiAuth: false,
            localConfigKeys: Array.Empty<string>());
    }

    public static IResourceBuilder<ProjectResource> WithWebApiAuth(
        this IResourceBuilder<ProjectResource> project)
    {
        // Local: forwarded only when provided in AppHost configuration (e.g. to try JwtBearer locally).
        var localConfigKeys = new[]
        {
            "Auth:Mode",
            "Auth:Authority",
            "Auth:Audience",
            "Auth:Scalar:ClientId",
            "Auth:Scalar:AuthorizationUrl",
            "Auth:Scalar:TokenUrl",
            "Auth:Scalar:Scopes:0",
            "Auth:Scalar:Scopes:1",
            "Docs:Enabled"
        };

        return project.WithAuth(
            containerApp: "web-api-ca",
            identityName: "Dst-web-api-identity",
            identity: "web-api-id",
            webApiAuth: true,
            localConfigKeys: localConfigKeys);
    }
}
