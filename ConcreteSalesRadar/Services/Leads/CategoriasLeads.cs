using System.Text.RegularExpressions;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Filtro de categorías de proyecto para el job en background. Se evalúa contra el texto libre
/// que cada fuente ya trae (Tipo + NombreProyecto + Nota), porque cada proveedor (TDLR, Socrata,
/// ArcGIS, SAM.gov, Shovels...) nombra sus tipos de permiso distinto — no hay un código común.
/// Por eso el match es por palabras clave (regex, insensible a mayúsculas), no por un enum.
/// </summary>
public static class CategoriasLeads
{
    /// <summary>Categorías por defecto que pidió el negocio para la tarea en background.</summary>
    public static readonly string[] PorDefecto =
    {
        "FOUNDATION", "PAVING", "DRIVEWAY", "SIDEWALK", "NEW CONSTRUCTION", "NEW BUILDING",
        "COMMERCIAL NEW", "INDUSTRIAL NEW", "MULTIFAMILY NEW", "SINGLE FAMILY NEW",
        "WAREHOUSE", "DATA CENTER", "PARKING LOT", "GRADING", "ADDITION", "POOL"
    };

    /// <summary>Un patrón regex por categoría; cubre variantes comunes de redacción de permisos.</summary>
    // Nota: los permisos suelen venir en plural ("Additions and Renovations", "Sidewalks"),
    // así que casi todos los sustantivos permiten una 's' final opcional antes del límite.
    private static readonly Dictionary<string, Regex> Patrones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FOUNDATION"] = Rx(@"\bfoundations?\b"),
        ["PAVING"] = Rx(@"\bpav(e[sd]?|ing|ements?)\b"),
        ["DRIVEWAY"] = Rx(@"\bdriveways?\b"),
        ["SIDEWALK"] = Rx(@"\bside\s?walks?\b"),
        // Incluye fraseo típico de ingeniería vial (TxDOT y similares): "Construct New Road",
        // "New Arterial", "New Roadway Lanes" son obra nueva aunque no digan "construction".
        ["NEW CONSTRUCTION"] = Rx(@"\bnew\s+construction\b|\bnew\s+builds?\b|\bconstruct\s+new\b|\bnew\s+(road|roadway|arterial|lanes?)\b"),
        ["NEW BUILDING"] = Rx(@"\bnew\s+buildings?\b"),
        ["COMMERCIAL NEW"] = Rx(@"\bnew\b[^.]{0,30}\bcommercial\b|\bcommercial\b[^.]{0,30}\bnew\b"),
        ["INDUSTRIAL NEW"] = Rx(@"\bnew\b[^.]{0,30}\bindustrial\b|\bindustrial\b[^.]{0,30}\bnew\b"),
        ["MULTIFAMILY NEW"] = Rx(@"\bnew\b[^.]{0,30}\bmulti[\s-]?family\b|\bmulti[\s-]?family\b[^.]{0,30}\bnew\b"),
        ["SINGLE FAMILY NEW"] = Rx(@"\bnew\b[^.]{0,30}\bsingle[\s-]?family\b|\bsingle[\s-]?family\b[^.]{0,30}\bnew\b"),
        ["WAREHOUSE"] = Rx(@"\bwarehouses?\b"),
        ["DATA CENTER"] = Rx(@"\bdata\s?centers?\b"),
        ["PARKING LOT"] = Rx(@"\bparking\s?lots?\b|\bparking\s+(area|structure|garage)s?\b"),
        ["GRADING"] = Rx(@"\bgrading\b|\bsite\s+grading\b"),
        ["ADDITION"] = Rx(@"\badditions?\b"),
        ["POOL"] = Rx(@"\bpools?\b|\bswimming\s+pools?\b"),
    };

    private static Regex Rx(string patron) => new(patron, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// True si el texto combinado del lead (tipo/nombre/nota) coincide con alguna de las
    /// categorías dadas. Categorías desconocidas (no están en <see cref="Patrones"/>) se
    /// ignoran en vez de bloquear todo el filtro.
    /// </summary>
    public static bool Coincide(string? tipo, string? nombre, string? nota, IEnumerable<string> categorias)
    {
        var texto = string.Join(" | ", new[] { tipo, nombre, nota }.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (string.IsNullOrWhiteSpace(texto)) return false;

        foreach (var cat in categorias)
        {
            if (Patrones.TryGetValue(cat.Trim(), out var rx) && rx.IsMatch(texto))
                return true;
        }
        return false;
    }

    /// <summary>Convierte el CSV guardado en configuración a la lista de categorías activas.</summary>
    public static string[] DesdeCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
