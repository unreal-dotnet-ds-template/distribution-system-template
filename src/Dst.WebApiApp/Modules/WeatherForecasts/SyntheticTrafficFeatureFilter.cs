using Microsoft.FeatureManagement;

namespace Dst.WebApiApp.Modules.WeatherForecasts;

[FilterAlias("SyntheticTraffic")]
internal sealed class SyntheticTrafficFeatureFilter : IFeatureFilter
{
    public const string FeatureName = "SyntheticTraffic";
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
