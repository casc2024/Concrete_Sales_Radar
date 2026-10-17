namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Configuración del job automático que puebla la tabla de proyectos con leads reales.
/// Se enlaza desde la sección "Leads" de appsettings / variables de entorno.
/// </summary>
public class OpcionesLeads
{
    /// <summary>Interruptor general. Si es false, el job no hace nada.</summary>
    public bool Activo { get; set; }

    /// <summary>Cada cuántas horas corre el job.</summary>
    public int IntervaloHoras { get; set; } = 1;

    /// <summary>
    /// Máximo de leads nuevos a insertar por ejecución. 0 (o negativo) = SIN TOPE: inserta todos
    /// los que devuelvan las fuentes, y cada fuente trae una página grande en vez de un puñado.
    /// </summary>
    public int MaxPorEjecucion { get; set; } = 25;

    // ---- Criterios fijos de búsqueda (el job corre sin que nadie los escriba) ----
    public string Ubicacion { get; set; } = "North Texas";
    public int RadioMillas { get; set; } = 50;
    public string TipoProyecto { get; set; } = "All";
    public decimal PresupuestoMinimo { get; set; } = 5_000_000m;

    /// <summary>
    /// Categorías de proyecto que el job acepta, separadas por coma (ver CategoriasLeads).
    /// Se evalúa por palabra clave contra Tipo + NombreProyecto + Nota del lead, porque cada
    /// fuente nombra sus tipos distinto. Vacío = sin filtro (acepta cualquier categoría).
    /// </summary>
    public string? Categorias { get; set; } = string.Join(',', CategoriasLeads.PorDefecto);

    /// <summary>Fuente de datos real (API de construcción).</summary>
    public FuenteApiOpciones Fuente { get; set; } = new();

    /// <summary>Configuración de la IA (Claude) que normaliza y prioriza la data.</summary>
    public IaOpciones Ia { get; set; } = new();

    /// <summary>
    /// (DatosAbiertos) Catálogo de datasets Socrata de datos abiertos municipales a consultar.
    /// Si está vacío, se usan los predefinidos (Dallas, Austin, NYC, Chicago, LA). Agregar una
    /// ciudad = agregar una entrada aquí, sin tocar código → cobertura nacional incremental.
    /// </summary>
    public List<DatasetAbierto> Datasets { get; set; } = new();
}

/// <summary>
/// Mapeo de un dataset Socrata a nuestro esquema. Cada propiedad es el NOMBRE DE COLUMNA en el
/// dataset, o un literal con prefijo "@" (ej. Ciudad = "@Dallas"). Vacío = ese campo no aplica.
/// </summary>
public class DatasetAbierto
{
    public string Fuente { get; set; } = "Datos Abiertos";
    public string Prefijo { get; set; } = "OD";
    public string Url { get; set; } = "";
    public string? Orden { get; set; }
    public string? Clave { get; set; }
    public string? Nombre { get; set; }
    public string? Tipo { get; set; }
    /// <summary>Columnas de calle a concatenar (CSV), ej. "street_number,street_direction,street_name".</summary>
    public string? DireccionCols { get; set; }
    public string? Ciudad { get; set; }
    public string? Estado { get; set; }
    public string? Zip { get; set; }
    public string? Presupuesto { get; set; }
    public string? Empresa { get; set; }
    public string? Contacto { get; set; }
    public string? Telefono { get; set; }
    public string? Fecha { get; set; }
    public string? Nota { get; set; }
}

/// <summary>
/// Conexión a la API real de datos de construcción (Dodge, ConstructConnect, Shovels.ai,
/// un portal de permisos, etc.). El job es agnóstico al proveedor: pide los datos crudos a
/// este endpoint y deja que la IA los mapee a nuestra tabla.
/// </summary>
public class FuenteApiOpciones
{
    /// <summary>Proveedor de datos: "Shovels" (permisos de construcción) o "Http" (REST genérico).</summary>
    public string Proveedor { get; set; } = "Shovels";

    /// <summary>API key del proveedor. Si está vacía, el job no inserta nada (nunca inventa datos).</summary>
    public string? ApiKey { get; set; }

    // ---- Específico de Shovels (https://api.shovels.ai/v2/permits/search) ----

    /// <summary>
    /// Filtro geográfico de Shovels: código de estado ("TX"), ZIP, o un geo-id de Shovels
    /// (ej. una ciudad/condado del área DFW). NO acepta texto libre como "North Texas".
    /// Shovels no filtra por radio en millas; se acota eligiendo el geo_id correcto.
    /// </summary>
    public string GeoId { get; set; } = "TX";

    /// <summary>Ventana de búsqueda: permisos iniciados en los últimos N días.</summary>
    public int DiasAtras { get; set; } = 30;

    /// <summary>Si es true, solo permisos que ya tienen contratista asignado.</summary>
    public bool SoloConContratista { get; set; }

    /// <summary>
    /// (TdlrTabs) Códigos de condado de TDLR separados por coma para acotar la búsqueda
    /// (ej. "2057,2220,2061" para DFW). Vacío = TODO Texas (estatal).
    /// </summary>
    public string? Condados { get; set; }

    /// <summary>
    /// (SamGov) API key gratuita de SAM.gov / api.data.gov. Si está vacía se usa la variable
    /// de entorno SAM_GOV_API_KEY. Se obtiene en https://sam.gov (cuenta) o api.data.gov.
    /// </summary>
    public string? SamApiKey { get; set; }

    /// <summary>
    /// (SamGov) Códigos NAICS de construcción separados por coma. Vacío = set por defecto
    /// (edificación, carreteras, obra pesada, etc.).
    /// </summary>
    public string? SamNaics { get; set; }

    // ---- Específico del proveedor "Http" genérico ----

    /// <summary>URL del endpoint REST genérico (solo cuando Proveedor = "Http").</summary>
    public string? Endpoint { get; set; }

    /// <summary>GET o POST (proveedor "Http").</summary>
    public string Metodo { get; set; } = "GET";

    /// <summary>Cabecera donde viaja la API key (proveedor "Http").</summary>
    public string NombreCabeceraApiKey { get; set; } = "Authorization";

    /// <summary>Prefijo de la API key, ej. "Bearer " (proveedor "Http").</summary>
    public string PrefijoApiKey { get; set; } = "Bearer ";

    /// <summary>Parámetros de query adicionales (proveedor "Http").</summary>
    public Dictionary<string, string> QueryExtra { get; set; } = new();
}

public class IaOpciones
{
    /// <summary>
    /// Modo de normalización: "Claude" (IA real, requiere saldo de API) o "Local" (mapeo en
    /// C# sin IA, costo cero, para probar el flujo). Por defecto "Claude".
    /// </summary>
    public string Modo { get; set; } = "Claude";

    /// <summary>API key de Anthropic. Si está vacía se usa la variable ANTHROPIC_API_KEY.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Modelo de Claude a usar.</summary>
    public string Modelo { get; set; } = "claude-sonnet-5-5";
}
