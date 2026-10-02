namespace ConcreteSalesRadar.Models.Entities;

/// <summary>Propósito del código de verificación enviado por correo.</summary>
public enum TipoCodigo
{
    /// <summary>Confirmación de correo tras el registro (doble autenticación de alta).</summary>
    ConfirmacionCorreo = 0,

    /// <summary>Código de inicio de sesión (segundo factor por correo).</summary>
    InicioSesion = 1,

    /// <summary>Restablecimiento de contraseña.</summary>
    RecuperacionPassword = 2
}

/// <summary>
/// Token/código de un solo uso enviado al correo del usuario.
/// </summary>
public class CodigoVerificacion
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    /// <summary>Código numérico de 6 dígitos.</summary>
    public string Codigo { get; set; } = string.Empty;

    public TipoCodigo Tipo { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime FechaExpiracion { get; set; }

    public bool Usado { get; set; }

    public bool EsValido => !Usado && DateTime.UtcNow <= FechaExpiracion;
}
