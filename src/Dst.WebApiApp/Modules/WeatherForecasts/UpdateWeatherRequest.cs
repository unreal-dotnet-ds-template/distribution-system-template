namespace Dst.WebApiApp.Modules.WeatherForecasts;

/// <summary>Payload for updating a city's weather forecast.</summary>
internal sealed record UpdateWeatherRequest(DateOnly Date, string Location);
