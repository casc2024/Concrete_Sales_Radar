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

    /// <summary>Prioridad comercial 0-100.</summary>
    public int Prioridad { get; set; } = 55;

    [StringLength(500)]
    public string? Nota { get; set; }

    // Propietario del registro
    public int? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
