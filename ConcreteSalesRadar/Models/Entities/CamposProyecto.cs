namespace ConcreteSalesRadar.Models.Entities;

/// <summary>
/// Catálogo de campos de la grilla de proyectos que se pueden habilitar/ocultar por membresía.
/// La clave es el identificador estable (se guarda en Membresia.CamposVisibles como CSV); la
/// clave de recurso localiza la etiqueta en el editor/grilla.
/// </summary>
public static class CamposProyecto
{
    /// <summary>(clave estable, clave de recurso para la etiqueta).</summary>
    public static readonly (string Clave, string Recurso)[] Todos =
    {
        ("prioridad", "Col_Priority"),
        ("direccion", "Col_Address"),
        ("proyecto", "Col_Project"),
        ("tipo", "Col_Type"),
        ("antiguedad", "Col_Age"),
        ("presupuesto", "Col_Budget"),
        ("yardas", "Col_Yards"),
        ("empresa", "Col_ContactCompany"),
        ("nombre", "Col_Name"),
        ("email", "Col_Email"),
        ("telefono", "Col_PhoneDirect"),
        ("telefonoEmpresa", "Col_PhoneCompany"),
        ("fuente", "Col_Source"),
    };

    public static readonly string[] Claves = Todos.Select(c => c.Clave).ToArray();

    /// <summary>
    /// Convierte el CSV guardado en la membresía a un conjunto. Si está vacío/nulo, devuelve
    /// TODOS los campos (una membresía sin configurar muestra todo, no oculta todo).
    /// </summary>
    public static HashSet<string> Conjunto(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return new HashSet<string>(Claves, StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>(
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
    }
}
