using System.ComponentModel.DataAnnotations;

namespace ConcreteSalesRadar.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "El correo es obligatorio.")]
    [RegularExpression(@"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$", ErrorMessage = "Ingresa un correo electrónico válido.")]
    [Display(Name = "Correo electrónico")]
    public string Correo { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;

    public bool Recordarme { get; set; }
}

public class VerificarCodigoViewModel
{
    [Required]
    public int UsuarioId { get; set; }

    public string? Correo { get; set; }

    [Required(ErrorMessage = "Ingresa el código que recibiste por correo.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "El código debe tener 6 dígitos.")]
    [Display(Name = "Código de verificación")]
    public string Codigo { get; set; } = string.Empty;
}
