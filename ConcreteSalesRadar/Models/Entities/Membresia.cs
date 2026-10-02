using System.ComponentModel.DataAnnotations;

namespace ConcreteSalesRadar.Models.Entities;

/// <summary>
/// Catálogo de planes de membresía (Starter, Professional, Business, Enterprise).
/// </summary>
public class Membresia
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Descripcion { get; set; }

    /// <summary>Precio mensual en USD. 0 = plan de contacto / personalizado.</summary>
    public decimal PrecioMensual { get; set; }

    /// <summary>Cantidad máxima de usuarios incluidos en el plan.</summary>
    public int MaxUsuarios { get; set; } = 1;

    /// <summary>Características separadas por salto de línea, para mostrar en la lista de precios.</summary>
    [StringLength(1000)]
    public string? Caracteristicas { get; set; }

    /// <summary>Marca visual "Más popular".</summary>
    public bool EsPopular { get; set; }

    public bool Activa { get; set; } = true;

    /// <summary>Orden de aparición en la página de precios.</summary>
    public int Orden { get; set; }

    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}
