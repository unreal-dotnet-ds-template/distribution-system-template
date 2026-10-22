using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Microsoft.Extensions.Hosting;

// Cell id and build number of the running app. In Azure they are injected by the deployment (the build number is the
// git tag version without the "v" prefix); locally the build number falls back to the MinVer assembly version.
internal sealed record BuildInfo(string CellId, string BuildNumber)
{
    public const string CellIdTag = "app.cell.id";
    public const string BuildNumberTag = "app.build.number";

    public static BuildInfo From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var informational = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var assemblyVersion = informational?.Split('+')[0] ?? "local";

        return new BuildInfo(
            configuration["APP_CELL_ID"] ?? "local",
            configuration["BUILD_NUMBER"] ?? assemblyVersion);
    }
}

// Stamps every span (including Orleans grain call spans) with the cell id and build number.
// Azure Monitor does not export custom resource attributes, so they are added per signal.
internal sealed class BuildInfoActivityProcessor(BuildInfo info) : BaseProcessor<Activity>
{
    public override void OnStart(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.SetTag(BuildInfo.CellIdTag, info.CellId);
        data.SetTag(BuildInfo.BuildNumberTag, info.BuildNumber);
    }
}

// Stamps every log record (including logs written inside grains) with the cell id and build number.
internal sealed class BuildInfoLogProcessor(BuildInfo info) : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var attributes = new List<KeyValuePair<string, object?>>(data.Attributes ?? [])
        {
            new(BuildInfo.CellIdTag, info.CellId),
            new(BuildInfo.BuildNumberTag, info.BuildNumber),
        };
        data.Attributes = attributes;
    }
}
