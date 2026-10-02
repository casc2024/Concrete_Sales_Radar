using System.ComponentModel.DataAnnotations;

namespace ConcreteSalesRadar.Models.Entities;

/// <summary>
/// Rol de acceso del sistema (Administrador / Usuario).
/// </summary>
public class Rol
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Descripcion { get; set; }

    public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}

/// <summary>Nombres de rol usados como constantes a lo largo de la aplicación.</summary>
public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Usuario = "Usuario";
}
