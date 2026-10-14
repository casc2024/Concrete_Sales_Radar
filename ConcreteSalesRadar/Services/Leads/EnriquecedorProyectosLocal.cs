using System.Globalization;
using System.Text.Json;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Normalizador SIN IA: mapea el JSON de permisos de Shovels a <see cref="ProyectoLead"/>
/// en C# puro, sin llamar a Claude (costo cero). Sirve para probar el flujo completo del job
/// —Shovels → normalización → dedup → inserción → dashboard— cuando no hay saldo de API.
///
/// Se activa con Leads:Ia:Modo = "Local". No inventa contactos: Shovels no trae email ni
/// teléfono, así que esos campos quedan en null. 'yardas' y 'prioridad' son estimaciones
/// deterministas a partir del valor de obra (no son datos de la fuente).
/// </summary>
public class EnriquecedorProyectosLocal : IEnriquecedorProyectosIa
{
    private readonly ILogger<EnriquecedorProyectosLocal> _logger;

    public EnriquecedorProyectosLocal(ILogger<EnriquecedorProyectosLocal> logger) => _logger = logger;

    public bool EstaConfigurado => true; // No necesita API key.

    public Task<IReadOnlyList<ProyectoLead>> NormalizarAsync(
        string datosCrudos, CriteriosBusqueda criterios, CancellationToken ct)
    {
        var leads = new List<ProyectoLead>();

        using var doc = JsonDocument.Parse(datosCrudos);
        if (!doc.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Respuesta de Shovels sin arreglo 'items'. Nada que normalizar.");
            return Task.FromResult<IReadOnlyList<ProyectoLead>>(leads);
        }

        foreach (var item in items.EnumerateArray())
        {
            var jobValueCent = LeerNumero(item, "job_value");
            var presupuesto = jobValueCent / 100m; // Shovels entrega el valor en centavos.

            var tipo = Texto(item, "property_type") ?? Texto(item, "type");
            var nombre = Texto(item, "description")
                         ?? Texto(item, "description_derived")
                         ?? Texto(item, "number")
                         ?? "Permiso sin descripción";

            leads.Add(new ProyectoLead
            {
                ClaveExterna = Texto(item, "id") ?? Texto(item, "number"),
                NombreProyecto = nombre,
                Tipo = tipo,
                Direccion = ComponerDireccion(item),
                Presupuesto = presupuesto,
                FechaInicio = LeerFecha(item, "start_date")
                              ?? LeerFecha(item, "file_date")
                              ?? LeerFecha(item, "issue_date"),
                EmpresaContacto = Texto(item, "property_legal_owner"),
                Fuente = Texto(item, "jurisdiction") is { } j ? $"Shovels / {j}" : "Shovels",
                EstadoVerificacion = Models.Entities.EstadosVerificacion.Comercial,
                Yardas = EstimarYardas(presupuesto),
                Prioridad = EstimarPrioridad(presupuesto),
                Nota = ComponerNota(item),
                // Shovels no trae contacto directo: nunca lo inventamos.
                NombreContacto = null,
                Email = null,
                Telefono = null,
                TelefonoEmpresa = null
            });
        }

        _logger.LogInformation("Normalizador local: {N} lead(s) mapeado(s) sin IA.", leads.Count);
        return Task.FromResult<IReadOnlyList<ProyectoLead>>(leads);
    }

    // ---- Estimaciones deterministas (no son datos de Shovels) ----

    /// <summary>Estimación tosca de yardas de concreto: ~7% del valor de obra a ~$180/yarda.</summary>
    private static int EstimarYardas(decimal presupuesto)
    {
        if (presupuesto <= 0) return 0;
        var yardas = presupuesto * 0.07m / 180m;
        return (int)Math.Clamp(Math.Round(yardas), 0, int.MaxValue);
    }

    /// <summary>Prioridad 0-100 escalada por tamaño de obra.</summary>
    private static int EstimarPrioridad(decimal presupuesto) => presupuesto switch
    {
        >= 20_000_000m => 95,
        >= 10_000_000m => 85,
        >= 5_000_000m => 75,
        >= 1_000_000m => 65,
        > 0 => 55,
        _ => 45
    };

    private static string? ComponerNota(JsonElement item)
    {
        var partes = new[] { Texto(item, "type"), Texto(item, "subtype"), Texto(item, "status") }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var nota = string.Join(" · ", partes);
        return string.IsNullOrWhiteSpace(nota) ? null : nota;
    }

    private static string? ComponerDireccion(JsonElement item)
    {
        if (!item.TryGetProperty("address", out var dir) || dir.ValueKind != JsonValueKind.Object)
            return null;

        var calle = string.Join(" ", new[] { Texto(dir, "street_no"), Texto(dir, "street") }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
        var cola = string.Join(", ", new[] { Texto(dir, "city"), Texto(dir, "state"), Texto(dir, "zip_code") }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

        var completa = string.Join(", ", new[] { calle, cola }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrWhiteSpace(completa) ? null : completa;
    }

    // ---- Helpers de lectura null-safe ----

    private static string? Texto(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
        if (v.ValueKind == JsonValueKind.Number) return v.ToString();
        return null;
    }

    private static decimal LeerNumero(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)
            ? d : 0m;

    private static DateTime? LeerFecha(JsonElement el, string prop)
    {
        var s = Texto(el, prop);
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
    }
}
