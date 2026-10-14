namespace ConcreteSalesRadar.Models.Entities;

/// <summary>
/// Valores permitidos del estado de verificación de un proyecto (regla de negocio de calidad
/// del dato). Se guardan como texto legible en la columna EstadoVerificacion.
/// </summary>
public static class EstadosVerificacion
{
    public const string PermisoOficial = "Verificado – Permiso oficial";
    public const string LicitacionMunicipal = "Verificado – Licitación municipal";
    public const string TdlrEstatal = "Verificado – Registro TDLR/Estatal";
    public const string Comercial = "Fuente de datos comerciales";
    public const string Planificacion = "Planificación / Permiso previo";
    public const string Estimado = "Estimado";

    public static readonly string[] Todos =
        { PermisoOficial, LicitacionMunicipal, TdlrEstatal, Comercial, Planificacion, Estimado };

    /// <summary>True si el estado corresponde a una fuente oficial verificada.</summary>
    public static bool EsVerificadoOficial(string? estado) =>
        estado is PermisoOficial or LicitacionMunicipal or TdlrEstatal;
}
