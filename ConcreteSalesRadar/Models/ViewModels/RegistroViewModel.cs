using System.ComponentModel.DataAnnotations;

namespace ConcreteSalesRadar.Models.ViewModels;

public class RegistroViewModel
{
    // Permite letras (con acentos), números, espacios y signos comunes.
    private const string TextoNumero = @"^[A-Za-zÀ-ÿ0-9\s.,#'/()\-+&]+$";
    private const string MsgTextoNumero = "Solo se permiten letras y números.";
    private const string RegexCorreo = @"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$";

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(50, ErrorMessage = "Máximo 50 caracteres.")]
    [RegularExpression(TextoNumero, ErrorMessage = MsgTextoNumero)]
    [Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "El apellido es obligatorio.")]
    [StringLength(50, ErrorMessage = "Máximo 50 caracteres.")]
    [RegularExpression(TextoNumero, ErrorMessage = MsgTextoNumero)]
    [Display(Name = "Apellido")]
    public string Apellido { get; set; } = string.Empty;

    [Required(ErrorMessage = "La compañía es obligatoria.")]
    [StringLength(50, ErrorMessage = "Máximo 50 caracteres.")]
    [RegularExpression(TextoNumero, ErrorMessage = MsgTextoNumero)]
    [Display(Name = "Compañía")]
    public string Compania { get; set; } = string.Empty;

    [Required(ErrorMessage = "El correo es obligatorio.")]
    [StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
    [RegularExpression(RegexCorreo, ErrorMessage = "Ingresa un correo electrónico válido.")]
    [Display(Name = "Correo electrónico")]
    public string Correo { get; set; } = string.Empty;

    [Required(ErrorMessage = "El teléfono es obligatorio.")]
    [StringLength(50, ErrorMessage = "Máximo 50 caracteres.")]
    [RegularExpression(TextoNumero, ErrorMessage = MsgTextoNumero)]
    [Display(Name = "Teléfono de contacto")]
    public string Telefono { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre 6 y 100 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirma tu contraseña.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
    [Display(Name = "Confirmar contraseña")]
    public string ConfirmarPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecciona un plan de membresía.")]
    [Display(Name = "Plan de membresía")]
    public int MembresiaId { get; set; }
}
