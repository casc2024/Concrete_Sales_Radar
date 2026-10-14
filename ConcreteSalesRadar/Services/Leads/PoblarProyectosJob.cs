using ConcreteSalesRadar.Data;
using ConcreteSalesRadar.Models.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Job de Hangfire que consulta la fuente real de proyectos de construcción, los normaliza con IA
/// y los inserta en la tabla de proyectos (sin dueño, visibles para el admin). Deduplica por
/// ClaveExterna para no repetir leads. Su ejecución e historial se ven en /hangfire.
/// </summary>
public class PoblarProyectosJob
{
    /// <summary>Id fijo del job recurrente en Hangfire.</summary>
    public const string IdRecurrente = "poblar-proyectos";

    private readonly OpcionesLeads _opciones;
    private readonly IFuenteProyectos _fuente;
    private readonly IEnriquecedorProyectosIa _ia;
    private readonly AppDbContext _db;
    private readonly ILogger<PoblarProyectosJob> _logger;

    public PoblarProyectosJob(IOptions<OpcionesLeads> opciones, IFuenteProyectos fuente,
        IEnriquecedorProyectosIa ia, AppDbContext db, ILogger<PoblarProyectosJob> logger)
    {
        _opciones = opciones.Value;
        _fuente = fuente;
        _ia = ia;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Devuelve un resumen que Hangfire guarda como resultado del job (visible en el dashboard).
    /// Si falta configuración o la fuente no responde, termina con éxito y lo indica en el resumen.
    /// </summary>
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 })]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 30)]
    public async Task<string> EjecutarAsync(CancellationToken ct)
    {
        if (!_fuente.EstaConfigurada)
            return "Omitido: fuente de leads sin configurar (Leads:Fuente:ApiKey).";
        if (!_ia.EstaConfigurado)
            return "Omitido: IA sin configurar (ANTHROPIC_API_KEY / Leads:Ia:ApiKey).";

        var criterios = new CriteriosBusqueda(
            _opciones.Ubicacion, _opciones.RadioMillas, _opciones.TipoProyecto, _opciones.PresupuestoMinimo);

        var crudo = await _fuente.ObtenerDatosCrudosAsync(criterios, ct);
        if (string.IsNullOrWhiteSpace(crudo))
            return "Sin datos: la fuente no devolvió respuesta válida.";

        var leads = await _ia.NormalizarAsync(crudo, criterios, ct);
        if (leads.Count == 0)
            return "Sin leads: la IA no devolvió proyectos normalizados.";

        // Dedup: contra la BD (por ClaveExterna) y dentro del propio lote.
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
            // Red amplia (auto-mapeado): se topa la confianza para no inflar datos sin verificar.
            if (lead.BajaConfianza) confianza = Math.Min(confianza, 60);

            _db.Proyectos.Add(new Proyecto
            {
                // Recortamos cada campo a su límite en la entidad: la fuente puede traer
                // textos más largos (ej. un 'type'/'subtype' de Shovels > 50 chars) y sin
                // esto Postgres rechaza el insert con "value too long for varying(N)".
                ClaveExterna = Cortar(string.IsNullOrWhiteSpace(clave) ? null : clave, 200),
                Direccion = Cortar(lead.Direccion, 250),
                NombreProyecto = Cortar(lead.NombreProyecto, 200) ?? string.Empty,
                Tipo = Cortar(lead.Tipo, 50),
                FechaInicio = lead.FechaInicio.HasValue
                    ? DateTime.SpecifyKind(lead.FechaInicio.Value, DateTimeKind.Unspecified)
                    : null,
                Presupuesto = lead.Presupuesto,
                Yardas = lead.Yardas,
                YardasReportadas = lead.YardasReportadas, // casi siempre false = estimadas
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
        return $"OK: {insertados} proyecto(s) nuevo(s) de {leads.Count} normalizado(s).";
    }

    /// <summary>Recorta un texto al máximo permitido por la columna (null-safe).</summary>
    private static string? Cortar(string? texto, int max)
    {
        if (string.IsNullOrEmpty(texto)) return texto;
        texto = texto.Trim();
        return texto.Length <= max ? texto : texto[..max];
    }
}
