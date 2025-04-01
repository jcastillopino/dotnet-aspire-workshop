using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text;
using Api.Data;

namespace Api
{
    public class AemetManager
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly string _apiKey;
        private static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
        };

        static AemetManager()
        {
            // Register the Western European (ISO-8859-15) encoding provider
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public AemetManager(HttpClient httpClient, IMemoryCache cache, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _cache = cache;
            _apiKey = configuration["AEMET:ApiKey"] ?? throw new ArgumentNullException("AEMET:ApiKey configuration is required");
        }

        private async Task<string> ReadResponseContentAsync(HttpResponseMessage response)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.CharSet ?? "ISO-8859-15";

            try
            {
                var encoding = Encoding.GetEncoding(contentType);
                return encoding.GetString(bytes);
            }
            catch (ArgumentException)
            {
                return Encoding.GetEncoding("ISO-8859-15").GetString(bytes);
            }
        }

        private async Task<AemetResponse> GetInitialMunicipiosDataAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "api/maestro/municipios");
            request.Headers.Add("api_key", _apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var plainTextContent = await ReadResponseContentAsync(response);
            return JsonSerializer.Deserialize<AemetResponse>(plainTextContent, options) 
                ?? new AemetResponse { Datos = null, Metadatos = null };
        }

        private async Task<List<MunicipioData>?> GetDetailedMunicipiosDataAsync(string dataUrl)
        {
            var dataResponse = await _httpClient.GetAsync(dataUrl);
            dataResponse.EnsureSuccessStatusCode();

            var dataContent = await ReadResponseContentAsync(dataResponse);
            return JsonSerializer.Deserialize<List<MunicipioData>>(dataContent, options);
        }

        private static Municipio[] TransformMunicipiosData(List<MunicipioData> municipioList)
        {
            return municipioList
                .Where(m => m.Id != null && m.Nombre != null && m.Id_Old != null)
                .Select(m => new Municipio(
                    m.Id ?? string.Empty,
                    m.Nombre ?? string.Empty,
                    ProvinceHelper.GetProvinciaFromPostalCode(m.Id_Old ?? string.Empty)))
                .ToArray();
        }

        public async Task<Municipio[]> GetMunicipiosAsync()
        {
            return await _cache.GetOrCreateAsync<Municipio[]>("municipios", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);

                var aemetResponse = await GetInitialMunicipiosDataAsync();
                if (string.IsNullOrEmpty(aemetResponse.Datos))
                {
                    return Array.Empty<Municipio>();
                }

                try
                {
                    var municipioList = await GetDetailedMunicipiosDataAsync(aemetResponse.Datos);
                    if (municipioList == null)
                    {
                        return Array.Empty<Municipio>();
                    }

                    return TransformMunicipiosData(municipioList);
                }
                catch (JsonException)
                {
                    throw;
                }
            });
        }

        private static int forecastCount = 0;
        
        public async Task<Data.AemetForecast[]> GetForecastByMunicipioAsync(string municipioId)
        {
            ValidateInput(municipioId);
            
            var aemetResponse = await GetInitialForecastDataAsync(municipioId);
            if (string.IsNullOrEmpty(aemetResponse?.Datos))
            {
                return Array.Empty<Data.AemetForecast>();
            }

            var forecastData = await GetDetailedForecastDataAsync(aemetResponse.Datos);
            if (forecastData is null || !IsForecastDataValid(forecastData))
            {
                return Array.Empty<Data.AemetForecast>();
            }

            var dias = forecastData[0].Prediccion?.Dia;
            if (dias == null)
            {
                return Array.Empty<Data.AemetForecast>();
            }

            return TransformForecastData(dias);
        }

        private void ValidateInput(string municipioId)
        {
            // Note: This should be removed in production
            forecastCount++;
            if (forecastCount % 5 == 0)
            {
                throw new Exception("Random exception thrown by NwsManager.GetForecastAsync");
            }
        }

        private async Task<AemetResponse> GetInitialForecastDataAsync(string municipioId)
        {
            municipioId = municipioId.Replace("id", string.Empty).Trim();
            
            var request = new HttpRequestMessage(HttpMethod.Get, $"api/prediccion/especifica/municipio/diaria/{municipioId}");
            request.Headers.Add("api_key", _apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var plainTextContent = await ReadResponseContentAsync(response);
            var result = JsonSerializer.Deserialize<AemetResponse>(plainTextContent, options);
            return result ?? new AemetResponse { Datos = null, Metadatos = null };
        }

        private async Task<List<Data.AemetForecastData>?> GetDetailedForecastDataAsync(string dataUrl)
        {
            var dataResponse = await _httpClient.GetAsync(dataUrl);
            dataResponse.EnsureSuccessStatusCode();

            var dataContent = await ReadResponseContentAsync(dataResponse);

            try
            {
                // Try parsing as JObject first to debug the structure
                var tempObj = JsonSerializer.Deserialize<JsonDocument>(dataContent, options);
                return JsonSerializer.Deserialize<List<Data.AemetForecastData>>(dataContent, options);
            }
            catch (JsonException)
            {
                throw;
            }
        }

        private bool IsForecastDataValid(List<Data.AemetForecastData> forecastData)
        {
            return forecastData.Count > 0 && 
                   forecastData[0]?.Prediccion?.Dia is List<AemetForecastDataDia> dias && 
                   dias.Any();
        }

        private Data.AemetForecast[] TransformForecastData(List<AemetForecastDataDia> dias)
        {
            return dias
                .Where(day => day is not null && day.Fecha is not null)
                .Select(day => new Data.AemetForecast(
                    GetFriendlyDate(day.Fecha) + " - " + day.Fecha?.ToString("dd/MM/yyyy"),
                    BuildForecastDescription(day)))
                .ToArray();
        }

        private string BuildForecastDescription(AemetForecastDataDia day)
        {
            var description = new StringBuilder();

            // Estado del cielo (Sky condition)
            var skyCondition = day.EstadoCielo?
                .OrderBy(e => e.Periodo)
                .FirstOrDefault()?.Descripcion ?? "No sky data";
            description.AppendLine($"{skyCondition}.");

            // Temperature
            if (day.Temperatura != null)
            {
                description.AppendLine($"Temperatura: Max {day.Temperatura.Maxima}°C, Min {day.Temperatura.Minima}°C.");
            }

            // Wind
            AddWindInfo(description, day.Viento);

            // Precipitation probability
            AddPrecipitationInfo(description, day.ProbPrecipitacion);

            return description.ToString().TrimEnd();
        }

        private void AddWindInfo(StringBuilder description, List<AemetForecastDataViento>? viento)
        {
            var wind = viento?
                .OrderBy(w => w.Periodo)
                .FirstOrDefault();
            var windInfo = wind != null ? $"{wind.Velocidad}km/h {wind.Direccion}" : "Light winds";
            description.AppendLine($"Viento: {windInfo}.");
        }

        private void AddPrecipitationInfo(StringBuilder description, List<AemetForecastDataProbPrecipitacion>? probPrecipitacion)
        {
            var precipProbs = probPrecipitacion?
                .Where(p => p.Value > 0)
                .OrderBy(p => p.Periodo)
                .ToList();

            if (precipProbs?.Any() == true)
            {
                foreach (var prob in precipProbs)
                {
                    var period = !string.IsNullOrEmpty(prob.Periodo)
                        ? $" ({prob.Periodo}h)"
                        : "";
                    description.AppendLine($"Probabilidad de precipitación{period}: {prob.Value}%");
                }
            }
        }

        private static string GetFriendlyDate(DateTime? date)
        {
            if (date == null) return "Fecha desconocida";
            
            var today = DateTime.Today;
            var dateValue = date.Value.Date;
            
            if (dateValue == today)
                return "Hoy";
            if (dateValue == today.AddDays(1))
                return "Mañana";

            // Spanish day names
            var dayNames = new string[] { "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" };
            return dayNames[(int)dateValue.DayOfWeek];
        }
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    public static class AemetManagerExtensions
    {
        public static IServiceCollection AddAemetManager(this IServiceCollection services)
        {
            services.AddHttpClient<Api.AemetManager>(client =>
            {
                client.BaseAddress = new Uri("https://opendata.aemet.es/opendata/");
                client.DefaultRequestHeaders.Add("User-Agent", "Microsoft - .NET Aspire Demo");
            });

            services.AddMemoryCache();

            return services;
        }

        public static WebApplication MapAemetApiEndpoints(this WebApplication app)
        {
            app.MapGet("/municipios", async (Api.AemetManager manager) =>
            {
                var municipios = await manager.GetMunicipiosAsync();
                return TypedResults.Ok(municipios);
            })
                .CacheOutput(policy => policy.Expire(TimeSpan.FromHours(1)))
                .WithName("GetMunicipios")
                .WithOpenApi();

            app.MapGet("/aemet-forecast/{municipioId}", async Task<Results<Ok<Api.Data.AemetForecast[]>, NotFound>> (Api.AemetManager manager, string municipioId) =>
            {
                try
                {
                    var forecasts = await manager.GetForecastByMunicipioAsync(municipioId);
                    return TypedResults.Ok(forecasts);
                }
                catch (HttpRequestException)
                {
                    return TypedResults.NotFound();
                }
            })
                .CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(15)).SetVaryByRouteValue("municipioId"))
                .WithName("GetForecastByMunicipio")
                .WithOpenApi();

            return app;
        }
    }
}