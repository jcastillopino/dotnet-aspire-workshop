using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Data;

public class MunicipioData
{
    public string? Id { get; set; }
    public string? Nombre { get; set; }
    public string? Id_Old { get; set; }
}

public class AemetResponse
{
    [JsonPropertyName("estado")]
    public int Estado { get; set; }
    public string? Datos { get; set; }
    public string? Metadatos { get; set; }
}

public class AemetForecast
{
    [JsonPropertyName("name")]
    public string? Date { get; set; }
    
    [JsonPropertyName("detailedForecast")]
    public string? Description { get; set; }

    public AemetForecast(string date, string description)
    {
        Date = date;
        Description = description;
    }
}

public class AemetForecastData
{
    public DateTime? Elaborado { get; set; }
    public string? Nombre { get; set; }
    public string? Provincia { get; set; }
    public AemetForecastDataPrediccion? Prediccion { get; set; }
}

public class AemetForecastDataPrediccion
{
    public List<AemetForecastDataDia>? Dia { get; set; }
}

public class AemetForecastDataDia
{
    public DateTime? Fecha { get; set; }
    public AemetForecastDataTemperatura? Temperatura { get; set; }
    public List<AemetForecastDataEstadoCielo>? EstadoCielo { get; set; }
    public List<AemetForecastDataViento>? Viento { get; set; }
    public List<AemetForecastDataProbPrecipitacion>? ProbPrecipitacion { get; set; }
}

public class AemetForecastDataProbPrecipitacion
{
    [JsonPropertyName("periodo")]
    public string? Periodo { get; set; }
    
    [JsonPropertyName("value")]
    public int Value { get; set; }
}

public class AemetForecastDataViento
{
    [JsonPropertyName("direccion")]
    public string? Direccion { get; set; }
    
    [JsonPropertyName("velocidad")]
    public int? Velocidad { get; set; }
    
    [JsonPropertyName("periodo")]
    public string? Periodo { get; set; }
}

public class AemetForecastDataTemperatura
{
    public int? Maxima { get; set; }
    public int? Minima { get; set; }
}

public class AemetForecastDataEstadoCielo
{
    [JsonPropertyName("descripcion")]
    public string? Descripcion { get; set; }
    
    [JsonPropertyName("periodo")]
    public string? Periodo { get; set; }
}