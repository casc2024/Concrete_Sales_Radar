using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Mapeo genérico de una fila de datos abiertos (Socrata o ArcGIS) a <see cref="ProyectoLead"/>,
/// guiado por un <see cref="DatasetAbierto"/>. Compartido por FuenteDatosAbiertos (Socrata) y
/// FuenteArcGis (Esri), que solo difieren en cómo obtienen las filas.
/// </summary>
public static class MapeoDatosAbiertos
{
    private static readonly Regex RxTelefono =
        new(@"\(?\d{3}\)?[\s.-]?\d{3}[\s.-]?\d{4}", RegexOptions.Compiled);

    public static ProyectoLead? Mapear(JsonElement r, DatasetAbierto ds, string estadoVerificacion)
    {
        var clave = Val(r, ds.Clave);
        if (string.IsNullOrWhiteSpace(clave)) return null;

        var empresa = Val(r, ds.Empresa);
        var telefono = Val(r, ds.Telefono);
        // Teléfono embebido en el nombre de la empresa (caso Dallas): extraer y limpiar.
        if (string.IsNullOrWhiteSpace(telefono) && !string.IsNullOrWhiteSpace(empresa))
        {
            var m = RxTelefono.Match(empresa);
            if (m.Success)
            {
                telefono = m.Value.Trim();
                var corte = Regex.Match(empresa, @"\s\d");
                empresa = (corte.Success ? empresa[..corte.Index] : empresa.Replace(m.Value, "")).Trim();
            }
        }

        var presupuesto = Dec(Val(r, ds.Presupuesto));
        return new ProyectoLead
        {
            ClaveExterna = $"{ds.Prefijo}-{clave}",
            NombreProyecto = Val(r, ds.Nombre) ?? Val(r, ds.Tipo) ?? $"Permiso {clave}",
            Tipo = Val(r, ds.Tipo),
            Direccion = Direccion(r, ds),
            Presupuesto = presupuesto,
            FechaInicio = Fecha(Val(r, ds.Fecha)),
            EmpresaContacto = string.IsNullOrWhiteSpace(empresa) ? null : empresa,
            NombreContacto = Val(r, ds.Contacto),
            TelefonoEmpresa = telefono,
            Fuente = ds.Fuente,
            EstadoVerificacion = estadoVerificacion,
            Yardas = Yardas(presupuesto),
            Prioridad = Prioridad(presupuesto),
            Nota = Val(r, ds.Nota)
        };
    }

    private static string? Direccion(JsonElement r, DatasetAbierto ds)
    {
        var calle = string.Join(" ", (ds.DireccionCols ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => Val(r, c)).Where(v => !string.IsNullOrWhiteSpace(v)));
        var cola = string.Join(", ", new[] { Val(r, ds.Ciudad), Val(r, ds.Estado), Val(r, ds.Zip) }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
        var full = string.Join(", ", new[] { calle, cola }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return string.IsNullOrWhiteSpace(full) ? null : full;
    }

    /// <summary>Literal si empieza con "@"; si no, nombre de columna/atributo a leer.</summary>
    public static string? Val(JsonElement r, string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        if (spec[0] == '@') return spec[1..];
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty(spec, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString()!.Trim(),
            JsonValueKind.Number => v.ToString(),
            _ => null
        };
    }

    private static decimal Dec(string? s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : 0m;

    private static DateTime? Fecha(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // ArcGIS suele dar fechas como epoch en milisegundos.
        if (long.TryParse(s, out var ms) && ms > 100000000000)
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
    }

    private static int Yardas(decimal p) =>
        p <= 0 ? 0 : (int)Math.Clamp(Math.Round(p * 0.07m / 180m), 0, int.MaxValue);

    private static int Prioridad(decimal p) => p switch
    {
        >= 20_000_000m => 95,
        >= 10_000_000m => 85,
        >= 5_000_000m => 75,
        >= 1_000_000m => 65,
        > 0 => 55,
        _ => 45
    };
}
