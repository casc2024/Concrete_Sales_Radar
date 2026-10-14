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

    /// <summary>
    /// Campos de la grilla de proyectos que ESTE plan deja ver, como CSV de claves
    /// (ver <see cref="CamposProyecto"/>). Vacío = muestra todos los campos.
    /// </summary>
    [StringLength(500)]
    public string? CamposVisibles { get; set; }

    /// <summary>
    /// Id del producto en Stripe (prod_xxx). Lo mantiene el sistema automáticamente:
    /// se crea/actualiza al guardar el plan. No se edita a mano.
    /// </summary>
    [StringLength(100)]
    public string? StripeProductId { get; set; }

    /// <summary>
    /// Id del precio recurrente en Stripe (price_xxx) con el que se hace el match
    /// al cobrar la suscripción. Lo mantiene el sistema automáticamente: como un precio
    /// de Stripe es inmutable, si cambia el monto se crea uno nuevo y se guarda aquí.
    /// </summary>
    [StringLength(100)]
    public string? StripePriceId { get; set; }

    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}
