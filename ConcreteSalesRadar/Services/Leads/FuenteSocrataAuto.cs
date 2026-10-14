using System.Text.Json;
using System.Text.RegularExpressions;
using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// RED AMPLIA (auto-descubrimiento): recorre el catálogo nacional de Socrata buscando datasets de
/// permisos de construcción en EE.UU., AUTO-MAPEA sus columnas por heurística (cada ciudad las
/// nombra distinto) y trae leads de decenas de ciudades sin configuración previa.
///
/// Como el mapeo es heurístico (no verificado a mano), estos leads se marcan BajaConfianza = true:
/// el job les topa el confidence score y el nombre de la fuente lleva "(auto)". Complementa a las
/// ciudades curadas de alta calidad. Se usa dentro del proveedor "Nacional".
/// </summary>
public class FuenteSocrataAuto : IFuenteProyectos
{
    private const string Catalogo = "http://api.us.socrata.com/api/catalog/v1?only=dataset&q=building%20permits&limit=40";
    private const int MaxDatasets = 12; // cota de cortesía por pasada

    private readonly HttpClient _http;
    private readonly OpcionesLeads _opciones;
    private readonly ILogger<FuenteSocrataAuto> _logger;

    public FuenteSocrataAuto(HttpClient http, OpcionesLeads opciones, ILogger<FuenteSocrataAuto> logger)
    {
        _http = http;
        _opciones = opciones;
        _logger = logger;
    }

    public bool EstaConfigurada => true;

    public async Task<string?> ObtenerDatosCrudosAsync(CriteriosBusqueda criterios, CancellationToken ct)
    {
        var porCiudad = _opciones.MaxPorEjecucion > 0 ? Math.Clamp(_opciones.MaxPorEjecucion, 1, 50) : 1000;
        var leads = new List<ProyectoLead>();

        List<(string dominio, string id, string nombre)> candidatos;
        try
        {
            candidatos = await DescubrirAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Red amplia: falló el catálogo de Socrata.");
            return JsonSerializer.Serialize(leads);
        }

        foreach (var (dominio, id, _) in candidatos.Take(MaxDatasets))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var ds = await AutoMapearAsync(dominio, id, ct);
                if (ds is null) continue;

                var url = $"https://{dominio}/resource/{id}.json?$limit={porCiudad}"
                    + (string.IsNullOrWhiteSpace(ds.Fecha) ? "" : $"&$order={Uri.EscapeDataString(ds.Fecha + " DESC")}");
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                var antes = leads.Count;
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    var lead = MapeoDatosAbiertos.Mapear(row, ds, EstadosVerificacion.PermisoOficial);
                    if (lead is null) continue;
                    lead.BajaConfianza = true; // red amplia: mapeo heurístico
                    if (lead.Presupuesto > 0 && lead.Presupuesto < criterios.PresupuestoMinimo) continue;
                    leads.Add(lead);
                }
                _logger.LogInformation("Red amplia {Dominio}: {N} lead(s).", dominio, leads.Count - antes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Red amplia: dataset {Dominio}/{Id} omitido.", dominio, id);
            }
        }

        return JsonSerializer.Serialize(leads);
    }

    /// <summary>Busca en el catálogo datasets de permisos frescos de EE.UU. (excluye Canadá/agregados).</summary>
    private async Task<List<(string, string, string)>> DescubrirAsync(CancellationToken ct)
    {
        var resultado = new List<(string, string, string)>();
        using var resp = await _http.GetAsync(Catalogo, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("results", out var arr)) return resultado;

        var vistos = new HashSet<string>();
        foreach (var r in arr.EnumerateArray())
        {
            var dominio = r.TryGetProperty("metadata", out var md) && md.TryGetProperty("domain", out var d)
                ? d.GetString() : null;
            var res = r.GetProperty("resource");
            var id = res.TryGetProperty("id", out var i) ? i.GetString() : null;
            var nombre = res.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (dominio is null || id is null) continue;
            if (dominio.EndsWith(".ca")) continue;                              // Canadá fuera
            if (!vistos.Add(dominio)) continue;                                 // una tabla por portal
            if (Regex.IsMatch(nombre, "retired|aggregate|count|timeline|metric|inspection|archive|fiscal",
                    RegexOptions.IgnoreCase)) continue;                         // no permisos crudos
            resultado.Add((dominio, id, nombre));
        }
        return resultado;
    }

    /// <summary>Lee las columnas reales del dataset y deduce el mapeo por nombre.</summary>
    private async Task<DatasetAbierto?> AutoMapearAsync(string dominio, string id, CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"https://{dominio}/resource/{id}.json?$limit=1", ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;

        var cols = doc.RootElement[0].EnumerateObject().Select(p => p.Name)
            .Where(c => !c.StartsWith(":@")).ToList();
        if (cols.Count == 0) return null;

        string? Buscar(string patron) =>
            cols.FirstOrDefault(c => Regex.IsMatch(c, patron, RegexOptions.IgnoreCase));

        var clave = Buscar(@"^permit_?(number|num|no|id)$") ?? Buscar(@"permit.*(num|no|id)")
                    ?? Buscar(@"^(permitnumber|permitnum|permit_)$") ?? Buscar(@"^id$");
        if (clave is null) return null; // sin clave no se puede deduplicar

        return new DatasetAbierto
        {
            Fuente = $"{dominio} (auto)",
            Prefijo = Prefijo(dominio),
            Clave = clave,
            Nombre = Buscar(@"work.*desc|description|project.*name|scope|proposed.*use"),
            Tipo = Buscar(@"permit.*type|work.*class|application.*type|^type$"),
            DireccionCols = Buscar(@"original.*address|street_address|primary_address|^address$|full.*address|street.*name")
                            ?? "",
            Ciudad = Buscar(@"^city$|original.*city|municipality"),
            Estado = Buscar(@"^state$|original.*state"),
            Zip = Buscar(@"zip"),
            Presupuesto = Buscar(@"valuation|reported.*cost|est.*cost|declaredvaluation|job.*value|project.*value|^value$|construction.*cost"),
            Empresa = Buscar(@"contractor|applicant|company|permittee.*business|owner.*business"),
            Fecha = Buscar(@"issue.*date|issued|issuance|status.*date|file.*date")
        };
    }

    private static string Prefijo(string dominio)
    {
        var raw = dominio.Replace("data.", "").Replace("www.", "").Split('.')[0];
        var letras = new string(raw.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return letras.Length >= 3 ? letras[..3] : (letras.Length == 0 ? "OD" : letras);
    }
}
