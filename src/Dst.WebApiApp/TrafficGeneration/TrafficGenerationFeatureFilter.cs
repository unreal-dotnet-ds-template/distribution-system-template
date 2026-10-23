using Microsoft.FeatureManagement;

namespace Dst.WebApiApp.TrafficGeneration;

[FilterAlias("TrafficGeneration")]
internal sealed class TrafficGenerationFeatureFilter : IFeatureFilter
{
    public const string FeatureName = "RandomWeatherTraffic";
    private int _enabledOverride = -1;

    public void SetEnabled(bool enabled) => Volatile.Write(ref _enabledOverride, enabled ? 1 : 0);

    public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var enabledOverride = Volatile.Read(ref _enabledOverride);
        return Task.FromResult(enabledOverride < 0
            ? context.Parameters.GetValue<bool>("Enabled")
            : enabledOverride == 1);
    }
}
