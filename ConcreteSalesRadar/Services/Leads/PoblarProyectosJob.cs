using ConcreteSalesRadar.Data;
using ConcreteSalesRadar.Models.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Job de Hangfire que consulta las fuentes reales de proyectos de construcción, las normaliza
/// (paso directo o IA según la fuente) y las inserta en la tabla de proyectos (sin dueño, visibles
/// para el admin). Corre TODOS los pipelines configurados en una misma ejecución, junta los leads
/// y deduplica por ClaveExterna. Su ejecución e historial se ven en /hangfire.
/// </summary>
public class PoblarProyectosJob
{
    /// <summary>Id fijo del job recurrente en Hangfire.</summary>
    public const string IdRecurrente = "poblar-proyectos";

    private readonly OpcionesLeads _opciones;
    private readonly IReadOnlyList<PipelineLeads> _pipelines;
    private readonly AppDbContext _db;
    private readonly ILogger<PoblarProyectosJob> _logger;

    public PoblarProyectosJob(IOptions<OpcionesLeads> opciones, IReadOnlyList<PipelineLeads> pipelines,
        AppDbContext db, ILogger<PoblarProyectosJob> logger)
    {
        _opciones = opciones.Value;
        _pipelines = pipelines;
        _db = db;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 30)]
    public async Task<string> EjecutarAsync(CancellationToken ct)
    {
        var criterios = new CriteriosBusqueda(
            _opciones.Ubicacion, _opciones.RadioMillas, _opciones.TipoProyecto, _opciones.PresupuestoMinimo);

        // --- Corre cada pipeline (fuente + su enriquecedor) y junta los leads ---
        var leads = new List<ProyectoLead>();
        var resumen = new List<string>();

        foreach (var pipe in _pipelines)
        {
            try
            {
                if (!pipe.Fuente.EstaConfigurada) { resumen.Add($"{pipe.Nombre}: fuente sin configurar"); continue; }
                if (!pipe.Ia.EstaConfigurado) { resumen.Add($"{pipe.Nombre}: IA sin configurar"); continue; }

                var crudo = await pipe.Fuente.ObtenerDatosCrudosAsync(criterios, ct);
                if (string.IsNullOrWhiteSpace(crudo)) { resumen.Add($"{pipe.Nombre}: sin datos"); continue; }

                var norm = await pipe.Ia.NormalizarAsync(crudo, criterios, ct);
                resumen.Add($"{pipe.Nombre}: {norm.Count} normalizado(s)");
                leads.AddRange(norm);
            }
            catch (Exception ex)
            {
                // Un pipeline que falla no tumba a los demás: se anota y se sigue.
                _logger.LogError(ex, "Pipeline de leads '{Nombre}' falló", pipe.Nombre);
                resumen.Add($"{pipe.Nombre}: ERROR {ex.Message}");
            }
        }

        var detalle = string.Join(" | ", resumen);
        if (leads.Count == 0)
            return $"Sin leads. {detalle}";

        // Filtro de categorías (aplica a todas las fuentes a la vez).
        var categorias = CategoriasLeads.DesdeCsv(_opciones.Categorias);
        var totalAntesDeCategoria = leads.Count;
        if (categorias.Length > 0)
            leads = leads.Where(l => CategoriasLeads.Coincide(l.Tipo, l.NombreProyecto, l.Nota, categorias))
                         .ToList();
        if (leads.Count == 0)
            return $"Sin leads: ninguno de los {totalAntesDeCategoria} coincide con las categorías. {detalle}";

        // Dedup: contra la BD (por ClaveExterna) y dentro del propio lote combinado.
        var claves = leads.Where(l => !string.IsNullOrWhiteSpace(l.ClaveExterna))
                          .Select(l => l.ClaveExterna!).Distinct().ToList();
        var existentes = claves.Count == 0
            ? new HashSet<string>()
            : (await _db.Proyectos.Where(p => p.ClaveExterna != null && claves.Contains(p.ClaveExterna))
                                  .Select(p => p.ClaveExterna!).ToListAsync(ct))
              .ToHashSet();

        var vistos = new HashSet<string>();
        var insertados = 0;

        foreach (var lead in leads)
        {
            if (_opciones.MaxPorEjecucion > 0 && insertados >= _opciones.MaxPorEjecucion) break;

            var clave = lead.ClaveExterna;
            if (!string.IsNullOrWhiteSpace(clave))
            {
                if (existentes.Contains(clave) || !vistos.Add(clave)) continue;
            }

            // --- Reglas de calidad del dato ---
            var estado = string.IsNullOrWhiteSpace(lead.EstadoVerificacion)
                ? EstadosVerificacion.Estimado : lead.EstadoVerificacion!;
            var tieneEmpresa = !string.IsNullOrWhiteSpace(lead.EmpresaContacto);
            var tieneContacto = !string.IsNullOrWhiteSpace(lead.Telefono)
                || !string.IsNullOrWhiteSpace(lead.TelefonoEmpresa)
                || !string.IsNullOrWhiteSpace(lead.Email)
                || !string.IsNullOrWhiteSpace(lead.NombreContacto);

            var confianza = ReglasCalidadDatos.Confianza(estado, tieneEmpresa, tieneContacto);
            if (lead.BajaConfianza) confianza = Math.Min(confianza, 60);

            _db.Proyectos.Add(new Proyecto
            {
                ClaveExterna = Cortar(string.IsNullOrWhiteSpace(clave) ? null : clave, 200),
                Direccion = Cortar(lead.Direccion, 250),
                NombreProyecto = Cortar(lead.NombreProyecto, 200) ?? string.Empty,
                Tipo = Cortar(lead.Tipo, 50),
                FechaInicio = lead.FechaInicio.HasValue
                    ? DateTime.SpecifyKind(lead.FechaInicio.Value, DateTimeKind.Unspecified)
                    : null,
                Presupuesto = lead.Presupuesto,
                Yardas = lead.Yardas,
                YardasReportadas = lead.YardasReportadas,
                EmpresaContacto = Cortar(lead.EmpresaContacto, 200),
                NombreContacto = Cortar(lead.NombreContacto, 120),
                Email = Cortar(lead.Email, 120),
                Telefono = Cortar(lead.Telefono, 50),
                TelefonoEmpresa = Cortar(lead.TelefonoEmpresa, 50),
                Fuente = Cortar(lead.Fuente ?? "IA + API de construcción", 250),
                EstadoVerificacion = estado,
                UltimaVerificacion = DateTime.UtcNow,
                ConfidenceScore = confianza,
                Prioridad = ReglasCalidadDatos.Oportunidad(lead.Presupuesto, tieneContacto, lead.FechaInicio, estado),
                Nota = Cortar(lead.Nota, 500),
                GeneradoPorIa = true,
                UsuarioId = null,
                FechaCreacion = DateTime.UtcNow
            });
            insertados++;
        }

        if (insertados > 0)
            await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Job de leads: {Insertados} proyecto(s) nuevo(s) insertado(s).", insertados);
        return $"OK: {insertados} nuevo(s) de {leads.Count} candidato(s). {detalle}";
    }

    /// <summary>Recorta un texto al máximo permitido por la columna (null-safe).</summary>
    private static string? Cortar(string? texto, int max)
    {
        if (string.IsNullOrEmpty(texto)) return texto;
        texto = texto.Trim();
        return texto.Length <= max ? texto : texto[..max];
    }
}
