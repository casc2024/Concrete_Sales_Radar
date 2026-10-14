using System.ComponentModel.DataAnnotations;

namespace ConcreteSalesRadar.Models.Entities;

/// <summary>
/// Obra / proyecto de construcción prospectado (equivalente al registro del
/// dashboard de "Concrete Sales Radar").
/// </summary>
public class Proyecto
{
    public int Id { get; set; }

    [StringLength(250)]
    public string? Direccion { get; set; }

    [Required]
    [StringLength(200)]
    public string NombreProyecto { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Tipo { get; set; }

    public DateTime? FechaInicio { get; set; }

    public decimal Presupuesto { get; set; }

    /// <summary>Yardas cúbicas de concreto estimadas.</summary>
    public int Yardas { get; set; }

    [StringLength(200)]
    public string? EmpresaContacto { get; set; }

    [StringLength(120)]
    public string? NombreContacto { get; set; }

    [StringLength(120)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Telefono { get; set; }

    [StringLength(50)]
    public string? TelefonoEmpresa { get; set; }

    [StringLength(250)]
    public string? Fuente { get; set; }

    /// <summary>
    /// Clave única del registro en la fuente externa (id de permiso/proyecto de la API
    /// de construcción). Se usa para no insertar el mismo lead dos veces cuando corre el
    /// job automático. Nulo para los proyectos creados manualmente.
    /// </summary>
    [StringLength(200)]
    public string? ClaveExterna { get; set; }

    /// <summary>True si el registro lo generó el job automático con IA (no manual).</summary>
    public bool GeneradoPorIa { get; set; }

    /// <summary>
    /// Sales Opportunity Score (0-100): qué tan buena oportunidad comercial es. Métrica SEPARADA
    /// del ConfidenceScore (que mide fiabilidad del dato, no probabilidad de venta).
    /// </summary>
    public int Prioridad { get; set; } = 55;

    [StringLength(500)]
    public string? Nota { get; set; }

    // --- Reglas de negocio: calidad y verificación del dato ---

    /// <summary>Estado de verificación (ver <see cref="EstadosVerificacion"/>).</summary>
    [Required]
    [StringLength(60)]
    public string EstadoVerificacion { get; set; } = EstadosVerificacion.Estimado;

    /// <summary>Fecha de la última revalidación del proyecto contra su fuente.</summary>
    public DateTime UltimaVerificacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Confidence Score (0-100): fiabilidad de la INFORMACIÓN del proyecto. NO representa la
    /// probabilidad de ganar la venta (eso es <see cref="Prioridad"/>).
    /// </summary>
    public int ConfidenceScore { get; set; }

    /// <summary>
    /// True solo si la fuente original publicó explícitamente el volumen de concreto
    /// ("Yardas reportadas"). False = <see cref="Yardas"/> es una ESTIMACIÓN del sistema.
    /// </summary>
    public bool YardasReportadas { get; set; }

    // Propietario del registro
    public int? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
