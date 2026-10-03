using System.ComponentModel.DataAnnotations;

namespace ConcreteSalesRadar.Models.Entities;

/// <summary>
/// Usuario registrado. Los datos básicos solicitados: Nombre, Apellido, Compañía,
/// Correo y Teléfono de contacto. Autenticación por contraseña + verificación de
/// correo (doble autenticación mediante token enviado a la bandeja).
/// </summary>
public class Usuario
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Apellido { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Compania { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Correo { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Telefono { get; set; } = string.Empty;

    /// <summary>Hash de la contraseña (nunca se guarda en texto plano).</summary>
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>True cuando el usuario validó el token enviado a su correo.</summary>
    public bool CorreoConfirmado { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    public DateTime? UltimoAcceso { get; set; }

    // --- Rol ---
    public int RolId { get; set; }
    public Rol? Rol { get; set; }

    // --- Membresía seleccionada ---
    public int? MembresiaId { get; set; }
    public Membresia? Membresia { get; set; }

    public DateTime? MembresiaInicio { get; set; }
    public DateTime? MembresiaFin { get; set; }

    /// <summary>True cuando la membresía actual fue pagada vía Stripe.</summary>
    public bool MembresiaPagada { get; set; }

    /// <summary>Id del cliente en Stripe (reutilizado entre pagos).</summary>
    [StringLength(100)]
    public string? StripeCustomerId { get; set; }

    public ICollection<CodigoVerificacion> Codigos { get; set; } = new List<CodigoVerificacion>();
    public ICollection<Pago> Pagos { get; set; } = new List<Pago>();
    public ICollection<Proyecto> Proyectos { get; set; } = new List<Proyecto>();

    public string NombreCompleto => $"{Nombre} {Apellido}".Trim();
}
