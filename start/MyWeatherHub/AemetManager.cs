using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyWeatherHub;

public class AemetManager(HttpClient client)
{
    private static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

    public async Task<Municipio[]> GetMunicipiosAsync()
    {
        var municipios = await client.GetFromJsonAsync<Municipio[]>("municipios", options);

        return municipios ?? [];
    }

    public async Task<AemetForecast[]> GetForecastByMunicipioAsync(string municipioId)
    {
        var forecast = await client.GetFromJsonAsync<AemetForecast[]>($"aemet-forecast/{municipioId}", options);

        return forecast ?? [];
    }
}

public record Municipio(string Key, string Name, string Provincia);

public record AemetForecast(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("detailedForecast")] string DetailedForecast);