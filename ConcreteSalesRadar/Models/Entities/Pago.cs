using System.ComponentModel.DataAnnotations;

namespace ConcreteSalesRadar.Models.Entities;

public enum EstadoPago
{
    Pendiente = 0,
    Pagado = 1,
    Cancelado = 2
}

/// <summary>
/// Registro de un pago de membresía realizado a través de Stripe Checkout.
/// </summary>
public class Pago
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public int MembresiaId { get; set; }
    public Membresia? Membresia { get; set; }

    /// <summary>Id de la Checkout Session de Stripe (único, para idempotencia).</summary>
    [StringLength(255)]
    public string StripeSessionId { get; set; } = string.Empty;

    public decimal Monto { get; set; }

    [StringLength(10)]
    public string Moneda { get; set; } = "usd";

    public EstadoPago Estado { get; set; } = EstadoPago.Pendiente;

    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    public DateTime? PeriodoInicio { get; set; }
    public DateTime? PeriodoFin { get; set; }
}
