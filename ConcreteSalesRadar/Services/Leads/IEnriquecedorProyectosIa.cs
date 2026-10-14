using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>Un lead ya normalizado por la IA, listo para mapear a la entidad Proyecto.</summary>
public class ProyectoLead
{
    public string? ClaveExterna { get; set; }
    public string? Direccion { get; set; }
    public string NombreProyecto { get; set; } = string.Empty;
    public string? Tipo { get; set; }
    public DateTime? FechaInicio { get; set; }
    public decimal Presupuesto { get; set; }
    public int Yardas { get; set; }
    public string? EmpresaContacto { get; set; }
    public string? NombreContacto { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public string? TelefonoEmpresa { get; set; }
    public string? Fuente { get; set; }
    public int Prioridad { get; set; } = 55;
    public string? Nota { get; set; }

    /// <summary>Estado de verificación que la fuente puede fijar (ver EstadosVerificacion). Si es
    /// nulo, el job usa "Estimado".</summary>
    public string? EstadoVerificacion { get; set; }

    /// <summary>True si la fuente publicó el volumen de concreto explícitamente (casi nunca).</summary>
    public bool YardasReportadas { get; set; }

    /// <summary>True para leads de la "red amplia" (auto-mapeados): el job les baja la confianza
    /// y los etiqueta, porque el mapeo de columnas fue heurístico, no verificado a mano.</summary>
    public bool BajaConfianza { get; set; }
}

public interface IEnriquecedorProyectosIa
{
    bool EstaConfigurado { get; }

    /// <summary>
    /// Toma la respuesta cruda de la API de construcción y devuelve leads normalizados a
    /// nuestra estructura. No inventa datos: solo reestructura lo que viene en la fuente.
    /// </summary>
    Task<IReadOnlyList<ProyectoLead>> NormalizarAsync(string datosCrudos, CriteriosBusqueda criterios, CancellationToken ct);
}
